export type TournamentType = 'SingleElimination' | 'DoubleElimination' | 'RoundRobin' | 'GroupStagePlayoff';
/** Bo2 is draw-capable (both games played, a 1-1 is a draw) and only used for a Best-of-2 group stage - never in the tournament's default-format picker. */
export type MatchFormat = 'Bo1' | 'Bo2' | 'Bo3' | 'Bo5' | 'Bo7';
export type TournamentStatus = 'Planned' | 'Running' | 'Finished';
/** How standings ties that change an outcome are resolved. Meaningful only for Round Robin and Group Stage + Playoff. */
export type TiebreakerPolicy = 'ComputedThenMatch' | 'AlwaysMatch';
/** Group Stage + Playoff only: how the group matches are played and scored. */
export type GroupStageFormat = 'Standard' | 'BestOfTwo';

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
  defaultMatchFormat: MatchFormat;
  thirdPlaceEnabled: boolean;
  defaultScoreType: ScoreType;
  /** Group Stage + Playoff only: number of groups; 0 for other types. */
  groupCount: number;
  /** Round Robin + Group Stage + Playoff: how standings ties are resolved. */
  tiebreakerPolicy: TiebreakerPolicy;
  /** Group Stage + Playoff only: Standard, or Best-of-2 ranked by games won. */
  groupStageFormat: GroupStageFormat;
  status: TournamentStatus;
  publicToken: string;
  createdAt: string;
}

export type MatchStatus = 'Pending' | 'InProgress' | 'Completed' | 'Forfeit';
export type BracketSegment = 'Winner' | 'Loser' | 'GrandFinal' | 'ThirdPlace' | 'RoundRobin' | 'Tiebreaker';

export interface Participant {
  id: string;
  name: string;
  /** Optional display emoji. Null until chosen, and write-once thereafter (the API rejects a change). */
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

export type ScoreType = 'WinnerOnly' | 'Games' | 'Points' | 'Sets';

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
  scoreType: ScoreType | null;
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
  /** Group Stage + Playoff only: Standard, or Best-of-2 ranked by games won. */
  groupStageFormat: GroupStageFormat;
  status: TournamentStatus;
  participants: Participant[];
  bracket: Bracket | null;
}

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
  /** Group Stage + Playoff only: Standard, or Best-of-2 ranked by games won. */
  groupStageFormat: GroupStageFormat;
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

export const groupStageFormatLabels: Record<GroupStageFormat, string> = {
  Standard: 'Single decisive match',
  BestOfTwo: 'Best of 2 (ranked by games won)',
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

/** Name prefixed with the emoji, for the few places that take a plain string instead of JSX (Chip/TextField labels, dialog titles). */
export function formatParticipantName(name: string, emoji: string | null | undefined): string {
  return emoji ? `${emoji} ${name}` : name;
}

export const scoreTypeLabels: Record<ScoreType, string> = {
  WinnerOnly: 'Winner Only',
  Games: 'Games',
  Points: 'Points',
  Sets: 'Sets',
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
