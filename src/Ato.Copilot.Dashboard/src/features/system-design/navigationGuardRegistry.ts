type HistoryGuard = (event: PopStateEvent) => void;
const guards = new Set<HistoryGuard>();

// Register before BrowserRouter: window-target popstate listeners can otherwise
// unmount an editor before its late-mounted listener gets a chance to cancel.
window.addEventListener('popstate', event => {
  for (const guard of guards) guard(event);
}, true);

export function registerHistoryGuard(guard: HistoryGuard): () => void {
  guards.add(guard);
  return () => { guards.delete(guard); };
}
