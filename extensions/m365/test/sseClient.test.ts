import { expect } from "chai";
import { parseSseChunk, SseClient } from "../src/services/sseClient";

describe("SSE Client (FR-029d, FR-029e)", () => {
  describe("parseSseChunk", () => {
    it("should parse a single SSE event", () => {
      const chunk = 'event: agentRouted\ndata: {"agent":"ComplianceAgent"}\n\n';
      const events = parseSseChunk(chunk);
      expect(events).to.have.length(1);
      expect(events[0].event).to.equal("agentRouted");
      expect(events[0].data.agent).to.equal("ComplianceAgent");
    });

    it("should parse multiple SSE events", () => {
      const chunk =
        'event: thinking\ndata: {"message":"Analyzing..."}\n\n' +
        'event: toolStart\ndata: {"tool":"scan"}\n\n';
      const events = parseSseChunk(chunk);
      expect(events).to.have.length(2);
      expect(events[0].event).to.equal("thinking");
      expect(events[1].event).to.equal("toolStart");
    });

    it("should default event type to message", () => {
      const chunk = 'data: {"text":"hello"}\n\n';
      const events = parseSseChunk(chunk);
      expect(events).to.have.length(1);
      expect(events[0].event).to.equal("message");
    });

    it("should handle malformed JSON gracefully", () => {
      const chunk = "event: error\ndata: not-json\n\n";
      const events = parseSseChunk(chunk);
      expect(events).to.have.length(1);
      expect(events[0].data.raw).to.equal("not-json");
    });

    it("should skip empty chunks", () => {
      const chunk = "\n\n\n\n";
      const events = parseSseChunk(chunk);
      expect(events).to.have.length(0);
    });

    it("should include timestamp", () => {
      const chunk = 'event: complete\ndata: {"timestamp":"2025-01-01T00:00:00Z"}\n\n';
      const events = parseSseChunk(chunk);
      expect(events[0].timestamp).to.equal("2025-01-01T00:00:00Z");
    });
  });

  describe("SseClient constructor", () => {
    it("should accept options", () => {
      const client = new SseClient({
        baseUrl: "http://localhost:5000",
        apiKey: "test-key",
        maxRetries: 5,
      });
      expect(client).to.exist;
    });

    it("should use default options when not specified", () => {
      const client = new SseClient({ baseUrl: "http://localhost:5000" });
      expect(client).to.exist;
    });
  });
});
