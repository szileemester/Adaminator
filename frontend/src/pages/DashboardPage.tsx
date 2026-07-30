import { useMemo, useState } from 'react';
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
  InputAdornment,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import SearchIcon from '@mui/icons-material/Search';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import { listTournaments } from '../api/tournaments';
import type { TournamentStatus, TournamentSummary } from '../api/types';
import { tournamentTypeLabels } from '../api/types';
import { StatusChip } from '../components/StatusChip';
import { breakAnywhereSx } from '../theme';
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
  const [search, setSearch] = useState('');
  const [showFinished, setShowFinished] = useState(false);

  const query = search.trim().toLowerCase();
  const filtered = useMemo(
    () => (query ? data?.filter((t) => t.name.toLowerCase().includes(query)) : data),
    [data, query],
  );

  return (
    <Stack spacing={4}>
      {/*
        Stacked on a phone: side by side, the heading and a button whose label can't wrap add up to
        more than a 375px viewport, so they collide instead of sharing the row.
      */}
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={2}
        sx={{ justifyContent: 'space-between', alignItems: { xs: 'flex-start', sm: 'center' } }}
      >
        <Typography variant="h4">Tournaments</Typography>
        <Button
          component={RouterLink}
          to="/tournaments/new"
          variant="contained"
          startIcon={<AddIcon />}
          sx={{ flexShrink: 0 }}
        >
          Create tournament
        </Button>
      </Stack>

      {data && data.length > 0 && (
        <TextField
          size="small"
          placeholder="Search tournaments"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          sx={{ maxWidth: 360 }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
        />
      )}

      {isLoading && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
          <CircularProgress />
        </Box>
      )}

      {isError && <Alert severity="error">{extractErrorMessage(error)}</Alert>}

      {data && data.length === 0 && (
        <Typography color="text.secondary">No tournaments yet. Create your first one.</Typography>
      )}

      {filtered && filtered.length === 0 && data && data.length > 0 && (
        <Typography color="text.secondary">No tournaments match "{search.trim()}".</Typography>
      )}

      {filtered &&
        sections.map((section) => {
          const items = filtered.filter((t) => t.status === section.status);
          if (items.length === 0) {
            return null;
          }
          // Finished tournaments accumulate over time and are rarely revisited, so they start
          // collapsed - unless a search is narrowing the list, in which case hiding a match would
          // be more surprising than showing it.
          const isFinished = section.status === 'Finished';
          const collapsed = isFinished && !showFinished && query === '';
          return (
            <Stack key={section.status} spacing={2}>
              {isFinished ? (
                <Button
                  onClick={() => setShowFinished((prev) => !prev)}
                  startIcon={collapsed ? <ExpandMoreIcon /> : <ExpandLessIcon />}
                  color="inherit"
                  sx={{ alignSelf: 'flex-start', color: 'text.secondary' }}
                >
                  {section.title} ({items.length})
                </Button>
              ) : (
                <Typography variant="h6" color="text.secondary">
                  {section.title}
                </Typography>
              )}
              {!collapsed && (
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
              )}
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
              {/* Not TournamentTitle itself - that is a page heading - but the same name, same problem. */}
              <Typography variant="h6" sx={{ pr: 1, ...breakAnywhereSx }}>
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
