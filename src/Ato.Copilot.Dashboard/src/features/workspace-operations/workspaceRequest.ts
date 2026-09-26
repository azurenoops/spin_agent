import axios, { type AxiosRequestConfig, type AxiosResponse } from 'axios';

export class WorkspaceOperationError extends Error {
  constructor(message: string, public readonly status?: number, public readonly code?: string) {
    super(message);
  }
}

function unwrap<T>(response: AxiosResponse<unknown>): T {
  const body = response.data as {
    status?: string;
    data?: T;
    error?: { code?: string; errorCode?: string; message?: string };
  };
  if (body?.data !== undefined && body.status !== 'error') return body.data;
  throw new WorkspaceOperationError(
    body?.error?.message ?? 'Unexpected workspace operation response.',
    response.status,
    body?.error?.code ?? body?.error?.errorCode,
  );
}

export async function workspaceRequest<T>(config: AxiosRequestConfig): Promise<T> {
  try {
    return unwrap<T>(await axios.request(config));
  } catch (error) {
    if (axios.isAxiosError(error)) {
      const detail = error.response?.data as { error?: { message?: string; code?: string; errorCode?: string } } | undefined;
      throw new WorkspaceOperationError(
        detail?.error?.message ?? error.message,
        error.response?.status,
        detail?.error?.code ?? detail?.error?.errorCode,
      );
    }
    throw error;
  }
}
