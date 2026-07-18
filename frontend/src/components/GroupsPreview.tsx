import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Box, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import CasinoIcon from '@mui/icons-material/Casino';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import { drawGroups, listParticipants, startTournament } from '../api/tournaments';
import { groupLabel } from '../api/types';
import { extractErrorMessage } from '../api/client';
import { ConfirmDialog } from './ConfirmDialog';

/** Group Stage + Playoff pre-start flow: random group draw (redraw-able) then start. */
export function GroupsPreview({ tournamentId, groupCount }: { tournamentId: string; groupCount: number }) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [confirmStart, setConfirmStart] = useState(false);

  const { data: participants = [] } = useQuery({
    queryKey: ['participants', tournamentId],
    queryFn: () => listParticipants(tournamentId),
  });

  const drawn = participants.length > 0 && participants.every((p) => p.groupIndex !== null);

  const drawMutation = useMutation({
    mutationFn: () => drawGroups(tournamentId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['participants', tournamentId] }),
    onError: (err) => setError(extractErrorMessage(err)),
  });

  const startMutation = useMutation({
    mutationFn: () => startTournament(tournamentId),
    onSuccess: async () => {
      setConfirmStart(false);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['tournaments'] }),
        queryClient.invalidateQueries({ queryKey: ['tournaments', tournamentId] }),
        queryClient.invalidateQueries({ queryKey: ['bracket', tournamentId] }),
      ]);
    },
    onError: (err) => {
      setConfirmStart(false);
      setError(extractErrorMessage(err));
    },
  });

  if (participants.length < 2) {
    return null;
  }

  const groups = Array.from({ length: groupCount }, (_, g) =>
    participants.filter((p) => p.groupIndex === g).sort((a, b) => a.seed - b.seed),
  );

  return (
    <Card>
      <CardContent>
        <Typography variant="h6" gutterBottom>
          Group draw
        </Typography>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
            {error}
          </Alert>
        )}

        {!drawn ? (
          <Stack spacing={2} sx={{ alignItems: 'flex-start' }}>
            <Typography variant="body2" color="text.secondary">
              Randomly draw the {participants.length} participants into {groupCount} groups.
            </Typography>
            <Button
              variant="contained"
              startIcon={<CasinoIcon />}
              onClick={() => drawMutation.mutate()}
              disabled={drawMutation.isPending}
            >
              Draw groups
            </Button>
          </Stack>
        ) : (
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
              Each group plays a round robin. The top half of each group advances to the Winner Bracket and the bottom half
              to the Loser Bracket of the playoff.
            </Typography>

            <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' } }}>
              {groups.map((members, g) => (
                <Card key={g} variant="outlined">
                  <CardContent>
                    <Typography variant="subtitle2" gutterBottom>
                      {groupLabel(g)}
                    </Typography>
                    <Stack spacing={0.5}>
                      {members.map((participant) => (
                        <Typography key={participant.id} variant="body2">
                          {participant.name}
                        </Typography>
                      ))}
                    </Stack>
                  </CardContent>
                </Card>
              ))}
            </Box>

            <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1 }}>
              <Button startIcon={<CasinoIcon />} onClick={() => drawMutation.mutate()} disabled={drawMutation.isPending}>
                Redraw
              </Button>
              <Button
                variant="contained"
                color="success"
                startIcon={<PlayArrowIcon />}
                onClick={() => setConfirmStart(true)}
              >
                Start tournament
              </Button>
            </Stack>
          </Stack>
        )}

        <ConfirmDialog
          open={confirmStart}
          title="Start tournament"
          message="Starting locks the groups and creates the group-stage matches. Continue?"
          confirmLabel="Start"
          busy={startMutation.isPending}
          onCancel={() => setConfirmStart(false)}
          onConfirm={() => startMutation.mutate()}
        />
      </CardContent>
    </Card>
  );
}
