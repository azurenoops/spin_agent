// =============================================================================
// featureFlags.ts — GATE-2437
//
// Central feature flag registry for the Chat ClientApp.
// All flags read from environment variables at module-load time so they can
// be overridden per-environment without code changes.
//
// CRA convention: env vars exposed to the browser must be prefixed REACT_APP_.
// (Note: the Hawkeye artifact uses VITE_FEATURE_* — that prefix is for Vite
//  projects. This app uses react-scripts, so the prefix is REACT_APP_.)
// =============================================================================

/**
 * GATE-2437 — TraceabilityPanel
 *
 * When true:
 *   - TraceabilityPanel renders in ChatWindow
 *   - Toolbar toggle button is visible
 *   - Alt+T keyboard shortcut is active
 *   - First-run nudge is eligible to appear
 *
 * Default: false (off until explicitly enabled via env var)
 *
 * To enable locally:  REACT_APP_FEATURE_TRACEABILITY_PANEL=true npm start
 * To enable in CI/CD: set the env var in the deployment pipeline
 */
export const isTraceabilityPanelEnabled: boolean =
  process.env.REACT_APP_FEATURE_TRACEABILITY_PANEL === 'true';

/**
 * GATE-1357 — Workspace Collaboration & Document Sharing
 *
 * When true:
 *   - CollaborationProvider connects to /hubs/collaboration
 *   - Share button + presence avatars are visible in Header
 *   - ConflictResolutionBanner and MobileCollaborationBanner mount in ChatWindow
 *
 * Default: false
 *
 * To enable locally:  REACT_APP_FEATURE_COLLABORATION=true npm start
 */
export const isCollaborationEnabled: boolean =
  process.env.REACT_APP_FEATURE_COLLABORATION === 'true';

/**
 * GATE-1703 — Citation Style Picker
 *
 * When true:
 *   - CitationStylePicker popover/bottom-sheet is accessible from Header
 *   - WorkspaceCitationsPanel renders in standard/research modes
 *   - Selected style persists to localStorage under citationStyleHistory:[userId]
 *
 * Default: false
 *
 * To enable locally:  REACT_APP_FEATURE_CITATION_STYLE_PICKER=true npm start
 */
export const isCitationStylePickerEnabled: boolean =
  process.env.REACT_APP_FEATURE_CITATION_STYLE_PICKER === 'true';

/**
 * GATE-940/939 — Editor Suite Phase 1: Provenance Span Tracking
 *
 * When true:
 *   - ProvenanceSpan objects are attached to AI-generated editor nodes
 *   - user_modified flag flips on any manual edit of a provenanced span
 *   - Split/merge operations append ProvenanceHistoryEntry (append-only; never delete)
 *
 * Default: false
 *
 * To enable locally:  REACT_APP_FEATURE_PROVENANCE=true npm start
 */
export const isProvenanceEnabled: boolean =
  process.env.REACT_APP_FEATURE_PROVENANCE === 'true';

/**
 * GATE-1458/P2 — Editor Suite Phase 2: Streaming Diff + Citation Badges
 *
 * Requires GATE-940/939 (isProvenanceEnabled) to be true for badges to show.
 * When true:
 *   - Editor renders token-level diff (added/removed runs) during AI streaming
 *   - Inline citation badges appear as provenance spans resolve (≤150ms)
 *
 * Default: false
 *
 * To enable locally:  REACT_APP_FEATURE_STREAMING_DIFF=true npm start
 */
export const isStreamingDiffEnabled: boolean =
  process.env.REACT_APP_FEATURE_STREAMING_DIFF === 'true';

/**
 * GATE-1457/P3 — Editor Suite Phase 3: Quote Anchoring + Drift Detection
 *
 * When true:
 *   - Pasted quotes store a QuoteAnchor {source_id, char_range, hash_of_quoted_text}
 *   - On document load, each anchor is drift-checked; mismatches show a visual warning
 *
 * Default: false
 *
 * To enable locally:  REACT_APP_FEATURE_QUOTE_ANCHORING=true npm start
 */
export const isQuoteAnchoringEnabled: boolean =
  process.env.REACT_APP_FEATURE_QUOTE_ANCHORING === 'true';

/**
 * GATE-1458/P4 — Editor Suite Phase 4: Caption Registry
 *
 * When true:
 *   - Session-scoped Caption Registry deduplicates figure/table captions on insert
 *   - Each caption entry back-links to its source_id
 *
 * Default: false
 *
 * To enable locally:  REACT_APP_FEATURE_CAPTION_REGISTRY=true npm start
 */
export const isCaptionRegistryEnabled: boolean =
  process.env.REACT_APP_FEATURE_CAPTION_REGISTRY === 'true';

/**
 * GATE-9e3ff67 — Editor Suite Phase 5: AnchorRegistry
 *
 * When true:
 *   - AnchorRegistry assigns stable UUID anchor IDs to provenance-tracked spans
 *   - Remap transactions keep IDs correct after insert/delete/replace operations
 *   - CI drift gate asserts 0 orphaned anchors after 200 remap cycles
 *
 * Default: false
 *
 * To enable locally:  REACT_APP_FEATURE_ANCHOR_REGISTRY=true npm start
 */
export const isAnchorRegistryEnabled: boolean =
  process.env.REACT_APP_FEATURE_ANCHOR_REGISTRY === 'true';

// DEV advisory — fires once at module-load time (not per render)
if (process.env.NODE_ENV === 'development' && !isTraceabilityPanelEnabled) {
  // eslint-disable-next-line no-console
  console.info(
    '[GATE-2437] TraceabilityPanel is OFF — set REACT_APP_FEATURE_TRACEABILITY_PANEL=true to enable'
  );
}
