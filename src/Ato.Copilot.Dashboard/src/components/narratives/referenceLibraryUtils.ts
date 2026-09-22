export function narrativeErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'error' in error) {
    if (typeof error.error === 'string') return error.error;
    if (error.error && typeof error.error === 'object' && 'message' in error.error && typeof error.error.message === 'string')
      return error.error.message;
  }
  return error instanceof Error ? error.message : 'The narrative request failed. Retry in the current authorized context.';
}

export function narrativeErrorCode(error: unknown): string | null {
  return error && typeof error === 'object' && 'errorCode' in error && typeof error.errorCode === 'string' ? error.errorCode : null;
}
