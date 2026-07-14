import { Chip } from '@mui/material';
import type { TournamentStatus } from '../api/types';
import { tournamentStatusLabels } from '../api/types';

const colorByStatus: Record<TournamentStatus, 'success' | 'warning' | 'default'> = {
  Running: 'success',
  Planned: 'warning',
  Finished: 'default',
};

export function StatusChip({ status }: { status: TournamentStatus }) {
  return (
    <Chip
      size="small"
      color={colorByStatus[status]}
      label={tournamentStatusLabels[status]}
      variant={status === 'Finished' ? 'outlined' : 'filled'}
    />
  );
}
