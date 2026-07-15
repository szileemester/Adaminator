import { apiClient } from './client';
import type { Bracket, MatchFormat, ScoreType } from './types';

export interface ScoreEntryInput {
  scoreA: number | null;
  scoreB: number | null;
  participantAWon: boolean;
}

export interface MatchResultInput {
  matchFormat: MatchFormat;
  scoreType: ScoreType;
  entries: ScoreEntryInput[];
}

export async function saveMatchResult(tournamentId: string, matchId: string, input: MatchResultInput): Promise<Bracket> {
  const { data } = await apiClient.put(`/api/tournaments/${tournamentId}/matches/${matchId}/result`, input);
  return data;
}

export async function completeMatch(tournamentId: string, matchId: string, input: MatchResultInput): Promise<Bracket> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/matches/${matchId}/complete`, input);
  return data;
}

export async function forfeitMatch(tournamentId: string, matchId: string, winnerId: string): Promise<Bracket> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/matches/${matchId}/forfeit`, { winnerId });
  return data;
}

export async function undoMatch(tournamentId: string, matchId: string): Promise<Bracket> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/matches/${matchId}/undo`);
  return data;
}
