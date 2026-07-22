import { Fragment, useMemo, useState } from 'react';
import type { ComponentProps } from 'react';
import {
  Alert,
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
import { ParticipantLabel } from './ParticipantLabel';
import { MatchResultDialog } from './MatchResultDialog';

const RANK_COLORS: Record<number, { trophy: string; bg: string }> = {
  1: { trophy: '#FFD700', bg: 'rgba(255,215,0,0.15)' },
  2: { trophy: '#C0C0C0', bg: 'rgba(192,192,192,0.15)' },
  3: { trophy: '#CD7F32', bg: 'rgba(205,127,50,0.15)' },
};

/** A group standing's projected playoff destination: the top half of the group advances to the Upper Bracket, the bottom half to the Lower Bracket - nobody is eliminated at the group stage. */
const BRACKET_TIER_COLORS = {
  upper: { text: '#3fb950', bg: 'rgba(63,185,80,0.15)' },
  lower: { text: '#ffa726', bg: 'rgba(255,167,38,0.15)' },
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

const CARD_WIDTH = 168;
const CARD_HEIGHT = 76;
const ROUND_VGAP = 28;
const CONNECTOR_WIDTH = 32;
const CONNECTOR_COLOR = 'rgba(255,255,255,0.2)';
const EXTRA_MATCH_GAP = 32; // space between the last round's card and the extra match's label
const LABEL_ROW_HEIGHT = 28; // reserved height for one subtitle2 label row (a round header, or the extra match's label)
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
        <Stack spacing={2} sx={{ alignItems: 'flex-start' }}>
          <StandingsTable standings={bracket.standings} hoveredId={hoveredId} onHover={setHoveredId} />
          {bracket.tiebreakerRounds.length > 0 && (
            <GroupMatchesTable
              rounds={bracket.tiebreakerRounds}
              onSelect={onSelect}
              hoveredId={hoveredId}
              onHover={setHoveredId}
              title="Tie-breakers"
            />
          )}
        </Stack>
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
 * straight across, later ones to 2r-2). That is what makes the winner bracket skip columns, so its
 * Final lines up with the Loser Bracket Final and the two feed a Grand Final in the column after them.
 *
 * This formula re-derives DoubleEliminationBracket.GenerateTopology's round-to-drop-in mapping
 * (backend/src/Adaminator.Domain/Brackets/DoubleEliminationBracket.cs) rather than reading it off the
 * data, because BracketRound carries no such field today. If that topology's drop-in schedule ever
 * changes, this silently mis-draws rather than failing loudly - keep the two in sync by hand, or add
 * an explicit column/drop-round field to the bracket DTO if this drifts again.
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

  // Pure geometry off the two layouts above - independent of hoveredId, so this must not re-run on
  // every hover (only cards.map(...) below reads hoveredId, once this array exists).
  const { width, height, cards, headers, paths } = useMemo(() => {
    const winnerTop = LABEL_ROW_HEIGHT;
    const winnerBottom = winnerTop + (winnerRounds.length > 0 ? winnerLayout.totalHeight : 0);
    const loserHeaderTop = winnerBottom + SECTION_GAP;
    const loserTop = loserHeaderTop + LABEL_ROW_HEIGHT;
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

    return { width, height, cards, headers, paths };
  }, [winnerRounds, loserRounds, grandFinal, winnerLayout, loserLayout]);

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

/** One labelled block of tie-breaker matches - a single group's, or the cross-group deciders. */
function TiebreakerSection({
  heading,
  title,
  rounds,
  onSelect,
  hoveredId,
  onHover,
}: {
  heading: string;
  title: string;
  rounds: BracketRound[];
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2" color="text.secondary">
        {heading}
      </Typography>
      <Box sx={{ maxWidth: 640 }}>
        <GroupMatchesTable rounds={rounds} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} title={title} />
      </Box>
    </Stack>
  );
}

/** Group Stage + Playoff: Group Stage, its own Tie-breakers stage, Playoffs, and Leaderboard. */
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
  const tiebreakerGroups = bracket.groups.filter((group) => group.tiebreakerRounds.length > 0);
  // Deciders played *between* groups, when equally-placed players contest the last playoff slots.
  const crossGroupTiebreakers = bracket.tiebreakerRounds;
  const hasTiebreakers = tiebreakerGroups.length > 0 || crossGroupTiebreakers.length > 0;
  // Tie-breakers are a real stage between the group stage and the playoff, so land on whichever
  // stage is actually live: the playoff once started, otherwise a pending/played tie-break.
  const [tab, setTab] = useState(() => {
    if (bracket.status === 'Finished') return 3;
    if (playoffStarted) return 2;
    return hasTiebreakers || bracket.needsTiebreakers ? 1 : 0;
  });

  return (
    <Box sx={{ pb: 1 }}>
      <Tabs value={tab} onChange={(_, value) => setTab(value)} sx={{ mb: 2, minHeight: 36 }}>
        <Tab label="Group Stage" sx={{ minHeight: 36, py: 0 }} />
        <Tab label="Tie-breakers" sx={{ minHeight: 36, py: 0 }} />
        <Tab label="Playoffs" sx={{ minHeight: 36, py: 0 }} />
        <Tab label="Leaderboard" sx={{ minHeight: 36, py: 0 }} />
      </Tabs>

      {tab === 0 && (
        <Stack spacing={4}>
          {bracket.groups.map((group) => (
            <Stack key={group.groupIndex} spacing={1}>
              <Typography variant="subtitle2" color="text.secondary">
                {groupLabel(group.groupIndex)}
              </Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '2fr 1fr' }, gap: 3, alignItems: 'start' }}>
                <GroupMatchesTable rounds={group.rounds} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
                <StandingsTable standings={group.standings} hoveredId={hoveredId} onHover={onHover} bracketSplit />
              </Box>
            </Stack>
          ))}
        </Stack>
      )}

      {tab === 1 && (
        <Stack spacing={3}>
          {bracket.needsTiebreakers && (
            <Alert severity="warning">
              A tie that decides the Upper/Lower split is unresolved. Use <strong>Resolve tie-breakers</strong> above to
              generate the next round - the playoff stays locked until it is settled.
            </Alert>
          )}
          {tiebreakerGroups.map((group) => (
            <TiebreakerSection
              key={group.groupIndex}
              heading={groupLabel(group.groupIndex)}
              title="Tie-breaker matches"
              rounds={group.tiebreakerRounds}
              onSelect={onSelect}
              hoveredId={hoveredId}
              onHover={onHover}
            />
          ))}

          {crossGroupTiebreakers.length > 0 && (
            <TiebreakerSection
              heading="Between groups"
              title="Deciding the last playoff slots"
              rounds={crossGroupTiebreakers}
              onSelect={onSelect}
              hoveredId={hoveredId}
              onHover={onHover}
            />
          )}

          {!hasTiebreakers && !bracket.needsTiebreakers && (
            <Typography color="text.secondary">
              No tie-breakers were needed - every group's standings separated on their own.
            </Typography>
          )}
        </Stack>
      )}

      {tab === 2 &&
        (playoffStarted ? (
          <Box sx={{ overflowX: 'auto' }}>
            <PlayoffGrid bracket={bracket} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
          </Box>
        ) : (
          <Typography color="text.secondary">
            The playoff bracket appears once the group stage is finished and the admin starts the playoffs.
          </Typography>
        ))}

      {tab === 3 && <PlacementsList placements={bracket.placements} hoveredId={hoveredId} onHover={onHover} />}
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
  const extraCardTop = extraLabelTop + LABEL_ROW_HEIGHT;
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
            d={elbowPath(0, y, CONNECTOR_WIDTH, targetY)}
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
  const groupRounds = bracket.groups.flatMap((group) => [...group.rounds, ...group.tiebreakerRounds]);
  for (const round of [...bracket.winnerRounds, ...bracket.loserRounds, ...bracket.tiebreakerRounds, ...groupRounds]) {
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

/**
 * Winner/hover styling for one slot of a match, shared by the card view (`SlotRow`) and the group
 * matches table (`GroupMatchRow`) so the two stay in sync: `rowSx` goes on the container (winner
 * tint + hover ring), `textSx` on the name itself (bold once either applies, dimmed when empty).
 */
function slotHighlight(slot: BracketSlot | null, winnerId: string | null, hoveredId: string | null) {
  const isWinner = slot != null && winnerId === slot.participantId;
  const isHovered = slot != null && slot.participantId === hoveredId;
  return {
    isWinner,
    rowSx: {
      bgcolor: isWinner ? 'rgba(63,185,80,0.15)' : 'transparent',
      boxShadow: isHovered ? 'inset 0 0 0 2px rgba(124,156,255,0.8)' : 'none',
    },
    textSx: {
      color: slot ? 'text.primary' : 'text.disabled',
      fontWeight: isWinner || isHovered ? 700 : 400,
    },
  };
}

/** The small "FF" badge for a forfeited match, shared by the card view and the group matches table. */
function ForfeitChip({ sx }: { sx?: ComponentProps<typeof Chip>['sx'] }) {
  return (
    <Chip
      size="small"
      color="warning"
      label="FF"
      sx={{ height: 18, fontSize: '0.65rem', '& .MuiChip-label': { px: 0.75 }, ...sx }}
    />
  );
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
      {match.status === 'Forfeit' && <ForfeitChip sx={{ position: 'absolute', top: 4, right: 4 }} />}
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
  const { isWinner, rowSx, textSx } = slotHighlight(slot, winnerId, hoveredId);
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
        ...rowSx,
      }}
    >
      <ParticipantLabel name={slot ? slot.name : 'TBD'} emoji={slot?.emoji} sx={textSx} />
      {isWinner && <Chip size="small" color="success" label="W" />}
    </Box>
  );
}

/** A group's schedule as one table - a header row per round, then one row per match with both participants side by side. */
function GroupMatchesTable({
  rounds,
  onSelect,
  hoveredId,
  onHover,
  title = 'Matches',
}: {
  rounds: BracketRound[];
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
  title?: string;
}) {
  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2" color="text.secondary">
        {title}
      </Typography>
      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableBody>
            {rounds.map((round) => (
              <Fragment key={round.round}>
                <TableRow>
                  <TableCell colSpan={3} sx={{ bgcolor: 'action.hover', fontWeight: 600, py: 0.5 }}>
                    {round.title}
                  </TableCell>
                </TableRow>
                {round.matches.map((match) => (
                  <GroupMatchRow key={match.id} match={match} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
                ))}
              </Fragment>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Stack>
  );
}

function GroupMatchRow({
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
  const isDecided = match.status === 'Completed' || match.status === 'Forfeit';

  const nameCell = (slot: BracketSlot | null) => {
    const { rowSx, textSx } = slotHighlight(slot, match.winnerId, hoveredId);
    return (
      <TableCell
        onMouseEnter={slot ? () => onHover(slot.participantId) : undefined}
        onMouseLeave={slot ? () => onHover(null) : undefined}
        sx={rowSx}
      >
        <ParticipantLabel name={slot ? slot.name : 'TBD'} emoji={slot?.emoji} sx={textSx} />
      </TableCell>
    );
  };

  return (
    <TableRow onClick={actionable ? () => onSelect!(match.id) : undefined} sx={{ cursor: actionable ? 'pointer' : 'default' }}>
      {nameCell(match.participantA)}
      <TableCell align="center" sx={{ whiteSpace: 'nowrap' }}>
        {isDecided ? (
          <Stack direction="row" spacing={0.5} sx={{ justifyContent: 'center', alignItems: 'center' }}>
            <Typography variant="body2" component="span">
              {match.aggregateScoreA} – {match.aggregateScoreB}
            </Typography>
            {match.status === 'Forfeit' && <ForfeitChip />}
          </Stack>
        ) : (
          <Typography variant="body2" color="text.secondary">
            vs
          </Typography>
        )}
      </TableCell>
      {nameCell(match.participantB)}
    </TableRow>
  );
}

function StandingsTable({
  standings,
  hoveredId,
  onHover,
  bracketSplit = false,
}: {
  standings: StandingRow[];
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
  /**
   * True for a Group Stage + Playoff group: rows are colored and labeled by playoff destination
   * (top half -> Upper Bracket, bottom half -> Lower Bracket) instead of the trophy-styled final
   * rank the Leaderboard tab uses - a group's rank 1 isn't a tournament placement.
   */
  bracketSplit?: boolean;
}) {
  const upperCount = Math.floor(standings.length / 2);

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
              <TableCell align="right">Games</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {standings.map((row, index) => {
              const tier = index < upperCount ? BRACKET_TIER_COLORS.upper : BRACKET_TIER_COLORS.lower;
              const colors = bracketSplit ? tier : RANK_COLORS[row.rank];
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
                    {bracketSplit ? (
                      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                        <Typography variant="body2">{row.rank}</Typography>
                        <Chip
                          size="small"
                          label={index < upperCount ? 'Upper Bracket' : 'Lower Bracket'}
                          sx={{
                            color: tier.text,
                            bgcolor: tier.bg,
                            fontWeight: 600,
                            '& .MuiChip-label': { px: 1 },
                          }}
                        />
                      </Stack>
                    ) : (
                      <PlaceCell rankStart={row.rank} rankEnd={row.rank} />
                    )}
                  </TableCell>
                  <TableCell>
                    <ParticipantLabel name={row.name} emoji={row.emoji} sx={{ fontWeight: isHovered ? 700 : 400 }} />
                  </TableCell>
                  <TableCell align="right">{row.played}</TableCell>
                  <TableCell align="right">{row.wins}</TableCell>
                  <TableCell align="right">{row.losses}</TableCell>
                  <TableCell align="right">{row.gamesWon}</TableCell>
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
        The results table appears once the bracket is generated.
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
                  {/* The row exists from the start; it stays a placeholder until this place is decided. */}
                  {group.participants.length === 0 ? (
                    <Typography variant="body2" color="text.disabled">
                      Undecided
                    </Typography>
                  ) : (
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
                            <ParticipantLabel
                              name={participant.name}
                              emoji={participant.emoji}
                              sx={{ fontWeight: isHovered ? 700 : 400 }}
                            />
                          </Box>
                        );
                      })}
                    </Stack>
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
