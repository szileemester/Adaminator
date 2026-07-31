import { apiClient } from './client';

/** Which side took the most recent game. Null before any has been recorded, or after a draw. */
export type UnmatchedTeam = 'Fiuk' | 'Lanyok';

export interface UnmatchedPick {
  playerName: string;
  character: string;
}

export interface UnmatchedScoreboard {
  fiukWins: number;
  lanyokWins: number;
  lastVictor: UnmatchedTeam | null;
  picks: UnmatchedPick[];
  updatedAt: string;
}

/** Readable without a login, so the page can be shared with the other players by link alone. */
export async function getUnmatchedScoreboard(): Promise<UnmatchedScoreboard> {
  const { data } = await apiClient.get('/api/unmatched');
  return data;
}

/** Requires the admin login - the page's hidden editor is a gesture, not a credential. */
export async function saveUnmatchedScoreboard(
  scoreboard: Pick<UnmatchedScoreboard, 'fiukWins' | 'lanyokWins' | 'lastVictor' | 'picks'>,
): Promise<UnmatchedScoreboard> {
  const { data } = await apiClient.put('/api/unmatched', scoreboard);
  return data;
}
