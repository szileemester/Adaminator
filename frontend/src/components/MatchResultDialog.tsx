import { useState, type ReactNode } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import type { BracketMatch, ScoreType } from '../api/types';
import { allowsDraw, formatParticipantName, isDecided, matchFormatGameCount, matchFormatLabels, requiredWins, scoreTypeLabels } from '../api/types';
import { completeMatch, forfeitMatch, saveMatchResult, undoMatch } from '../api/matches';
import type { ScoreEntryInput } from '../api/matches';
import { extractErrorMessage } from '../api/client';
import { ConfirmDialog } from './ConfirmDialog';
import { breakAnywhereSx } from '../theme';

/** One game slot's in-progress state - `participantAWon: null` means "not yet decided", distinct from the boolean the API wire format requires once a game is actually played. */
interface GameSlot {
  scoreA: number | null;
  scoreB: number | null;
  participantAWon: boolean | null;
}

/**
 * An untouched game. A Points match opens at 0-0 rather than empty: an empty numeric field parks the
 * participant's name inside the box where the score belongs, which reads as a value. 0-0 still counts
 * as "not entered" to `computeGameProgress`, since a tie can never be a final score. A Games match
 * has no scores at all - only who won.
 */
const blankSlot = (scoreType: ScoreType): GameSlot =>
  scoreType === 'Points'
    ? { scoreA: 0, scoreB: 0, participantAWon: null }
    : { scoreA: null, scoreB: null, participantAWon: null };

/**
 * Winner implied by a game's scores once both are entered, otherwise whatever was already selected
 * (e.g. by a toggle click). A tie has no implied winner - it is not a valid final score (the backend
 * rejects it, see Match.BuildEntries) - so it resolves to `null` rather than falling through to
 * whatever the fallback happened to be.
 */
function deriveWinner(scoreA: number | null, scoreB: number | null, fallback: boolean | null): boolean | null {
  if (scoreA == null || scoreB == null) {
    return fallback;
  }
  return scoreA === scoreB ? null : scoreA > scoreB;
}

/** Number(value) with a NaN guard: a syntactically incomplete number (e.g. a lone "-") is treated as "not entered" rather than a phantom score. */
function parseScore(value: string): number | null {
  if (value === '') {
    return null;
  }
  const parsed = Number(value);
  return Number.isNaN(parsed) ? null : parsed;
}

interface GameProgress {
  played: GameSlot[];
  playedWinsA: number;
  playedWinsB: number;
  /** Whether the result can be submitted: someone reached the required wins, or (a draw-capable format) every game is played. */
  canComplete: boolean;
  /** How many game panels are interactive: for a decisive format one ahead of what's filled and it stops once decided; a draw-capable format keeps them all open (both games are always played). */
  enabledCount: number;
}

function computeGameProgress(
  entries: GameSlot[], scoreType: ScoreType, required: number, maxGames: number, drawCapable: boolean): GameProgress {
  const isFilled = (slot: GameSlot) =>
    scoreType === 'Points'
      ? slot.scoreA != null && slot.scoreB != null && slot.scoreA !== slot.scoreB
      : slot.participantAWon != null;

  // Slots are filled strictly in order, so the first not-yet-filled slot is the one boundary that
  // matters - everything before it is played, everything from it onward isn't reachable yet.
  let filledCount = 0;
  while (filledCount < entries.length && isFilled(entries[filledCount])) {
    filledCount++;
  }

  const played = entries.slice(0, filledCount);
  const playedWinsA = played.filter((e) => e.participantAWon === true).length;
  const playedWinsB = filledCount - playedWinsA;
  // A draw-capable match (Best of 2) plays every game and may end level, so it is complete once all
  // games are entered regardless of who won; a decisive one completes as soon as someone clinches.
  const decisive = playedWinsA >= required || playedWinsB >= required;
  const canComplete = drawCapable ? filledCount === maxGames : decisive;
  const enabledCount = drawCapable ? maxGames : decisive ? filledCount : Math.min(filledCount + 1, maxGames);

  return { played, playedWinsA, playedWinsB, canComplete, enabledCount };
}

/**
 * Clip a single line and mark the cut with an ellipsis. Used by both places in this dialog that put a
 * participant name inside a control - the button labels and the score fields' floating labels - which
 * fail the same way for the same reason, so they share the recipe.
 */
const ELLIPSIS_SX = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const;

/**
 * A control's label that gives up its tail rather than its container's width. A participant name runs
 * to 30 characters and several labels here embed one ("<name> won", "Forfeit: <name> wins"); left to
 * size themselves they push the dialog wider than the screen. The emoji and opening characters are the
 * identifying part, so the tail is what goes.
 */
function TruncatedLabel({ children }: { children: ReactNode }) {
  return (
    <Box component="span" sx={{ minWidth: 0, ...ELLIPSIS_SX }}>
      {children}
    </Box>
  );
}

/**
 * One participant's score for one game. Emptying the field mid-edit is allowed - typing over a value
 * shouldn't fight the input - but it snaps back to 0 on blur, so a field is never left showing its
 * label where a number should be.
 */
function PointsScoreField({
  label,
  value,
  disabled,
  onChange,
}: {
  label: string;
  value: number | null;
  disabled: boolean;
  onChange: (score: number | null) => void;
}) {
  return (
    <TextField
      size="small"
      type="number"
      label={label}
      value={value ?? ''}
      disabled={disabled}
      // The label is a participant name, so the field shares the row rather than staying at its
      // natural width - and the label still truncates, since 30 characters outrun any sane field.
      sx={{
        flex: 1,
        minWidth: 90,
        maxWidth: 180,
        '& .MuiInputLabel-root': { maxWidth: 'calc(100% - 24px)', ...ELLIPSIS_SX },
      }}
      slotProps={{ htmlInput: { inputMode: 'numeric', pattern: '[0-9]*' } }}
      onFocus={(e) => e.target.select()}
      onChange={(e) => onChange(parseScore(e.target.value))}
      onBlur={() => value === null && onChange(0)}
    />
  );
}

interface MatchResultDialogProps {
  tournamentId: string;
  match: BracketMatch;
  onClose: () => void;
}

export function MatchResultDialog({ tournamentId, match, onClose }: MatchResultDialogProps) {
  const queryClient = useQueryClient();
  const decided = isDecided(match);

  // Fixed for the lifetime of the dialog: format and score type are set once when the bracket is
  // built (by the tournament's settings), never editable at result-entry time.
  const matchFormat = match.matchFormat;
  const scoreType = match.scoreType;
  // Only the played prefix is stored - a slot never exists in state until the admin has touched it,
  // so there is no fixed-length array to keep in sync with the (now fixed) match format.
  const [entries, setEntries] = useState<GameSlot[]>(() =>
    match.entries.map((e) => ({ scoreA: e.scoreA, scoreB: e.scoreB, participantAWon: e.participantAWon })),
  );
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [forfeitWinnerId, setForfeitWinnerId] = useState<string | null>(null);
  const [confirmUndo, setConfirmUndo] = useState(false);

  const maxGames = matchFormatGameCount(matchFormat);
  const required = requiredWins(matchFormat);
  const drawCapable = allowsDraw(matchFormat);
  const { played, playedWinsA, playedWinsB, canComplete, enabledCount } = computeGameProgress(
    entries,
    scoreType,
    required,
    maxGames,
    drawCapable,
  );

  const invalidateBracket = () => queryClient.invalidateQueries({ queryKey: ['bracket', tournamentId] });

  const buildPayload = () => {
    const playedEntries: ScoreEntryInput[] = played.map((slot) => ({
      scoreA: slot.scoreA,
      scoreB: slot.scoreB,
      participantAWon: slot.participantAWon ?? false, // guaranteed non-null: every slot in `played` passed isFilled
    }));

    return { matchFormat, scoreType, entries: playedEntries };
  };

  const saveMutation = useMutation({
    mutationFn: () => saveMatchResult(tournamentId, match.id, buildPayload()),
    onSuccess: async () => {
      setError(null);
      setDirty(false);
      await invalidateBracket();
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const completeMutation = useMutation({
    mutationFn: () => completeMatch(tournamentId, match.id, buildPayload()),
    onSuccess: async () => {
      await invalidateBracket();
      onClose();
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const forfeitMutation = useMutation({
    mutationFn: (winnerId: string) => forfeitMatch(tournamentId, match.id, winnerId),
    onSuccess: async () => {
      await invalidateBracket();
      onClose();
    },
    onError: (err) => {
      setForfeitWinnerId(null);
      setError(extractErrorMessage(err));
    },
  });

  const undoMutation = useMutation({
    mutationFn: () => undoMatch(tournamentId, match.id),
    onSuccess: async () => {
      await invalidateBracket();
      onClose();
    },
    onError: (err) => {
      setConfirmUndo(false);
      setError(extractErrorMessage(err));
    },
  });

  const busy =
    saveMutation.isPending || completeMutation.isPending || forfeitMutation.isPending || undoMutation.isPending;

  // Patches game `index` and drops every slot after it, since `entries` only ever stores the played
  // prefix: editing an earlier game changes the history leading up to the later ones, so their old
  // results (e.g. game 4's stale "A" after game 3 changes from B to A) are no longer meaningful and
  // must be re-entered rather than silently carried over.
  const updateEntry = (index: number, patch: Partial<GameSlot>) => {
    setEntries((prev) => [...prev.slice(0, index), { ...(prev[index] ?? blankSlot(scoreType)), ...patch }]);
    setDirty(true);
  };

  const handleClose = () => {
    if (busy) return;
    if (dirty && !window.confirm('Discard unsaved changes to this match?')) {
      return;
    }
    onClose();
  };

  // Emoji-prefixed strings rather than <ParticipantLabel>: several of these feed `label` props
  // (TextField, ToggleButton) and the dialog title, which take a string, not JSX.
  const nameA = match.participantA ? formatParticipantName(match.participantA.name, match.participantA.emoji) : 'TBD';
  const nameB = match.participantB ? formatParticipantName(match.participantB.name, match.participantB.emoji) : 'TBD';

  return (
    <Dialog open onClose={handleClose} maxWidth="sm" fullWidth>
      <DialogTitle sx={breakAnywhereSx}>
        {nameA} vs {nameB}
      </DialogTitle>
      <DialogContent>
        {/* Applied once here rather than on each line: every piece of prose below embeds a name. */}
        <Stack spacing={2} sx={{ mt: 1, ...breakAnywhereSx }}>
          {error && (
            <Alert severity="error" onClose={() => setError(null)}>
              {error}
            </Alert>
          )}

          {decided ? (
            <Stack spacing={1}>
              <Typography variant="body2" color="text.secondary">
                {match.status === 'Forfeit' ? 'Completed by forfeit.' : 'Completed.'}
              </Typography>
              <Typography variant="h6">
                {match.aggregateScoreA} – {match.aggregateScoreB}
              </Typography>
              <Typography variant="body2">
                {match.winnerId === null ? 'Draw' : `Winner: ${match.winnerId === match.participantA?.participantId ? nameA : nameB}`}
              </Typography>
              {/*
                The headline is games won, which is all the bracket shows. For a Points match that
                hides the actual scores, so list them per game - this dialog is the one place they
                are recorded.
              */}
              {scoreType === 'Points' && match.entries.length > 0 && (
                <Stack spacing={0.5} sx={{ pt: 1 }}>
                  <Typography variant="subtitle2" color="text.secondary">
                    Points per game
                  </Typography>
                  {match.entries.map((entry, index) => (
                    <Stack key={index} direction="row" spacing={1} sx={{ alignItems: 'baseline' }}>
                      <Typography variant="body2" color="text.secondary" sx={{ minWidth: 56 }}>
                        Game {index + 1}
                      </Typography>
                      <Typography variant="body2">
                        {nameA} {entry.scoreA ?? '-'} – {entry.scoreB ?? '-'} {nameB}
                      </Typography>
                    </Stack>
                  ))}
                </Stack>
              )}
            </Stack>
          ) : (
            <>
              <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
                <Typography variant="body2" color="text.secondary">
                  Format: {matchFormatLabels[matchFormat]}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Score type: {scoreTypeLabels[scoreType]}
                </Typography>
              </Stack>

              <Stack spacing={1}>
                {Array.from({ length: maxGames }, (_, index) => entries[index] ?? blankSlot(scoreType)).map((slot, index) => {
                  const enabled = index < enabledCount;
                  return (
                    <Stack key={index} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                      <Typography variant="body2" color={enabled ? 'text.primary' : 'text.disabled'} sx={{ minWidth: 56 }}>
                        Game {index + 1}
                      </Typography>
                      {scoreType === 'Points' ? (
                        <>
                          <PointsScoreField
                            label={nameA}
                            value={slot.scoreA}
                            disabled={!enabled}
                            onChange={(scoreA) =>
                              updateEntry(index, {
                                scoreA,
                                participantAWon: deriveWinner(scoreA, slot.scoreB, slot.participantAWon),
                              })
                            }
                          />
                          <PointsScoreField
                            label={nameB}
                            value={slot.scoreB}
                            disabled={!enabled}
                            onChange={(scoreB) =>
                              updateEntry(index, {
                                scoreB,
                                participantAWon: deriveWinner(slot.scoreA, scoreB, slot.participantAWon),
                              })
                            }
                          />
                        </>
                      ) : (
                        // Splits the space left beside the "Game n" label evenly instead of sizing to
                        // the two names, so the pair always fits the dialog whatever they are called.
                        <ToggleButtonGroup
                          size="small"
                          exclusive
                          value={slot.participantAWon}
                          onChange={(_, value) => updateEntry(index, { participantAWon: value })}
                          sx={{ flexGrow: 1, minWidth: 0 }}
                        >
                          <ToggleButton value={true} disabled={!enabled} sx={{ flex: 1, minWidth: 0 }}>
                            <TruncatedLabel>{nameA} won</TruncatedLabel>
                          </ToggleButton>
                          <ToggleButton value={false} disabled={!enabled} sx={{ flex: 1, minWidth: 0 }}>
                            <TruncatedLabel>{nameB} won</TruncatedLabel>
                          </ToggleButton>
                        </ToggleButtonGroup>
                      )}
                    </Stack>
                  );
                })}
              </Stack>

              <Typography variant="body2" color="text.secondary">
                Aggregate: {playedWinsA} – {playedWinsB}
                {drawCapable
                  ? ` (all ${maxGames} games; a ${maxGames / 2}-${maxGames / 2} is a draw)`
                  : ` (needs ${required} to win)`}
              </Typography>

              {match.participantA && match.participantB && (
                <Stack direction="row" spacing={1} sx={{ minWidth: 0 }}>
                  <Button
                    size="small"
                    color="warning"
                    sx={{ flex: 1, minWidth: 0 }}
                    onClick={() => setForfeitWinnerId(match.participantA!.participantId)}
                  >
                    <TruncatedLabel>Forfeit: {nameA} wins</TruncatedLabel>
                  </Button>
                  <Button
                    size="small"
                    color="warning"
                    sx={{ flex: 1, minWidth: 0 }}
                    onClick={() => setForfeitWinnerId(match.participantB!.participantId)}
                  >
                    <TruncatedLabel>Forfeit: {nameB} wins</TruncatedLabel>
                  </Button>
                </Stack>
              )}
            </>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        {decided ? (
          <>
            <Button onClick={handleClose}>Close</Button>
            {match.canUndo && (
              <Button color="error" onClick={() => setConfirmUndo(true)} disabled={busy}>
                Undo
              </Button>
            )}
          </>
        ) : (
          <>
            <Button onClick={handleClose} disabled={busy}>
              Cancel
            </Button>
            <Button onClick={() => saveMutation.mutate()} disabled={busy}>
              Save
            </Button>
            <Button variant="contained" onClick={() => completeMutation.mutate()} disabled={busy || !canComplete}>
              Complete
            </Button>
          </>
        )}
      </DialogActions>

      <ConfirmDialog
        open={forfeitWinnerId !== null}
        title="Complete as forfeit"
        message="This completes the match by forfeit and no further scores can be entered. Continue?"
        confirmLabel="Forfeit"
        confirmColor="error"
        busy={forfeitMutation.isPending}
        onCancel={() => setForfeitWinnerId(null)}
        onConfirm={() => forfeitWinnerId && forfeitMutation.mutate(forfeitWinnerId)}
      />

      <ConfirmDialog
        open={confirmUndo}
        title="Undo match"
        message="This removes the winner from the next match and restores this match to its saved score. Continue?"
        confirmLabel="Undo"
        confirmColor="error"
        busy={undoMutation.isPending}
        onCancel={() => setConfirmUndo(false)}
        onConfirm={() => undoMutation.mutate()}
      />
    </Dialog>
  );
}
