import axios from 'axios';

const TOKEN_KEY = 'adaminator.token';
const EXPIRY_KEY = 'adaminator.token.expiresAt';

function clearToken() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(EXPIRY_KEY);
}

/**
 * Plain storage for the session, with no opinion about whether it is still valid - deciding that is
 * AuthContext's job, because only it can act on the answer. Reporting an expired token as absent from
 * here would strip the header off the request that is about to fail, and the 401 handler below would
 * then see no token and leave the admin sitting on a page that can no longer load anything.
 */
export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  getExpiresAt: () => localStorage.getItem(EXPIRY_KEY),
  set: (token: string, expiresAt: string) => {
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(EXPIRY_KEY, expiresAt);
  },
  clear: clearToken,
};

export const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? 'http://localhost:5091',
});

apiClient.interceptors.request.use((config) => {
  const token = tokenStore.get();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

const LOGIN_PATH = '/api/auth/login';

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    // A 401 from the login call itself means the password was wrong, not that the session ended -
    // without this, mistyping the password on /login while already signed in would throw away a
    // perfectly good token and leave the app authenticated in memory with nothing to send.
    const isLoginAttempt = error.config?.url?.includes(LOGIN_PATH) ?? false;
    if (error.response?.status === 401 && !isLoginAttempt && tokenStore.get()) {
      // Token expired or invalid: drop it and send the admin back to login.
      tokenStore.clear();
      if (!window.location.pathname.startsWith('/login')) {
        window.location.assign('/login');
      }
    }
    return Promise.reject(error);
  },
);

/** Extracts a human-friendly message from an Axios error (ProblemDetails aware). */
export function extractErrorMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as { detail?: string; title?: string } | undefined;
    return data?.detail ?? data?.title ?? error.message ?? fallback;
  }
  return fallback;
}
