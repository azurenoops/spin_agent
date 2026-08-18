// =============================================================================
// CitationContext.tsx — #1703
//
// Provides selected citation style state + recently-used history across the app.
// Persists to localStorage under citationStyleHistory:[userId] (max 5 entries).
// Pre-populated with APA 7th, MLA 9th, Chicago 17th on first use.
// =============================================================================

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react';
import { CITATION_STYLES, CitationStyle } from '../data/citationStyles';

// ── Constants ─────────────────────────────────────────────────────────────────

const MAX_HISTORY = 5;
const HISTORY_STORAGE_KEY = (userId: string) => `citationStyleHistory:${userId}`;

const DEFAULT_HISTORY_IDS = ['apa-7', 'mla-9', 'chicago-17'];

function loadHistory(userId: string): CitationStyle[] {
  try {
    const raw = localStorage.getItem(HISTORY_STORAGE_KEY(userId));
    if (!raw) return resolveStyles(DEFAULT_HISTORY_IDS);
    const ids: string[] = JSON.parse(raw);
    return resolveStyles(ids);
  } catch {
    return resolveStyles(DEFAULT_HISTORY_IDS);
  }
}

function resolveStyles(ids: string[]): CitationStyle[] {
  return ids
    .map((id) => CITATION_STYLES.find((s) => s.id === id))
    .filter((s): s is CitationStyle => s !== undefined);
}

function saveHistory(userId: string, styles: CitationStyle[]): void {
  try {
    localStorage.setItem(
      HISTORY_STORAGE_KEY(userId),
      JSON.stringify(styles.map((s) => s.id))
    );
  } catch {
    // localStorage unavailable — silently skip
  }
}

// ── Context shape ─────────────────────────────────────────────────────────────

export interface CitationContextValue {
  /** Currently selected style (defaults to APA 7th). */
  selectedStyle: CitationStyle;
  /** Up to 5 recently used styles (most-recent first). */
  recentStyles: CitationStyle[];
  /** Change the selected style and push it to the top of recents. */
  setSelectedStyle: (style: CitationStyle) => void;
  /** Whether the picker popover is open. */
  pickerOpen: boolean;
  openPicker: () => void;
  closePicker: () => void;
}

const CitationContext = createContext<CitationContextValue | undefined>(undefined);

// ── Provider ──────────────────────────────────────────────────────────────────

interface CitationProviderProps {
  /** User id — used to scope localStorage key. Pass 'anon' when unauthenticated. */
  userId?: string;
  children: React.ReactNode;
}

const APA7 = CITATION_STYLES.find((s) => s.id === 'apa-7') ?? CITATION_STYLES[0];

export function CitationProvider({ userId = 'anon', children }: CitationProviderProps) {
  const [selectedStyle, setSelectedStyleState] = useState<CitationStyle>(APA7);
  const [recentStyles, setRecentStyles] = useState<CitationStyle[]>(() =>
    loadHistory(userId)
  );
  const [pickerOpen, setPickerOpen] = useState(false);

  // Re-load history if userId changes (e.g. user signs in)
  useEffect(() => {
    setRecentStyles(loadHistory(userId));
  }, [userId]);

  const setSelectedStyle = useCallback(
    (style: CitationStyle) => {
      setSelectedStyleState(style);
      setRecentStyles((prev) => {
        const next = [style, ...prev.filter((s) => s.id !== style.id)].slice(
          0,
          MAX_HISTORY
        );
        saveHistory(userId, next);
        return next;
      });
    },
    [userId]
  );

  const openPicker = useCallback(() => setPickerOpen(true), []);
  const closePicker = useCallback(() => setPickerOpen(false), []);

  const value = useMemo<CitationContextValue>(
    () => ({ selectedStyle, recentStyles, setSelectedStyle, pickerOpen, openPicker, closePicker }),
    [selectedStyle, recentStyles, setSelectedStyle, pickerOpen, openPicker, closePicker]
  );

  return <CitationContext.Provider value={value}>{children}</CitationContext.Provider>;
}

// ── Hook ──────────────────────────────────────────────────────────────────────

export function useCitation(): CitationContextValue {
  const ctx = useContext(CitationContext);
  if (!ctx) {
    throw new Error('useCitation must be used inside <CitationProvider>');
  }
  return ctx;
}
