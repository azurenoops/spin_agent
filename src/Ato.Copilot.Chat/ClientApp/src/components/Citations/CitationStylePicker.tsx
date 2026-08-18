// =============================================================================
// CitationStylePicker.tsx — #1703
//
// Searchable citation style picker (2,600+ styles).
//
// UX:
//   - Desktop (≥ 640px): fixed-position popover anchored below the trigger
//   - Mobile (< 640px):  bottom-sheet overlay
//   - Search box filters by name + aliases (journal auto-suggest)
//   - Recently-used section (up to 5) shown when query is empty
//   - Discipline filter chips
//   - Live count via aria-live="polite"
//   - Reformat warning via role="alert" when style changes
//
// ARIA contract:
//   trigger   → role="combobox"  aria-haspopup="listbox"  aria-expanded
//   search    → role="searchbox"
//   list      → role="listbox"
//   items     → role="option"  aria-selected
//   live      → aria-live="polite"
//   warning   → role="alert"
// =============================================================================

import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { CITATION_STYLES, CitationStyle, Discipline } from '../../data/citationStyles';
import { useCitation } from '../../contexts/CitationContext';

// ── Helpers ───────────────────────────────────────────────────────────────────

const DISCIPLINES: { value: Discipline | 'all'; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'general', label: 'General' },
  { value: 'sciences', label: 'Sciences' },
  { value: 'humanities', label: 'Humanities' },
  { value: 'medicine', label: 'Medicine' },
  { value: 'law', label: 'Law' },
];

function normalise(s: string): string {
  return s.toLowerCase().replace(/[^a-z0-9]/g, '');
}

function matchesQuery(style: CitationStyle, query: string): boolean {
  if (!query) return true;
  const q = normalise(query);
  if (normalise(style.name).includes(q)) return true;
  return style.aliases.some((a) => normalise(a).includes(q));
}

// ── Component ─────────────────────────────────────────────────────────────────

interface CitationStylePickerProps {
  /** Optional class for the trigger button wrapper. */
  className?: string;
  /** Render prop that exposes the trigger element. Defaults to a small chip. */
  trigger?: (props: {
    onClick: () => void;
    'aria-expanded': boolean;
    'aria-haspopup': 'listbox';
    role: 'combobox';
    ref: React.Ref<HTMLButtonElement>;
  }) => React.ReactNode;
}

export default function CitationStylePicker({ className, trigger }: CitationStylePickerProps) {
  const { selectedStyle, recentStyles, setSelectedStyle, pickerOpen, openPicker, closePicker } =
    useCitation();

  const [query, setQuery] = useState('');
  const [discipline, setDiscipline] = useState<Discipline | 'all'>('all');
  const [reformatWarning, setReformatWarning] = useState(false);

  const triggerRef = useRef<HTMLButtonElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const [focusedIndex, setFocusedIndex] = useState(-1);

  // Viewport width for mobile vs desktop
  const [isMobile, setIsMobile] = useState(() => window.innerWidth < 640);
  useEffect(() => {
    const handler = () => setIsMobile(window.innerWidth < 640);
    window.addEventListener('resize', handler);
    return () => window.removeEventListener('resize', handler);
  }, []);

  // ── Filtered results ────────────────────────────────────────────────────────

  const filtered = useMemo(() => {
    return CITATION_STYLES.filter(
      (s) =>
        (discipline === 'all' || s.discipline === discipline) &&
        matchesQuery(s, query)
    );
  }, [query, discipline]);

  const showRecents = !query && recentStyles.length > 0;
  const displayList = showRecents ? recentStyles : filtered;

  // ── Open / close ────────────────────────────────────────────────────────────

  const handleOpen = useCallback(() => {
    openPicker();
    setQuery('');
    setDiscipline('all');
    setFocusedIndex(-1);
  }, [openPicker]);

  const handleClose = useCallback(() => {
    closePicker();
    triggerRef.current?.focus();
  }, [closePicker]);

  // Close on click-outside
  const containerRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!pickerOpen) return;
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        handleClose();
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [pickerOpen, handleClose]);

  // Close on Escape; trap focus inside panel
  useEffect(() => {
    if (!pickerOpen) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        handleClose();
        return;
      }
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setFocusedIndex((i) => Math.min(i + 1, displayList.length - 1));
      }
      if (e.key === 'ArrowUp') {
        e.preventDefault();
        setFocusedIndex((i) => Math.max(i - 1, 0));
      }
      if (e.key === 'Enter' && focusedIndex >= 0) {
        e.preventDefault();
        handleSelect(displayList[focusedIndex]);
      }
    };
    document.addEventListener('keydown', handler);
    return () => document.removeEventListener('keydown', handler);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pickerOpen, displayList, focusedIndex]);

  // Focus search on open
  useEffect(() => {
    if (pickerOpen) {
      setTimeout(() => searchRef.current?.focus(), 50);
    }
  }, [pickerOpen]);

  // Scroll focused option into view
  useEffect(() => {
    if (focusedIndex < 0 || !listRef.current) return;
    const item = listRef.current.children[focusedIndex] as HTMLElement | undefined;
    item?.scrollIntoView?.({ block: 'nearest' });
  }, [focusedIndex]);

  // ── Selection ───────────────────────────────────────────────────────────────

  const handleSelect = useCallback(
    (style: CitationStyle) => {
      if (style.id !== selectedStyle.id) {
        setSelectedStyle(style);
        setReformatWarning(true);
        setTimeout(() => setReformatWarning(false), 4000);
      }
      handleClose();
    },
    [selectedStyle, setSelectedStyle, handleClose]
  );

  // ── Trigger element ─────────────────────────────────────────────────────────

  const triggerProps = {
    onClick: handleOpen,
    'aria-expanded': pickerOpen,
    'aria-haspopup': 'listbox' as const,
    role: 'combobox' as const,
    ref: triggerRef,
  };

  const defaultTrigger = (
    <button
      {...triggerProps}
      className={`flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium text-gray-600
        hover:text-gray-800 hover:bg-gray-100 rounded-lg border border-gray-200 transition-colors
        focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-500 ${className ?? ''}`}
      aria-label={`Citation style: ${selectedStyle.name}. Click to change.`}
    >
      <svg className="w-3.5 h-3.5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
      </svg>
      <span className="truncate max-w-[120px]">{selectedStyle.name}</span>
      <svg className="w-3 h-3 text-gray-400 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
      </svg>
    </button>
  );

  // ── Panel ───────────────────────────────────────────────────────────────────

  const panelClasses = isMobile
    ? 'fixed inset-x-0 bottom-0 z-50 bg-white rounded-t-2xl shadow-2xl border-t border-gray-200'
    : 'fixed z-50 bg-white rounded-xl shadow-2xl border border-gray-200 w-80';

  // Anchor popover below trigger on desktop
  const [anchorStyle, setAnchorStyle] = useState<React.CSSProperties>({});
  useEffect(() => {
    if (!pickerOpen || isMobile || !triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    setAnchorStyle({
      top: rect.bottom + 6,
      left: Math.min(rect.left, window.innerWidth - 320 - 8),
    });
  }, [pickerOpen, isMobile]);

  const panel = pickerOpen ? (
    <div
      ref={containerRef}
      className={panelClasses}
      style={isMobile ? {} : anchorStyle}
      role="dialog"
      aria-modal="true"
      aria-label="Choose citation style"
    >
      {/* Mobile drag handle */}
      {isMobile && (
        <div className="flex justify-center pt-3 pb-1">
          <div className="w-10 h-1 rounded-full bg-gray-300" />
        </div>
      )}

      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100">
        <h2 className="text-sm font-semibold text-gray-800">Citation Style</h2>
        <button
          onClick={handleClose}
          className="p-1 text-gray-400 hover:text-gray-600 rounded transition-colors"
          aria-label="Close citation style picker"
        >
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      {/* Search */}
      <div className="px-4 py-2 border-b border-gray-100">
        <div className="relative">
          <svg
            className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400 pointer-events-none"
            fill="none" stroke="currentColor" viewBox="0 0 24 24"
          >
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
              d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
          </svg>
          <input
            ref={searchRef}
            role="searchbox"
            type="text"
            placeholder="Search styles or journals…"
            value={query}
            onChange={(e) => { setQuery(e.target.value); setFocusedIndex(-1); }}
            className="w-full pl-8 pr-3 py-1.5 text-sm border border-gray-200 rounded-lg
              focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            aria-label="Search citation styles"
            aria-autocomplete="list"
            aria-controls="citation-style-listbox"
          />
        </div>
      </div>

      {/* Discipline filter */}
      <div className="flex gap-1 px-4 py-2 border-b border-gray-100 overflow-x-auto scrollbar-none">
        {DISCIPLINES.map((d) => (
          <button
            key={d.value}
            onClick={() => { setDiscipline(d.value as Discipline | 'all'); setFocusedIndex(-1); }}
            className={`flex-shrink-0 px-2 py-0.5 text-xs rounded-full border transition-colors ${
              discipline === d.value
                ? 'bg-blue-600 text-white border-blue-600'
                : 'text-gray-500 border-gray-200 hover:border-gray-400'
            }`}
          >
            {d.label}
          </button>
        ))}
      </div>

      {/* Count */}
      <div
        className="px-4 pt-2 pb-1 text-xs text-gray-400"
        aria-live="polite"
        aria-atomic="true"
      >
        {showRecents
          ? 'Recently used'
          : `${filtered.length.toLocaleString()} style${filtered.length !== 1 ? 's' : ''}`}
      </div>

      {/* List */}
      <ul
        id="citation-style-listbox"
        ref={listRef}
        role="listbox"
        aria-label="Citation styles"
        aria-activedescendant={focusedIndex >= 0 ? `cs-option-${focusedIndex}` : undefined}
        className="overflow-y-auto max-h-60 pb-2"
      >
        {displayList.length === 0 ? (
          <li className="px-4 py-6 text-sm text-gray-400 text-center">No styles found</li>
        ) : (
          displayList.map((style, idx) => (
            <li
              key={style.id}
              id={`cs-option-${idx}`}
              role="option"
              aria-selected={style.id === selectedStyle.id}
              onClick={() => handleSelect(style)}
              onMouseEnter={() => setFocusedIndex(idx)}
              className={`flex items-center gap-2 px-4 py-2 cursor-pointer text-sm transition-colors
                ${idx === focusedIndex ? 'bg-blue-50' : 'hover:bg-gray-50'}
                ${style.id === selectedStyle.id ? 'text-blue-700 font-medium' : 'text-gray-700'}`}
            >
              {style.id === selectedStyle.id && (
                <svg className="w-3.5 h-3.5 text-blue-600 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                  <path fillRule="evenodd"
                    d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z"
                    clipRule="evenodd" />
                </svg>
              )}
              <span className={`truncate ${style.id !== selectedStyle.id ? 'ml-5' : ''}`}>
                {style.name}
              </span>
              <span className="ml-auto text-xs text-gray-400 capitalize flex-shrink-0">
                {style.discipline}
              </span>
            </li>
          ))
        )}
      </ul>
    </div>
  ) : null;

  // ── Reformat warning ────────────────────────────────────────────────────────

  const warning = reformatWarning ? (
    <div
      role="alert"
      aria-live="assertive"
      className="fixed bottom-4 left-1/2 -translate-x-1/2 z-50
        flex items-center gap-2 px-4 py-2.5 bg-amber-50 border border-amber-200 rounded-xl
        shadow-lg text-sm text-amber-800 animate-fade-in"
    >
      <svg className="w-4 h-4 text-amber-500 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
      </svg>
      Citation style changed to <strong>{selectedStyle.name}</strong> — existing citations may need reformatting.
    </div>
  ) : null;

  // ── Backdrop for mobile ─────────────────────────────────────────────────────

  const backdrop = pickerOpen && isMobile ? (
    <div
      className="fixed inset-0 z-40 bg-black/30"
      aria-hidden="true"
      onClick={handleClose}
    />
  ) : null;

  return (
    <>
      {trigger ? trigger(triggerProps) : defaultTrigger}
      {backdrop}
      {panel}
      {warning}
    </>
  );
}
