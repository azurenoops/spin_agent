/**
 * EditorBridge harness tests (#2343, AC6).
 *
 * Pure unit tests — no VS Code API required.
 * Covers: guard accept/reject, emit shape, handler dispatch simulation.
 */

import { expect } from "chai";
import {
  isEditorToHostEvent,
  isValidOpenCitationSourcePanel,
} from "../../src/bridge/editorBridgeGuard";
import type {
  OpenCitationSourcePanelEvent,
  EditorToHostEvent,
} from "../../src/bridge/editorBridgeEvents";

// ─── Guard: valid payloads ──────────────────────────────────────

describe("EditorBridgeGuard — isEditorToHostEvent", () => {
  it("accepts a minimal valid openCitationSourcePanel payload", () => {
    const msg: unknown = {
      command: "openCitationSourcePanel",
      citationId: "cite-ac2-1",
      origin: "click",
      requestId: "f47ac10b-58cc-4372-a567-0e02b2c3d479",
    };
    expect(isEditorToHostEvent(msg)).to.equal(true);
  });

  it("accepts a full valid openCitationSourcePanel payload", () => {
    const msg: unknown = {
      command: "openCitationSourcePanel",
      citationId: "cite-ac2-1",
      sourceId: "source-nist-ac2",
      anchor: { blockId: "block-001", offset: 42 },
      origin: "keyboard",
      requestId: "aaaabbbb-cccc-dddd-eeee-ffffffffffff",
      meta: { wave: 14 },
    };
    expect(isEditorToHostEvent(msg)).to.equal(true);
  });

  it("accepts all three valid origin values", () => {
    for (const origin of ["click", "keyboard", "programmatic"] as const) {
      const msg: unknown = {
        command: "openCitationSourcePanel",
        citationId: "cid",
        origin,
        requestId: "rid",
      };
      expect(isEditorToHostEvent(msg), `origin=${origin}`).to.equal(true);
    }
  });

  it("accepts existing commands without strict field validation", () => {
    const existing = [
      { command: "drillDown", controlId: "AC-2" },
      { command: "applyFix", findingId: "f1", title: "t" },
      { command: "confirmRemediation", findingId: "f1", controlId: "AC-2" },
      { command: "updateStatus", findingId: "f1", newStatus: "acknowledged" },
      { command: "checkPim" },
    ];
    for (const msg of existing) {
      expect(isEditorToHostEvent(msg), `command=${msg.command}`).to.equal(true);
    }
  });
});

// ─── Guard: invalid payloads ───────────────────────────────────

describe("EditorBridgeGuard — rejects malformed openCitationSourcePanel", () => {
  it("rejects null", () => {
    expect(isEditorToHostEvent(null)).to.equal(false);
  });

  it("rejects non-object", () => {
    expect(isEditorToHostEvent("hello")).to.equal(false);
    expect(isEditorToHostEvent(42)).to.equal(false);
  });

  it("rejects unknown command", () => {
    expect(isEditorToHostEvent({ command: "unknownCmd" })).to.equal(false);
  });

  it("rejects missing citationId", () => {
    const msg = { command: "openCitationSourcePanel", origin: "click", requestId: "rid" };
    expect(isEditorToHostEvent(msg)).to.equal(false);
  });

  it("rejects empty citationId", () => {
    const msg = { command: "openCitationSourcePanel", citationId: "", origin: "click", requestId: "rid" };
    expect(isEditorToHostEvent(msg)).to.equal(false);
  });

  it("rejects missing requestId", () => {
    const msg = { command: "openCitationSourcePanel", citationId: "cid", origin: "click" };
    expect(isEditorToHostEvent(msg)).to.equal(false);
  });

  it("rejects invalid origin value", () => {
    const msg = { command: "openCitationSourcePanel", citationId: "cid", origin: "hover", requestId: "rid" };
    expect(isEditorToHostEvent(msg)).to.equal(false);
  });

  it("rejects non-string sourceId", () => {
    const msg = { command: "openCitationSourcePanel", citationId: "cid", sourceId: 123, origin: "click", requestId: "rid" };
    expect(isEditorToHostEvent(msg)).to.equal(false);
  });

  it("rejects anchor without blockId", () => {
    const msg = {
      command: "openCitationSourcePanel",
      citationId: "cid",
      origin: "click",
      requestId: "rid",
      anchor: { offset: 5 },
    };
    expect(isEditorToHostEvent(msg)).to.equal(false);
  });

  it("rejects anchor with non-number offset", () => {
    const msg = {
      command: "openCitationSourcePanel",
      citationId: "cid",
      origin: "click",
      requestId: "rid",
      anchor: { blockId: "b1", offset: "5" },
    };
    expect(isEditorToHostEvent(msg)).to.equal(false);
  });
});

// ─── isValidOpenCitationSourcePanel direct ─────────────────────

describe("isValidOpenCitationSourcePanel", () => {
  it("accepts anchor with only blockId (offset optional)", () => {
    const msg = {
      command: "openCitationSourcePanel",
      citationId: "cid",
      origin: "programmatic",
      requestId: "rid",
      anchor: { blockId: "b1" },
    };
    expect(isValidOpenCitationSourcePanel(msg as Record<string, unknown>)).to.equal(true);
  });
});

// ─── Emit shape ────────────────────────────────────────────────

describe("OpenCitationSourcePanel emit shape", () => {
  /**
   * Simulates what the webview-side openCitationSourcePanel() function builds
   * and verifies the shape matches the EditorBridgeEvents contract.
   */
  function buildEmitPayload(
    citationId: string,
    origin: "click" | "keyboard" | "programmatic",
    sourceId?: string
  ): OpenCitationSourcePanelEvent {
    const requestId = "test-request-id-" + Math.random().toString(36).slice(2);
    const msg: OpenCitationSourcePanelEvent = {
      command: "openCitationSourcePanel",
      citationId,
      origin,
      requestId,
    };
    if (sourceId) {
      msg.sourceId = sourceId;
    }
    return msg;
  }

  it("click emit has correct shape", () => {
    const payload = buildEmitPayload("cite-ac2-1", "click", "source-nist-ac2");
    expect(payload.command).to.equal("openCitationSourcePanel");
    expect(payload.citationId).to.equal("cite-ac2-1");
    expect(payload.origin).to.equal("click");
    expect(payload.sourceId).to.equal("source-nist-ac2");
    expect(typeof payload.requestId).to.equal("string");
    expect(payload.requestId.length).to.be.greaterThan(0);
  });

  it("keyboard emit has correct origin", () => {
    const payload = buildEmitPayload("cite-ac2-2", "keyboard");
    expect(payload.origin).to.equal("keyboard");
    expect(payload.sourceId).to.be.undefined;
  });

  it("programmatic emit carries correct origin", () => {
    const payload = buildEmitPayload("cite-ac2-3", "programmatic");
    expect(payload.origin).to.equal("programmatic");
  });

  it("emitted payload passes the bridge guard", () => {
    const payload = buildEmitPayload("cite-ac2-1", "click");
    expect(isEditorToHostEvent(payload)).to.equal(true);
  });

  it("each emit generates a distinct requestId", () => {
    const ids = new Set(
      Array.from({ length: 20 }, () => buildEmitPayload("cid", "click").requestId)
    );
    expect(ids.size).to.equal(20);
  });
});

// ─── Handler dispatch simulation ───────────────────────────────

describe("Handler dispatch — openCitationSourcePanel routing", () => {
  /**
   * Simulates the receive switch selecting the correct handler branch
   * without importing VS Code APIs.
   */
  function dispatchMessage(
    message: unknown,
    handlers: Partial<Record<string, (msg: EditorToHostEvent) => void>>
  ): { handled: boolean; rejected: boolean } {
    if (!isEditorToHostEvent(message)) {
      return { handled: false, rejected: true };
    }
    const handler = handlers[message.command];
    if (handler) {
      handler(message);
      return { handled: true, rejected: false };
    }
    return { handled: false, rejected: false };
  }

  it("routes a valid openCitationSourcePanel to the correct handler", () => {
    let received: OpenCitationSourcePanelEvent | null = null;
    const result = dispatchMessage(
      {
        command: "openCitationSourcePanel",
        citationId: "cite-1",
        origin: "click",
        requestId: "r1",
      },
      {
        openCitationSourcePanel: (msg) => {
          received = msg as OpenCitationSourcePanelEvent;
        },
      }
    );
    expect(result.handled).to.equal(true);
    expect(result.rejected).to.equal(false);
    expect(received).to.not.be.null;
    expect(received!.citationId).to.equal("cite-1");
  });

  it("rejects a malformed payload without calling any handler", () => {
    let called = false;
    const result = dispatchMessage(
      { command: "openCitationSourcePanel", citationId: "", origin: "click", requestId: "r1" },
      {
        openCitationSourcePanel: () => { called = true; },
      }
    );
    expect(result.rejected).to.equal(true);
    expect(called).to.equal(false);
  });

  it("does not affect drillDown routing — no regression", () => {
    let drillDownCalled = false;
    const result = dispatchMessage(
      { command: "drillDown", controlId: "AC-2" },
      {
        drillDown: () => { drillDownCalled = true; },
      }
    );
    expect(result.handled).to.equal(true);
    expect(drillDownCalled).to.equal(true);
  });
});
