import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Card,
  CardContent,
  Chip,
  IconButton,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import RemoveIcon from '@mui/icons-material/Remove';
import DeleteIcon from '@mui/icons-material/Delete';
import DoneIcon from '@mui/icons-material/Done';
import { listParticipants, replaceRoster } from '../api/tournaments';
import type { Participant, TournamentType } from '../api/types';
import { MAX_PARTICIPANTS, PARTICIPANT_NAME_MAX_LENGTH, requiredByes } from '../api/types';
import { extractErrorMessage } from '../api/client';
import { EmojiPicker, PICKER_HEIGHT } from './EmojiPicker';

/**
 * The roster is filled in by first choosing how many players there are, then naming the panels that
 * appear, then committing the lot with one button - rather than by adding participants one at a time.
 *
 * Every edit here is local until "Participants are set" is pressed: that is the only point at which
 * the roster is validated and written. So a panel is not the same thing as a saved participant, and
 * each one is a "slot" carrying its own draft name and emoji plus the id of the participant it came
 * from (null for a panel that has never been saved). Saving hands the whole list to `replaceRoster`,
 * which works out the creates, renames and removals server-side in one transaction.
 */
interface Slot {
  key: string;
  /** The saved participant this panel stands for, or null while it is still a new, unsaved panel. */
  id: string | null;
  name: string;
  emoji: string | null;
}

const blankSlot = (): Slot => ({ key: crypto.randomUUID(), id: null, name: '', emoji: null });

/** Tops a list up to `minPanels` with blanks, so the roster always shows at least a startable shape. */
const padToMinimum = (slots: Slot[], minPanels: number): Slot[] =>
  slots.length >= minPanels
    ? slots
    : [...slots, ...Array.from({ length: minPanels - slots.length }, blankSlot)];

/**
 * The saved roster as panels, padded up to the minimum. A new tournament therefore opens with that
 * many blank panels rather than one, which states the requirement by showing it instead of waiting to
 * complain about it.
 */
const slotsFromRoster = (participants: Participant[], minPanels: number): Slot[] =>
  padToMinimum(
    participants.map((p) => ({ key: p.id, id: p.id, name: p.name, emoji: p.emoji })),
    minPanels,
  );

/** Whether the panels say anything different from the saved roster - name, emoji, order or membership. */
function differsFromRoster(slots: Slot[], participants: Participant[]): boolean {
  if (slots.length !== participants.length) return true;
  return slots.some((slot, index) => {
    const saved = participants[index];
    return slot.id !== saved.id || slot.name.trim() !== saved.name || slot.emoji !== saved.emoji;
  });
}

/**
 * Names used more than once, which the domain rejects. The only rule checked here: a blank name can't
 * reach this point, because the save button stays disabled while any panel is unnamed.
 */
function duplicateNames(slots: Slot[]): string[] {
  // Case-insensitive, matching the uniqueness rule the domain enforces.
  const seen = new Set<string>();
  const duplicates = new Set<string>();
  for (const slot of slots) {
    const name = slot.name.trim().toLocaleLowerCase();
    if (name.length === 0) continue;
    if (seen.has(name)) duplicates.add(slot.name.trim());
    seen.add(name);
  }
  return [...duplicates].map((name) => `More than one player is called "${name}".`);
}

export function ParticipantsSection({
  tournamentId,
  tournamentType,
  minPanels,
  onUnsavedChange,
}: {
  tournamentId: string;
  tournamentType: TournamentType;
  /** Fewest participants this tournament can start with, as the server computed it (Tournament.MinParticipantsToStart). */
  minPanels: number;
  /** Reports whether the panels differ from the saved roster, so the bracket preview can refuse to start. */
  onUnsavedChange?: (unsaved: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const [slots, setSlots] = useState<Slot[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [problems, setProblems] = useState<string[]>([]);
  // Which panels have been touched, so a blank one isn't flagged red the instant it appears.
  const [touched, setTouched] = useState<Set<string>>(new Set());
  const edited = useRef(false);

  const { data: participants = [], isSuccess: rosterLoaded } = useQuery({
    queryKey: ['participants', tournamentId],
    queryFn: () => listParticipants(tournamentId),
  });

  // Mirror the saved roster into the panels, but never on top of edits that haven't been committed.
  useEffect(() => {
    if (rosterLoaded && !edited.current) setSlots(slotsFromRoster(participants, minPanels));
  }, [participants, rosterLoaded, minPanels]);

  const blanks = slots.filter((slot) => slot.name.trim().length === 0).length;
  // A blank panel is a difference too (there is no saved participant behind it), so a brand-new
  // tournament reads as unsaved from the start - which is exactly the state that must block a start.
  const unsaved = rosterLoaded && differsFromRoster(slots, participants);
  // No blanks means every panel is named, and the panel count can't go below the minimum, so this one
  // condition covers both "nothing missing" and "enough to start" - no separate count check needed.
  const canSave = unsaved && blanks === 0;

  useEffect(() => {
    onUnsavedChange?.(unsaved);
  }, [unsaved, onUnsavedChange]);

  const editSlots = (update: (current: Slot[]) => Slot[]) => {
    edited.current = true;
    setError(null);
    setProblems([]);
    setSlots(update);
  };

  const saveMutation = useMutation({
    mutationFn: () =>
      replaceRoster(
        tournamentId,
        slots.map((slot) => ({ id: slot.id, name: slot.name.trim(), emoji: slot.emoji })),
      ),
    onSuccess: async (saved) => {
      // The response is the saved roster, ids and order included - the same thing the list endpoint
      // returns - so seed the cache with it instead of invalidating and fetching it straight back.
      edited.current = false;
      setTouched(new Set());
      queryClient.setQueryData(['participants', tournamentId], saved);
      // The dashboard's participant counts are a different query and do still need refreshing.
      await queryClient.invalidateQueries({ queryKey: ['tournaments'] });
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const save = () => {
    const found = duplicateNames(slots);
    setProblems(found);
    if (found.length === 0) saveMutation.mutate();
  };

  /** Trims or extends the panel list to `target`. Nothing is deleted server-side until the roster is saved. */
  const setPanelCount = (target: number) => {
    const clamped = Math.min(Math.max(target, minPanels), MAX_PARTICIPANTS);
    if (clamped === slots.length) return;
    editSlots((current) => padToMinimum(current.slice(0, clamped), clamped));
  };

  const count = participants.length;
  // Judged against this shape's own minimum, not the universal two: a Group Stage + Playoff with three
  // saved participants shouldn't read as complete when it can't start.
  const countColor = count < minPanels || count > MAX_PARTICIPANTS ? 'warning' : 'success';
  const byesNeeded = requiredByes(count, tournamentType);

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={2} sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6">Participants</Typography>
          <Chip size="small" color={countColor} label={`${count} / ${MAX_PARTICIPANTS}`} />
        </Stack>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        {problems.length > 0 && (
          <Alert severity="warning" sx={{ mb: 2 }}>
            <Stack spacing={0.5}>
              {problems.map((problem) => (
                <span key={problem}>{problem}</span>
              ))}
            </Stack>
          </Alert>
        )}

        <PanelCountControl
          value={slots.length}
          min={minPanels}
          onChange={setPanelCount}
          disabled={!rosterLoaded || saveMutation.isPending}
        />

        {/* One panel per row, each only as wide as it needs to be - a full-width name field beside a
            single emoji button reads as a form, not as a roster. */}
        <Stack spacing={1} sx={{ mt: 2, alignItems: 'flex-start' }}>
          {slots.map((slot, index) => (
            <ParticipantPanel
              key={slot.key}
              position={index + 1}
              slot={slot}
              blank={touched.has(slot.key) && slot.name.trim().length === 0}
              disabled={saveMutation.isPending}
              onChange={(changes) =>
                editSlots((current) => current.map((s) => (s.key === slot.key ? { ...s, ...changes } : s)))
              }
              onTouch={() => setTouched((current) => new Set(current).add(slot.key))}
              // Removing at the floor leaves a blank panel behind rather than shrinking the roster
              // below what the tournament could start with.
              onRemove={() =>
                editSlots((current) => padToMinimum(current.filter((s) => s.key !== slot.key), minPanels))
              }
            />
          ))}
        </Stack>

        <Stack direction="row" spacing={2} sx={{ mt: 2, alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
          <Button
            variant="contained"
            startIcon={<DoneIcon />}
            onClick={save}
            // canSave implies unsaved, which implies the roster has loaded - no separate check needed.
            disabled={!canSave || saveMutation.isPending}
          >
            Participants are set
          </Button>
          {blanks > 0 ? (
            <Typography variant="body2" color="warning.main">
              {blanks} panel{blanks === 1 ? '' : 's'} still need{blanks === 1 ? 's' : ''} a name - this
              tournament needs {minPanels} participants at minimum.
            </Typography>
          ) : (
            unsaved && (
              <Typography variant="body2" color="warning.main">
                Unsaved changes - nothing is stored until you press this.
              </Typography>
            )
          )}
        </Stack>

        {count >= minPanels && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
            Requires {byesNeeded} bye{byesNeeded === 1 ? '' : 's'} for a {count}-participant bracket.
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}

/**
 * The number of players, as a spinner. The text field keeps whatever is typed while it has focus and
 * only clamps on commit, so typing "12" isn't read as a 1 that drops ten panels on the way through.
 */
function PanelCountControl({
  value,
  min,
  onChange,
  disabled,
}: {
  value: number;
  /** Fewest players this tournament could start with; minus stops here. */
  min: number;
  onChange: (next: number) => void;
  disabled: boolean;
}) {
  const [draft, setDraft] = useState(String(value));

  // Mirrors the panel list, which the +/- buttons and Delete also move. No "is it focused" guard is
  // needed: typing only moves `draft`, and anything that changes `value` blurs the field first.
  useEffect(() => {
    setDraft(String(value));
  }, [value]);

  const commit = () => {
    const parsed = Number.parseInt(draft, 10);
    // An unparseable entry falls back to the current count rather than to a bound, so clearing the
    // field and clicking away can't wipe the panels.
    onChange(Number.isNaN(parsed) ? value : parsed);
    setDraft(String(value));
  };

  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
      <Typography variant="body2" color="text.secondary">
        Number of players
      </Typography>
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
        <IconButton
          size="small"
          onClick={() => onChange(value - 1)}
          disabled={disabled || value <= min}
          aria-label="One player fewer"
        >
          <RemoveIcon fontSize="small" />
        </IconButton>
        <TextField
          size="small"
          type="number"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onBlur={commit}
          onKeyDown={(e: KeyboardEvent) => e.key === 'Enter' && (e.target as HTMLInputElement).blur()}
          disabled={disabled}
          slotProps={{
            htmlInput: {
              'aria-label': 'Number of players',
              min,
              max: MAX_PARTICIPANTS,
              inputMode: 'numeric',
              style: { textAlign: 'center' },
            },
          }}
          sx={{ width: 88 }}
        />
        <IconButton
          size="small"
          onClick={() => onChange(value + 1)}
          disabled={disabled || value >= MAX_PARTICIPANTS}
          aria-label="One player more"
        >
          <AddIcon fontSize="small" />
        </IconButton>
      </Stack>
    </Stack>
  );
}

/** One player: name, emoji and Delete, all editable, all purely local until the roster is saved. */
function ParticipantPanel({
  position,
  slot,
  blank,
  disabled,
  onChange,
  onTouch,
  onRemove,
}: {
  position: number;
  slot: Slot;
  /** True when this panel has been left without a name, so it can be marked rather than silently skipped. */
  blank: boolean;
  disabled: boolean;
  onChange: (changes: Partial<Pick<Slot, 'name' | 'emoji'>>) => void;
  onTouch: () => void;
  onRemove: () => void;
}) {
  return (
    // Top-aligned, with the buttons in their own input-height row: the validation message grows the
    // text field downwards, and centring the whole panel would drift the buttons down with it.
    <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start', width: 380, maxWidth: '100%' }}>
      <TextField
        size="small"
        value={slot.name}
        placeholder={`Player ${position}`}
        error={blank}
        helperText={blank ? 'A name is required.' : undefined}
        onChange={(e) => onChange({ name: e.target.value })}
        onBlur={onTouch}
        disabled={disabled}
        slotProps={{ htmlInput: { 'aria-label': `Player ${position} name`, maxLength: PARTICIPANT_NAME_MAX_LENGTH } }}
        sx={{ flexGrow: 1 }}
      />
      <Stack direction="row" spacing={1} sx={{ height: PICKER_HEIGHT, alignItems: 'center' }}>
        <EmojiPicker value={slot.emoji} onChange={(emoji) => onChange({ emoji })} disabled={disabled} />
        <Tooltip title="Remove this player">
          <span>
            <IconButton
              size="small"
              color="error"
              onClick={onRemove}
              disabled={disabled}
              aria-label={`Remove player ${position}`}
            >
              <DeleteIcon fontSize="small" />
            </IconButton>
          </span>
        </Tooltip>
      </Stack>
    </Stack>
  );
}
