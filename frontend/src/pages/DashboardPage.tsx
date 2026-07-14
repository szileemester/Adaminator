import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router-dom';
import {
  Alert,
  Box,
  Button,
  Card,
  CardActionArea,
  CardContent,
  CircularProgress,
  Stack,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import { listTournaments } from '../api/tournaments';
import type { TournamentStatus, TournamentSummary } from '../api/types';
import { tournamentTypeLabels } from '../api/types';
import { StatusChip } from '../components/StatusChip';
import { extractErrorMessage } from '../api/client';

const sections: { status: TournamentStatus; title: string }[] = [
  { status: 'Running', title: 'Running' },
  { status: 'Planned', title: 'Planned' },
  { status: 'Finished', title: 'Finished' },
];

export function DashboardPage() {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['tournaments'],
    queryFn: listTournaments,
  });

  return (
    <Stack spacing={4}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography variant="h4">Tournaments</Typography>
        <Button component={RouterLink} to="/tournaments/new" variant="contained" startIcon={<AddIcon />}>
          Create tournament
        </Button>
      </Stack>

      {isLoading && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
          <CircularProgress />
        </Box>
      )}

      {isError && <Alert severity="error">{extractErrorMessage(error)}</Alert>}

      {data && data.length === 0 && (
        <Typography color="text.secondary">No tournaments yet. Create your first one.</Typography>
      )}

      {data &&
        sections.map((section) => {
          const items = data.filter((t) => t.status === section.status);
          if (items.length === 0) {
            return null;
          }
          return (
            <Stack key={section.status} spacing={2}>
              <Typography variant="h6" color="text.secondary">
                {section.title}
              </Typography>
              <Box
                sx={{
                  display: 'grid',
                  gap: 2,
                  gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', md: '1fr 1fr 1fr' },
                }}
              >
                {items.map((tournament) => (
                  <TournamentCard key={tournament.id} tournament={tournament} />
                ))}
              </Box>
            </Stack>
          );
        })}
    </Stack>
  );
}

function TournamentCard({ tournament }: { tournament: TournamentSummary }) {
  return (
    <Card>
      <CardActionArea component={RouterLink} to={`/tournaments/${tournament.id}`}>
        <CardContent>
          <Stack spacing={1}>
            <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'flex-start' }}>
              <Typography variant="h6" sx={{ pr: 1 }}>
                {tournament.name}
              </Typography>
              <StatusChip status={tournament.status} />
            </Stack>
            <Typography variant="body2" color="text.secondary">
              {tournament.date}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {tournamentTypeLabels[tournament.type]}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {tournament.participantCount} participant{tournament.participantCount === 1 ? '' : 's'}
            </Typography>
          </Stack>
        </CardContent>
      </CardActionArea>
    </Card>
  );
}
