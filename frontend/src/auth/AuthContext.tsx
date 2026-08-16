import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { login as loginRequest } from '../api/tournaments';
import { tokenStore } from '../api/client';

interface AuthContextValue {
  isAuthenticated: boolean;
  login: (password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

/**
 * The stored session, or null if it has already run out. Checking the expiry here means an 8-hour-old
 * session is recognised as over when the app loads, so ProtectedRoute redirects on arrival rather than
 * letting a whole form be filled in and then thrown away by the 401 handler. A session that expires
 * while the tab is open is still caught the other way round: the token is sent, the server rejects it,
 * and the 401 handler clears it and redirects.
 */
function readLiveToken(): string | null {
  const token = tokenStore.get();
  if (!token) {
    return null;
  }

  const expiresAt = tokenStore.getExpiresAt();
  if (expiresAt && Date.parse(expiresAt) <= Date.now()) {
    tokenStore.clear();
    return null;
  }

  return token;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(readLiveToken);

  const login = useCallback(async (password: string) => {
    const result = await loginRequest(password);
    tokenStore.set(result.token, result.expiresAt);
    setToken(result.token);
  }, []);

  const logout = useCallback(() => {
    tokenStore.clear();
    setToken(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ isAuthenticated: Boolean(token), login, logout }),
    [token, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
