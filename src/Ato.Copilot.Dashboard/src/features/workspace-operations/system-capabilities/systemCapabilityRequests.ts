export async function boundedRequest<T>(
  request: (signal: AbortSignal) => Promise<T>, signal?: AbortSignal, milliseconds = 30000,
): Promise<T> {
  const controller = new AbortController();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let cancel: () => void = () => undefined;
  const interrupted = new Promise<never>((_resolve, reject) => {
    cancel = () => {
      controller.abort(signal?.reason);
      reject(new Error('The request was cancelled.'));
    };
    timer = setTimeout(() => {
      controller.abort('timeout');
      reject(new Error('The request timed out. Refresh saved server state before retrying a write.'));
    }, milliseconds);
    if (signal?.aborted) cancel();
    else signal?.addEventListener('abort', cancel, { once: true });
  });
  try { return await Promise.race([request(controller.signal), interrupted]); }
  finally {
    clearTimeout(timer);
    signal?.removeEventListener('abort', cancel);
  }
}

export function sameScope(
  operation: { tenantId: string; systemId: string; kind: string },
  tenantId: string, systemId: string, kind: 'Setup' | 'Removal',
) {
  return operation.tenantId.toLowerCase() === tenantId.toLowerCase()
    && operation.systemId.toLowerCase() === systemId.toLowerCase() && operation.kind === kind;
}
