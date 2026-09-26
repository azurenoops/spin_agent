import { useCallback, useState } from 'react';
import { message } from '../workspace-operations/workspaceUi';

export function usePublicationIntent(packageId: string) {
  const key = `ato:package-publication-intent:${packageId}`;
  const [state, setState] = useState<{ previewId: string | null; error: string | null }>(() => {
    try { return { previewId: sessionStorage.getItem(key), error: null }; }
    catch (reason) { return { previewId: null, error: `Unable to restore publication retry intent. Enable session storage and reload. ${message(reason)}` }; }
  });
  const begin = useCallback((previewId: string) => {
    // Store request identity only; approval and publication always come from the server.
    sessionStorage.setItem(key, previewId);
    setState({ previewId, error: null });
  }, [key]);
  const clear = useCallback(() => {
    try { sessionStorage.removeItem(key); setState({ previewId: null, error: null }); }
    catch (reason) { setState({ previewId: null, error: `Unable to clear publication retry intent. Enable session storage and reload. ${message(reason)}` }); }
  }, [key]);
  return { pendingPreviewId: state.previewId, error: state.error, begin, clear };
}
