import { useState } from 'react';
import { Box, Chip, Paper, Stack, Typography } from '@mui/material';
import type { Bracket, BracketMatch, BracketRound, BracketSlot } from '../api/types';
import { MatchResultDialog } from './MatchResultDialog';

export function BracketView({ bracket, tournamentId }: { bracket: Bracket; tournamentId?: string }) {
  const [selectedMatchId, setSelectedMatchId] = useState<string | null>(null);
  const selectedMatch = tournamentId && selectedMatchId ? findMatch(bracket, selectedMatchId) : null;
  const onSelect = tournamentId ? setSelectedMatchId : undefined;

  if (bracket.winnerRounds.length === 0) {
    return (
      <Typography color="text.secondary">
        The bracket appears once the tournament starts.
      </Typography>
    );
  }

  return (
    <Box sx={{ overflowX: 'auto', pb: 1 }}>
      {bracket.type === 'DoubleElimination' ? (
        <Stack spacing={3}>
          <BracketSection label="Winner Bracket" rounds={bracket.winnerRounds} onSelect={onSelect} />
          {bracket.loserRounds.length > 0 && (
            <BracketSection label="Loser Bracket" rounds={bracket.loserRounds} onSelect={onSelect} />
          )}
          {bracket.grandFinal && (
            <Stack spacing={1}>
              <Typography variant="subtitle2" color="text.secondary">
                Grand Final
              </Typography>
              <Stack direction="row" spacing={3} sx={{ alignItems: 'flex-start' }}>
                <Box sx={{ minWidth: 220 }}>
                  <MatchCard match={bracket.grandFinal} onSelect={onSelect} />
                </Box>
                {bracket.thirdPlacePodium && (
                  <Chip
                    variant="outlined"
                    label={`3rd place: ${bracket.thirdPlacePodium.name}`}
                    sx={{ alignSelf: 'center' }}
                  />
                )}
              </Stack>
            </Stack>
          )}
        </Stack>
      ) : (
        <Stack direction="row" spacing={3} sx={{ alignItems: 'stretch', minWidth: 'min-content' }}>
          <RoundColumns rounds={bracket.winnerRounds} onSelect={onSelect} />

          {bracket.thirdPlace && (
            <Stack spacing={2} sx={{ minWidth: 220, justifyContent: 'space-around' }}>
              <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
                Third Place
              </Typography>
              <MatchCard match={bracket.thirdPlace} onSelect={onSelect} />
            </Stack>
          )}
        </Stack>
      )}

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

function BracketSection({
  label,
  rounds,
  onSelect,
}: {
  label: string;
  rounds: BracketRound[];
  onSelect?: (matchId: string) => void;
}) {
  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2" color="text.secondary">
        {label}
      </Typography>
      <Box sx={{ overflowX: 'auto' }}>
        <Stack direction="row" spacing={3} sx={{ alignItems: 'stretch', minWidth: 'min-content' }}>
          <RoundColumns rounds={rounds} onSelect={onSelect} />
        </Stack>
      </Box>
    </Stack>
  );
}

function RoundColumns({ rounds, onSelect }: { rounds: BracketRound[]; onSelect?: (matchId: string) => void }) {
  return (
    <>
      {rounds.map((round) => (
        <Stack key={round.round} spacing={2} sx={{ minWidth: 220, justifyContent: 'space-around' }}>
          <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
            {round.title}
          </Typography>
          {round.matches.map((match) => (
            <MatchCard key={match.id} match={match} onSelect={onSelect} />
          ))}
        </Stack>
      ))}
    </>
  );
}

function findMatch(bracket: Bracket, matchId: string): BracketMatch | null {
  for (const round of [...bracket.winnerRounds, ...bracket.loserRounds]) {
    const found = round.matches.find((m) => m.id === matchId);
    if (found) {
      return found;
    }
  }
  if (bracket.grandFinal?.id === matchId) {
    return bracket.grandFinal;
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
