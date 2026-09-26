import { isAxiosError } from 'axios';

export function isStaleSimulationSession(error: unknown): boolean {
  return isAxiosError<{ data?: { errorCode?: string } }>(error)
    && error.response?.data?.data?.errorCode === 'SIMULATED_IDENTITY_NOT_FOUND';
}
