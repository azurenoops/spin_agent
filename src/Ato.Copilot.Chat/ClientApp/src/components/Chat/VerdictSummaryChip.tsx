// =============================================================================
// VerdictSummaryChip — Issue #2437 / TraceabilityPanel
//
// Rendered below each assistant MessageBubble. Shows aggregated verdict
// counts for that message. Clicking/tapping opens the TraceabilityPanel.
//
// States:
//   • All PENDING   → "Verifying…" spinner
//   • All SUPPORTED → "✓ All claims verified" (--color-success)
//   • Mixed         → "✓ N verified · ⚠ N partial · ✗ N contradicted …"
//                     coloured warning/error if any non-green verdicts exist
//   • No results    → renders nothing (null)
// =============================================================================

import React from 'react';
import {
  useVerdictStore,
  getResultsForMessage,
  countsByVerdict,
} from '../../lib/verdict-store';

export interface VerdictSummaryChipProps {
  /** The assistant message whose verdicts to summarise. */
  messageId: string;
  /** Whether the TraceabilityPanel is currently open for this message. */
  panelOpen: boolean;
  /** Called when the chip is activated. */
  onTogglePanel: () => void;
}

export function VerdictSummaryChip({
  messageId,
  panelOpen,
  onTogglePanel,
}: VerdictSummaryChipProps) {
  const { state } = useVerdictStore();
  const results = getResultsForMessage(state, messageId);

  // Nothing to show until at least one result is registered.
  if (results.length === 0) return null;

  const counts = countsByVerdict(results);

  // ── Verifying state ──────────────────────────────────────────────────────
  if (counts.pending === counts.total) {
    return (
      <div
        role="button"
        tabIndex={0}
        aria-expanded={panelOpen}
        aria-controls="traceability-panel"
        aria-label="Traceability: verifying…"
        onClick={onTogglePanel}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            onTogglePanel();
          }
        }}
        className="verdict-summary-chip verdict-summary-chip--pending"
        style={chipBaseStyle}
      >
        <SpinnerIcon />
        <span style={{ marginLeft: '6px' }}>Verifying…</span>
      </div>
    );
  }

  // ── All green ────────────────────────────────────────────────────────────
  const allSupported =
    counts.verified === counts.total - counts.pending && counts.pending === 0 &&
    counts.partial === 0 && counts.contradicted === 0 &&
    counts.insufficient === 0 && counts.noEvidence === 0;

  if (allSupported) {
    const ariaLabel = `Traceability: ${counts.verified} verified`;
    return (
      <ChipButton
        ariaLabel={ariaLabel}
        panelOpen={panelOpen}
        onToggle={onTogglePanel}
        style={{ color: 'var(--color-success, #16a34a)', ...chipBaseStyle }}
      >
        ✓ All claims verified
      </ChipButton>
    );
  }

  // ── Mixed state ──────────────────────────────────────────────────────────
  const hasError   = counts.contradicted > 0;
  const hasWarning = counts.insufficient > 0 || counts.partial > 0;
  const accentColor = hasError
    ? 'var(--color-error, #dc2626)'
    : hasWarning
    ? 'var(--color-warning, #d97706)'
    : 'var(--color-success, #16a34a)';

  const parts: string[] = [];
  if (counts.verified > 0)     parts.push(`✓ ${counts.verified} verified`);
  if (counts.partial > 0)      parts.push(`⚠ ${counts.partial} partial`);
  if (counts.contradicted > 0) parts.push(`✗ ${counts.contradicted} contradicted`);
  if (counts.insufficient > 0) parts.push(`? ${counts.insufficient} insufficient`);
  if (counts.noEvidence > 0)   parts.push(`○ ${counts.noEvidence} no evidence`);

  const summaryText = parts.join(' · ');

  const ariaLabel = `Traceability: ${[
    counts.verified > 0     ? `${counts.verified} verified`     : '',
    counts.partial > 0      ? `${counts.partial} partial`       : '',
    counts.contradicted > 0 ? `${counts.contradicted} contradicted` : '',
    counts.insufficient > 0 ? `${counts.insufficient} insufficient` : '',
  ].filter(Boolean).join(', ')}`;

  return (
    <ChipButton
      ariaLabel={ariaLabel}
      panelOpen={panelOpen}
      onToggle={onTogglePanel}
      style={{ color: accentColor, ...chipBaseStyle }}
    >
      {summaryText}
    </ChipButton>
  );
}

// ── Internal helpers ─────────────────────────────────────────────────────────

interface ChipButtonProps {
  ariaLabel: string;
  panelOpen: boolean;
  onToggle: () => void;
  style?: React.CSSProperties;
  children: React.ReactNode;
}

function ChipButton({ ariaLabel, panelOpen, onToggle, style, children }: ChipButtonProps) {
  return (
    <div
      role="button"
      tabIndex={0}
      aria-expanded={panelOpen}
      aria-controls="traceability-panel"
      aria-label={ariaLabel}
      onClick={onToggle}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          onToggle();
        }
      }}
      className="verdict-summary-chip"
      style={style}
    >
      {children}
    </div>
  );
}

const chipBaseStyle: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  fontSize: '12px',
  fontWeight: 500,
  padding: '3px 8px',
  borderRadius: '12px',
  background: 'rgba(0,0,0,0.04)',
  cursor: 'pointer',
  userSelect: 'none',
  marginTop: '6px',
  outline: 'none',
};

function SpinnerIcon() {
  return (
    <svg
      width="12"
      height="12"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      style={{ animation: 'spin 1s linear infinite', color: '#60a5fa' }}
      aria-hidden="true"
    >
      <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83" />
    </svg>
  );
}
