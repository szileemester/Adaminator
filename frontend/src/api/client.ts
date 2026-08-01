import axios from 'axios';

const TOKEN_KEY = 'adaminator.token';
const EXPIRY_KEY = 'adaminator.token.expiresAt';

/** A stored token counts as gone once it expires - the server would only reject it anyway. */
function readToken(): string | null {
  const token = localStorage.getItem(TOKEN_KEY);
  if (!token) {
    return null;
  }

  const expiresAt = localStorage.getItem(EXPIRY_KEY);
  if (expiresAt && Date.parse(expiresAt) <= Date.now()) {
    clearToken();
    return null;
  }

  return token;
}

function clearToken() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(EXPIRY_KEY);
}

export const tokenStore = {
  get: readToken,
  set: (token: string, expiresAt?: string) => {
    localStorage.setItem(TOKEN_KEY, token);
    if (expiresAt) {
      localStorage.setItem(EXPIRY_KEY, expiresAt);
    } else {
      localStorage.removeItem(EXPIRY_KEY);
    }
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
