import { useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  IconButton,
  Snackbar,
  Stack,
  Tooltip,
  Typography,
} from '@mui/material';
import type { SxProps, Theme } from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { deleteTournament, getBracket, getTournament } from '../api/tournaments';
import { matchFormatLabels, tournamentTypeLabels } from '../api/types';
import { StatusChip } from '../components/StatusChip';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ParticipantsSection } from '../components/ParticipantsSection';
import { BracketPreview } from '../components/BracketPreview';
import { BracketView } from '../components/BracketView';
import { extractErrorMessage } from '../api/client';

export function TournamentDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [copied, setCopied] = useState(false);

  const { data: tournament, isLoading, isError, error } = useQuery({
    queryKey: ['tournaments', id],
    queryFn: () => getTournament(id),
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteTournament(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tournaments'] });
      navigate('/');
    },
  });

  const isPlanned = tournament?.status === 'Planned';

  const { data: bracket } = useQuery({
    queryKey: ['bracket', id],
    queryFn: () => getBracket(id),
    enabled: Boolean(tournament) && !isPlanned,
  });

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (isError || !tournament) {
    return <Alert severity="error">{extractErrorMessage(error, 'Tournament not found.')}</Alert>;
  }

  const publicUrl = `${window.location.origin}/public/${tournament.publicToken}`;

  const copyPublicLink = async () => {
    await navigator.clipboard.writeText(publicUrl);
    setCopied(true);
  };

  return (
    <Stack spacing={3}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2 }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Typography variant="h4">{tournament.name}</Typography>
          <StatusChip status={tournament.status} />
        </Stack>
        <Stack direction="row" spacing={1}>
          {tournament.status === 'Planned' && (
            <Button
              component={RouterLink}
              to={`/tournaments/${tournament.id}/edit`}
              startIcon={<EditIcon />}
              variant="outlined"
            >
              Edit
            </Button>
          )}
          <Button color="error" variant="outlined" startIcon={<DeleteIcon />} onClick={() => setConfirmOpen(true)}>
            Delete
          </Button>
        </Stack>
      </Stack>

      <Card>
        <CardContent>
          <Typography variant="h6" gutterBottom>
            Overview
          </Typography>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, columnGap: 4, rowGap: 1.5 }}>
            <DetailRow label="Date" value={tournament.date} />
            <DetailRow label="Type" value={tournamentTypeLabels[tournament.type]} />
            <DetailRow label="Default match format" value={matchFormatLabels[tournament.defaultMatchFormat]} />
            <DetailRow label="Third place match" value={tournament.thirdPlaceEnabled ? 'Enabled' : 'Disabled'} />
            <DetailRow
              label="Notes"
              value={tournament.notes?.trim() ? tournament.notes : '—'}
              sx={{ gridColumn: { xs: '1', sm: '1 / -1' } }}
            />
          </Box>
        </CardContent>
      </Card>

      {tournament.status === 'Planned' ? (
        <>
          <ParticipantsSection tournamentId={tournament.id} tournamentType={tournament.type} />
          <BracketPreview tournamentId={tournament.id} tournamentType={tournament.type} />
        </>
      ) : (
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Bracket
            </Typography>
            {bracket ? (
              <BracketView bracket={bracket} tournamentId={tournament.id} />
            ) : (
              <CircularProgress size={24} />
            )}
          </CardContent>
        </Card>
      )}

      <Stack
        direction="row"
        spacing={1}
        sx={{ alignItems: 'center', flexWrap: 'wrap', color: 'text.secondary' }}
      >
        <Typography variant="caption">Public view:</Typography>
        <Typography variant="caption" sx={{ wordBreak: 'break-all' }}>
          {publicUrl}
        </Typography>
        <Tooltip title="Copy link">
          <IconButton size="small" onClick={copyPublicLink}>
            <ContentCopyIcon fontSize="inherit" />
          </IconButton>
        </Tooltip>
        <Tooltip title="Open">
          <IconButton size="small" component={RouterLink} to={`/public/${tournament.publicToken}`} target="_blank">
            <OpenInNewIcon fontSize="inherit" />
          </IconButton>
        </Tooltip>
      </Stack>

      <ConfirmDialog
        open={confirmOpen}
        title="Delete tournament"
        message={`Delete "${tournament.name}"? This action cannot be undone.`}
        confirmLabel="Delete"
        confirmColor="error"
        busy={deleteMutation.isPending}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={() => deleteMutation.mutate()}
      />

      <Snackbar
        open={copied}
        autoHideDuration={2000}
        onClose={() => setCopied(false)}
        message="Public link copied"
      />
    </Stack>
  );
}

function DetailRow({ label, value, sx }: { label: string; value: string; sx?: SxProps<Theme> }) {
  return (
    <Stack direction="row" spacing={2} sx={sx}>
      <Typography variant="body2" color="text.secondary" sx={{ minWidth: 180 }}>
        {label}
      </Typography>
      <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
        {value}
      </Typography>
    </Stack>
  );
}
