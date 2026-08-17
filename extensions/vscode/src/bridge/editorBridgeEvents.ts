/**
 * EditorBridgeEvents — typed discriminated union for all webview↔host messages.
 *
 * Schema version: 1.1.0
 *   v1.0.0 — initial retroactive typing of existing ad-hoc commands
 *   v1.1.0 — added OpenCitationSourcePanel (#2343, Wave 14 cutover gate)
 *
 * Direction legend:
 *   editor→host  webview postMessage → extension onDidReceiveMessage
 *   host→editor  panel.webview.postMessage → webview window.addEventListener('message')
 */

// ─── Editor → Host ─────────────────────────────────────────────

export interface DrillDownEvent {
  command: "drillDown";
  controlId: string;
  conversationId?: string;
}

export interface ApplyFixEvent {
  command: "applyFix";
  findingId: string;
  title: string;
  remediationScript?: string;
}

export interface ConfirmRemediationEvent {
  command: "confirmRemediation";
  findingId: string;
  controlId: string;
  conversationId?: string;
}

export interface UpdateStatusEvent {
  command: "updateStatus";
  findingId: string;
  newStatus: string;
  conversationId?: string;
}

export interface CheckPimEvent {
  command: "checkPim";
  conversationId?: string;
}

/**
 * OpenCitationSourcePanel — editor→host signal to open/focus the source-evidence
 * panel for a specific inline citation (#2343).
 *
 * Fields:
 *   citationId  — stable citation identifier (required)
 *   sourceId    — optional pre-resolved source; host resolves from citationId when absent
 *   anchor      — optional position hint for deep-linking within the source
 *   origin      — interaction type for UX/analytics
 *   requestId   — caller-generated UUID for correlation and future ack
 *   meta        — reserved optional bag for forward-compatible extensions (v1 additive)
 */
export interface OpenCitationSourcePanelEvent {
  command: "openCitationSourcePanel";
  citationId: string;
  sourceId?: string;
  anchor?: {
    blockId: string;
    offset?: number;
  };
  origin: "click" | "keyboard" | "programmatic";
  requestId: string;
  meta?: Record<string, unknown>;
}

/** All editor→host events. */
export type EditorToHostEvent =
  | DrillDownEvent
  | ApplyFixEvent
  | ConfirmRemediationEvent
  | UpdateStatusEvent
  | CheckPimEvent
  | OpenCitationSourcePanelEvent;

// ─── Host → Editor ─────────────────────────────────────────────

export interface DrillDownResultEvent {
  command: "drillDownResult";
  controlId: string;
  data?: unknown;
  response?: string;
}

export interface ProgressUpdateEvent {
  command: "progressUpdate";
  percentage: number;
  label?: string;
}

/** All host→editor events. */
export type HostToEditorEvent = DrillDownResultEvent | ProgressUpdateEvent;

/** Union of all bridge events (both directions). */
export type EditorBridgeEvents = EditorToHostEvent | HostToEditorEvent;
