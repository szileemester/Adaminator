import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  IconButton,
  List,
  ListItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import CheckIcon from '@mui/icons-material/Check';
import CloseIcon from '@mui/icons-material/Close';
import {
  addParticipant,
  listParticipants,
  removeParticipant,
  updateParticipant,
} from '../api/tournaments';
import type { Participant, TournamentType } from '../api/types';
import { requiredByes } from '../api/types';
import { extractErrorMessage } from '../api/client';
import { EmojiPicker } from './EmojiPicker';
import { ParticipantLabel } from './ParticipantLabel';

export function ParticipantsSection({ tournamentId, tournamentType }: { tournamentId: string; tournamentType: TournamentType }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [emoji, setEmoji] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const { data: participants = [] } = useQuery({
    queryKey: ['participants', tournamentId],
    queryFn: () => listParticipants(tournamentId),
  });

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['participants', tournamentId] });
    await queryClient.invalidateQueries({ queryKey: ['tournaments'] });
  };

  const addMutation = useMutation({
    mutationFn: (input: { value: string; emoji: string | null }) => addParticipant(tournamentId, input.value, input.emoji),
    onSuccess: async () => {
      setName('');
      setEmoji(null);
      await invalidate();
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const removeMutation = useMutation({
    mutationFn: (participantId: string) => removeParticipant(tournamentId, participantId),
    onSuccess: invalidate,
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const updateMutation = useMutation({
    mutationFn: (input: { id: string; value: string; emoji: string | null }) =>
      updateParticipant(tournamentId, input.id, input.value, input.emoji),
    onSuccess: invalidate,
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const handleAdd = (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    const trimmed = name.trim();
    if (trimmed) {
      addMutation.mutate({ value: trimmed, emoji });
    }
  };

  const count = participants.length;
  const countColor = count < 2 || count > 32 ? 'warning' : 'success';
  const byesNeeded = requiredByes(count, tournamentType);

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={2} sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6">Participants</Typography>
          <Chip size="small" color={countColor} label={`${count} / 32`} />
        </Stack>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        <Box component="form" onSubmit={handleAdd} sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
          <TextField
            size="small"
            label="Participant name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            fullWidth
          />
          <EmojiPicker value={emoji} onChange={setEmoji} disabled={addMutation.isPending} />
          <Button type="submit" variant="contained" disabled={addMutation.isPending || name.trim().length === 0}>
            Add
          </Button>
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
          Pick an optional emoji next to the name. It's permanent once saved.
        </Typography>

        {count < 2 && (
          <Typography variant="body2" color="text.secondary">
            Add at least 2 participants to generate a bracket.
          </Typography>
        )}
        {count > 32 && (
          <Typography variant="body2" color="warning.main">
            A tournament may have at most 32 participants.
          </Typography>
        )}

        <List dense>
          {participants.map((participant) => (
            <ParticipantRow
              key={participant.id}
              participant={participant}
              onSave={(value, chosenEmoji) => updateMutation.mutate({ id: participant.id, value, emoji: chosenEmoji })}
              onRemove={() => removeMutation.mutate(participant.id)}
            />
          ))}
        </List>

        {count >= 2 && (
          <Typography variant="body2" color="text.secondary">
            Requires {byesNeeded} bye{byesNeeded === 1 ? '' : 's'} for a {count}-participant bracket.
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}

function ParticipantRow({
  participant,
  onSave,
  onRemove,
}: {
  participant: Participant;
  onSave: (value: string, emoji: string | null) => void;
  onRemove: () => void;
}) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(participant.name);
  const [emoji, setEmoji] = useState<string | null>(participant.emoji);

  const cancel = () => {
    setValue(participant.name);
    setEmoji(participant.emoji);
    setEditing(false);
  };

  const commit = () => {
    const trimmed = value.trim();
    if (trimmed && (trimmed !== participant.name || emoji !== participant.emoji)) {
      onSave(trimmed, emoji);
    }
    setEditing(false);
  };

  return (
    <ListItem
      disableGutters
      secondaryAction={
        editing ? (
          <>
            <IconButton size="small" onClick={commit} aria-label="Save">
              <CheckIcon fontSize="small" />
            </IconButton>
            <IconButton size="small" onClick={cancel} aria-label="Cancel">
              <CloseIcon fontSize="small" />
            </IconButton>
          </>
        ) : (
          <>
            <IconButton size="small" onClick={() => setEditing(true)} aria-label="Edit">
              <EditIcon fontSize="small" />
            </IconButton>
            <IconButton size="small" color="error" onClick={onRemove} aria-label="Remove">
              <DeleteIcon fontSize="small" />
            </IconButton>
          </>
        )
      }
    >
      {editing ? (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <TextField
            size="small"
            value={value}
            onChange={(e) => setValue(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && commit()}
            autoFocus
            sx={{ maxWidth: 260 }}
          />
          {/* Write-once: someone who already has an emoji can never change it, but someone who skipped it can still choose (once). */}
          <EmojiPicker value={emoji} onChange={setEmoji} locked={participant.emoji !== null} />
        </Stack>
      ) : (
        <ParticipantLabel name={participant.name} emoji={participant.emoji} />
      )}
    </ListItem>
  );
}
