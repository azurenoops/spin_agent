/**
 * EditorBridge payload guard (#2343).
 *
 * Validates that an unknown message received at the bridge boundary is a
 * well-formed EditorToHostEvent. Malformed payloads return false; the caller
 * is responsible for deciding whether to warn/log — the guard itself never
 * throws and never swallows errors silently.
 */

import type {
  EditorToHostEvent,
  OpenCitationSourcePanelEvent,
} from "./editorBridgeEvents";

const EDITOR_TO_HOST_COMMANDS = new Set<string>([
  "drillDown",
  "applyFix",
  "confirmRemediation",
  "updateStatus",
  "checkPim",
  "openCitationSourcePanel",
]);

const OPEN_CITATION_ORIGINS = new Set<string>(["click", "keyboard", "programmatic"]);

/**
 * Returns true when `msg` is a structurally valid EditorToHostEvent.
 *
 * For most existing commands only `command` membership is checked — they were
 * already handled without runtime validation and this guard must not break them.
 *
 * For `openCitationSourcePanel` full payload validation is enforced per AC4.
 */
export function isEditorToHostEvent(msg: unknown): msg is EditorToHostEvent {
  if (typeof msg !== "object" || msg === null) {
    return false;
  }
  const m = msg as Record<string, unknown>;
  if (typeof m["command"] !== "string") {
    return false;
  }
  if (!EDITOR_TO_HOST_COMMANDS.has(m["command"])) {
    return false;
  }
  if (m["command"] === "openCitationSourcePanel") {
    return isValidOpenCitationSourcePanel(m);
  }
  return true;
}

/**
 * Validates the full payload of an OpenCitationSourcePanel message.
 * Returns false (not throws) on any structural violation.
 */
export function isValidOpenCitationSourcePanel(
  msg: Record<string, unknown>
): msg is Record<string, unknown> & OpenCitationSourcePanelEvent {
  if (typeof msg["citationId"] !== "string" || msg["citationId"].length === 0) {
    return false;
  }
  if (typeof msg["requestId"] !== "string" || msg["requestId"].length === 0) {
    return false;
  }
  if (
    typeof msg["origin"] !== "string" ||
    !OPEN_CITATION_ORIGINS.has(msg["origin"])
  ) {
    return false;
  }
  // sourceId — optional string
  if (msg["sourceId"] !== undefined && typeof msg["sourceId"] !== "string") {
    return false;
  }
  // anchor — optional { blockId: string; offset?: number }
  if (msg["anchor"] !== undefined) {
    if (typeof msg["anchor"] !== "object" || msg["anchor"] === null) {
      return false;
    }
    const anchor = msg["anchor"] as Record<string, unknown>;
    if (typeof anchor["blockId"] !== "string") {
      return false;
    }
    if (anchor["offset"] !== undefined && typeof anchor["offset"] !== "number") {
      return false;
    }
  }
  return true;
}
