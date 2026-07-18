import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import type { BracketMatch, MatchFormat, ScoreType } from '../api/types';
import { matchFormatGameCount, matchFormatLabels, requiredWins, scoreTypeLabels } from '../api/types';
import { completeMatch, forfeitMatch, saveMatchResult, undoMatch } from '../api/matches';
import type { ScoreEntryInput } from '../api/matches';
import { extractErrorMessage } from '../api/client';
import { ConfirmDialog } from './ConfirmDialog';

const MATCH_FORMATS: MatchFormat[] = ['Bo1', 'Bo3', 'Bo5', 'Bo7'];
const SCORE_TYPES: ScoreType[] = ['Games', 'Sets', 'Points', 'WinnerOnly'];

/** One game slot's in-progress state - `participantAWon: null` means "not yet decided", distinct from the boolean the API wire format requires once a game is actually played. */
interface GameSlot {
  scoreA: number | null;
  scoreB: number | null;
  participantAWon: boolean | null;
}

const blankSlot = (): GameSlot => ({ scoreA: null, scoreB: null, participantAWon: null });

function initEntries(existing: BracketMatch['entries'], maxGames: number): GameSlot[] {
  return Array.from({ length: maxGames }, (_, i) => {
    const entry = existing[i];
    return entry ? { scoreA: entry.scoreA, scoreB: entry.scoreB, participantAWon: entry.participantAWon } : blankSlot();
  });
}

interface MatchResultDialogProps {
  tournamentId: string;
  match: BracketMatch;
  onClose: () => void;
}

export function MatchResultDialog({ tournamentId, match, onClose }: MatchResultDialogProps) {
  const queryClient = useQueryClient();
  const isDecided = match.status === 'Completed' || match.status === 'Forfeit';

  const [matchFormat, setMatchFormat] = useState<MatchFormat>(match.matchFormat);
  // Null until the admin picks one explicitly - a fresh match has no scoreType on the wire either
  // (Match.ScoreType is only set once a result is first saved), so there is no sensible default to
  // silently preselect here.
  const [scoreType, setScoreType] = useState<ScoreType | null>(match.scoreType);
  const [entries, setEntries] = useState<GameSlot[]>(() => initEntries(match.entries, matchFormatGameCount(matchFormat)));
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [forfeitWinnerId, setForfeitWinnerId] = useState<string | null>(null);
  const [confirmUndo, setConfirmUndo] = useState(false);

  const maxGames = matchFormatGameCount(matchFormat);
  const required = requiredWins(matchFormat);

  // Resize the fixed slot list when the format changes (e.g. Bo5 -> Bo3 drops the trailing slots),
  // keeping whatever was already entered in the slots that still exist.
  useEffect(() => {
    setEntries((prev) => {
      if (prev.length === maxGames) {
        return prev;
      }

      const next = prev.slice(0, maxGames);
      while (next.length < maxGames) {
        next.push(blankSlot());
      }

      return next;
    });
  }, [maxGames]);

  const isFilled = (slot: GameSlot) =>
    scoreType === 'Points' ? slot.scoreA != null && slot.scoreB != null : slot.participantAWon != null;

  // Slots are filled strictly in order, so the first not-yet-filled slot is the one boundary that
  // matters - everything before it is played, everything from it onward isn't reachable yet.
  let filledCount = 0;
  while (filledCount < entries.length && isFilled(entries[filledCount])) {
    filledCount++;
  }

  const playedWinsA = entries.slice(0, filledCount).filter((e) => e.participantAWon === true).length;
  const playedWinsB = filledCount - playedWinsA;
  const isDecisive = playedWinsA >= required || playedWinsB >= required;
  // One panel enabled at a time: the played ones, plus exactly one more to play next - unless the
  // match is already decided, in which case no further panel is needed.
  const enabledCount = isDecisive ? filledCount : Math.min(filledCount + 1, maxGames);

  const invalidateBracket = () => queryClient.invalidateQueries({ queryKey: ['bracket', tournamentId] });

  const playedEntries = (): ScoreEntryInput[] =>
    entries.slice(0, filledCount).map((slot) => ({
      scoreA: slot.scoreA,
      scoreB: slot.scoreB,
      participantAWon: slot.participantAWon ?? false, // guaranteed non-null: every slot before filledCount passed isFilled
    }));

  const saveMutation = useMutation({
    mutationFn: () => {
      if (!scoreType) {
        throw new Error('Choose a score type first.');
      }

      return saveMatchResult(tournamentId, match.id, { matchFormat, scoreType, entries: playedEntries() });
    },
    onSuccess: async () => {
      setError(null);
      setDirty(false);
      await invalidateBracket();
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const completeMutation = useMutation({
    mutationFn: () => {
      if (!scoreType) {
        throw new Error('Choose a score type first.');
      }

      return completeMatch(tournamentId, match.id, { matchFormat, scoreType, entries: playedEntries() });
    },
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

  // Changing game `index` clears every game after it - their results were only meaningful given the
  // history leading up to them, and that history just changed (e.g. edit game 3 of an A,A,B,A run and
  // game 4's stale "A" would otherwise survive even though the decisive game is now earlier).
  const updateEntry = (index: number, patch: Partial<GameSlot>) => {
    setEntries((prev) => prev.map((slot, i) => (i < index ? slot : i === index ? { ...slot, ...patch } : blankSlot())));
    setDirty(true);
  };

  const handleClose = () => {
    if (busy) return;
    if (dirty && !window.confirm('Discard unsaved changes to this match?')) {
      return;
    }
    onClose();
  };

  const nameA = match.participantA?.name ?? 'TBD';
  const nameB = match.participantB?.name ?? 'TBD';

  return (
    <Dialog open onClose={handleClose} maxWidth="sm" fullWidth>
      <DialogTitle>
        {nameA} vs {nameB}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && (
            <Alert severity="error" onClose={() => setError(null)}>
              {error}
            </Alert>
          )}

          {isDecided ? (
            <Stack spacing={1}>
              <Typography variant="body2" color="text.secondary">
                {match.status === 'Forfeit' ? 'Completed by forfeit.' : 'Completed.'}
              </Typography>
              <Typography variant="h6">
                {match.aggregateScoreA} – {match.aggregateScoreB}
              </Typography>
              <Typography variant="body2">
                Winner: {match.winnerId === match.participantA?.participantId ? nameA : nameB}
              </Typography>
            </Stack>
          ) : (
            <>
              <Stack direction="row" spacing={2}>
                <FormControl fullWidth size="small">
                  <InputLabel id="match-format-label">Match format</InputLabel>
                  <Select
                    labelId="match-format-label"
                    label="Match format"
                    value={matchFormat}
                    onChange={(e) => {
                      setMatchFormat(e.target.value as MatchFormat);
                      setDirty(true);
                    }}
                  >
                    {MATCH_FORMATS.map((format) => (
                      <MenuItem key={format} value={format}>
                        {matchFormatLabels[format]}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
                <FormControl fullWidth size="small">
                  <InputLabel id="score-type-label" shrink>
                    Score type
                  </InputLabel>
                  <Select
                    labelId="score-type-label"
                    label="Score type"
                    displayEmpty
                    value={scoreType ?? ''}
                    onChange={(e) => {
                      setScoreType(e.target.value as ScoreType);
                      setDirty(true);
                    }}
                  >
                    <MenuItem value="" disabled>
                      <em>Select…</em>
                    </MenuItem>
                    {SCORE_TYPES.map((type) => (
                      <MenuItem key={type} value={type} disabled={type === 'WinnerOnly' && matchFormat !== 'Bo1'}>
                        {scoreTypeLabels[type]}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
              </Stack>

              {scoreType == null ? (
                <Typography variant="body2" color="text.secondary">
                  Choose a score type to enter results.
                </Typography>
              ) : (
                <Stack spacing={1}>
                  {entries.map((slot, index) => {
                    const enabled = index < enabledCount;
                    return (
                      <Stack key={index} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                        <Typography variant="body2" color={enabled ? 'text.primary' : 'text.disabled'} sx={{ minWidth: 56 }}>
                          Game {index + 1}
                        </Typography>
                        {scoreType === 'Points' ? (
                          <>
                            <TextField
                              size="small"
                              type="number"
                              label={nameA}
                              value={slot.scoreA ?? ''}
                              disabled={!enabled}
                              sx={{ width: 90 }}
                              slotProps={{ htmlInput: { inputMode: 'numeric', pattern: '[0-9]*' } }}
                              onFocus={(e) => e.target.select()}
                              onChange={(e) => {
                                const scoreA = e.target.value === '' ? null : Number(e.target.value);
                                const { scoreB } = slot;
                                updateEntry(index, {
                                  scoreA,
                                  participantAWon: scoreA != null && scoreB != null ? scoreA > scoreB : slot.participantAWon,
                                });
                              }}
                            />
                            <TextField
                              size="small"
                              type="number"
                              label={nameB}
                              value={slot.scoreB ?? ''}
                              disabled={!enabled}
                              sx={{ width: 90 }}
                              slotProps={{ htmlInput: { inputMode: 'numeric', pattern: '[0-9]*' } }}
                              onFocus={(e) => e.target.select()}
                              onChange={(e) => {
                                const scoreB = e.target.value === '' ? null : Number(e.target.value);
                                const { scoreA } = slot;
                                updateEntry(index, {
                                  scoreB,
                                  participantAWon: scoreA != null && scoreB != null ? scoreA > scoreB : slot.participantAWon,
                                });
                              }}
                            />
                          </>
                        ) : (
                          <ToggleButtonGroup
                            size="small"
                            exclusive
                            value={slot.participantAWon}
                            onChange={(_, value) => updateEntry(index, { participantAWon: value })}
                          >
                            <ToggleButton value={true} disabled={!enabled}>
                              {nameA} won
                            </ToggleButton>
                            <ToggleButton value={false} disabled={!enabled}>
                              {nameB} won
                            </ToggleButton>
                          </ToggleButtonGroup>
                        )}
                      </Stack>
                    );
                  })}
                </Stack>
              )}

              {scoreType != null && (
                <Typography variant="body2" color="text.secondary">
                  Aggregate: {playedWinsA} – {playedWinsB} (needs {required} to win)
                </Typography>
              )}

              {match.participantA && match.participantB && (
                <Stack direction="row" spacing={1}>
                  <Button size="small" color="warning" onClick={() => setForfeitWinnerId(match.participantA!.participantId)}>
                    Forfeit: {nameA} wins
                  </Button>
                  <Button size="small" color="warning" onClick={() => setForfeitWinnerId(match.participantB!.participantId)}>
                    Forfeit: {nameB} wins
                  </Button>
                </Stack>
              )}
            </>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        {isDecided ? (
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
            <Button onClick={() => saveMutation.mutate()} disabled={busy || scoreType == null}>
              Save
            </Button>
            <Button variant="contained" onClick={() => completeMutation.mutate()} disabled={busy || scoreType == null || !isDecisive}>
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
