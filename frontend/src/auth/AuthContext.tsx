import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { login as loginRequest } from '../api/tournaments';
import { tokenStore } from '../api/client';

interface AuthContextValue {
  isAuthenticated: boolean;
  login: (password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(() => tokenStore.get());

  const login = useCallback(async (password: string) => {
    const result = await loginRequest(password);
    tokenStore.set(result.token);
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
