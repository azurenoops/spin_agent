import axios, { type AxiosRequestConfig } from 'axios';

interface PackageErrorEnvelope {
  error?: { code?: string; errorCode?: string; message?: string; suggestion?: string };
}

export class PackageImportError extends Error {
  constructor(message: string, public readonly status?: number, public readonly code?: string) {
    super(message);
    this.name = 'PackageImportError';
  }
}

function errorMessage(body: PackageErrorEnvelope | undefined, fallback: string) {
  return [body?.error?.message ?? fallback, body?.error?.suggestion].filter(Boolean).join(' ');
}

export async function packageRequest<T>(config: AxiosRequestConfig): Promise<T> {
  try {
    const response = await axios.request<PackageErrorEnvelope & { status?: string; data?: T }>(config);
    const body = response.data;
    if (body?.status === 'success' && body.data !== undefined) return body.data;
    throw new PackageImportError(
      errorMessage(body, 'The server did not return package data.'),
      response.status, body?.error?.code ?? body?.error?.errorCode,
    );
  } catch (error) {
    if (axios.isAxiosError<PackageErrorEnvelope>(error)) {
      const body = error.response?.data;
      throw new PackageImportError(errorMessage(body, error.message), error.response?.status, body?.error?.code ?? body?.error?.errorCode);
    }
    throw error;
  }
}
