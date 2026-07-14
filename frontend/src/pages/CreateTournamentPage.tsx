import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Alert, Card, CardContent, Stack, Typography } from '@mui/material';
import { createTournament } from '../api/tournaments';
import type { TournamentInput } from '../api/types';
import { TournamentForm } from '../components/TournamentForm';
import { extractErrorMessage } from '../api/client';

export function CreateTournamentPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: TournamentInput) => createTournament(input),
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: ['tournaments'] });
      navigate(`/tournaments/${created.id}`);
    },
    onError: (err) => setError(extractErrorMessage(err)),
  });

  return (
    <Stack spacing={3}>
      <Typography variant="h4">Create tournament</Typography>
      <Card>
        <CardContent>
          <Stack spacing={3}>
            {error && <Alert severity="error">{error}</Alert>}
            <TournamentForm
              submitLabel="Create"
              submitting={mutation.isPending}
              onSubmit={(values) => {
                setError(null);
                mutation.mutate(values);
              }}
              onCancel={() => navigate('/')}
            />
          </Stack>
        </CardContent>
      </Card>
    </Stack>
  );
}
