// =============================================================================
// NliVerdictBadge — Issue #2437 / TraceabilityPanel
//
// Inline per-sentence NLI verdict badge.
// Previously a decorative dead-end span (cursor:default, no click handler).
// Now a keyboard-accessible button that opens the TraceabilityPanel and
// scrolls to the clicked claim's row.
//
// Visual appearance is unchanged from the original 11px shield icon design.
// The only additions are: role="button", tabIndex, onKeyDown, and onClick.
// =============================================================================

import React from 'react';
import { VerdictClass } from '../../lib/verdict-store';

export interface NliVerdictBadgeProps {
  /** The verdict class for this badge. */
  verdict: VerdictClass;
  /** Human-readable tooltip / aria-label text. */
  label: string;
  /** Result row id — passed to onOpen so the panel can scroll to this row. */
  resultId: string;
  /** Called when the badge is activated (click or Enter/Space). */
  onOpen: (resultId: string) => void;
}

const VERDICT_STYLES: Record<VerdictClass, { color: string; icon: string }> = {
  SUPPORTED:           { color: '#16a34a', icon: '✓' },
  PARTIALLY_SUPPORTED: { color: '#d97706', icon: '⚠' },
  INSUFFICIENT:        { color: '#9ca3af', icon: '?' },
  CONTRADICTED:        { color: '#dc2626', icon: '✗' },
  NO_EVIDENCE:         { color: '#9ca3af', icon: '○' },
  PENDING:             { color: '#60a5fa', icon: '…' },
};

export function NliVerdictBadge({ verdict, label, resultId, onOpen }: NliVerdictBadgeProps) {
  const style = VERDICT_STYLES[verdict] ?? VERDICT_STYLES.PENDING;

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      onOpen(resultId);
    }
  }

  return (
    <span
      role="button"
      tabIndex={0}
      aria-label={label}
      onClick={() => onOpen(resultId)}
      onKeyDown={handleKeyDown}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        fontSize: '11px',
        color: style.color,
        cursor: 'pointer',
        marginLeft: '2px',
        borderRadius: '3px',
        padding: '0 2px',
        outline: 'none',
        userSelect: 'none',
      }}
      // Focus ring via CSS class; we keep inline style minimal for backward compat.
      className="nli-verdict-badge"
    >
      {style.icon}
    </span>
  );
}
