export type TournamentType = 'SingleElimination' | 'DoubleElimination' | 'RoundRobin';
export type MatchFormat = 'Bo1' | 'Bo3' | 'Bo5' | 'Bo7';
export type TournamentStatus = 'Planned' | 'Running' | 'Finished';

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
  status: TournamentStatus;
  publicToken: string;
  createdAt: string;
}

export type MatchStatus = 'Pending' | 'InProgress' | 'Completed' | 'Forfeit';
export type BracketSegment = 'Winner' | 'Loser' | 'GrandFinal' | 'ThirdPlace' | 'RoundRobin';

export interface Participant {
  id: string;
  name: string;
  seed: number;
  hasBye: boolean;
}

export interface BracketSlot {
  participantId: string;
  name: string;
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
  played: number;
  wins: number;
  losses: number;
}

/** Single/Double Elimination only: one rung of the final-placements leaderboard; more than one participant means a tie. */
export interface PlacementGroup {
  rankStart: number;
  rankEnd: number;
  label: string;
  participants: BracketSlot[];
}

export interface Bracket {
  type: TournamentType;
  status: TournamentStatus;
  /** Round Robin: the flat round-by-round schedule (no advancement between rounds). */
  winnerRounds: BracketRound[];
  /** Double Elimination only; empty for Single Elimination and Round Robin. */
  loserRounds: BracketRound[];
  /** Double Elimination only; null for Single Elimination and Round Robin. */
  grandFinal: BracketMatch | null;
  /** Single Elimination only: a real match. Null for Double Elimination (see thirdPlacePodium) and Round Robin. */
  thirdPlace: BracketMatch | null;
  /** Double Elimination only: derived from the Loser Bracket Final's result - there is no separate match. */
  thirdPlacePodium: BracketSlot | null;
  /** Round Robin only: participants ranked by win-loss record; empty for other tournament types. */
  standings: StandingRow[];
  /** Single/Double Elimination only: Champion/Runner-up/3rd place and eliminations by round; empty for Round Robin. */
  placements: PlacementGroup[];
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
  status: TournamentStatus;
  participants: Participant[];
  bracket: Bracket | null;
}

/**
 * Smallest power of two >= n (the bracket size). Double Elimination has no 2-slot topology, so it
 * floors at 4 (mirrors DoubleEliminationBracket.ComputeBracketSize on the backend). Round Robin has
 * no bracket padding at all - its "size" is just the participant count.
 */
export function bracketSize(participantCount: number, type: TournamentType = 'SingleElimination'): number {
  if (participantCount < 2) return 0;
  if (type === 'RoundRobin') return participantCount;
  let size = 1;
  while (size < participantCount) size <<= 1;
  return type === 'DoubleElimination' ? Math.max(4, size) : size;
}

/** Number of first-round byes required for the given participant count. Round Robin never has admin-chosen byes. */
export function requiredByes(participantCount: number, type: TournamentType = 'SingleElimination'): number {
  if (type === 'RoundRobin') return 0;
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
}

export const tournamentTypeLabels: Record<TournamentType, string> = {
  SingleElimination: 'Single Elimination',
  DoubleElimination: 'Double Elimination',
  RoundRobin: 'Round Robin',
};

export const matchFormatLabels: Record<MatchFormat, string> = {
  Bo1: 'Best of 1',
  Bo3: 'Best of 3',
  Bo5: 'Best of 5',
  Bo7: 'Best of 7',
};

export const tournamentStatusLabels: Record<TournamentStatus, string> = {
  Planned: 'Planned',
  Running: 'Running',
  Finished: 'Finished',
};

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
  return { Bo1: 1, Bo3: 3, Bo5: 5, Bo7: 7 }[format];
}
