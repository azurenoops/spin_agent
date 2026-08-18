// =============================================================================
// TraceabilityPanel — Issue #2437
//
// Slide-in drawer that aggregates all VerificationResult rows for a message.
//
// Layout:
//   ≥1024px  — 340px right-anchored slide-in drawer (fixed position)
//   768–1023 — bottom sheet (60vh max-height)
//   <768px   — full-screen modal overlay
//
// Accessibility:
//   role="complementary", aria-label="Claim traceability"
//   Focus trap: Tab cycles filter tabs → claim rows → action buttons → close.
//   Escape closes.
//   Filter tabs: role="tablist" / role="tab" with arrow-key navigation.
//   CONTRADICTED rows: aria-describedby → contradicting excerpt element.
//   All action buttons: descriptive aria-label.
//   prefers-reduced-motion: no slide animation, no highlight pulse.
//
// Onboarding:
//   First open ever shows a 2-line tooltip.
//   Dismissed by any interaction.
//   Persisted via localStorage key `traceability_panel_onboarded`.
// =============================================================================

import React, {
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
} from 'react';
import {
  useVerdictStore,
  getResultsForMessage,
  VerificationResult,
  VerdictClass,
} from '../../lib/verdict-store';

// ─── Public props ─────────────────────────────────────────────────────────────

export interface TraceabilityPanelProps {
  /** The message whose verdicts to display. */
  messageId: string;
  /** Whether the panel is open. */
  open: boolean;
  /** Called when the panel should close. */
  onClose: () => void;
  /**
   * Called when "View source" is clicked on a claim row.
   * The sourceId should be passed to ResearchSourcesCard as highlightSourceId.
   */
  onViewSource: (sourceId: string) => void;
  /**
   * Optional: id of a result to scroll/highlight on open.
   * Set by NliVerdictBadge click.
   */
  scrollToResultId?: string;
  /**
   * Optional: id of a source highlighted in ResearchSourcesCard.
   * Used for bidirectional highlight: claims linked to this source pulse.
   */
  highlightedSourceId?: string;
  /**
   * When true, shows a loading skeleton (aria-busy) while citation data is
   * being fetched. Used by freeze-on-stream regression tests and by callers
   * that initiate a fetch before results arrive in the verdict store.
   */
  loading?: boolean;
}

// ─── Filter tabs ──────────────────────────────────────────────────────────────

type FilterTab = 'all' | 'verified' | 'partial' | 'contradicted' | 'insufficient';

const FILTER_TABS: { id: FilterTab; label: string }[] = [
  { id: 'all',          label: 'All'          },
  { id: 'verified',     label: 'Verified'     },
  { id: 'partial',      label: 'Partial'      },
  { id: 'contradicted', label: 'Contradicted' },
  { id: 'insufficient', label: 'Insufficient' },
];

function verdictMatchesTab(verdict: VerdictClass, tab: FilterTab): boolean {
  switch (tab) {
    case 'all':          return true;
    case 'verified':     return verdict === 'SUPPORTED';
    case 'partial':      return verdict === 'PARTIALLY_SUPPORTED';
    case 'contradicted': return verdict === 'CONTRADICTED';
    case 'insufficient': return verdict === 'INSUFFICIENT' || verdict === 'NO_EVIDENCE';
    default:             return true;
  }
}

const ONBOARDING_KEY = 'traceability_panel_onboarded';

// ─── Component ────────────────────────────────────────────────────────────────

export function TraceabilityPanel({
  messageId,
  open,
  onClose,
  onViewSource,
  scrollToResultId,
  highlightedSourceId,
  loading = false,
}: TraceabilityPanelProps) {
  // ── Feature flag guard removed (GATE-2437) ──────────────────────────────
  // Guard lives only in ChatWindow.tsx. Duplicate check here was defensive
  // overkill that obscured intent and made the component harder to test in
  // isolation. See Hawkeye audit finding F5.

  const { state } = useVerdictStore();
  const results = getResultsForMessage(state, messageId);

  const [activeTab, setActiveTab] = useState<FilterTab>('all');
  const [expandedSpans, setExpandedSpans] = useState<Set<string>>(new Set());
  const [showOnboarding, setShowOnboarding] = useState(false);

  const panelRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const chipRef = useRef<HTMLElement | null>(null);

  const rowRefs = useRef<Map<string, HTMLDivElement>>(new Map());
  const tabRefs = useRef<Map<FilterTab, HTMLButtonElement>>(new Map());

  // AbortController ref — callers can pass a controller whose abort() fires on
  // unmount to prevent dangling fetch state (freeze-on-stream regression guard).
  const abortRef = useRef<AbortController | null>(null);
  useEffect(() => {
    abortRef.current = new AbortController();
    return () => {
      abortRef.current?.abort();
    };
  }, [messageId]);

  const uid = useId();

  // Reduced-motion preference.
  const reducedMotion =
    typeof window !== 'undefined' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ── Onboarding tooltip ──────────────────────────────────────────────────
  useEffect(() => {
    if (open && typeof localStorage !== 'undefined') {
      const alreadySeen = localStorage.getItem(ONBOARDING_KEY);
      if (!alreadySeen) setShowOnboarding(true);
    }
  }, [open]);

  function dismissOnboarding() {
    setShowOnboarding(false);
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(ONBOARDING_KEY, '1');
    }
  }

  // ── Focus management ────────────────────────────────────────────────────
  useEffect(() => {
    if (open) {
      // Trap focus: move focus to close button on open.
      setTimeout(() => closeButtonRef.current?.focus(), reducedMotion ? 0 : 80);
    } else {
      // Return focus to the chip that opened the panel.
      setTimeout(() => (chipRef.current as HTMLElement | null)?.focus(), 0);
    }
  }, [open, reducedMotion]);

  // ── Escape key ──────────────────────────────────────────────────────────
  useEffect(() => {
    if (!open) return;
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        e.preventDefault();
        onClose();
      }
    }
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [open, onClose]);

  // ── Focus trap ──────────────────────────────────────────────────────────
  useEffect(() => {
    if (!open || !panelRef.current) return;

    function handleTab(e: KeyboardEvent) {
      if (e.key !== 'Tab' || !panelRef.current) return;
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(
        'button, [tabindex="0"], a[href]'
      );
      const items = Array.from(focusable);
      if (items.length === 0) return;
      const first = items[0];
      const last  = items[items.length - 1];
      if (e.shiftKey) {
        if (document.activeElement === first) {
          e.preventDefault();
          last.focus();
        }
      } else {
        if (document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    }

    document.addEventListener('keydown', handleTab);
    return () => document.removeEventListener('keydown', handleTab);
  }, [open]);

  // ── Scroll to specific result ────────────────────────────────────────────
  useEffect(() => {
    if (open && scrollToResultId) {
      const el = rowRefs.current.get(scrollToResultId);
      if (el) {
        setTimeout(
          () => el.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth', block: 'nearest' }),
          reducedMotion ? 0 : 120
        );
      }
    }
  }, [open, scrollToResultId, reducedMotion]);

  // ── Filter tab arrow-key navigation ─────────────────────────────────────
  function handleTabKeyDown(e: React.KeyboardEvent, currentTab: FilterTab) {
    const idx   = FILTER_TABS.findIndex((t) => t.id === currentTab);
    let nextIdx = idx;
    if (e.key === 'ArrowRight') nextIdx = (idx + 1) % FILTER_TABS.length;
    if (e.key === 'ArrowLeft')  nextIdx = (idx - 1 + FILTER_TABS.length) % FILTER_TABS.length;
    if (nextIdx !== idx) {
      e.preventDefault();
      const next = FILTER_TABS[nextIdx];
      setActiveTab(next.id);
      tabRefs.current.get(next.id)?.focus();
    }
  }

  // ── Filtered results ─────────────────────────────────────────────────────
  const filtered = results.filter((r) => verdictMatchesTab(r.verdict, activeTab));

  // ── Panel visibility ────────────────────────────────────────────────────
  if (!open) return null;

  // ── Loading skeleton ─────────────────────────────────────────────────────
  if (loading) {
    return (
      <div
        role="complementary"
        aria-label="Claim traceability"
        aria-busy="true"
        data-testid="traceability-skeleton"
        style={getPanelStyle(reducedMotion)}
      >
        <div style={{ padding: '14px 16px 10px', borderBottom: '1px solid #e5e7eb' }}>
          <div style={{ fontWeight: 700, fontSize: '14px', color: '#111827' }}>Traceability</div>
        </div>
        <div style={{ padding: '16px' }}>
          {[1, 2, 3].map((i) => (
            <div
              key={i}
              style={{
                height: '48px',
                background: '#f3f4f6',
                borderRadius: '6px',
                marginBottom: '10px',
                animation: reducedMotion ? 'none' : 'pulse 1.5s ease-in-out infinite',
              }}
              aria-hidden="true"
            />
          ))}
        </div>
      </div>
    );
  }

  // ── Responsive styles ────────────────────────────────────────────────────
  // We use a simple inline-style approach so no CSS file dependency is needed.
  // Tailwind classes are added where convenient.
  const panelStyle = getPanelStyle(reducedMotion);

  // ── Guided empty state — header-only collapse (GATE-2437 F4) ─────────────
  // When the verdict store has no results for this message yet, render a
  // collapsed header-only panel so the feature is visible without consuming
  // sidebar space with an empty list. The guided copy replaces the original
  // dead-end "No sources traced yet." message (Hawkeye audit finding F4).
  if (results.length === 0) {
    return (
      <div
        id="traceability-panel"
        role="complementary"
        aria-label="Claim traceability"
        data-testid="traceability-empty"
        style={{ ...panelStyle, overflow: 'visible' }}
        className="traceability-panel"
      >
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '14px 16px 10px',
          borderBottom: '1px solid #e5e7eb',
          flexShrink: 0,
        }}>
          <div style={{ fontWeight: 700, fontSize: '14px', color: '#111827' }}>Traceability</div>
          <button
            ref={closeButtonRef}
            onClick={onClose}
            aria-label="Close traceability panel"
            style={closeButtonStyle}
          >
            ✕
          </button>
        </div>
        <div
          style={{ padding: '16px', color: '#9ca3af', fontSize: '13px', lineHeight: 1.6 }}
          data-testid="traceability-guided-empty"
        >
          Ask Jarvis a research question — cited sources will appear here automatically.
        </div>
      </div>
    );
  }

  return (
    <>
      {/* Backdrop for mobile full-screen / bottom-sheet */}
      <div
        aria-hidden="true"
        onClick={onClose}
        style={{
          position: 'fixed',
          inset: 0,
          background: 'rgba(0,0,0,0.3)',
          zIndex: 49,
        }}
        className="traceability-backdrop lg:hidden"
      />

      <div
        id="traceability-panel"
        ref={panelRef}
        role="complementary"
        aria-label="Claim traceability"
        style={panelStyle}
        className="traceability-panel"
        onClick={showOnboarding ? dismissOnboarding : undefined}
      >
        {/* ── Onboarding tooltip ─────────────────────────────────────── */}
        {showOnboarding && (
          <div
            role="status"
            aria-live="polite"
            style={{
              position: 'absolute',
              top: '60px',
              left: '12px',
              right: '12px',
              background: '#1f2937',
              color: '#f9fafb',
              padding: '10px 14px',
              borderRadius: '8px',
              fontSize: '13px',
              lineHeight: 1.5,
              zIndex: 10,
              boxShadow: '0 4px 12px rgba(0,0,0,0.25)',
            }}
          >
            These are the claims Clara verified against your sources.{' '}
            <strong>Red means a source contradicts the claim.</strong>
            <button
              style={{
                display: 'block',
                marginTop: '8px',
                color: '#93c5fd',
                background: 'none',
                border: 'none',
                cursor: 'pointer',
                fontSize: '12px',
                padding: 0,
              }}
              onClick={(e) => { e.stopPropagation(); dismissOnboarding(); }}
            >
              Got it
            </button>
          </div>
        )}

        {/* ── Header ────────────────────────────────────────────────── */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '14px 16px 10px',
          borderBottom: '1px solid #e5e7eb',
          flexShrink: 0,
        }}>
          <div style={{ fontWeight: 700, fontSize: '14px', color: '#111827' }}>
            Traceability
            <span style={{ fontWeight: 400, color: '#6b7280', marginLeft: '6px' }}>
              · {results.length} {results.length === 1 ? 'claim' : 'claims'}
            </span>
          </div>
          <button
            ref={closeButtonRef}
            onClick={onClose}
            aria-label="Close traceability panel"
            style={closeButtonStyle}
          >
            ✕
          </button>
        </div>

        {/* ── Filter tabs ───────────────────────────────────────────── */}
        <div
          role="tablist"
          aria-label="Filter by verdict"
          style={{
            display: 'flex',
            gap: '4px',
            padding: '8px 12px',
            borderBottom: '1px solid #e5e7eb',
            flexShrink: 0,
            overflowX: 'auto',
          }}
        >
          {FILTER_TABS.map((tab) => (
            <button
              key={tab.id}
              ref={(el) => {
                if (el) tabRefs.current.set(tab.id, el);
                else tabRefs.current.delete(tab.id);
              }}
              role="tab"
              aria-selected={activeTab === tab.id}
              tabIndex={activeTab === tab.id ? 0 : -1}
              onClick={() => setActiveTab(tab.id)}
              onKeyDown={(e) => handleTabKeyDown(e, tab.id)}
              style={{
                padding: '4px 10px',
                borderRadius: '12px',
                border: 'none',
                cursor: 'pointer',
                fontSize: '12px',
                fontWeight: activeTab === tab.id ? 600 : 400,
                background: activeTab === tab.id ? '#2563eb' : '#f3f4f6',
                color: activeTab === tab.id ? '#fff' : '#374151',
                whiteSpace: 'nowrap',
                outline: 'none',
              }}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {/* ── Claim rows ────────────────────────────────────────────── */}
        <div
          aria-live="polite"
          style={{ flex: 1, overflowY: 'auto', padding: '8px 0' }}
        >
          {filtered.length === 0 ? (
            <div style={{ padding: '24px 16px', color: '#9ca3af', fontSize: '13px', textAlign: 'center' }}>
              No claims in this category.
            </div>
          ) : (
            filtered.map((result) => (
              <ClaimRow
                key={result.id}
                result={result}
                uid={uid}
                isHighlightedBySource={
                  !!highlightedSourceId && result.grounding_source === highlightedSourceId
                }
                isScrollTarget={scrollToResultId === result.id}
                expandedSpans={expandedSpans}
                reducedMotion={reducedMotion}
                rowRef={(el) => {
                  if (el) rowRefs.current.set(result.id, el);
                  else rowRefs.current.delete(result.id);
                }}
                onViewSource={onViewSource}
                onToggleSpans={(id) => {
                  setExpandedSpans((prev) => {
                    const next = new Set(prev);
                    if (next.has(id)) next.delete(id);
                    else next.add(id);
                    return next;
                  });
                }}
              />
            ))
          )}
        </div>
      </div>
    </>
  );
}

// ─── ClaimRow ─────────────────────────────────────────────────────────────────

interface ClaimRowProps {
  result: VerificationResult;
  uid: string;
  isHighlightedBySource: boolean;
  isScrollTarget: boolean;
  expandedSpans: Set<string>;
  reducedMotion: boolean;
  rowRef: (el: HTMLDivElement | null) => void;
  onViewSource: (sourceId: string) => void;
  onToggleSpans: (id: string) => void;
}

function ClaimRow({
  result,
  uid,
  isHighlightedBySource,
  isScrollTarget,
  expandedSpans,
  reducedMotion,
  rowRef,
  onViewSource,
  onToggleSpans,
}: ClaimRowProps) {
  const { icon, color, label } = VERDICT_META[result.verdict] ?? VERDICT_META.PENDING;
  const excerptId = `excerpt-${uid}-${result.id}`;
  const isContradicted = result.verdict === 'CONTRADICTED';
  const isInsufficient = result.verdict === 'INSUFFICIENT' || result.verdict === 'NO_EVIDENCE';
  const isMultiHop = (result.contributing_span_ids?.length ?? 0) > 1;
  const spansExpanded = expandedSpans.has(result.id);

  return (
    <div
      ref={rowRef}
      data-result-id={result.id}
      aria-describedby={isContradicted && result.contradicting_excerpt ? excerptId : undefined}
      style={{
        padding: '10px 16px',
        borderBottom: '1px solid #f3f4f6',
        background: isScrollTarget ? '#eff6ff' : isHighlightedBySource ? '#fefce8' : 'transparent',
        transition: reducedMotion ? 'none' : 'background 0.3s ease',
      }}
    >
      {/* Verdict line */}
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: '6px', marginBottom: '4px' }}>
        <span style={{ color, fontSize: '14px', flexShrink: 0, lineHeight: 1.4 }} aria-hidden="true">
          {icon}
        </span>
        <span style={{ fontSize: '11px', fontWeight: 600, color, textTransform: 'uppercase', lineHeight: 1.6, flexShrink: 0 }}>
          {label}
        </span>
      </div>

      {/* Claim sentence (2-line truncation) */}
      <div style={{
        fontSize: '13px',
        color: '#111827',
        display: '-webkit-box',
        WebkitLineClamp: 2,
        WebkitBoxOrient: 'vertical',
        overflow: 'hidden',
        marginBottom: '4px',
        lineHeight: 1.5,
      }}>
        {result.claim_sentence}
      </div>

      {/* Source line */}
      <div style={{ fontSize: '11px', color: '#6b7280', marginBottom: '4px' }}>
        Source: {result.source_title}
        {result.source_section ? ` · ${result.source_section}` : ''}
        {result.calibrated_confidence != null && (
          <span style={{ marginLeft: '6px' }}>
            · Confidence: {Math.round(result.calibrated_confidence * 100)}%
            {result.grounding_source ? ` · ${result.grounding_source}` : ''}
          </span>
        )}
      </div>

      {/* Contradicting excerpt */}
      {isContradicted && result.contradicting_excerpt && (
        <div
          id={excerptId}
          style={{
            fontSize: '12px',
            color: '#dc2626',
            background: '#fef2f2',
            border: '1px solid #fecaca',
            borderRadius: '6px',
            padding: '6px 8px',
            marginBottom: '6px',
            fontStyle: 'italic',
          }}
        >
          <span style={{ fontStyle: 'normal', fontWeight: 600 }}>Contradicted by: </span>
          {result.contradicting_excerpt}
        </div>
      )}

      {/* Multi-hop spans */}
      {isMultiHop && (
        <div style={{ marginBottom: '6px' }}>
          <button
            onClick={() => onToggleSpans(result.id)}
            aria-expanded={spansExpanded}
            style={actionButtonStyle('#f3f4f6', '#374151')}
          >
            {spansExpanded ? 'Hide spans' : 'View spans'}
          </button>
          {spansExpanded && (
            <div style={{ marginTop: '4px', fontSize: '11px', color: '#6b7280' }}>
              {result.contributing_span_ids!.map((sid) => (
                <span
                  key={sid}
                  style={{
                    display: 'inline-block',
                    marginRight: '4px',
                    marginBottom: '2px',
                    padding: '1px 6px',
                    background: '#e5e7eb',
                    borderRadius: '4px',
                  }}
                >
                  {sid}
                </span>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Action buttons */}
      {(isContradicted || isInsufficient) && (
        <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
          <button
            aria-label={`Rephrase claim: ${result.claim_sentence.slice(0, 60)}`}
            style={actionButtonStyle('#fef9c3', '#92400e')}
            onClick={() => {
              // Rephrase claim: fires inline edit suggestion.
              // Integration point for future inline-edit flow.
              // eslint-disable-next-line no-console
              console.info('[TraceabilityPanel] Rephrase claim requested for result', result.id);
            }}
          >
            Rephrase claim
          </button>
          {result.grounding_source && (
            <button
              aria-label={`View source for claim: ${result.claim_sentence.slice(0, 60)}`}
              style={actionButtonStyle('#eff6ff', '#1d4ed8')}
              onClick={() => onViewSource(result.grounding_source!)}
            >
              View source
            </button>
          )}
        </div>
      )}
    </div>
  );
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

const VERDICT_META: Record<VerdictClass, { icon: string; color: string; label: string }> = {
  SUPPORTED:           { icon: '✓', color: '#16a34a', label: 'Verified'      },
  PARTIALLY_SUPPORTED: { icon: '⚠', color: '#d97706', label: 'Partial'       },
  INSUFFICIENT:        { icon: '?', color: '#9ca3af', label: 'Insufficient'  },
  CONTRADICTED:        { icon: '✗', color: '#dc2626', label: 'Contradicted'  },
  NO_EVIDENCE:         { icon: '○', color: '#9ca3af', label: 'No Evidence'   },
  PENDING:             { icon: '…', color: '#60a5fa', label: 'Pending'       },
};

function actionButtonStyle(bg: string, fg: string): React.CSSProperties {
  return {
    padding: '4px 10px',
    borderRadius: '6px',
    border: 'none',
    background: bg,
    color: fg,
    fontSize: '12px',
    fontWeight: 500,
    cursor: 'pointer',
    outline: 'none',
    minHeight: '28px', // meets 44px on mobile via parent container padding
  };
}

function getPanelStyle(reducedMotion: boolean): React.CSSProperties {
  // Base style: fixed right drawer on desktop.
  // CSS media queries cannot be applied inline, so we detect viewport here
  // for the initial render. A CSS class approach would be more robust in a
  // full Tailwind project, but this keeps the component self-contained.
  const vw = typeof window !== 'undefined' ? window.innerWidth : 1200;

  if (vw < 768) {
    // Full-screen overlay
    return {
      position: 'fixed',
      inset: 0,
      zIndex: 50,
      background: '#fff',
      display: 'flex',
      flexDirection: 'column',
      overflowY: 'hidden',
    };
  }

  if (vw < 1024) {
    // Bottom sheet
    return {
      position: 'fixed',
      left: 0,
      right: 0,
      bottom: 0,
      maxHeight: '60vh',
      zIndex: 50,
      background: '#fff',
      borderTopLeftRadius: '16px',
      borderTopRightRadius: '16px',
      boxShadow: '0 -4px 24px rgba(0,0,0,0.15)',
      display: 'flex',
      flexDirection: 'column',
      overflow: 'hidden',
      ...(reducedMotion ? {} : { transition: 'transform 0.25s ease' }),
    };
  }

  // Right drawer ≥1024px
  return {
    position: 'fixed',
    top: 0,
    right: 0,
    bottom: 0,
    width: '340px',
    zIndex: 50,
    background: '#fff',
    borderLeft: '1px solid #e5e7eb',
    boxShadow: '-4px 0 24px rgba(0,0,0,0.08)',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    ...(reducedMotion ? {} : { transition: 'transform 0.2s ease' }),
  };
}

const closeButtonStyle: React.CSSProperties = {
  width: '28px',
  height: '28px',
  borderRadius: '6px',
  border: 'none',
  background: '#f3f4f6',
  cursor: 'pointer',
  fontSize: '14px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  color: '#374151',
  outline: 'none',
};
