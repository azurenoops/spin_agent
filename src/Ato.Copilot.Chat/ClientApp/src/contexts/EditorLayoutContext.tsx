// =============================================================================
// EditorLayoutContext — Issue #256 ARD: Editor Shell v2 Full-Bleed Focused
//
// Provides { mode, sidebarWidth, panelWidth } to all child components so they
// can adapt their footprint without hardcoding width/position assumptions.
//
// Three modes:
//   focused  — sidebar 0px, AppBar micro-bar 36px, canvas full-bleed
//   standard — sidebar 240px, AppBar 56px, editor ~860px (v1 parity)
//   research — sidebar 56px icon-rail, right panel 360px
//
// Persistence: localStorage key `editorLayout:[conversationId]`.
// Falls back to 'standard' if no stored value.
//
// prefers-reduced-motion: consumers should skip CSS transitions when this
// media query matches. The context exposes `reducedMotion` for convenience.
// =============================================================================

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react';

// ─── Types ────────────────────────────────────────────────────────────────────

export type LayoutMode = 'focused' | 'standard' | 'research';

export interface EditorLayoutValue {
  /** Current layout mode. */
  mode: LayoutMode;
  /** Pixel width of the left sidebar in the current mode. */
  sidebarWidth: number;
  /** Pixel width of the right panel area in the current mode. */
  panelWidth: number;
  /** true when prefers-reduced-motion is active — skip transitions. */
  reducedMotion: boolean;
  /** Switch to a different mode. */
  setMode: (mode: LayoutMode) => void;
}

// ─── Width constants ──────────────────────────────────────────────────────────

export const SIDEBAR_WIDTHS: Record<LayoutMode, number> = {
  focused: 0,
  standard: 240,
  research: 56,
};

export const PANEL_WIDTHS: Record<LayoutMode, number> = {
  focused: 0,
  standard: 0,    // TraceabilityPanel is conditionally shown, not always present
  research: 360,
};

// ─── localStorage helpers ─────────────────────────────────────────────────────

const LS_PREFIX = 'editorLayout';

function storageKey(conversationId: string | null): string {
  return conversationId ? `${LS_PREFIX}:${conversationId}` : LS_PREFIX;
}

function loadMode(conversationId: string | null): LayoutMode {
  try {
    const raw = localStorage.getItem(storageKey(conversationId));
    if (raw === 'focused' || raw === 'standard' || raw === 'research') {
      return raw;
    }
  } catch {
    // SSR / storage unavailable
  }
  return 'standard';
}

function saveMode(conversationId: string | null, mode: LayoutMode): void {
  try {
    localStorage.setItem(storageKey(conversationId), mode);
  } catch {
    // Storage unavailable — non-fatal
  }
}

// ─── Context ──────────────────────────────────────────────────────────────────

const EditorLayoutContext = createContext<EditorLayoutValue>({
  mode: 'standard',
  sidebarWidth: SIDEBAR_WIDTHS.standard,
  panelWidth: PANEL_WIDTHS.standard,
  reducedMotion: false,
  setMode: () => {},
});

// ─── Provider ─────────────────────────────────────────────────────────────────

export interface EditorLayoutProviderProps {
  /** The active conversation/document ID used for per-document persistence. */
  conversationId: string | null;
  children: React.ReactNode;
}

export function EditorLayoutProvider({
  conversationId,
  children,
}: EditorLayoutProviderProps) {
  const [mode, setModeState] = useState<LayoutMode>(() =>
    loadMode(conversationId)
  );

  // Reload stored mode when the active conversation changes.
  useEffect(() => {
    setModeState(loadMode(conversationId));
  }, [conversationId]);

  const setMode = useCallback(
    (next: LayoutMode) => {
      setModeState(next);
      saveMode(conversationId, next);
    },
    [conversationId]
  );

  // Detect prefers-reduced-motion once at mount; re-evaluate on change.
  const [reducedMotion, setReducedMotion] = useState(() => {
    if (typeof window === 'undefined') return false;
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  });

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const mq = window.matchMedia('(prefers-reduced-motion: reduce)');
    const handler = (e: MediaQueryListEvent) => setReducedMotion(e.matches);
    mq.addEventListener('change', handler);
    return () => mq.removeEventListener('change', handler);
  }, []);

  const value = useMemo<EditorLayoutValue>(
    () => ({
      mode,
      sidebarWidth: SIDEBAR_WIDTHS[mode],
      panelWidth: PANEL_WIDTHS[mode],
      reducedMotion,
      setMode,
    }),
    [mode, reducedMotion, setMode]
  );

  return (
    <EditorLayoutContext.Provider value={value}>
      {children}
    </EditorLayoutContext.Provider>
  );
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

/** Consume EditorLayoutContext. Must be used inside EditorLayoutProvider. */
export function useEditorLayout(): EditorLayoutValue {
  return useContext(EditorLayoutContext);
}
