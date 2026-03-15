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
