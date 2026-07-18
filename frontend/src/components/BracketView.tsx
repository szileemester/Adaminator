import { useMemo, useState } from 'react';
import {
  Box,
  Chip,
  Paper,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tabs,
  Typography,
} from '@mui/material';
import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import type { Bracket, BracketMatch, BracketRound, BracketSlot, PlacementGroup, StandingRow } from '../api/types';
import { groupLabel } from '../api/types';
import { MatchResultDialog } from './MatchResultDialog';

const RANK_COLORS: Record<number, { trophy: string; bg: string }> = {
  1: { trophy: '#FFD700', bg: 'rgba(255,215,0,0.15)' },
  2: { trophy: '#C0C0C0', bg: 'rgba(192,192,192,0.15)' },
  3: { trophy: '#CD7F32', bg: 'rgba(205,127,50,0.15)' },
};

function formatOrdinal(n: number): string {
  const mod100 = n % 100;
  if (mod100 >= 11 && mod100 <= 13) {
    return `${n}th`;
  }

  switch (n % 10) {
    case 1:
      return `${n}st`;
    case 2:
      return `${n}nd`;
    case 3:
      return `${n}rd`;
    default:
      return `${n}th`;
  }
}

function formatRank(start: number, end: number): string {
  return start === end ? formatOrdinal(start) : `${formatOrdinal(start)}-${formatOrdinal(end)}`;
}

function PlaceCell({ rankStart, rankEnd }: { rankStart: number; rankEnd: number }) {
  const colors = RANK_COLORS[rankStart];
  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
      {colors && <EmojiEventsIcon fontSize="small" sx={{ color: colors.trophy }} />}
      <Typography variant="body2">{formatRank(rankStart, rankEnd)}</Typography>
    </Stack>
  );
}

const CARD_WIDTH = 220;
const CARD_HEIGHT = 76;
const ROUND_VGAP = 28;
const CONNECTOR_WIDTH = 40;
const CONNECTOR_COLOR = 'rgba(255,255,255,0.2)';
const EXTRA_MATCH_GAP = 32; // space between the last round's card and the extra match's label
const EXTRA_LABEL_HEIGHT = 28; // reserved height for the extra match's label row
const SECTION_HEADER_HEIGHT = 28; // reserved height for a playoff grid round-header row
const SECTION_GAP = 48; // vertical space between the winner and loser bracket regions

export function BracketView({ bracket, tournamentId }: { bracket: Bracket; tournamentId?: string }) {
  const [selectedMatchId, setSelectedMatchId] = useState<string | null>(null);
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  // Defaults to the Leaderboard once the tournament is Finished, otherwise the Bracket - only at
  // mount, so finishing the tournament while this is open doesn't yank the admin's current tab.
  const [tab, setTab] = useState(() => (bracket.status === 'Finished' ? 1 : 0));
  const selectedMatch = tournamentId && selectedMatchId ? findMatch(bracket, selectedMatchId) : null;
  const onSelect = tournamentId ? setSelectedMatchId : undefined;

  const dialog =
    selectedMatch && tournamentId ? (
      <MatchResultDialog
        key={selectedMatch.id}
        tournamentId={tournamentId}
        match={selectedMatch}
        onClose={() => setSelectedMatchId(null)}
      />
    ) : null;

  if (bracket.type === 'GroupStagePlayoff') {
    return (
      <>
        <GroupStagePlayoffView bracket={bracket} onSelect={onSelect} hoveredId={hoveredId} onHover={setHoveredId} />
        {dialog}
      </>
    );
  }

  if (bracket.winnerRounds.length === 0) {
    return (
      <Typography color="text.secondary">
        The bracket appears once the tournament starts.
      </Typography>
    );
  }

  const isRoundRobin = bracket.type === 'RoundRobin';

  return (
    <Box sx={{ pb: 1 }}>
      <Tabs value={tab} onChange={(_, value) => setTab(value)} sx={{ mb: 2, minHeight: 36 }}>
        <Tab label={isRoundRobin ? 'Schedule' : 'Bracket'} sx={{ minHeight: 36, py: 0 }} />
        <Tab label="Leaderboard" sx={{ minHeight: 36, py: 0 }} />
      </Tabs>

      {tab === 0 ? (
        <Box sx={{ overflowX: 'auto' }}>
          {bracket.type === 'DoubleElimination' ? (
            <PlayoffGrid bracket={bracket} onSelect={onSelect} hoveredId={hoveredId} onHover={setHoveredId} />
          ) : isRoundRobin ? (
            <Stack direction="row" spacing={3} sx={{ alignItems: 'stretch', minWidth: 'min-content' }}>
              <RoundColumns rounds={bracket.winnerRounds} onSelect={onSelect} hoveredId={hoveredId} onHover={setHoveredId} />
            </Stack>
          ) : (
            <BracketTree
              rounds={bracket.winnerRounds}
              onSelect={onSelect}
              hoveredId={hoveredId}
              onHover={setHoveredId}
              extraMatch={bracket.thirdPlace ? { label: 'Third Place Match', match: bracket.thirdPlace } : null}
            />
          )}
        </Box>
      ) : isRoundRobin ? (
        <StandingsTable standings={bracket.standings} hoveredId={hoveredId} onHover={setHoveredId} />
      ) : (
        <PlacementsList placements={bracket.placements} hoveredId={hoveredId} onHover={setHoveredId} />
      )}

      {dialog}
    </Box>
  );
}

/**
 * Column a winner-bracket round occupies in the shared playoff grid. A loser round L sits in column
 * L; a winner round sits in the column of the loser round its losers drop into (rounds 1-2 map
 * straight across, later ones to 2r-2, mirroring the backend topology). That is what makes the
 * winner bracket skip columns, so its Final lines up with the Loser Bracket Final and the two feed a
 * Grand Final in the column after them.
 */
function winnerColumn(round: number): number {
  return round <= 2 ? round : 2 * round - 2;
}

function elbowPath(fromX: number, fromY: number, toX: number, toY: number): string {
  const midX = (fromX + toX) / 2;
  return `M${fromX} ${fromY} H${midX} V${toY} H${toX}`;
}

/**
 * The whole playoff on one shared column grid: the winner bracket across the top, the loser bracket
 * below it, and the Grand Final alone in the rightmost column, centred between the two finals that
 * feed it. Cards and connectors are absolutely positioned so a winner-bracket round can skip a
 * column (drawing one long connector across it) rather than each bracket being its own independent
 * strip.
 */
function PlayoffGrid({
  bracket,
  onSelect,
  hoveredId,
  onHover,
}: {
  bracket: Bracket;
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
  const winnerRounds = bracket.winnerRounds;
  const loserRounds = bracket.loserRounds;
  const grandFinal = bracket.grandFinal;

  const winnerLayout = useMemo(() => computeTreeLayout(winnerRounds, true), [winnerRounds]);
  const loserLayout = useMemo(() => computeTreeLayout(loserRounds, false), [loserRounds]);

  const winnerTop = SECTION_HEADER_HEIGHT;
  const winnerBottom = winnerTop + (winnerRounds.length > 0 ? winnerLayout.totalHeight : 0);
  const loserHeaderTop = winnerBottom + SECTION_GAP;
  const loserTop = loserHeaderTop + SECTION_HEADER_HEIGHT;
  const height = loserRounds.length > 0 ? loserTop + loserLayout.totalHeight : winnerBottom;

  const columnX = (column: number) => (column - 1) * (CARD_WIDTH + CONNECTOR_WIDTH);
  const winnerColumns = winnerRounds.map((round) => winnerColumn(round.round));
  const loserColumns = loserRounds.map((round) => round.round);
  const lastColumn = Math.max(1, ...winnerColumns, ...loserColumns);
  const grandFinalColumn = lastColumn + 1;
  const width = columnX(grandFinal ? grandFinalColumn : lastColumn) + CARD_WIDTH;

  const cards: { match: BracketMatch; x: number; y: number }[] = [];
  const headers: { key: string; title: string; x: number; top: number }[] = [];
  const paths: string[] = [];

  // One bracket's cards, its round headers, and the connectors into its next round.
  const place = (
    rounds: BracketRound[],
    layout: ReturnType<typeof computeTreeLayout>,
    columns: number[],
    top: number,
    headerTop: number,
    titlePrefix: string,
  ) => {
    rounds.forEach((round, ri) => {
      const x = columnX(columns[ri]);
      headers.push({ key: `${titlePrefix}-${round.round}`, title: `${titlePrefix} ${round.title}`, x, top: headerTop });

      round.matches.forEach((match) => {
        const y = layout.positions[ri]?.get(match.indexInRound);
        if (y === undefined) {
          return;
        }

        cards.push({ match, x, y: top + y });

        if (ri < rounds.length - 1) {
          const target = targetIndex(layout.widths[ri], layout.widths[ri + 1], match.indexInRound);
          const targetY = layout.positions[ri + 1]?.get(target);
          if (targetY !== undefined) {
            paths.push(elbowPath(x + CARD_WIDTH, top + y, columnX(columns[ri + 1]), top + targetY));
          }
        }
      });
    });
  };

  place(winnerRounds, winnerLayout, winnerColumns, winnerTop, 0, 'Upper');
  place(loserRounds, loserLayout, loserColumns, loserTop, loserHeaderTop, 'Lower');

  // The Grand Final sits between the two finals that feed it.
  const finalOf = (rounds: BracketRound[], layout: ReturnType<typeof computeTreeLayout>, top: number, columns: number[]) => {
    const ri = rounds.length - 1;
    const match = rounds[ri]?.matches[0];
    const y = match ? layout.positions[ri]?.get(match.indexInRound) : undefined;
    return y === undefined ? null : { x: columnX(columns[ri]), y: top + y };
  };

  if (grandFinal) {
    const winnerFinal = finalOf(winnerRounds, winnerLayout, winnerTop, winnerColumns);
    const loserFinal = finalOf(loserRounds, loserLayout, loserTop, loserColumns);
    const x = columnX(grandFinalColumn);
    const y = winnerFinal && loserFinal ? (winnerFinal.y + loserFinal.y) / 2 : (winnerFinal ?? loserFinal)?.y ?? CARD_HEIGHT / 2;

    headers.push({ key: 'grand-final', title: 'Grand Final', x, top: 0 });
    cards.push({ match: grandFinal, x, y });
    for (const source of [winnerFinal, loserFinal]) {
      if (source) {
        paths.push(elbowPath(source.x + CARD_WIDTH, source.y, x, y));
      }
    }
  }

  return (
    <Box sx={{ position: 'relative', width, height, minWidth: width }}>
      <Box
        component="svg"
        width={width}
        height={height}
        sx={{ position: 'absolute', inset: 0, pointerEvents: 'none' }}
      >
        {paths.map((d) => (
          <path key={d} d={d} fill="none" stroke={CONNECTOR_COLOR} strokeWidth={1.5} />
        ))}
      </Box>

      {headers.map((header) => (
        <Box key={header.key} sx={{ position: 'absolute', left: header.x, top: header.top, width: CARD_WIDTH }}>
          <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }} noWrap>
            {header.title}
          </Typography>
        </Box>
      ))}

      {cards.map((card) => (
        <Box
          key={card.match.id}
          sx={{ position: 'absolute', left: card.x, top: card.y - CARD_HEIGHT / 2, width: CARD_WIDTH }}
        >
          <MatchCard match={card.match} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
        </Box>
      ))}
    </Box>
  );
}

/** Group Stage + Playoff: Group Stage tab (per-group schedule + standings), Playoffs tab, and Leaderboard tab. */
function GroupStagePlayoffView({
  bracket,
  onSelect,
  hoveredId,
  onHover,
}: {
  bracket: Bracket;
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
  const playoffStarted = bracket.winnerRounds.length > 0;
  const [tab, setTab] = useState(() => (bracket.status === 'Finished' ? 2 : playoffStarted ? 1 : 0));

  return (
    <Box sx={{ pb: 1 }}>
      <Tabs value={tab} onChange={(_, value) => setTab(value)} sx={{ mb: 2, minHeight: 36 }}>
        <Tab label="Group Stage" sx={{ minHeight: 36, py: 0 }} />
        <Tab label="Playoffs" sx={{ minHeight: 36, py: 0 }} />
        <Tab label="Leaderboard" sx={{ minHeight: 36, py: 0 }} />
      </Tabs>

      {tab === 0 && (
        <Box sx={{ overflowX: 'auto' }}>
          <Stack spacing={4}>
            {bracket.groups.map((group) => (
              <Stack key={group.groupIndex} spacing={1}>
                <Typography variant="subtitle2" color="text.secondary">
                  {groupLabel(group.groupIndex)}
                </Typography>
                <Box sx={{ overflowX: 'auto' }}>
                  <Stack direction="row" spacing={3} sx={{ alignItems: 'stretch', minWidth: 'min-content' }}>
                    <RoundColumns rounds={group.rounds} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
                  </Stack>
                </Box>
                <StandingsTable standings={group.standings} hoveredId={hoveredId} onHover={onHover} />
              </Stack>
            ))}
          </Stack>
        </Box>
      )}

      {tab === 1 &&
        (playoffStarted ? (
          <Box sx={{ overflowX: 'auto' }}>
            <PlayoffGrid bracket={bracket} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
          </Box>
        ) : (
          <Typography color="text.secondary">
            The playoff bracket appears once the group stage is finished and the admin starts the playoffs.
          </Typography>
        ))}

      {tab === 2 && <PlacementsList placements={bracket.placements} hoveredId={hoveredId} onHover={onHover} />}
    </Box>
  );
}

/**
 * The slot that slot `slot` of a round `fromWidth` wide feeds in the next round, `toWidth` wide:
 * a halving round merges slots 2j/2j+1 into j, while a loser bracket's drop-in round is 1:1 (its
 * other feeder is a Winner Bracket dropout, which has no card in this bracket). The proportional
 * fallback only catches irregular widths a bye cascade can leave behind.
 */
function targetIndex(fromWidth: number, toWidth: number, slot: number): number {
  if (toWidth <= 0) {
    return 0;
  }

  if (fromWidth === toWidth * 2) {
    return Math.floor(slot / 2);
  }

  if (fromWidth === toWidth) {
    return slot;
  }

  return Math.min(toWidth - 1, Math.floor((slot * toWidth) / fromWidth));
}

/** Highest slot actually occupied in a round; matches carry their real `indexInRound`, which can be sparse. */
function occupiedWidth(round: BracketRound): number {
  return round.matches.reduce((max, match) => Math.max(max, match.indexInRound + 1), 0);
}

/**
 * Per-round slot count (bracket positions at that round, whether or not a Match row exists there -
 * a bye pairing never gets one).
 *
 * A **winner** bracket (and Single Elimination) strictly halves every round, so its widths come from
 * doubling backward off the final round's width of 1 - exact even when byes leave round 1's match
 * array sparse, and tolerant of a round number skipping entirely via `2 ** (next - round)`.
 *
 * A **loser** bracket does not halve: it alternates "drop-in" rounds (same match count - each Loser
 * Bracket survivor meets an incoming Winner Bracket dropout) with consolidation rounds (halving).
 * Doubling backward there over-estimates the early rounds several times over and drags the later
 * ones out of alignment, so those widths are taken from the rounds' own occupied slots instead,
 * never narrower than the round they feed.
 */
function computeRoundWidths(rounds: BracketRound[], roundsAlwaysHalve: boolean): Map<number, number> {
  const widths = new Map<number, number>();
  if (rounds.length === 0) {
    return widths;
  }

  const last = rounds[rounds.length - 1];
  widths.set(last.round, roundsAlwaysHalve ? 1 : Math.max(1, occupiedWidth(last)));

  for (let i = rounds.length - 2; i >= 0; i--) {
    const round = rounds[i].round;
    const nextWidth = widths.get(rounds[i + 1].round) ?? 1;
    widths.set(
      round,
      roundsAlwaysHalve
        ? nextWidth * 2 ** (rounds[i + 1].round - round)
        : Math.max(occupiedWidth(rounds[i]), nextWidth),
    );
  }

  return widths;
}

/**
 * Vertical center (px) of every slot in every round, keyed by each match's own `indexInRound` rather
 * than its position within the round's match array - that array can be sparse (byes in round 1 never
 * get a Match row, so a lone real match can be e.g. index 3 of 4 while being the only entry), and
 * positioning by array position instead collapses distinct slots onto the same Y.
 *
 * Two passes. First the clean tree layout: round 1 evenly spaced across its full slot width, then
 * every later slot centered on the average of the slots feeding it (via {@link targetIndex}, which
 * handles both a halving round and a loser bracket's 1:1 drop-in round). Then, working backward from
 * the last round, any slot that is the *sole* real feeder of its target is leveled with that target -
 * so a lone feeder (its sibling being a bye that advanced with no Match row, or a drop-in round where
 * one feeder is a Winner Bracket dropout rather than a card) draws a straight connector instead of an
 * unnecessary vertical jog.
 */
function computeTreeLayout(
  rounds: BracketRound[],
  roundsAlwaysHalve: boolean,
): { positions: Map<number, number>[]; widths: number[]; totalHeight: number } {
  const widthMap = computeRoundWidths(rounds, roundsAlwaysHalve);
  const widths = rounds.map((round) => widthMap.get(round.round) ?? Math.max(1, occupiedWidth(round)));

  const firstWidth = widths[0] ?? 0;
  let slotY = Array.from({ length: firstWidth }, (_, i) => i * (CARD_HEIGHT + ROUND_VGAP) + CARD_HEIGHT / 2);
  const roundY: number[][] = [slotY];

  for (let ri = 1; ri < rounds.length; ri++) {
    const sums = new Array<number>(widths[ri]).fill(0);
    const counts = new Array<number>(widths[ri]).fill(0);
    slotY.forEach((y, slot) => {
      const target = targetIndex(widths[ri - 1], widths[ri], slot);
      sums[target] += y;
      counts[target] += 1;
    });

    slotY = sums.map((sum, j) => (counts[j] > 0 ? sum / counts[j] : CARD_HEIGHT / 2));
    roundY.push(slotY);
  }

  const realSlots = rounds.map((round) => new Set(round.matches.map((m) => m.indexInRound)));
  for (let ri = rounds.length - 2; ri >= 0; ri--) {
    const feedersByTarget = new Map<number, number[]>();
    for (const slot of realSlots[ri]) {
      const target = targetIndex(widths[ri], widths[ri + 1], slot);
      const feeders = feedersByTarget.get(target);
      if (feeders) {
        feeders.push(slot);
      } else {
        feedersByTarget.set(target, [slot]);
      }
    }

    for (const [target, feeders] of feedersByTarget) {
      if (feeders.length === 1 && roundY[ri + 1][target] !== undefined) {
        roundY[ri][feeders[0]] = roundY[ri + 1][target];
      }
    }
  }

  const positions = roundY.map((slots) => new Map(slots.map((y, i) => [i, y])));

  const totalHeight = Math.max(firstWidth * (CARD_HEIGHT + ROUND_VGAP) - ROUND_VGAP, CARD_HEIGHT);
  return { positions, widths, totalHeight };
}

function BracketTree({
  rounds,
  onSelect,
  hoveredId,
  onHover,
  extraMatch,
}: {
  rounds: BracketRound[];
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
  extraMatch?: { label: string; match: BracketMatch } | null;
}) {
  // Single Elimination only; its rounds always halve. The playoff grid lays its two brackets out itself.
  const { positions, widths, totalHeight } = useMemo(() => computeTreeLayout(rounds, true), [rounds]);

  // Anchor the extra match (Third Place) to the bottom edge of the last round's lowest card, not
  // its center - using the center directly under-accounted for the card's own half-height and let
  // the label overlap the Final's card.
  const lastRoundYs = Array.from(positions[positions.length - 1]?.values() ?? []);
  const lastRoundBottom = Math.max(0, ...lastRoundYs) + CARD_HEIGHT / 2;
  const extraLabelTop = lastRoundBottom + EXTRA_MATCH_GAP;
  const extraCardTop = extraLabelTop + EXTRA_LABEL_HEIGHT;
  const containerHeight = extraMatch ? extraCardTop + CARD_HEIGHT + 16 : totalHeight;

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
              {round.matches.map((match) => {
                const y = positions[ri].get(match.indexInRound);
                return y === undefined ? null : (
                  <Box key={match.id} sx={{ position: 'absolute', top: y - CARD_HEIGHT / 2, left: 0, width: CARD_WIDTH }}>
                    <MatchCard match={match} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
                  </Box>
                );
              })}
              {ri === rounds.length - 1 && extraMatch && (
                <>
                  <Box sx={{ position: 'absolute', top: extraLabelTop, left: 0, width: CARD_WIDTH }}>
                    <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
                      {extraMatch.label}
                    </Typography>
                  </Box>
                  <Box sx={{ position: 'absolute', top: extraCardTop, left: 0, width: CARD_WIDTH }}>
                    <MatchCard match={extraMatch.match} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
                  </Box>
                </>
              )}
            </Box>

            {ri < rounds.length - 1 && (
              <Connector
                height={containerHeight}
                matches={round.matches}
                fromPositions={positions[ri]}
                toPositions={positions[ri + 1]}
                fromWidth={widths[ri]}
                toWidth={widths[ri + 1]}
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
  matches,
  fromPositions,
  toPositions,
  fromWidth,
  toWidth,
}: {
  height: number;
  matches: BracketMatch[];
  fromPositions: Map<number, number>;
  toPositions: Map<number, number>;
  fromWidth: number;
  toWidth: number;
}) {
  const midX = CONNECTOR_WIDTH / 2;

  return (
    <Box component="svg" width={CONNECTOR_WIDTH} height={height} sx={{ display: 'block', flexShrink: 0 }}>
      {matches.map((match) => {
        const y = fromPositions.get(match.indexInRound);
        const targetY = toPositions.get(targetIndex(fromWidth, toWidth, match.indexInRound));
        if (y === undefined || targetY === undefined) {
          return null;
        }

        return (
          <path
            key={match.id}
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

function RoundColumns({
  rounds,
  onSelect,
  hoveredId,
  onHover,
}: {
  rounds: BracketRound[];
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
  return (
    <>
      {rounds.map((round) => (
        <Stack key={round.round} spacing={2} sx={{ minWidth: 220, justifyContent: 'space-around' }}>
          <Typography variant="subtitle2" color="text.secondary" sx={{ textAlign: 'center' }}>
            {round.title}
          </Typography>
          {round.matches.map((match) => (
            <MatchCard key={match.id} match={match} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
          ))}
        </Stack>
      ))}
    </>
  );
}

function findMatch(bracket: Bracket, matchId: string): BracketMatch | null {
  const groupRounds = bracket.groups.flatMap((group) => group.rounds);
  for (const round of [...bracket.winnerRounds, ...bracket.loserRounds, ...groupRounds]) {
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

function MatchCard({
  match,
  onSelect,
  hoveredId,
  onHover,
}: {
  match: BracketMatch;
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
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
      <SlotRow slot={match.participantA} winnerId={match.winnerId} hoveredId={hoveredId} onHover={onHover} />
      <Box sx={{ borderTop: '1px solid rgba(255,255,255,0.08)' }} />
      <SlotRow slot={match.participantB} winnerId={match.winnerId} hoveredId={hoveredId} onHover={onHover} />
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

function SlotRow({
  slot,
  winnerId,
  hoveredId,
  onHover,
}: {
  slot: BracketSlot | null;
  winnerId: string | null;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
  const isWinner = slot != null && winnerId === slot.participantId;
  const isHovered = slot != null && slot.participantId === hoveredId;
  return (
    <Box
      onMouseEnter={slot ? () => onHover(slot.participantId) : undefined}
      onMouseLeave={slot ? () => onHover(null) : undefined}
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
        boxShadow: isHovered ? 'inset 0 0 0 2px rgba(124,156,255,0.8)' : 'none',
      }}
    >
      <Typography
        variant="body2"
        noWrap
        sx={{
          color: slot ? 'text.primary' : 'text.disabled',
          fontWeight: isWinner || isHovered ? 700 : 400,
        }}
      >
        {slot ? slot.name : 'TBD'}
      </Typography>
      {isWinner && <Chip size="small" color="success" label="W" />}
    </Box>
  );
}

function StandingsTable({
  standings,
  hoveredId,
  onHover,
}: {
  standings: StandingRow[];
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2" color="text.secondary">
        Standings
      </Typography>
      <TableContainer component={Paper} variant="outlined" sx={{ maxWidth: 480 }}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Place</TableCell>
              <TableCell>Name</TableCell>
              <TableCell align="right">Played</TableCell>
              <TableCell align="right">Wins</TableCell>
              <TableCell align="right">Losses</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {standings.map((row) => {
              const colors = RANK_COLORS[row.rank];
              const isHovered = row.participantId === hoveredId;
              return (
                <TableRow
                  key={row.participantId}
                  onMouseEnter={() => onHover(row.participantId)}
                  onMouseLeave={() => onHover(null)}
                  sx={{
                    bgcolor: colors?.bg ?? 'transparent',
                    boxShadow: isHovered ? 'inset 0 0 0 2px rgba(124,156,255,0.8)' : 'none',
                  }}
                >
                  <TableCell>
                    <PlaceCell rankStart={row.rank} rankEnd={row.rank} />
                  </TableCell>
                  <TableCell sx={{ fontWeight: isHovered ? 700 : 400 }}>{row.name}</TableCell>
                  <TableCell align="right">{row.played}</TableCell>
                  <TableCell align="right">{row.wins}</TableCell>
                  <TableCell align="right">{row.losses}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableContainer>
    </Stack>
  );
}

function PlacementsList({
  placements,
  hoveredId,
  onHover,
}: {
  placements: PlacementGroup[];
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
  if (placements.length === 0) {
    return (
      <Typography color="text.secondary">
        Placements will appear here as participants are eliminated.
      </Typography>
    );
  }

  return (
    <TableContainer component={Paper} variant="outlined" sx={{ maxWidth: 480 }}>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Place</TableCell>
            <TableCell>Name</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {placements.map((group) => {
            const colors = RANK_COLORS[group.rankStart];
            return (
              <TableRow key={group.label} sx={{ bgcolor: colors?.bg ?? 'transparent' }}>
                <TableCell sx={{ verticalAlign: 'top' }}>
                  <PlaceCell rankStart={group.rankStart} rankEnd={group.rankEnd} />
                </TableCell>
                <TableCell>
                  <Stack spacing={0.5}>
                    {group.participants.map((participant) => {
                      const isHovered = participant.participantId === hoveredId;
                      return (
                        <Box
                          key={participant.participantId}
                          onMouseEnter={() => onHover(participant.participantId)}
                          onMouseLeave={() => onHover(null)}
                          sx={{
                            borderRadius: 1,
                            boxShadow: isHovered ? 'inset 0 0 0 2px rgba(124,156,255,0.8)' : 'none',
                          }}
                        >
                          <Typography variant="body2" sx={{ fontWeight: isHovered ? 700 : 400 }}>
                            {participant.name}
                          </Typography>
                        </Box>
                      );
                    })}
                  </Stack>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
