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
  renameParticipant,
} from '../api/tournaments';
import type { Participant } from '../api/types';
import { requiredByes } from '../api/types';
import { extractErrorMessage } from '../api/client';

export function ParticipantsSection({ tournamentId }: { tournamentId: string }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
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
    mutationFn: (value: string) => addParticipant(tournamentId, value),
    onSuccess: async () => {
      setName('');
      await invalidate();
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const removeMutation = useMutation({
    mutationFn: (participantId: string) => removeParticipant(tournamentId, participantId),
    onSuccess: invalidate,
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const renameMutation = useMutation({
    mutationFn: (input: { id: string; value: string }) => renameParticipant(tournamentId, input.id, input.value),
    onSuccess: invalidate,
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const handleAdd = (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    const trimmed = name.trim();
    if (trimmed) {
      addMutation.mutate(trimmed);
    }
  };

  const count = participants.length;
  const countColor = count < 2 || count > 32 ? 'warning' : 'success';

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

        <Box component="form" onSubmit={handleAdd} sx={{ display: 'flex', gap: 1, mb: 1 }}>
          <TextField
            size="small"
            label="Participant name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            fullWidth
          />
          <Button type="submit" variant="contained" disabled={addMutation.isPending || name.trim().length === 0}>
            Add
          </Button>
        </Box>

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
              onRename={(value) => renameMutation.mutate({ id: participant.id, value })}
              onRemove={() => removeMutation.mutate(participant.id)}
            />
          ))}
        </List>

        {count >= 2 && (
          <Typography variant="body2" color="text.secondary">
            Requires {requiredByes(count)} bye{requiredByes(count) === 1 ? '' : 's'} for a {count}-participant bracket.
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}

function ParticipantRow({
  participant,
  onRename,
  onRemove,
}: {
  participant: Participant;
  onRename: (value: string) => void;
  onRemove: () => void;
}) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(participant.name);

  const commit = () => {
    const trimmed = value.trim();
    if (trimmed && trimmed !== participant.name) {
      onRename(trimmed);
    }
    setEditing(false);
  };

  return (
    <ListItem
      disableGutters
      secondaryAction={
        editing ? (
          <>
            <IconButton size="small" onClick={commit} aria-label="Save name">
              <CheckIcon fontSize="small" />
            </IconButton>
            <IconButton
              size="small"
              onClick={() => {
                setValue(participant.name);
                setEditing(false);
              }}
              aria-label="Cancel"
            >
              <CloseIcon fontSize="small" />
            </IconButton>
          </>
        ) : (
          <>
            <IconButton size="small" onClick={() => setEditing(true)} aria-label="Rename">
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
        <TextField
          size="small"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && commit()}
          autoFocus
          sx={{ maxWidth: 260 }}
        />
      ) : (
        <Typography>{participant.name}</Typography>
      )}
    </ListItem>
  );
}
