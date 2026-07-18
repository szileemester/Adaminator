import { apiClient } from './client';
import type {
  Bracket,
  Participant,
  PublicTournament,
  Tournament,
  TournamentInput,
  TournamentSummary,
} from './types';

export async function login(password: string): Promise<{ token: string; expiresAt: string }> {
  const { data } = await apiClient.post('/api/auth/login', { password });
  return data;
}

export async function listTournaments(): Promise<TournamentSummary[]> {
  const { data } = await apiClient.get('/api/tournaments');
  return data;
}

export async function getTournament(id: string): Promise<Tournament> {
  const { data } = await apiClient.get(`/api/tournaments/${id}`);
  return data;
}

export async function createTournament(input: TournamentInput): Promise<Tournament> {
  const { data } = await apiClient.post('/api/tournaments', input);
  return data;
}

export async function updateTournament(id: string, input: TournamentInput): Promise<Tournament> {
  const { data } = await apiClient.put(`/api/tournaments/${id}`, input);
  return data;
}

export async function deleteTournament(id: string): Promise<void> {
  await apiClient.delete(`/api/tournaments/${id}`);
}

export async function getPublicTournament(token: string): Promise<PublicTournament> {
  const { data } = await apiClient.get(`/api/public/tournaments/${token}`);
  return data;
}

// ---- Participants ----

export async function listParticipants(tournamentId: string): Promise<Participant[]> {
  const { data } = await apiClient.get(`/api/tournaments/${tournamentId}/participants`);
  return data;
}

export async function addParticipant(tournamentId: string, name: string, emoji: string | null = null): Promise<Participant> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/participants`, { name, emoji });
  return data;
}

/** Carries the emoji alongside the name; re-sending the stored one is a no-op server-side, changing it is rejected. */
export async function updateParticipant(
  tournamentId: string,
  participantId: string,
  name: string,
  emoji: string | null = null,
): Promise<Participant> {
  const { data } = await apiClient.put(`/api/tournaments/${tournamentId}/participants/${participantId}`, { name, emoji });
  return data;
}

export async function removeParticipant(tournamentId: string, participantId: string): Promise<void> {
  await apiClient.delete(`/api/tournaments/${tournamentId}/participants/${participantId}`);
}

// ---- Bracket ----

export async function generateBracket(tournamentId: string): Promise<Participant[]> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/bracket/generate`);
  return data;
}

export async function drawGroups(tournamentId: string): Promise<Participant[]> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/bracket/draw-groups`);
  return data;
}

export async function updateBracket(tournamentId: string, order: string[], byes: string[]): Promise<Participant[]> {
  const { data } = await apiClient.put(`/api/tournaments/${tournamentId}/bracket`, { order, byes });
  return data;
}

export async function startTournament(tournamentId: string): Promise<Tournament> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/start`);
  return data;
}

export async function finishTournament(tournamentId: string): Promise<Tournament> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/finish`);
  return data;
}

export async function startPlayoffs(tournamentId: string): Promise<Tournament> {
  const { data } = await apiClient.post(`/api/tournaments/${tournamentId}/start-playoffs`);
  return data;
}

export async function getBracket(tournamentId: string): Promise<Bracket> {
  const { data } = await apiClient.get(`/api/tournaments/${tournamentId}/bracket`);
  return data;
}
