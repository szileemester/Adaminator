import { Fragment, useEffect, useMemo, useState } from 'react';
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
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import type {
  Bracket,
  BracketMatch,
  BracketRound,
  BracketSlot,
  GroupStage,
  LevelOutcome,
  PlacementGroup,
  StandingRow,
} from '../api/types';
import { findBracketMatch, groupLabel, isDecided, roundIsDecided } from '../api/types';
import { ParticipantLabel } from './ParticipantLabel';
import { MatchResultDialog } from './MatchResultDialog';

const RANK_COLORS: Record<number, { trophy: string; bg: string }> = {
  1: { trophy: '#FFD700', bg: 'rgba(255,215,0,0.15)' },
  2: { trophy: '#C0C0C0', bg: 'rgba(192,192,192,0.15)' },
  3: { trophy: '#CD7F32', bg: 'rgba(205,127,50,0.15)' },
};

/**
 * Where a group/Swiss standing sends a participant, as classified by the server from the playoff's
 * shape and cut. A double-elimination playoff splits its qualifiers across two brackets; a single
 * elimination one has just the one, so "Upper" is simply "in". "Contested" means equally-placed
 * participants are still competing for the last slots either side of a cut.
 */
const PLAYOFF_DESTINATIONS: Record<LevelOutcome, { label: string; short: string; text: string; bg: string }> = {
  Upper: { label: 'Upper Bracket', short: 'UB', text: '#3fb950', bg: 'rgba(63,185,80,0.15)' },
  Lower: { label: 'Lower Bracket', short: 'LB', text: '#ffa726', bg: 'rgba(255,167,38,0.15)' },
  Contested: { label: 'Contested', short: 'CT', text: '#7c9cff', bg: 'rgba(124,156,255,0.15)' },
  Eliminated: { label: 'Eliminated', short: 'EL', text: '#8b949e', bg: 'rgba(139,148,158,0.15)' },
};

/**
 * Full wording on a normal screen, an abbreviation on a phone. Rendered as two spans toggled by CSS
 * rather than a `useMediaQuery` branch, so the standings table needs no width measurement to lay out
 * and the abbreviation never flashes before the query resolves.
 */
function ResponsiveChipLabel({ full, short }: { full: string; short: string }) {
  return (
    <>
      <Box component="span" sx={{ display: { xs: 'none', sm: 'inline' } }}>
        {full}
      </Box>
      <Box component="span" sx={{ display: { xs: 'inline', sm: 'none' } }} title={full}>
        {short}
      </Box>
    </>
  );
}

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
const ROUND_VGAP = 14; // vertical gap between two sibling cards in the same round
const CONNECTOR_WIDTH = 32;
const CONNECTOR_COLOR = 'rgba(255,255,255,0.2)';
// Matches theme.ts's shape.borderRadius: the card Paper clips to this radius via overflow:
// hidden, so a slot's own hover/winner ring (drawn on an inner, unrounded Box) needs the same
// radius on its outer corners - otherwise the ring's sharp corner gets clipped mid-curve instead
// of following the card's rounded edge.
const CARD_RADIUS = 10;
const EXTRA_MATCH_GAP = 20; // space between the last round's card and the extra match's label
const LABEL_ROW_HEIGHT = 28; // reserved height for one subtitle2 label row (a round header, or the extra match's label)
const SECTION_GAP = 32; // vertical space between the winner and loser bracket regions

/**
 * The narrowest the standings table stays readable at. Below this it scrolls sideways inside its own
 * container rather than squeezing six columns into a phone: a "Played / Wins / Losses / Games" row
 * compressed to 300px wraps every heading onto three lines and still clips the names.
 *
 * The matches table has no such floor - it is three columns wide and always fits, truncating names
 * instead (see GroupMatchesTable).
 */
const GROUP_TABLE_MIN_WIDTH = 460;

/** Width of a match's middle column - wide enough for "12 - 10" plus a forfeit badge. */
const MATCH_SCORE_COLUMN = 96;

/**
 * A stage the admin just created matches in, so the view can jump to it. Carries an `at` stamp rather
 * than being a bare stage name: generating a second tie-breaker wave has to re-fire the jump even
 * though the stage is unchanged.
 */
export type FocusStage = { stage: 'groupStage' | 'tiebreakers' | 'playoffs'; at: number };

/**
 * Tabs are keyed by name, not by position: which ones exist varies by tournament type, so an index
 * would mean something different per view and shift whenever a tab is added. A key is also exactly
 * what {@link FocusStage} already carries, so jumping to a stage needs no mapping.
 */
type TabKey = FocusStage['stage'] | 'main' | 'leaderboard';

export function BracketView({
  bracket,
  tournamentId,
  focusStage,
}: {
  bracket: Bracket;
  tournamentId?: string;
  focusStage?: FocusStage | null;
}) {
  const [selectedMatchId, setSelectedMatchId] = useState<string | null>(null);
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  // Generated but not yet played. Distinct from bracket.needsTiebreakers, which means "a tie still
  // needs matches generated" - the two gate different messages.
  const tiebreakersPending = !bracket.tiebreakerRounds.every(roundIsDecided);
  // Lands on whichever stage is actually live - only at mount, so playing a match while this is open
  // never yanks the admin's current tab.
  const [tab, setTab] = useState<TabKey>(() => {
    if (bracket.status === 'Finished') {
      return 'leaderboard';
    }

    // A Round Robin's tie-breakers are a stage between the schedule and the final standings, so an
    // unresolved or unplayed tie is what the admin needs to see first.
    const roundRobin = bracket.type === 'RoundRobin';
    return roundRobin && (bracket.needsTiebreakers || tiebreakersPending) ? 'tiebreakers' : 'main';
  });
  // Generating a stage's matches jumps to it - the admin just asked for them, so show them. Must sit
  // above the early returns below to stay an unconditional hook.
  useEffect(() => {
    if (focusStage?.stage === 'tiebreakers' && bracket.type === 'RoundRobin') {
      setTab('tiebreakers');
    }
  }, [focusStage, bracket.type]);

  const selectedMatch = tournamentId && selectedMatchId ? findBracketMatch(bracket, selectedMatchId) : null;
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

  if (bracket.type === 'GroupStagePlayoff' && bracket.groupStage) {
    return (
      <>
        <GroupStagePlayoffView
          bracket={bracket}
          groupStage={bracket.groupStage}
          focusStage={focusStage}
          onSelect={onSelect}
          hoveredId={hoveredId}
          onHover={setHoveredId}
        />
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
      {/* Round Robin gets a Tie-breakers tab of its own, like the Group Stage + Playoff view - they
          are a real stage of play, not a footnote to the standings they resolve. */}
      <Tabs value={tab} onChange={(_, value: TabKey) => setTab(value)} sx={{ mb: 2, minHeight: 36 }}>
        <Tab value="main" label={isRoundRobin ? 'Schedule' : 'Bracket'} sx={{ minHeight: 36, py: 0 }} />
        {isRoundRobin && <Tab value="tiebreakers" label="Tie-breakers" sx={{ minHeight: 36, py: 0 }} />}
        <Tab value="leaderboard" label="Leaderboard" sx={{ minHeight: 36, py: 0 }} />
      </Tabs>

      {tab === 'main' && (
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
      )}

      {isRoundRobin && tab === 'tiebreakers' && (
        <Stack spacing={2} sx={{ alignItems: 'flex-start' }}>
          {tiebreakersPending && (
            <Alert severity="warning" sx={{ width: '100%' }}>
              A standings tie needs to be played off. Enter these tie-breaker results to settle the final
              order - the tournament cannot be finished until they are all decided.
            </Alert>
          )}
          {bracket.needsTiebreakers && (
            <Alert severity="warning" sx={{ width: '100%' }}>
              A tie that decides a final position is unresolved. Use <strong>Resolve tie-breakers</strong>{' '}
              above to generate the next round.
            </Alert>
          )}
          {/*
            The surprising part in practice is that these results stand on their own, so spell it out
            rather than leaving an admin to reverse-engineer it from the standings.
          */}
          {bracket.tiebreakerRounds.length > 0 && (
            <Alert severity="info" sx={{ width: '100%' }}>
              These are ranked on their own results: first by how many of these matches each player
              wins, then - between players still level - by who beat whom here. A player can therefore
              finish above someone who beat them in the main schedule.
            </Alert>
          )}
          {bracket.tiebreakerRounds.length > 0 ? (
            <GroupMatchesTable
              rounds={bracket.tiebreakerRounds}
              onSelect={onSelect}
              hoveredId={hoveredId}
              onHover={setHoveredId}
              title="Tie-breaker matches"
            />
          ) : (
            !bracket.needsTiebreakers && (
              <Typography color="text.secondary">
                No tie-breakers were needed - the standings separated on their own.
              </Typography>
            )
          )}
        </Stack>
      )}

      {/*
        No hover on the final standings: every participant has exactly one row here, so there is
        nothing to cross-reference and the highlight is just noise (the same reason PlacementsList
        never had one).
      */}
      {tab === 'leaderboard' &&
        (isRoundRobin ? (
          <StandingsTable standings={bracket.standings} />
        ) : (
          <PlacementsList placements={bracket.placements} />
        ))}

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
  groupStage,
  focusStage,
  onSelect,
  hoveredId,
  onHover,
}: {
  bracket: Bracket;
  groupStage: GroupStage;
  focusStage?: FocusStage | null;
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
}) {
  const playoffStarted = bracket.winnerRounds.length > 0;
  const isSwiss = groupStage.kind === 'Swiss';
  const playoffIsSingleElimination = groupStage.playoffKind === 'SingleElimination';
  const tiebreakerGroups = bracket.groups.filter((group) => group.tiebreakerRounds.length > 0);
  // Deciders played *between* groups, when equally-placed players contest the last playoff slots.
  const crossGroupTiebreakers = bracket.tiebreakerRounds;
  const hasTiebreakers = tiebreakerGroups.length > 0 || crossGroupTiebreakers.length > 0;
  // Tie-breakers are a real stage between the group stage and the playoff, so land on whichever
  // stage is actually live: the playoff once started, otherwise a pending/played tie-break.
  const [tab, setTab] = useState<TabKey>(() => {
    if (bracket.status === 'Finished') return 'leaderboard';
    if (playoffStarted) return 'playoffs';
    return hasTiebreakers || bracket.needsTiebreakers ? 'tiebreakers' : 'groupStage';
  });

  // Generating a stage's matches jumps to it - the admin just asked for them, so show them. The tab
  // keys are the stage names, so this needs no mapping.
  useEffect(() => {
    if (focusStage) {
      setTab(focusStage.stage);
    }
  }, [focusStage]);

  return (
    <Box sx={{ pb: 1 }}>
      <Tabs
        value={tab}
        onChange={(_, value: TabKey) => setTab(value)}
        variant="scrollable"
        scrollButtons="auto"
        allowScrollButtonsMobile
        sx={{ mb: 2, minHeight: 36 }}
      >
        <Tab value="groupStage" label="Group Stage" sx={{ minHeight: 36, py: 0 }} />
        <Tab value="tiebreakers" label="Tie-breakers" sx={{ minHeight: 36, py: 0 }} />
        <Tab value="playoffs" label="Playoffs" sx={{ minHeight: 36, py: 0 }} />
        <Tab value="leaderboard" label="Leaderboard" sx={{ minHeight: 36, py: 0 }} />
      </Tabs>

      {tab === 'groupStage' && (
        isSwiss ? (
          <Stack spacing={1}>
            <Typography variant="subtitle2" color="text.secondary">
              {groupStage.roundsPlayed > 0
                ? `Swiss pool - round ${groupStage.roundsPlayed} of ${groupStage.roundsTotal}`
                : 'Swiss pool'}
            </Typography>
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 3, alignItems: 'start' }}>
              <GroupMatchesTable
                rounds={groupStage.rounds}
                onSelect={onSelect}
                hoveredId={hoveredId}
                onHover={onHover}
                collapseDecidedRounds
              />
              <StandingsTable standings={groupStage.standings} hoveredId={hoveredId} onHover={onHover} showsPlayoffDestination singleBracketPlayoff={playoffIsSingleElimination} />
            </Box>
          </Stack>
        ) : (
          <Stack spacing={4}>
            {bracket.groups.map((group) => (
              <Stack key={group.groupIndex} spacing={1}>
                <Typography variant="subtitle2" color="text.secondary">
                  {groupLabel(group.groupIndex)}
                </Typography>
                <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 3, alignItems: 'start' }}>
                  <GroupMatchesTable rounds={group.rounds} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
                  <StandingsTable standings={group.standings} hoveredId={hoveredId} onHover={onHover} showsPlayoffDestination singleBracketPlayoff={playoffIsSingleElimination} />
                </Box>
              </Stack>
            ))}
          </Stack>
        )
      )}

      {tab === 'tiebreakers' && (
        <Stack spacing={3}>
          {bracket.needsTiebreakers && (
            <Alert severity="warning">
              A tie that decides who reaches the playoff is unresolved. Use <strong>Resolve tie-breakers</strong> above to
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

      {tab === 'playoffs' &&
        (playoffStarted ? (
          <Box sx={{ overflowX: 'auto' }}>
            {playoffIsSingleElimination ? (
              <BracketTree
                rounds={bracket.winnerRounds}
                onSelect={onSelect}
                hoveredId={hoveredId}
                onHover={onHover}
                extraMatch={bracket.thirdPlace ? { label: 'Third Place Match', match: bracket.thirdPlace } : null}
              />
            ) : (
              <PlayoffGrid bracket={bracket} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
            )}
          </Box>
        ) : (
          <Typography color="text.secondary">
            The playoff bracket appears once the group stage is finished and the admin starts the playoffs.
          </Typography>
        ))}

      {tab === 'leaderboard' && <PlacementsList placements={bracket.placements} />}
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
 * Vertical center (px) of every match in every round, keyed by each match's own `indexInRound` rather
 * than its position within the round's match array - that array can be sparse (a bye never gets a
 * Match row, so a lone real match can be index 7 of 8 while being the round's only entry).
 *
 * Only real matches take vertical space. Laying out every *theoretical* slot instead leaves a roster
 * that isn't a power of two mostly empty - a 9-player bracket reserves 8 round-1 rows to show one
 * match - which both stretches the tree and turns its connectors into long vertical jogs.
 *
 * So: a match no real match feeds into is a "leaf" and takes the next row down, ordered by where it
 * sits in the full bracket so the rows still read top-to-bottom. Every other match centers on the
 * feeders it actually has, which leaves a lone feeder level with its target and its connector
 * straight (its sibling being a bye, or - in a loser bracket's drop-in round - a Winner Bracket
 * dropout that has no card here).
 */
function computeTreeLayout(
  rounds: BracketRound[],
  roundsAlwaysHalve: boolean,
): { positions: Map<number, number>[]; widths: number[]; totalHeight: number } {
  const widthMap = computeRoundWidths(rounds, roundsAlwaysHalve);
  const widths = rounds.map((round) => widthMap.get(round.round) ?? Math.max(1, occupiedWidth(round)));
  const realSlots = rounds.map((round) => round.matches.map((m) => m.indexInRound).sort((a, b) => a - b));

  // Which real matches of the previous round feed each slot of this one.
  const feedersOf = rounds.map(() => new Map<number, number[]>());
  for (let ri = 1; ri < rounds.length; ri++) {
    for (const slot of realSlots[ri - 1]) {
      const target = targetIndex(widths[ri - 1], widths[ri], slot);
      const feeders = feedersOf[ri].get(target);
      if (feeders) {
        feeders.push(slot);
      } else {
        feedersOf[ri].set(target, [slot]);
      }
    }
  }

  // A leaf's position in the full bracket, in first-round slot units, so leaves from different rounds
  // still stack in bracket order (a round-2 match whose feeders were all byes sits among round 1's).
  const firstWidth = widths[0] ?? 1;
  const spanStart = (ri: number, slot: number) => slot * (firstWidth / (widths[ri] || 1));

  const leaves: { ri: number; slot: number }[] = [];
  for (let ri = 0; ri < rounds.length; ri++) {
    for (const slot of realSlots[ri]) {
      if (ri === 0 || !feedersOf[ri].get(slot)?.length) {
        leaves.push({ ri, slot });
      }
    }
  }
  leaves.sort((a, b) => spanStart(a.ri, a.slot) - spanStart(b.ri, b.slot) || a.ri - b.ri);

  const positions: Map<number, number>[] = rounds.map(() => new Map<number, number>());
  leaves.forEach(({ ri, slot }, row) => {
    positions[ri].set(slot, row * (CARD_HEIGHT + ROUND_VGAP) + CARD_HEIGHT / 2);
  });

  // Rounds ascend, so a match's feeders are always already placed by the time it is.
  for (let ri = 1; ri < rounds.length; ri++) {
    for (const slot of realSlots[ri]) {
      if (positions[ri].has(slot)) {
        continue;
      }

      const ys = (feedersOf[ri].get(slot) ?? [])
        .map((feeder) => positions[ri - 1].get(feeder))
        .filter((y): y is number => y !== undefined);
      if (ys.length > 0) {
        positions[ri].set(slot, ys.reduce((sum, y) => sum + y, 0) / ys.length);
      }
    }
  }

  const totalHeight = Math.max(leaves.length * (CARD_HEIGHT + ROUND_VGAP) - ROUND_VGAP, CARD_HEIGHT);
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

/**
 * Winner/hover styling for one slot of a match, shared by the card view (`SlotRow`) and the group
 * matches table (`GroupMatchRow`) so the two stay in sync: `rowSx` goes on the container (winner
 * tint + hover ring), `textSx` on the name itself (bold for a winner, dimmed when empty).
 *
 * Hovering deliberately changes no text weight. Re-weighting a name reflows the line under the
 * cursor, so tracking one participant across the bracket makes every row they appear in twitch; the
 * ring alone marks them, and it costs no layout.
 */
function slotHighlight(slot: BracketSlot | null, winnerId: string | null, hoveredId: string | null) {
  const isWinner = slot != null && winnerId === slot.participantId;
  const isHovered = slot != null && slot.participantId === hoveredId;
  return {
    rowSx: {
      bgcolor: isWinner ? 'rgba(63,185,80,0.15)' : 'transparent',
      boxShadow: isHovered ? 'inset 0 0 0 2px rgba(124,156,255,0.8)' : 'none',
    },
    textSx: {
      color: slot ? 'text.primary' : 'text.disabled',
      fontWeight: isWinner ? 700 : 400,
    },
  };
}

/**
 * The score to show for one side of a decided match: always the match result (games won), never the
 * points from within a game. A bracket reports who won the match; per-game points belong in the
 * result dialog, which lists them game by game. Null while the match isn't decided yet.
 */
function displayScore(match: BracketMatch, isSlotA: boolean): number | null {
  if (!isDecided(match)) {
    return null;
  }

  return isSlotA ? match.aggregateScoreA : match.aggregateScoreB;
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
      <SlotRow slot={match.participantA} winnerId={match.winnerId} hoveredId={hoveredId} onHover={onHover} score={displayScore(match, true)} corner="top" />
      <Box sx={{ borderTop: '1px solid rgba(255,255,255,0.08)' }} />
      <SlotRow slot={match.participantB} winnerId={match.winnerId} hoveredId={hoveredId} onHover={onHover} score={displayScore(match, false)} corner="bottom" />
      {match.status === 'Forfeit' && <ForfeitChip sx={{ position: 'absolute', top: 4, right: 4 }} />}
    </Paper>
  );
}

function SlotRow({
  slot,
  winnerId,
  hoveredId,
  onHover,
  score,
  corner,
}: {
  slot: BracketSlot | null;
  winnerId: string | null;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
  score: number | null;
  /** Which end of the card this slot is at, so its hover/winner ring rounds the same corners as the card's own clip - see CARD_RADIUS. */
  corner: 'top' | 'bottom';
}) {
  const { rowSx, textSx } = slotHighlight(slot, winnerId, hoveredId);
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
        borderTopLeftRadius: corner === 'top' ? CARD_RADIUS : 0,
        borderTopRightRadius: corner === 'top' ? CARD_RADIUS : 0,
        borderBottomLeftRadius: corner === 'bottom' ? CARD_RADIUS : 0,
        borderBottomRightRadius: corner === 'bottom' ? CARD_RADIUS : 0,
        ...rowSx,
      }}
    >
      <ParticipantLabel name={slot ? slot.name : 'TBD'} emoji={slot?.emoji} pending={!slot} sx={textSx} />
      {score != null && (
        <Box
          sx={{
            minWidth: 26,
            textAlign: 'center',
            flexShrink: 0,
            border: '1px solid rgba(255,255,255,0.25)',
            borderRadius: 0.5,
            px: 0.5,
            py: 0.25,
          }}
        >
          <Typography variant="body2" sx={textSx}>{score}</Typography>
        </Box>
      )}
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
  collapseDecidedRounds = false,
}: {
  rounds: BracketRound[];
  onSelect?: (matchId: string) => void;
  hoveredId: string | null;
  onHover: (participantId: string | null) => void;
  title?: string;
  /**
   * Fold a round away once every match in it is decided, so a stage that grows a round at a time
   * (Swiss) keeps the round still being played in view instead of pushing it below the finished ones.
   */
  collapseDecidedRounds?: boolean;
}) {
  // Only explicit clicks are remembered; everything else follows the results, so a round generated
  // later starts open and the one it supersedes folds itself away.
  const [overrides, setOverrides] = useState<Record<number, boolean>>({});
  const isOpen = (round: BracketRound) =>
    overrides[round.round] ?? !(collapseDecidedRounds && roundIsDecided(round));

  return (
    // minWidth: 0 so this flex child may shrink below its content. Without it the wrapper grows to
    // the table's natural width, the TableContainer never becomes narrower than what it holds, and
    // its overflow-x has nothing to scroll - the card just clips the overhang.
    <Stack spacing={1} sx={{ minWidth: 0 }}>
      <Typography variant="subtitle2" color="text.secondary">
        {title}
      </Typography>
      <TableContainer component={Paper} variant="outlined">
        {/*
          Fixed layout with an explicit column split, so a fixture always fits the width it is given
          and long names ellipsize (ParticipantLabel's name is already noWrap) instead of widening the
          table. Declared as a colgroup rather than as widths on the cells because the round header
          above them spans all three columns, and an auto-layout table would size the columns from it.
        */}
        <Table size="small" sx={{ tableLayout: 'fixed' }}>
          <Box component="colgroup">
            <col />
            <Box component="col" sx={{ width: MATCH_SCORE_COLUMN }} />
            <col />
          </Box>
          <TableBody>
            {rounds.map((round) => {
              const open = isOpen(round);
              return (
                <Fragment key={round.round}>
                  <TableRow
                    onClick={
                      collapseDecidedRounds
                        ? () => setOverrides((prev) => ({ ...prev, [round.round]: !open }))
                        : undefined
                    }
                    sx={{ cursor: collapseDecidedRounds ? 'pointer' : 'default' }}
                  >
                    <TableCell colSpan={3} sx={{ bgcolor: 'action.hover', fontWeight: 600, py: 0.5 }}>
                      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                        {collapseDecidedRounds &&
                          (open ? <ExpandLessIcon fontSize="small" /> : <ExpandMoreIcon fontSize="small" />)}
                        <span>{round.title}</span>
                        {collapseDecidedRounds && !open && (
                          <Typography variant="caption" color="text.secondary">
                            {round.matches.length} played
                          </Typography>
                        )}
                      </Stack>
                    </TableCell>
                  </TableRow>
                  {open &&
                    round.matches.map((match) => (
                      <GroupMatchRow key={match.id} match={match} onSelect={onSelect} hoveredId={hoveredId} onHover={onHover} />
                    ))}
                </Fragment>
              );
            })}
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
  const decided = isDecided(match);
  // A Swiss bye: one participant, no opponent, already credited the win. There is no score to show
  // and nothing to enter, so it reads as a labelled sit-out rather than an empty fixture.
  const isBye = match.participantA != null && match.participantB == null;

  // The two sides face each other across the score: the first reads left-to-right from the left edge,
  // the second is mirrored against the right edge.
  const nameCell = (slot: BracketSlot | null, align: 'start' | 'end' = 'start') => {
    const { rowSx, textSx } = slotHighlight(slot, match.winnerId, hoveredId);
    return (
      <TableCell
        onMouseEnter={slot ? () => onHover(slot.participantId) : undefined}
        onMouseLeave={slot ? () => onHover(null) : undefined}
        sx={rowSx}
      >
        <ParticipantLabel name={slot ? slot.name : 'TBD'} emoji={slot?.emoji} pending={!slot} sx={textSx} align={align} />
      </TableCell>
    );
  };

  return (
    <TableRow onClick={actionable ? () => onSelect!(match.id) : undefined} sx={{ cursor: actionable ? 'pointer' : 'default' }}>
      {nameCell(match.participantA)}
      <TableCell align="center" sx={{ whiteSpace: 'nowrap' }}>
        {isBye ? (
          <Chip size="small" label="BYE" sx={{ fontWeight: 600, '& .MuiChip-label': { px: 1 } }} />
        ) : decided ? (
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
      {isBye ? (
        <TableCell align="right">
          <Typography variant="body2" color="text.secondary" noWrap>
            sat out - win credited
          </Typography>
        </TableCell>
      ) : (
        nameCell(match.participantB, 'end')
      )}
    </TableRow>
  );
}

function StandingsTable({
  standings,
  hoveredId,
  onHover,
  showsPlayoffDestination = false,
  singleBracketPlayoff = false,
}: {
  standings: StandingRow[];
  /**
   * Cross-referencing hover, for the places a participant appears more than once on screen - a group's
   * standings beside its match table, say. Omit both on a final standings table, where every
   * participant has exactly one row and there is nothing to cross-reference.
   */
  hoveredId?: string | null;
  onHover?: (participantId: string | null) => void;
  /**
   * True for a Group Stage + Playoff group or Swiss pool: rows are colored and labeled by where the
   * position sends the participant, instead of the trophy-styled final rank the Leaderboard tab uses
   * - a group's rank 1 isn't a tournament placement. The destination comes from the server, which
   * knows the playoff's shape and cut; the table never derives it.
   */
  showsPlayoffDestination?: boolean;
  /** A single-elimination playoff has one bracket, so its qualifiers are simply "Qualified". */
  singleBracketPlayoff?: boolean;
}) {
  return (
    // See GroupMatchesTable: minWidth: 0 is what lets the container scroll instead of overflowing.
    <Stack spacing={1} sx={{ minWidth: 0 }}>
      <Typography variant="subtitle2" color="text.secondary">
        Standings
      </Typography>
      <TableContainer component={Paper} variant="outlined">
        <Table size="small" sx={{ minWidth: GROUP_TABLE_MIN_WIDTH }}>
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
            {standings.map((row) => {
              const destination = row.playoffDestination;
              const tier = destination ? PLAYOFF_DESTINATIONS[destination] : null;
              const colors = showsPlayoffDestination ? tier : RANK_COLORS[row.rank];
              const isHovered = row.participantId === hoveredId;
              // A single-elimination playoff has one bracket, so reaching it is simply "Qualified".
              const wording =
                singleBracketPlayoff && destination === 'Upper'
                  ? { full: 'Qualified', short: 'Q' }
                  : { full: tier?.label ?? '', short: tier?.short ?? '' };
              return (
                <TableRow
                  key={row.participantId}
                  onMouseEnter={onHover && (() => onHover(row.participantId))}
                  onMouseLeave={onHover && (() => onHover(null))}
                  sx={{
                    bgcolor: colors?.bg ?? 'transparent',
                    boxShadow: isHovered ? 'inset 0 0 0 2px rgba(124,156,255,0.8)' : 'none',
                  }}
                >
                  <TableCell>
                    {showsPlayoffDestination ? (
                      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                        <Typography variant="body2">{row.rank}</Typography>
                        {tier && (
                          <Chip
                            size="small"
                            label={<ResponsiveChipLabel full={wording.full} short={wording.short} />}
                            sx={{
                              color: tier.text,
                              bgcolor: tier.bg,
                              fontWeight: 600,
                              '& .MuiChip-label': { px: 1 },
                            }}
                          />
                        )}
                      </Stack>
                    ) : (
                      <PlaceCell rankStart={row.rank} rankEnd={row.rank} />
                    )}
                  </TableCell>
                  <TableCell>
                    {/* No hover weight change - see slotHighlight; the row's ring is the highlight. */}
                    <ParticipantLabel name={row.name} emoji={row.emoji} />
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

function PlacementsList({ placements }: { placements: PlacementGroup[] }) {
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
                      {group.participants.map((participant) => (
                        <ParticipantLabel
                          key={participant.participantId}
                          name={participant.name}
                          emoji={participant.emoji}
                        />
                      ))}
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
