import { Box, Chip, Paper, Stack, Typography } from '@mui/material';
import type { Bracket, BracketMatch, BracketSlot } from '../api/types';

export function BracketView({ bracket }: { bracket: Bracket }) {
  if (bracket.rounds.length === 0) {
    return (
      <Typography color="text.secondary">
        The bracket appears once the tournament starts.
      </Typography>
    );
  }

  return (
    <Box sx={{ overflowX: 'auto', pb: 1 }}>
      <Stack direction="row" spacing={3} sx={{ alignItems: 'stretch', minWidth: 'min-content' }}>
        {bracket.rounds.map((round) => (
          <Stack key={round.round} spacing={2} sx={{ minWidth: 220, justifyContent: 'space-around' }}>
            <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
              {round.title}
            </Typography>
            {round.matches.map((match) => (
              <MatchCard key={match.id} match={match} />
            ))}
          </Stack>
        ))}

        {bracket.thirdPlace && (
          <Stack spacing={2} sx={{ minWidth: 220, justifyContent: 'space-around' }}>
            <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
              Third Place
            </Typography>
            <MatchCard match={bracket.thirdPlace} />
          </Stack>
        )}
      </Stack>
    </Box>
  );
}

function MatchCard({ match }: { match: BracketMatch }) {
  return (
    <Paper variant="outlined" sx={{ overflow: 'hidden' }}>
      <SlotRow slot={match.participantA} winnerId={match.winnerId} />
      <Box sx={{ borderTop: '1px solid rgba(255,255,255,0.08)' }} />
      <SlotRow slot={match.participantB} winnerId={match.winnerId} />
      {match.status === 'Forfeit' && (
        <Box sx={{ px: 1, pb: 0.5 }}>
          <Chip size="small" color="warning" label="Forfeit" />
        </Box>
      )}
    </Paper>
  );
}

function SlotRow({ slot, winnerId }: { slot: BracketSlot | null; winnerId: string | null }) {
  const isWinner = slot != null && winnerId === slot.participantId;
  return (
    <Box
      sx={{
        px: 1.5,
        py: 1,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        bgcolor: isWinner ? 'rgba(63,185,80,0.15)' : 'transparent',
      }}
    >
      <Typography
        variant="body2"
        sx={{
          color: slot ? 'text.primary' : 'text.disabled',
          fontWeight: isWinner ? 700 : 400,
        }}
      >
        {slot ? slot.name : 'TBD'}
      </Typography>
      {isWinner && <Chip size="small" color="success" label="W" />}
    </Box>
  );
}
