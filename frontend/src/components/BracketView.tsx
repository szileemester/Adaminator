import { useMemo, useState } from 'react';
import {
  Box,
  Chip,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import type { Bracket, BracketMatch, BracketRound, BracketSlot, StandingRow } from '../api/types';
import { MatchResultDialog } from './MatchResultDialog';

const CARD_WIDTH = 220;
const CARD_HEIGHT = 76;
const ROUND_VGAP = 28;
const CONNECTOR_WIDTH = 40;
const CONNECTOR_COLOR = 'rgba(255,255,255,0.2)';

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
        <Stack spacing={4}>
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
                <Box sx={{ width: CARD_WIDTH }}>
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
      ) : bracket.type === 'RoundRobin' ? (
        <Stack spacing={3}>
          <Box sx={{ overflowX: 'auto' }}>
            <Stack direction="row" spacing={3} sx={{ alignItems: 'stretch', minWidth: 'min-content' }}>
              <RoundColumns rounds={bracket.winnerRounds} onSelect={onSelect} />
            </Stack>
          </Box>
          {bracket.standings.length > 0 && <StandingsTable standings={bracket.standings} />}
        </Stack>
      ) : (
        <BracketTree
          rounds={bracket.winnerRounds}
          onSelect={onSelect}
          extraMatch={bracket.thirdPlace ? { label: 'Third Place Match', match: bracket.thirdPlace } : null}
        />
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
        <BracketTree rounds={rounds} onSelect={onSelect} />
      </Box>
    </Stack>
  );
}

/** Maps a match index in a round of `fromCount` matches to the index it feeds into in a round of `toCount` matches. */
function targetIndex(fromCount: number, toCount: number, i: number): number {
  if (toCount <= 0) {
    return 0;
  }

  if (fromCount === toCount * 2) {
    return Math.floor(i / 2);
  }

  if (fromCount === toCount) {
    return i;
  }

  return Math.min(toCount - 1, Math.floor((i * toCount) / fromCount));
}

/**
 * Vertical center (px) of every match in every round, laid out so that a round's matches are
 * centered on the matches that feed into them - the classic bracket "elbow" shape. Round 1 is
 * evenly spaced; every later round's position is the average of the source matches mapped to it
 * via {@link targetIndex} (an exact halving for Single/Double Elimination's Winner Bracket; a
 * best-effort grouping for the Loser Bracket's non-halving consolidation/drop-in rounds).
 */
function computeTreeLayout(rounds: BracketRound[]): { positions: number[][]; totalHeight: number } {
  const positions: number[][] = [];
  let prev: number[] = [];

  rounds.forEach((round, ri) => {
    const count = round.matches.length;
    if (ri === 0) {
      prev = round.matches.map((_, i) => i * (CARD_HEIGHT + ROUND_VGAP) + CARD_HEIGHT / 2);
    } else {
      const sums = new Array(count).fill(0);
      const counts = new Array(count).fill(0);
      prev.forEach((y, i) => {
        const j = targetIndex(prev.length, count, i);
        sums[j] += y;
        counts[j] += 1;
      });
      prev = sums.map((sum, j) => (counts[j] > 0 ? sum / counts[j] : CARD_HEIGHT / 2));
    }

    positions.push(prev);
  });

  const firstRoundCount = rounds[0]?.matches.length ?? 0;
  const totalHeight = Math.max(firstRoundCount * (CARD_HEIGHT + ROUND_VGAP) - ROUND_VGAP, CARD_HEIGHT);
  return { positions, totalHeight };
}

function BracketTree({
  rounds,
  onSelect,
  extraMatch,
}: {
  rounds: BracketRound[];
  onSelect?: (matchId: string) => void;
  extraMatch?: { label: string; match: BracketMatch } | null;
}) {
  const { positions, totalHeight } = useMemo(() => computeTreeLayout(rounds), [rounds]);

  const lastRoundY = positions[positions.length - 1]?.[0] ?? 0;
  const extraTop = lastRoundY + CARD_HEIGHT / 2 + 56;
  const containerHeight = extraMatch ? extraTop + CARD_HEIGHT + 16 : totalHeight;

  return (
    <Stack spacing={2} sx={{ minWidth: 'min-content' }}>
      <Stack direction="row">
        {rounds.map((round, ri) => (
          <Box
            key={round.round}
            sx={{ width: CARD_WIDTH, mr: ri < rounds.length - 1 ? `${CONNECTOR_WIDTH}px` : 0 }}
          >
            <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
              {round.title}
            </Typography>
          </Box>
        ))}
      </Stack>

      <Stack direction="row" sx={{ alignItems: 'flex-start' }}>
        {rounds.map((round, ri) => (
          <Stack key={round.round} direction="row" sx={{ alignItems: 'flex-start' }}>
            <Box sx={{ position: 'relative', width: CARD_WIDTH, height: containerHeight }}>
              {round.matches.map((match, i) => (
                <Box
                  key={match.id}
                  sx={{ position: 'absolute', top: positions[ri][i] - CARD_HEIGHT / 2, left: 0, width: CARD_WIDTH }}
                >
                  <MatchCard match={match} onSelect={onSelect} />
                </Box>
              ))}
              {ri === rounds.length - 1 && extraMatch && (
                <>
                  <Box sx={{ position: 'absolute', top: extraTop - CARD_HEIGHT / 2 - 32, left: 0, width: CARD_WIDTH }}>
                    <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
                      {extraMatch.label}
                    </Typography>
                  </Box>
                  <Box sx={{ position: 'absolute', top: extraTop - CARD_HEIGHT / 2, left: 0, width: CARD_WIDTH }}>
                    <MatchCard match={extraMatch.match} onSelect={onSelect} />
                  </Box>
                </>
              )}
            </Box>

            {ri < rounds.length - 1 && (
              <Connector
                height={containerHeight}
                from={positions[ri]}
                to={positions[ri + 1]}
                fromCount={round.matches.length}
                toCount={rounds[ri + 1].matches.length}
              />
            )}
          </Stack>
        ))}
      </Stack>
    </Stack>
  );
}

function Connector({
  height,
  from,
  to,
  fromCount,
  toCount,
}: {
  height: number;
  from: number[];
  to: number[];
  fromCount: number;
  toCount: number;
}) {
  const midX = CONNECTOR_WIDTH / 2;

  return (
    <Box component="svg" width={CONNECTOR_WIDTH} height={height} sx={{ display: 'block', flexShrink: 0 }}>
      {from.map((y, i) => {
        const targetY = to[targetIndex(fromCount, toCount, i)];
        return (
          <path
            key={i}
            d={`M0 ${y} H${midX} V${targetY} H${CONNECTOR_WIDTH}`}
            fill="none"
            stroke={CONNECTOR_COLOR}
            strokeWidth={1.5}
          />
        );
      })}
    </Box>
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
      sx={{
        position: 'relative',
        overflow: 'hidden',
        height: CARD_HEIGHT,
        display: 'flex',
        flexDirection: 'column',
        cursor: actionable ? 'pointer' : 'default',
      }}
      onClick={actionable ? () => onSelect!(match.id) : undefined}
    >
      <SlotRow slot={match.participantA} winnerId={match.winnerId} />
      <Box sx={{ borderTop: '1px solid rgba(255,255,255,0.08)' }} />
      <SlotRow slot={match.participantB} winnerId={match.winnerId} />
      {match.status === 'Forfeit' && (
        <Chip
          size="small"
          color="warning"
          label="FF"
          sx={{ position: 'absolute', top: 4, right: 4, height: 18, fontSize: '0.65rem', '& .MuiChip-label': { px: 0.75 } }}
        />
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
        flex: 1,
        minHeight: 0,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        overflow: 'hidden',
        gap: 1,
        bgcolor: isWinner ? 'rgba(63,185,80,0.15)' : 'transparent',
      }}
    >
      <Typography
        variant="body2"
        noWrap
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

function StandingsTable({ standings }: { standings: StandingRow[] }) {
  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2" color="text.secondary">
        Standings
      </Typography>
      <TableContainer component={Paper} variant="outlined" sx={{ maxWidth: 480 }}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>#</TableCell>
              <TableCell>Name</TableCell>
              <TableCell align="right">Played</TableCell>
              <TableCell align="right">Wins</TableCell>
              <TableCell align="right">Losses</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {standings.map((row) => (
              <TableRow key={row.participantId}>
                <TableCell>{row.rank}</TableCell>
                <TableCell>{row.name}</TableCell>
                <TableCell align="right">{row.played}</TableCell>
                <TableCell align="right">{row.wins}</TableCell>
                <TableCell align="right">{row.losses}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Stack>
  );
}
