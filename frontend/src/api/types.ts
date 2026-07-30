export type TournamentType = 'SingleElimination' | 'DoubleElimination' | 'RoundRobin' | 'GroupStagePlayoff';
/** Bo2 is draw-capable (both games played, a 1-1 is a draw) and only offered for the Group Stage match format - every other format picker (Upper/Lower/Grand Final/Match format) is decisive-only. */
export type MatchFormat = 'Bo1' | 'Bo2' | 'Bo3' | 'Bo5' | 'Bo7';
export type TournamentStatus = 'Planned' | 'Running' | 'Finished';
/** How standings ties that change an outcome are resolved. Meaningful only for Round Robin and Group Stage + Playoff. */
export type TiebreakerPolicy = 'ComputedThenMatch' | 'AlwaysMatch';
/** Group Stage + Playoff only: how the group stage is played before the playoff is seeded. */
export type GroupStageKind = 'RoundRobin' | 'Swiss';
/** Group Stage + Playoff only: the playoff's elimination structure. */
export type PlayoffKind = 'DoubleElimination' | 'SingleElimination';
/** Where finishing in a given standings position sends a participant. */
export type LevelOutcome = 'Upper' | 'Lower' | 'Contested' | 'Eliminated';

export interface TournamentSummary {
  id: string;
  name: string;
  date: string;
  type: TournamentType;
  status: TournamentStatus;
  participantCount: number;
}

export interface Tournament {
  id: string;
  name: string;
  date: string;
  notes: string | null;
  type: TournamentType;
  /** Single Elimination + Round Robin only: the tournament's one match format. Equal to one of the segment formats below for every other type (unused). */
  defaultMatchFormat: MatchFormat;
  thirdPlaceEnabled: boolean;
  defaultScoreType: ScoreType;
  /** Group Stage + Playoff with round-robin groups: number of groups; 0 for Swiss and other types. */
  groupCount: number;
  /** Group Stage + Playoff only: how the group stage is played. */
  groupStageKind: GroupStageKind;
  /** Group Stage + Playoff only: the playoff's structure. */
  playoffKind: PlayoffKind;
  /** Group Stage + Playoff only: the admin's raw choice; 0 means "the largest capacity the roster fills". */
  playoffSize: number;
  /** Group Stage + Playoff with a Swiss group stage: the admin's raw choice; 0 means ceil(log2 roster). */
  swissRounds: number;
  /** The playoff cut actually in force, with 0 resolved against the current roster. */
  playoffCapacity: number;
  /** The Swiss round count actually in force, with 0 resolved against the current roster. */
  resolvedSwissRounds: number;
  /**
   * Fewest participants these settings can start with, computed by the domain from every start-time
   * rule (playoff cut, group count, Swiss rounds). The roster editor floors its player count here
   * rather than restating those rules, which would drift the moment one of them changes.
   */
  minParticipants: number;
  /** Round Robin + Group Stage + Playoff: how standings ties are resolved. */
  tiebreakerPolicy: TiebreakerPolicy;
  /** Group Stage + Playoff only: how group matches are played and scored - any format, including Bo2 (draws, ranked by games won). */
  groupStageMatchFormat: MatchFormat;
  /** Double Elimination + Group Stage + Playoff only: the Winner Bracket's match format. */
  upperBracketFormat: MatchFormat;
  /** Double Elimination + Group Stage + Playoff only: the Loser Bracket's match format. */
  lowerBracketFormat: MatchFormat;
  /** Double Elimination + Group Stage + Playoff only: the Grand Final's match format. */
  grandFinalFormat: MatchFormat;
  status: TournamentStatus;
  publicToken: string;
  createdAt: string;
}

export type MatchStatus = 'Pending' | 'InProgress' | 'Completed' | 'Forfeit';
export type BracketSegment = 'Winner' | 'Loser' | 'GrandFinal' | 'ThirdPlace' | 'RoundRobin' | 'Tiebreaker';

export interface Participant {
  id: string;
  name: string;
  /** Optional display emoji. Null until chosen, and freely changeable while the tournament is Planned. */
  emoji: string | null;
  seed: number;
  hasBye: boolean;
  /** Group Stage + Playoff only: the group this participant was drawn into; null otherwise. */
  groupIndex: number | null;
}

export interface BracketSlot {
  participantId: string;
  name: string;
  emoji: string | null;
}

export type ScoreType = 'Games' | 'Points';

export interface ScoreEntry {
  sequenceNumber: number;
  scoreA: number | null;
  scoreB: number | null;
  participantAWon: boolean;
}

export interface BracketMatch {
  id: string;
  segment: BracketSegment;
  round: number;
  indexInRound: number;
  participantA: BracketSlot | null;
  participantB: BracketSlot | null;
  status: MatchStatus;
  winnerId: string | null;
  matchFormat: MatchFormat;
  scoreType: ScoreType;
  entries: ScoreEntry[];
  aggregateScoreA: number;
  aggregateScoreB: number;
  completedAt: string | null;
  canUndo: boolean;
}

export interface BracketRound {
  round: number;
  title: string;
  matches: BracketMatch[];
}

export interface StandingRow {
  rank: number;
  participantId: string;
  name: string;
  emoji: string | null;
  played: number;
  wins: number;
  losses: number;
  /** Total games won - the primary ranking key for a Best-of-2 group. */
  gamesWon: number;
  /**
   * Group Stage + Playoff only: where finishing in this position sends the participant. Computed from
   * the playoff cut on the server, so the table never has to guess the split itself. Null for a plain
   * Round Robin, which has no playoff to qualify for.
   */
  playoffDestination: LevelOutcome | null;
}

/** Single/Double Elimination only: one rung of the final-placements leaderboard; more than one participant means a tie. */
export interface PlacementGroup {
  rankStart: number;
  rankEnd: number;
  label: string;
  participants: BracketSlot[];
}

/** Group Stage + Playoff only: one group's round-robin schedule, standings, and any played tie-breaker matches. */
export interface Group {
  groupIndex: number;
  rounds: BracketRound[];
  standings: StandingRow[];
  /** Played tie-breaker matches for this group; empty unless a straddling tie needed resolving. */
  tiebreakerRounds: BracketRound[];
}

/**
 * Group Stage + Playoff only: which variant is being played, plus - for a Swiss group stage, whose
 * single pool has no `Group` to hang them on - the pool's own schedule, standings and round progress.
 */
export interface GroupStage {
  kind: GroupStageKind;
  playoffKind: PlayoffKind;
  /** Swiss only: the single pool's round-by-round schedule. Empty for round-robin groups. */
  rounds: BracketRound[];
  /** Swiss only: the single pool's standings. Empty for round-robin groups. */
  standings: StandingRow[];
  roundsPlayed: number;
  /** Swiss only: how many rounds the pool plays in total; 0 for round-robin groups. */
  roundsTotal: number;
  canStartNextRound: boolean;
}

export interface Bracket {
  type: TournamentType;
  status: TournamentStatus;
  /** Round Robin: the flat round-by-round schedule. Group Stage + Playoff: the double-elimination playoff's winner bracket. */
  winnerRounds: BracketRound[];
  /** Double Elimination + Group Stage + Playoff; empty otherwise. */
  loserRounds: BracketRound[];
  /** Double Elimination + Group Stage + Playoff; null otherwise. */
  grandFinal: BracketMatch | null;
  /** Single Elimination only: a real match. Null for Double Elimination (see thirdPlacePodium) and Round Robin. */
  thirdPlace: BracketMatch | null;
  /** Double Elimination + Group Stage + Playoff: derived from the Loser Bracket Final's result - there is no separate match. */
  thirdPlacePodium: BracketSlot | null;
  /** Round Robin only: participants ranked by win-loss record; empty for other tournament types. */
  standings: StandingRow[];
  /** Single/Double Elimination + Group Stage + Playoff: Champion/Runner-up/3rd place and eliminations by round; empty for Round Robin. */
  placements: PlacementGroup[];
  /** Group Stage + Playoff only: each group's schedule + standings; empty for other types. */
  groups: Group[];
  /** Round Robin only: played tie-breaker matches; Group Stage + Playoff carries these per-group on each Group. */
  tiebreakerRounds: BracketRound[];
  /** Round Robin + Group Stage + Playoff: true when a standings tie needs played tie-breaker matches generated. */
  needsTiebreakers: boolean;
  /** Group Stage + Playoff only: true once the group stage is done and the admin can generate the playoff. */
  canStartPlayoffs: boolean;
  /** True once every deciding match is decided and the admin can finish the tournament by hand. */
  canFinish: boolean;
  /** Group Stage + Playoff only; null for every other type. */
  groupStage: GroupStage | null;
}

export interface PublicTournament {
  name: string;
  date: string;
  notes: string | null;
  type: TournamentType;
  defaultMatchFormat: MatchFormat;
  defaultScoreType: ScoreType;
  groupCount: number;
  tiebreakerPolicy: TiebreakerPolicy;
  /** Group Stage + Playoff only: how group matches are played and scored - any format, including Bo2 (draws, ranked by games won). */
  groupStageMatchFormat: MatchFormat;
  /** Double Elimination + Group Stage + Playoff only: the Winner Bracket's match format. */
  upperBracketFormat: MatchFormat;
  /** Double Elimination + Group Stage + Playoff only: the Loser Bracket's match format. */
  lowerBracketFormat: MatchFormat;
  /** Double Elimination + Group Stage + Playoff only: the Grand Final's match format. */
  grandFinalFormat: MatchFormat;
  groupStageKind: GroupStageKind;
  playoffKind: PlayoffKind;
  playoffCapacity: number;
  resolvedSwissRounds: number;
  status: TournamentStatus;
  participants: Participant[];
  bracket: Bracket | null;
}

/** Roster limits, mirroring Tournament.MinParticipants / Tournament.MaxParticipants on the backend. */
export const MIN_PARTICIPANTS = 2;
export const MAX_PARTICIPANTS = 32;

/** Name limits, mirroring Tournament.NotesMaxLength on the backend. */
export const NOTES_MAX_LENGTH = 2000;

/** Name limits, mirroring Tournament.NameMaxLength / Participant.NameMaxLength on the backend. */
export const TOURNAMENT_NAME_MAX_LENGTH = 50;
export const PARTICIPANT_NAME_MAX_LENGTH = 30;

/**
 * Smallest power of two >= n (the bracket size). Double Elimination has no 2-slot topology, so it
 * floors at 4 (mirrors DoubleEliminationBracket.ComputeBracketSize on the backend). Round Robin and
 * Group Stage + Playoff have no bracket padding - their "size" is just the participant count (the
 * latter requires a power-of-two roster anyway, and every participant reaches the playoff).
 */
export function bracketSize(participantCount: number, type: TournamentType = 'SingleElimination'): number {
  if (participantCount < 2) return 0;
  if (type === 'RoundRobin' || type === 'GroupStagePlayoff') return participantCount;
  let size = 1;
  while (size < participantCount) size <<= 1;
  return type === 'DoubleElimination' ? Math.max(4, size) : size;
}

/**
 * Number of first-round byes required for the given participant count. Round Robin and Group Stage +
 * Playoff never have admin-chosen byes (the former sits participants out per round, the latter draws
 * groups instead).
 */
export function requiredByes(participantCount: number, type: TournamentType = 'SingleElimination'): number {
  if (type === 'RoundRobin' || type === 'GroupStagePlayoff') return 0;
  return participantCount < 2 ? 0 : bracketSize(participantCount, type) - participantCount;
}

export interface TournamentInput {
  name: string;
  date: string;
  notes?: string | null;
  type: TournamentType;
  defaultMatchFormat: MatchFormat;
  thirdPlaceEnabled: boolean;
  defaultScoreType: ScoreType;
  groupCount: number;
  tiebreakerPolicy: TiebreakerPolicy;
  groupStageMatchFormat: MatchFormat;
  upperBracketFormat: MatchFormat;
  lowerBracketFormat: MatchFormat;
  grandFinalFormat: MatchFormat;
  groupStageKind: GroupStageKind;
  playoffKind: PlayoffKind;
  /** 0 means "the largest capacity the roster fills". */
  playoffSize: number;
  /** 0 means ceil(log2 roster). */
  swissRounds: number;
}

export const groupStageKindLabels: Record<GroupStageKind, string> = {
  RoundRobin: 'Round Robin groups',
  Swiss: 'Swiss (single pool)',
};

export const playoffKindLabels: Record<PlayoffKind, string> = {
  DoubleElimination: 'Double Elimination',
  SingleElimination: 'Single Elimination',
};

/** The playoff cuts an admin may choose, plus 0 for "the largest that fits the roster". */
export const playoffSizes = [0, 4, 8, 16, 32] as const;

/**
 * Which settings a tournament shape actually uses. Shared by the create/edit form and the detail
 * page's Overview so the two can never disagree about what applies - a setting the form hides is
 * exactly the one the Overview must not report.
 */
export function tournamentSettingsShape(
  type: TournamentType,
  groupStageKind: GroupStageKind,
  playoffKind: PlayoffKind,
) {
  const isGroupStagePlayoff = type === 'GroupStagePlayoff';
  const playoffIsSingleElimination =
    type === 'SingleElimination' || (isGroupStagePlayoff && playoffKind === 'SingleElimination');

  return {
    isGroupStagePlayoff,
    isRoundRobin: type === 'RoundRobin',
    // A Swiss group stage is one pool, so it has no group count; round-robin groups have no round count.
    usesRoundRobinGroups: isGroupStagePlayoff && groupStageKind === 'RoundRobin',
    usesSwissPool: isGroupStagePlayoff && groupStageKind === 'Swiss',
    // Third Place belongs to whichever bracket is single elimination - standalone, or a playoff.
    playoffIsSingleElimination,
    // Single Elimination and Round Robin have one kind of match; the others split it by segment.
    showsSingleFormat: type === 'SingleElimination' || type === 'RoundRobin',
    usesBracketFormats: type === 'DoubleElimination' || isGroupStagePlayoff,
    // A single-elimination playoff has no Loser Bracket and no Grand Final, so one picker covers it.
    showsAllBracketFormats:
      type === 'DoubleElimination' || (isGroupStagePlayoff && playoffKind === 'DoubleElimination'),
    // Only these two produce standings that can tie in a way that changes an outcome.
    showsTiebreakerPolicy: type === 'RoundRobin' || isGroupStagePlayoff,
    showsRules: playoffIsSingleElimination || type === 'RoundRobin' || isGroupStagePlayoff,
  };
}

export const tournamentTypeLabels: Record<TournamentType, string> = {
  SingleElimination: 'Single Elimination',
  DoubleElimination: 'Double Elimination',
  RoundRobin: 'Round Robin',
  GroupStagePlayoff: 'Group Stage + Playoff',
};

export const matchFormatLabels: Record<MatchFormat, string> = {
  Bo1: 'Best of 1',
  Bo2: 'Best of 2',
  Bo3: 'Best of 3',
  Bo5: 'Best of 5',
  Bo7: 'Best of 7',
};

export const tiebreakerPolicyLabels: Record<TiebreakerPolicy, string> = {
  ComputedThenMatch: 'Head-to-head, then a decider match',
  AlwaysMatch: 'Always play a decider match',
};

export const tournamentStatusLabels: Record<TournamentStatus, string> = {
  Planned: 'Planned',
  Running: 'Running',
  Finished: 'Finished',
};

/** Display name for a 0-based group index: "Group A", "Group B", … */
export function groupLabel(groupIndex: number): string {
  return `Group ${String.fromCharCode(65 + groupIndex)}`;
}

/** Shown in place of a real emoji until the admin picks one - a participant is never displayed with no emoji at all. */
export const DEFAULT_PARTICIPANT_EMOJI = '😐';

/** Name prefixed with the emoji (or the "not set" placeholder), for the few places that take a plain string instead of JSX (Chip/TextField labels, dialog titles). */
export function formatParticipantName(name: string, emoji: string | null | undefined): string {
  return `${emoji ?? DEFAULT_PARTICIPANT_EMOJI} ${name}`;
}

export const scoreTypeLabels: Record<ScoreType, string> = {
  Games: 'Games',
  Points: 'Points',
};

/** Number of game/set wins one participant needs to win a match of the given format. */
export function requiredWins(format: MatchFormat): number {
  return Math.ceil(matchFormatGameCount(format) / 2);
}

/** The most games/sets a match of the given format can ever be decided in. */
export function matchFormatGameCount(format: MatchFormat): number {
  return { Bo1: 1, Bo2: 2, Bo3: 3, Bo5: 5, Bo7: 7 }[format];
}

/** An even format (Best of 2) plays both games and may end level, so a match can be a draw. */
export function allowsDraw(format: MatchFormat): boolean {
  return matchFormatGameCount(format) % 2 === 0;
}

/**
 * Whether a match has a final result. The one definition of "terminal" over {@link MatchStatus}, so a
 * new terminal state is a single edit rather than a hunt through every screen that shows a score.
 */
export function isDecided(match: { status: MatchStatus }): boolean {
  return match.status === 'Completed' || match.status === 'Forfeit';
}

/** Whether a round exists and every match in it is decided. */
export function roundIsDecided(round: BracketRound): boolean {
  return round.matches.length > 0 && round.matches.every(isDecided);
}

/**
 * Locates a match anywhere in a bracket. Deliberately lives next to the `Bracket` interface: it has to
 * name every collection that can hold matches, and a new one added above without a matching line here
 * fails silently - clicking such a match would simply open nothing. A Swiss group stage is the easy
 * one to miss, since it has no groups and its pool rounds hang off `groupStage` alone.
 */
export function findBracketMatch(bracket: Bracket, matchId: string): BracketMatch | null {
  const rounds = [
    ...bracket.winnerRounds,
    ...bracket.loserRounds,
    ...bracket.tiebreakerRounds,
    ...bracket.groups.flatMap((group) => [...group.rounds, ...group.tiebreakerRounds]),
    ...(bracket.groupStage?.rounds ?? []),
  ];
  for (const round of rounds) {
    const found = round.matches.find((match) => match.id === matchId);
    if (found) {
      return found;
    }
  }

  // The two matches that sit outside any round.
  return [bracket.grandFinal, bracket.thirdPlace].find((match) => match?.id === matchId) ?? null;
}
