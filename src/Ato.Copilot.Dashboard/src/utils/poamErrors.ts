export function poamErrorMessage(value: unknown): string {
  if (value && typeof value === 'object') {
    if ('details' in value && typeof value.details === 'string') return value.details;
    if ('error' in value) {
      if (typeof value.error === 'string') return value.error;
      if (value.error && typeof value.error === 'object' && 'message' in value.error && typeof value.error.message === 'string') return value.error.message;
    }
    if ('response' in value && value.response && typeof value.response === 'object' && 'data' in value.response)
      return poamErrorMessage(value.response.data);
  }
  return value instanceof Error ? value.message : 'The request could not be confirmed. Refresh the retained record before retrying.';
}
