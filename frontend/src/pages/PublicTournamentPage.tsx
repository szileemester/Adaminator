import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Alert, Box, Card, CardContent, Chip, CircularProgress, Divider, Stack, Typography } from '@mui/material';
import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import { getPublicTournament } from '../api/tournaments';
import { formatParticipantName, matchFormatLabels, scoreTypeLabels, tournamentTypeLabels } from '../api/types';
import { StatusChip } from '../components/StatusChip';
import { BracketView } from '../components/BracketView';
import { extractErrorMessage } from '../api/client';

export function PublicTournamentPage() {
  const { token = '' } = useParams();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['public', token],
    queryFn: () => getPublicTournament(token),
  });

  return (
    <Box sx={{ minHeight: '100vh', p: { xs: 2, md: 4 } }}>
      <Stack spacing={3} sx={{ maxWidth: 1400, mx: 'auto' }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <EmojiEventsIcon color="primary" />
          <Typography variant="h5">Adaminator</Typography>
        </Stack>

        {isLoading && (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
            <CircularProgress />
          </Box>
        )}

        {isError && <Alert severity="error">{extractErrorMessage(error, 'Tournament not found.')}</Alert>}

        {data && (
          <Card>
            <CardContent>
              <Stack spacing={2}>
                <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
                  <Typography variant="h4">{data.name}</Typography>
                  <StatusChip status={data.status} />
                </Stack>
                <Stack spacing={1.5} divider={<Divider flexItem />}>
                  <PublicRow label="Date" value={data.date} />
                  <PublicRow label="Type" value={tournamentTypeLabels[data.type]} />
                  {data.type === 'GroupStagePlayoff' && <PublicRow label="Groups" value={String(data.groupCount)} />}
                  <PublicRow label="Default match format" value={matchFormatLabels[data.defaultMatchFormat]} />
                  <PublicRow label="Default score type" value={scoreTypeLabels[data.defaultScoreType]} />
                  {data.notes?.trim() && <PublicRow label="Notes" value={data.notes} />}
                </Stack>
              </Stack>
            </CardContent>
          </Card>
        )}

        {data && data.participants.length > 0 && (
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Participants ({data.participants.length})
              </Typography>
              <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                {data.participants.map((participant) => {
                  const label = formatParticipantName(participant.name, participant.emoji);
                  return (
                    <Chip
                      key={participant.id}
                      label={participant.hasBye ? `${label} · bye` : label}
                      variant="outlined"
                    />
                  );
                })}
              </Box>
            </CardContent>
          </Card>
        )}

        {data?.bracket && (data.bracket.winnerRounds.length > 0 || data.bracket.groups.length > 0) && (
          <Card>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Bracket
              </Typography>
              <BracketView bracket={data.bracket} />
            </CardContent>
          </Card>
        )}
      </Stack>
    </Box>
  );
}

function PublicRow({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" spacing={2}>
      <Typography variant="body2" color="text.secondary" sx={{ minWidth: 180 }}>
        {label}
      </Typography>
      <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
        {value}
      </Typography>
    </Stack>
  );
}
