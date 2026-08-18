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

// DEV advisory — fires once at module-load time (not per render)
if (process.env.NODE_ENV === 'development' && !isTraceabilityPanelEnabled) {
  // eslint-disable-next-line no-console
  console.info(
    '[GATE-2437] TraceabilityPanel is OFF — set REACT_APP_FEATURE_TRACEABILITY_PANEL=true to enable'
  );
}
