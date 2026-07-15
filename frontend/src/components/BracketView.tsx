import { useState } from 'react';
import { Box, Chip, Paper, Stack, Typography } from '@mui/material';
import type { Bracket, BracketMatch, BracketSlot } from '../api/types';
import { MatchResultDialog } from './MatchResultDialog';

export function BracketView({ bracket, tournamentId }: { bracket: Bracket; tournamentId?: string }) {
  const [selectedMatchId, setSelectedMatchId] = useState<string | null>(null);
  const selectedMatch = tournamentId && selectedMatchId ? findMatch(bracket, selectedMatchId) : null;

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
              <MatchCard key={match.id} match={match} onSelect={tournamentId ? setSelectedMatchId : undefined} />
            ))}
          </Stack>
        ))}

        {bracket.thirdPlace && (
          <Stack spacing={2} sx={{ minWidth: 220, justifyContent: 'space-around' }}>
            <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
              Third Place
            </Typography>
            <MatchCard match={bracket.thirdPlace} onSelect={tournamentId ? setSelectedMatchId : undefined} />
          </Stack>
        )}
      </Stack>

      {selectedMatch && tournamentId && (
        <MatchResultDialog
          key={selectedMatch.id}
          tournamentId={tournamentId}
          match={selectedMatch}
          onClose={() => setSelectedMatchId(null)}
        />
      )}
    </Box>
  );
}

function findMatch(bracket: Bracket, matchId: string): BracketMatch | null {
  for (const round of bracket.rounds) {
    const found = round.matches.find((m) => m.id === matchId);
    if (found) {
      return found;
    }
  }
  return bracket.thirdPlace?.id === matchId ? bracket.thirdPlace : null;
}

function MatchCard({ match, onSelect }: { match: BracketMatch; onSelect?: (matchId: string) => void }) {
  const actionable = Boolean(onSelect) && match.participantA != null && match.participantB != null;
  return (
    <Paper
      variant="outlined"
      sx={{ overflow: 'hidden', cursor: actionable ? 'pointer' : 'default' }}
      onClick={actionable ? () => onSelect!(match.id) : undefined}
    >
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
