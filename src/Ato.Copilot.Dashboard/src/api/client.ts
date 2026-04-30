import axios from 'axios';
import type { ErrorResponse } from '../types/dashboard';

const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api/dashboard',
  headers: { 'Content-Type': 'application/json' },
});

apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }

  // Dev-only simulated role header (FR-048).
  // Ignored when real CAC auth is active on the server.
  try {
    const raw = localStorage.getItem('ato-dashboard-settings');
    if (raw) {
      const settings = JSON.parse(raw) as { role?: string };
      if (settings.role) {
        config.headers['X-Simulated-Role'] = settings.role;
      }
    }
  } catch {
    // Ignore parse errors
  }

  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    if (axios.isAxiosError(error)) {
      const status = error.response?.status;
      const originalRequest = error.config as (typeof error.config & { __retriedWithoutAuth?: boolean }) | undefined;

      // Recover from stale local tokens: clear token and retry once without Authorization.
      if (status === 401 && originalRequest && !originalRequest.__retriedWithoutAuth) {
        originalRequest.__retriedWithoutAuth = true;

        try {
          localStorage.removeItem('auth_token');
        } catch {
          // Ignore storage errors
        }

        if (originalRequest.headers) {
          const headers = originalRequest.headers as Record<string, unknown> & {
            set?: (name: string, value: string | undefined) => void;
            delete?: (name: string) => void;
          };

          // Axios may use AxiosHeaders in browser builds; clear both key variants defensively.
          headers.set?.('Authorization', undefined);
          headers.set?.('authorization', undefined);
          headers.delete?.('Authorization');
          headers.delete?.('authorization');
          delete headers.Authorization;
          delete headers.authorization;
        }

        return apiClient.request(originalRequest);
      }

      if (error.response?.data) {
        const errorResponse = error.response.data as ErrorResponse;
        return Promise.reject(errorResponse);
      }
    }

    return Promise.reject(error);
  },
);

export default apiClient;
