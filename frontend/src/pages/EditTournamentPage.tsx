import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Box, Card, CardContent, CircularProgress, Stack, Typography } from '@mui/material';
import { getTournament, updateTournament } from '../api/tournaments';
import type { MatchFormat, TournamentInput } from '../api/types';
import { allowsDraw } from '../api/types';
import { TournamentForm } from '../components/TournamentForm';
import { extractErrorMessage } from '../api/client';

/** Narrows a possibly-draw-capable format to a decisive one - these four fields are always decisive in practice (the tournament's own invariant), but the shared MatchFormat type still allows Bo2. */
const toDecisive = (format: MatchFormat): 'Bo1' | 'Bo3' | 'Bo5' | 'Bo7' => (allowsDraw(format) ? 'Bo3' : (format as 'Bo1' | 'Bo3' | 'Bo5' | 'Bo7'));

export function EditTournamentPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const { data: tournament, isLoading, isError, error: loadError } = useQuery({
    queryKey: ['tournaments', id],
    queryFn: () => getTournament(id),
  });

  const mutation = useMutation({
    mutationFn: (input: TournamentInput) => updateTournament(id, input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tournaments'] });
      navigate(`/tournaments/${id}`);
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (isError || !tournament) {
    return <Alert severity="error">{extractErrorMessage(loadError, 'Tournament not found.')}</Alert>;
  }

  if (tournament.status !== 'Planned') {
    return <Alert severity="warning">Only planned tournaments can be edited.</Alert>;
  }

  return (
    <Stack spacing={3}>
      <Typography variant="h4">Edit tournament</Typography>
      <Card>
        <CardContent>
          <Stack spacing={3}>
            {error && <Alert severity="error">{error}</Alert>}
            <TournamentForm
              submitLabel="Save changes"
              submitting={mutation.isPending}
              initialValues={{
                name: tournament.name,
                date: tournament.date,
                notes: tournament.notes ?? '',
                type: tournament.type,
                defaultMatchFormat: toDecisive(tournament.defaultMatchFormat),
                thirdPlaceEnabled: tournament.thirdPlaceEnabled,
                defaultScoreType: tournament.defaultScoreType,
                groupCount: tournament.groupCount || 2,
                tiebreakerPolicy: tournament.tiebreakerPolicy,
                groupStageMatchFormat: tournament.groupStageMatchFormat,
                upperBracketFormat: toDecisive(tournament.upperBracketFormat),
                lowerBracketFormat: toDecisive(tournament.lowerBracketFormat),
                grandFinalFormat: toDecisive(tournament.grandFinalFormat),
              }}
              onSubmit={(values) => {
                setError(null);
                mutation.mutate(values);
              }}
              onCancel={() => navigate(`/tournaments/${id}`)}
            />
          </Stack>
        </CardContent>
      </Card>
    </Stack>
  );
}
