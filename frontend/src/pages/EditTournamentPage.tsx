import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Box, Card, CardContent, CircularProgress, Stack, Typography } from '@mui/material';
import { getTournament, updateTournament } from '../api/tournaments';
import type { TournamentInput } from '../api/types';
import { TournamentForm } from '../components/TournamentForm';
import { extractErrorMessage } from '../api/client';

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
                defaultMatchFormat: tournament.defaultMatchFormat,
                thirdPlaceEnabled: tournament.thirdPlaceEnabled,
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
