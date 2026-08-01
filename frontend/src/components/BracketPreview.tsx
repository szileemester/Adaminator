import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Checkbox,
  Chip,
  IconButton,
  List,
  ListItem,
  Stack,
  Tooltip,
  Typography,
} from '@mui/material';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import CasinoIcon from '@mui/icons-material/Casino';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import { generateBracket, listParticipants, startTournament, updateBracket } from '../api/tournaments';
import type { Participant, TournamentType } from '../api/types';
import { MIN_PARTICIPANTS, requiredByes } from '../api/types';
import { extractErrorMessage } from '../api/client';
import { ConfirmDialog } from './ConfirmDialog';
import { ParticipantLabel } from './ParticipantLabel';
import { RosterUnsavedNotice } from './RosterUnsavedNotice';

export function BracketPreview({
  tournamentId,
  tournamentType,
  rosterUnsaved = false,
}: {
  tournamentId: string;
  tournamentType: TournamentType;
  /** True while the participants section holds roster edits that have not been saved. */
  rosterUnsaved?: boolean;
}) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [order, setOrder] = useState<Participant[]>([]);
  const [byes, setByes] = useState<Set<string>>(new Set());
  const [dirty, setDirty] = useState(false);
  const [confirmStart, setConfirmStart] = useState(false);

  const { data: participants = [] } = useQuery({
    queryKey: ['participants', tournamentId],
    queryFn: () => listParticipants(tournamentId),
  });

  const seeded = participants.length >= 2 && participants.every((p) => p.seed > 0);
  const required = requiredByes(participants.length, tournamentType);
  const isRoundRobin = tournamentType === 'RoundRobin';
  // Byes only exist when the roster has to be padded to fill the bracket. A Round Robin or a Group
  // Stage + Playoff never pads, and neither does a roster that is already a power of two - in all of
  // those cases exactly zero byes may be chosen, so offering the control at all is a dead end.
  const showsByes = required > 0;

  // What this preview is actually for, decided once. Only a Swiss group stage reaches this component -
  // round-robin groups get GroupsPreview instead.
  const shape = isRoundRobin
    ? 'roundRobin'
    : tournamentType === 'GroupStagePlayoff'
      ? 'swissPool'
      : showsByes
        ? 'bracketWithByes'
        : 'exactBracket';
  const copy = {
    roundRobin: {
      generate: 'Generate a schedule order for the round robin.',
      reorder: 'Reorder the schedule order. Every participant plays every other participant once.',
    },
    swissPool: {
      generate: 'Generate a starting order for the Swiss pool.',
      reorder:
        'Reorder the pool. Round 1 pairs adjacent entries; every round after that pairs on results. An odd roster sits one player out per round automatically.',
    },
    bracketWithByes: {
      generate: 'Generate a bracket to seed participants randomly and choose bye recipients.',
      reorder: `Reorder seeds and select exactly ${required} bye recipient${required === 1 ? '' : 's'}. Bye recipients skip the first round.`,
    },
    exactBracket: {
      generate: 'Generate a bracket to seed participants randomly.',
      reorder: 'Reorder seeds. The roster fills the bracket exactly, so nobody needs a bye.',
    },
  }[shape];

  // Mirror server state into local editable state whenever there are no unsaved edits.
  useEffect(() => {
    if (!dirty && seeded) {
      setOrder([...participants].sort((a, b) => a.seed - b.seed));
      setByes(new Set(participants.filter((p) => p.hasBye).map((p) => p.id)));
    }
  }, [participants, seeded, dirty]);

  const generateMutation = useMutation({
    mutationFn: () => generateBracket(tournamentId),
    onSuccess: async () => {
      setDirty(false);
      await queryClient.invalidateQueries({ queryKey: ['participants', tournamentId] });
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const saveMutation = useMutation({
    mutationFn: () => updateBracket(tournamentId, order.map((p) => p.id), [...byes]),
    onSuccess: async () => {
      setDirty(false);
      await queryClient.invalidateQueries({ queryKey: ['participants', tournamentId] });
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const startMutation = useMutation({
    mutationFn: () => startTournament(tournamentId),
    onSuccess: async () => {
      setConfirmStart(false);
      // ['tournaments'] alone would do it - it prefix-matches ['tournaments', id] - but the detail
      // query is named explicitly so the intent survives any later change to the list key.
      await queryClient.invalidateQueries({ queryKey: ['tournaments'] });
      await queryClient.invalidateQueries({ queryKey: ['tournaments', tournamentId] });
      await queryClient.invalidateQueries({ queryKey: ['bracket', tournamentId] });
    },
    onError: (err) => {
      setConfirmStart(false);
      setError(extractErrorMessage(err));
    },
  });

  const move = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= order.length) return;
    const next = [...order];
    [next[index], next[target]] = [next[target], next[index]];
    setOrder(next);
    setDirty(true);
  };

  const toggleBye = (id: string) => {
    const next = new Set(byes);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    setByes(next);
    setDirty(true);
  };

  const byesValid = byes.size === required;
  // Seeding an unsaved roster would place participants who are about to be renamed, added or removed.
  const canStart = seeded && !dirty && byesValid && !rosterUnsaved;
  const canSeed = !generateMutation.isPending && !rosterUnsaved;

  if (participants.length < MIN_PARTICIPANTS) {
    return null;
  }

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={2} sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="h6">Bracket preview</Typography>
          {seeded && showsByes && (
            <Chip
              size="small"
              color={byesValid ? 'success' : 'warning'}
              label={`${byes.size} / ${required} byes`}
            />
          )}
        </Stack>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        {!seeded ? (
          <Stack spacing={2} sx={{ alignItems: 'flex-start' }}>
            <Typography variant="body2" color="text.secondary">
              {copy.generate}
            </Typography>
            <Button
              variant="contained"
              startIcon={<CasinoIcon />}
              onClick={() => generateMutation.mutate()}
              disabled={!canSeed}
            >
              Generate bracket
            </Button>
            {rosterUnsaved && <RosterUnsavedNotice />}
          </Stack>
        ) : (
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
              {copy.reorder}
            </Typography>

            <List dense>
              {order.map((participant, index) => (
                <ListItem
                  key={participant.id}
                  disableGutters
                  secondaryAction={
                    <Stack direction="row" sx={{ alignItems: 'center' }}>
                      {showsByes && (
                        <Tooltip title="Give a first-round bye">
                          <Checkbox
                            edge="end"
                            checked={byes.has(participant.id)}
                            onChange={() => toggleBye(participant.id)}
                          />
                        </Tooltip>
                      )}
                      <IconButton size="small" onClick={() => move(index, -1)} disabled={index === 0} aria-label="Move up">
                        <ArrowUpwardIcon fontSize="small" />
                      </IconButton>
                      <IconButton
                        size="small"
                        onClick={() => move(index, 1)}
                        disabled={index === order.length - 1}
                        aria-label="Move down"
                      >
                        <ArrowDownwardIcon fontSize="small" />
                      </IconButton>
                    </Stack>
                  }
                >
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Chip size="small" variant="outlined" label={index + 1} />
                    <ParticipantLabel name={participant.name} emoji={participant.emoji} />
                    {byes.has(participant.id) && <Chip size="small" color="secondary" label="BYE" />}
                  </Box>
                </ListItem>
              ))}
            </List>

            <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1 }}>
              <Button startIcon={<CasinoIcon />} onClick={() => generateMutation.mutate()} disabled={!canSeed}>
                Regenerate
              </Button>
              <Button
                variant="outlined"
                onClick={() => saveMutation.mutate()}
                disabled={!dirty || !byesValid || saveMutation.isPending}
              >
                Save preview
              </Button>
              <Button
                variant="contained"
                color="success"
                startIcon={<PlayArrowIcon />}
                onClick={() => setConfirmStart(true)}
                disabled={!canStart}
              >
                Start tournament
              </Button>
            </Stack>

            {rosterUnsaved && <RosterUnsavedNotice />}
            {dirty && (
              <Typography variant="body2" color="warning.main">
                You have unsaved changes. Save the preview before starting.
              </Typography>
            )}
            {showsByes && !byesValid && (
              <Typography variant="body2" color="warning.main">
                Select exactly {required} bye recipient{required === 1 ? '' : 's'}.
              </Typography>
            )}
          </Stack>
        )}

        <ConfirmDialog
          open={confirmStart}
          title="Start tournament"
          message="Starting locks the participants and bracket, and creates the matches. Continue?"
          confirmLabel="Start"
          busy={startMutation.isPending}
          onCancel={() => setConfirmStart(false)}
          onConfirm={() => startMutation.mutate()}
        />
      </CardContent>
    </Card>
  );
}
