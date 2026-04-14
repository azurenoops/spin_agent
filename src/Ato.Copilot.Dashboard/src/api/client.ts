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
  (error) => {
    if (axios.isAxiosError(error) && error.response?.data) {
      const errorResponse = error.response.data as ErrorResponse;
      return Promise.reject(errorResponse);
    }
    return Promise.reject(error);
  },
);

export default apiClient;
