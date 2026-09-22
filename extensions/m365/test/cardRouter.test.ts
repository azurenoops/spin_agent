import { expect } from "chai";
import { selectCard } from "../src/cards/cardRouter";
import type { McpResponse } from "../src/services/atoApiClient";

describe("Card Router (FR-011)", () => {
  function makeResponse(overrides: Partial<McpResponse>): McpResponse {
    return {
      response: "Test response",
      ...overrides,
    };
  }

  it("should route errors to error card (P0)", () => {
    const card = selectCard(
      makeResponse({
        success: false,
        errors: [{ errorCode: "E001", message: "Something failed" }],
      })
    );
    const body = card.body as any[];
    const header = body.find((b: any) => b.color === "Attention");
    expect(header).to.exist;
  });

  it("should route follow-up to follow-up card (P1)", () => {
    const card = selectCard(
      makeResponse({
        requiresFollowUp: true,
        followUpPrompt: "Which subscription?",
        missingFields: ["subscriptionId"],
      })
    );
    const body = card.body as any[];
    const prompt = body.find((b: any) => b.text === "Which subscription?");
    expect(prompt).to.exist;
  });

  it("should route clarification to clarification card (P2)", () => {
    const card = selectCard(
      makeResponse({
        data: { type: "clarification" },
        followUpPrompt: "Need more info",
        missingFields: ["env"],
      })
    );
    const body = card.body as any[];
    const input = body.find((b: any) => b.type === "Input.Text");
    expect(input).to.exist;
  });

  it("should route compliance finding to finding card (P3)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "compliance",
        data: { type: "finding", title: "Encryption missing", severity: "High" },
      })
    );
    const body = card.body as any[];
    const titleBlock = body.find(
      (b: any) => typeof b.text === "string" && b.text.includes("Encryption missing")
    );
    expect(titleBlock).to.exist;
  });

  it("should route compliance remediationPlan to remediation card (P4)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "compliance",
        data: { type: "remediationPlan", riskReduction: 30 },
      })
    );
    const body = card.body as any[];
    const reductionBlock = body.find(
      (b: any) => typeof b.text === "string" && b.text.includes("30%")
    );
    expect(reductionBlock).to.exist;
  });

  it("should route compliance alert to alert card (P5)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "compliance",
        data: { type: "alert", alertId: "A-99", severity: "Medium" },
      })
    );
    const body = card.body as any[];
    const header = body.find(
      (b: any) => typeof b.text === "string" && b.text.includes("Security Alert")
    );
    expect(header).to.exist;
    // Alert ID is in action data
    const actions = card.actions as any[];
    const ackAction = actions.find((a: any) => a.title === "Acknowledge");
    expect(ackAction.data.actionContext.alertId).to.equal("A-99");
  });

  it("should route compliance trend to trend card (P6)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "compliance",
        data: {
          type: "trend",
          dataPoints: [{ date: "2025-01-01", score: 80 }],
          direction: "improving",
        },
      })
    );
    const body = card.body as any[];
    const sparkBlock = body.find(
      (b: any) => typeof b.text === "string" && /[▁▂▃▄▅▆▇█]/.test(b.text)
    );
    expect(sparkBlock).to.exist;
  });

  it("should route compliance evidence to evidence card (P7)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "compliance",
        data: { type: "evidence", completeness: 50, items: [] },
      })
    );
    const body = card.body as any[];
    const progressBlock = body.find(
      (b: any) => typeof b.text === "string" && b.text.includes("50%")
    );
    expect(progressBlock).to.exist;
  });

  it("should route compliance kanban to kanban card (P8)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "compliance",
        data: { type: "kanban", tasks: [] },
      })
    );
    const body = card.body as any[];
    const header = body.find(
      (b: any) => typeof b.text === "string" && b.text.includes("Remediation")
    );
    expect(header).to.exist;
  });

  it("should route generic compliance to compliance card (P9)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "compliance",
        data: { type: "assessment", complianceScore: 85 },
      })
    );
    const body = card.body as any[];
    const scoreBlock = body.find(
      (b: any) => typeof b.text === "string" && b.text.includes("85%")
    );
    expect(scoreBlock).to.exist;
  });

  it("should route knowledgebase control to NIST control card (P10)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "knowledgebase",
        data: { type: "control", controlId: "AC-2", statement: "Account management" },
      })
    );
    const body = card.body as any[];
    const controlBlock = body.find(
      (b: any) => typeof b.text === "string" && b.text.includes("AC-2")
    );
    expect(controlBlock).to.exist;
  });

  it("should route knowledgebase to knowledge base card (P11)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "knowledgebase",
      })
    );
    const body = card.body as any[];
    const header = body.find(
      (b: any) => typeof b.text === "string" && b.text.includes("Knowledge Base")
    );
    expect(header).to.exist;
  });

  it("should route configuration to configuration card (P12)", () => {
    const card = selectCard(
      makeResponse({
        intentType: "configuration",
        data: { framework: "NIST 800-53" },
      })
    );
    const body = card.body as any[];
    const factSet = body.find((b: any) => b.type === "FactSet");
    expect(factSet).to.exist;
  });

  it("should fall back to generic card (P99)", () => {
    const card = selectCard(makeResponse({}));
    const body = card.body as any[];
    const header = body.find(
      (b: any) => b.text === "Security Posture Intelligence Navigator"
    );
    expect(header).to.exist;
  });
});
