// =============================================================================
// ResearchSourcesCard — Issue #2437 / TraceabilityPanel
//
// Lists research source documents associated with an assistant message.
// Sources are supplied via the `sources` prop (derived from message.metadata).
//
// New in this version (Issue #2437):
//   • Accepts `highlightSourceId?: string` — when set, the matching card
//     receives a transient highlight ring: 300ms fade-in, 2s hold, 300ms out.
//     Respects prefers-reduced-motion (instant show, no animation).
//   • Exposes an aria-live="polite" region that announces which source was
//     highlighted (for screen readers).
//   • When a source card is clicked, calls `onSourceClick(sourceId)` so the
//     TraceabilityPanel can highlight any claims grounded in that source
//     (bidirectional traceability).
// =============================================================================

import React, { useEffect, useRef, useState } from 'react';

export interface ResearchSource {
  id: string;
  title: string;
  section?: string;
  url?: string;
}

export interface ResearchSourcesCardProps {
  sources: ResearchSource[];
  /** When set, applies a transient highlight ring to the matching source card. */
  highlightSourceId?: string;
  /** Called when the user clicks a source card (bidirectional traceability). */
  onSourceClick?: (sourceId: string) => void;
}

export function ResearchSourcesCard({
  sources,
  highlightSourceId,
  onSourceClick,
}: ResearchSourcesCardProps) {
  const [announcement, setAnnouncement] = useState('');
  const [highlightedId, setHighlightedId] = useState<string | undefined>();
  const cardRefs = useRef<Map<string, HTMLDivElement>>(new Map());
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Prefer-reduced-motion query — evaluated once at mount.
  const reducedMotion =
    typeof window !== 'undefined' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  useEffect(() => {
    if (!highlightSourceId) return;

    // Clear previous timeout.
    if (timeoutRef.current) clearTimeout(timeoutRef.current);

    setHighlightedId(highlightSourceId);

    // Scroll the card into view.
    const el = cardRefs.current.get(highlightSourceId);
    if (el) el.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth', block: 'nearest' });

    // Announce to screen reader.
    const src = sources.find((s) => s.id === highlightSourceId);
    if (src) setAnnouncement(`Source highlighted: ${src.title}`);

    // Clear highlight after 2.6s (300 + 2000 + 300).
    timeoutRef.current = setTimeout(
      () => {
        setHighlightedId(undefined);
        setAnnouncement('');
      },
      reducedMotion ? 0 : 2600
    );

    return () => {
      if (timeoutRef.current) clearTimeout(timeoutRef.current);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [highlightSourceId]);

  if (sources.length === 0) return null;

  return (
    <div className="research-sources-card" style={{ marginTop: '12px' }}>
      {/* aria-live region for screen reader announcements */}
      <div aria-live="polite" aria-atomic="true" style={srOnlyStyle}>
        {announcement}
      </div>

      <div style={{ fontSize: '11px', fontWeight: 600, color: '#6b7280', marginBottom: '6px', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
        Sources
      </div>

      {sources.map((source) => {
        const isHighlighted = highlightedId === source.id;
        return (
          <div
            key={source.id}
            ref={(el) => {
              if (el) cardRefs.current.set(source.id, el);
              else cardRefs.current.delete(source.id);
            }}
            data-highlighted={isHighlighted ? 'true' : undefined}
            onClick={() => onSourceClick?.(source.id)}
            role={onSourceClick ? 'button' : undefined}
            tabIndex={onSourceClick ? 0 : undefined}
            onKeyDown={
              onSourceClick
                ? (e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      onSourceClick(source.id);
                    }
                  }
                : undefined
            }
            aria-label={onSourceClick ? `Select source: ${source.title}` : undefined}
            style={{
              padding: '8px 10px',
              borderRadius: '8px',
              border: `1.5px solid ${isHighlighted ? '#3b82f6' : '#e5e7eb'}`,
              background: isHighlighted ? '#eff6ff' : '#fafafa',
              marginBottom: '4px',
              fontSize: '12px',
              cursor: onSourceClick ? 'pointer' : 'default',
              transition: reducedMotion ? 'none' : 'border-color 0.3s ease, background 0.3s ease',
              outline: 'none',
            }}
            className="research-source-card-item"
          >
            <div style={{ fontWeight: 600, color: '#1f2937', marginBottom: '2px' }}>{source.title}</div>
            {source.section && (
              <div style={{ color: '#6b7280' }}>{source.section}</div>
            )}
            {source.url && (
              <a
                href={source.url}
                target="_blank"
                rel="noopener noreferrer"
                style={{ color: '#3b82f6', textDecoration: 'underline', fontSize: '11px' }}
                onClick={(e) => e.stopPropagation()}
              >
                View source
              </a>
            )}
          </div>
        );
      })}
    </div>
  );
}

// ── Helpers ──────────────────────────────────────────────────────────────────

const srOnlyStyle: React.CSSProperties = {
  position: 'absolute',
  width: '1px',
  height: '1px',
  padding: 0,
  margin: '-1px',
  overflow: 'hidden',
  clip: 'rect(0,0,0,0)',
  whiteSpace: 'nowrap',
  border: 0,
};
