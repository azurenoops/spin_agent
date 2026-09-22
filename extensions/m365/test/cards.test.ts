import { expect } from "chai";
import {
  buildComplianceCard,
  buildGenericCard,
  buildErrorCard,
  buildFollowUpCard,
} from "../src/cards";
import { buildSystemSummaryCard, type SystemSummaryData } from "../src/cards/systemSummaryCard";
import { buildCategorizationCard, type CategorizationData } from "../src/cards/categorizationCard";
import { buildAuthorizationCard, type AuthorizationData } from "../src/cards/authorizationCard";
import { buildDashboardCard, type DashboardData } from "../src/cards/dashboardCard";

describe("Adaptive Card Builders", () => {
  describe("Common Card Properties", () => {
    it("should produce Adaptive Card v1.5 JSON for all card types", () => {
      const cards = [
        buildComplianceCard({ complianceScore: 85, passedControls: 10, warningControls: 2, failedControls: 1 }),
        buildGenericCard({ response: "Hello" }),
        buildErrorCard({ errorMessage: "Error occurred" }),
        buildFollowUpCard({ followUpPrompt: "Need more info", missingFields: ["subscription"] }),
      ];

      for (const card of cards) {
        expect(card.type).to.equal("AdaptiveCard");
        expect(card.version).to.equal("1.5");
        expect(card.$schema).to.equal("http://adaptivecards.io/schemas/adaptive-card.json");
        expect(card.body).to.be.an("array").that.is.not.empty;
      }
    });
  });

  describe("Compliance Card (FR-043)", () => {
    it("should show score ≥80% in Good (green) color", () => {
      const card = buildComplianceCard({
        complianceScore: 85,
        passedControls: 17,
        warningControls: 2,
        failedControls: 1,
      });
      const body = card.body as any[];
      const scoreBlock = body.find(
        (b: any) => b.type === "TextBlock" && b.text === "85%"
      );
      expect(scoreBlock).to.exist;
      expect(scoreBlock.color).to.equal("Good");
    });

    it("should show score ≥60% <80% in Warning (orange) color", () => {
      const card = buildComplianceCard({
        complianceScore: 65,
        passedControls: 13,
        warningControls: 4,
        failedControls: 3,
      });
      const body = card.body as any[];
      const scoreBlock = body.find(
        (b: any) => b.type === "TextBlock" && b.text === "65%"
      );
      expect(scoreBlock).to.exist;
      expect(scoreBlock.color).to.equal("Warning");
    });

    it("should show score <60% in Attention (red) color", () => {
      const card = buildComplianceCard({
        complianceScore: 45,
        passedControls: 9,
        warningControls: 3,
        failedControls: 8,
      });
      const body = card.body as any[];
      const scoreBlock = body.find(
        (b: any) => b.type === "TextBlock" && b.text === "45%"
      );
      expect(scoreBlock).to.exist;
      expect(scoreBlock.color).to.equal("Attention");
    });

    it("should include passed/warning/failed column counts", () => {
      const card = buildComplianceCard({
        complianceScore: 80,
        passedControls: 16,
        warningControls: 2,
        failedControls: 2,
      });
      const body = card.body as any[];
      const columnSet = body.find((b: any) => b.type === "ColumnSet");
      expect(columnSet).to.exist;
      expect(columnSet.columns).to.have.length(3);

      const passedCol = columnSet.columns[0];
      expect(passedCol.items[0].text).to.include("Passed");
      expect(passedCol.items[1].text).to.equal("16");

      const warningCol = columnSet.columns[1];
      expect(warningCol.items[0].text).to.include("Warning");
      expect(warningCol.items[1].text).to.equal("2");

      const failedCol = columnSet.columns[2];
      expect(failedCol.items[0].text).to.include("Failed");
      expect(failedCol.items[1].text).to.equal("2");
    });

    it("should include View Full Report and Generate Remediation Plan actions", () => {
      const card = buildComplianceCard({
        complianceScore: 90,
        passedControls: 18,
        warningControls: 1,
        failedControls: 1,
      });
      const actions = card.actions as any[];
      expect(actions).to.have.length(2);
      expect(actions[0].title).to.equal("View Full Report");
      expect(actions[0].type).to.equal("Action.OpenUrl");
      expect(actions[1].title).to.equal("Generate Remediation Plan");
      expect(actions[1].type).to.equal("Action.Submit");
      expect(actions[1].data.action).to.equal("remediate");
    });
  });

  describe("Generic Card", () => {
    it("should display response text", () => {
      const card = buildGenericCard({ response: "Hello from Security Posture Intelligence Navigator" });
      const body = card.body as any[];
      const textBlock = body.find(
        (b: any) => b.text === "Hello from Security Posture Intelligence Navigator"
      );
      expect(textBlock).to.exist;
    });

    it("should show agent attribution when provided", () => {
      const card = buildGenericCard({
        response: "Answer",
        agentUsed: "ComplianceAgent",
      });
      const body = card.body as any[];
      const attribution = body.find(
        (b: any) =>
          b.type === "TextBlock" && typeof b.text === "string" && b.text.includes("Processed by: ComplianceAgent")
      );
      expect(attribution).to.exist;
    });
  });

  describe("Error Card", () => {
    it("should display error message", () => {
      const card = buildErrorCard({ errorMessage: "Something went wrong" });
      const body = card.body as any[];
      const errorBlock = body.find(
        (b: any) => b.text === "Something went wrong"
      );
      expect(errorBlock).to.exist;
    });

    it("should show help text when provided", () => {
      const card = buildErrorCard({
        errorMessage: "Error",
        helpText: "Try again later",
      });
      const body = card.body as any[];
      const helpBlock = body.find(
        (b: any) => typeof b.text === "string" && b.text.includes("Try again later")
      );
      expect(helpBlock).to.exist;
    });

    it("should display error header in Attention color", () => {
      const card = buildErrorCard({ errorMessage: "Error" });
      const body = card.body as any[];
      const header = body.find(
        (b: any) => b.type === "TextBlock" && b.color === "Attention"
      );
      expect(header).to.exist;
    });
  });

  describe("Follow-Up Card (FR-041)", () => {
    it("should render missing fields as numbered list", () => {
      const card = buildFollowUpCard({
        followUpPrompt: "I need more details",
        missingFields: ["Subscription ID", "Resource Group"],
      });
      const body = card.body as any[];
      const fieldsBlock = body.find(
        (b: any) =>
          b.type === "TextBlock" && typeof b.text === "string" && b.text.includes("1. Subscription ID")
      );
      expect(fieldsBlock).to.exist;
      expect(fieldsBlock.text).to.include("2. Resource Group");
    });

    it("should create quick-reply actions for each missing field", () => {
      const card = buildFollowUpCard({
        followUpPrompt: "Need more info",
        missingFields: ["field1", "field2", "field3"],
      });
      const actions = card.actions as any[];
      expect(actions).to.have.length(3);
      expect(actions[0].title).to.equal("field1");
      expect(actions[0].data.quickReply).to.equal("field1");
      expect(actions[1].title).to.equal("field2");
      expect(actions[2].title).to.equal("field3");
    });

    it("should display follow-up prompt text", () => {
      const card = buildFollowUpCard({
        followUpPrompt: "What subscription should I scan?",
        missingFields: ["subscription"],
      });
      const body = card.body as any[];
      const promptBlock = body.find(
        (b: any) => b.text === "What subscription should I scan?"
      );
      expect(promptBlock).to.exist;
    });
  });

  // ── US13 Cards (T173) ──────────────────────────────────────────────

  describe("System Summary Card (US13, T168)", () => {
    const baseData: SystemSummaryData = {
      systemName: "ACME Portal",
      acronym: "AP",
      systemType: "Major Application",
      hostingEnvironment: "Azure Commercial",
      currentRmfStep: "Implement",
      rmfStepNumber: 4,
      missionCriticality: "Mission Essential",
      impactLevel: "IL4",
      complianceScore: 82,
      activeAlerts: 3,
      isActive: true,
    };

    it("should produce a valid Adaptive Card v1.5", () => {
      const card = buildSystemSummaryCard(baseData);
      expect(card.type).to.equal("AdaptiveCard");
      expect(card.version).to.equal("1.5");
      expect(card.$schema).to.equal("http://adaptivecards.io/schemas/adaptive-card.json");
      expect(card.body).to.be.an("array").that.is.not.empty;
    });

    it("should display the system name", () => {
      const card = buildSystemSummaryCard(baseData);
      const body = card.body as any[];
      const found = JSON.stringify(body).includes("ACME Portal");
      expect(found).to.be.true;
    });

    it("should display the acronym", () => {
      const card = buildSystemSummaryCard(baseData);
      const body = card.body as any[];
      const found = JSON.stringify(body).includes("(AP)");
      expect(found).to.be.true;
    });

    it("should color compliance score ≥80 as Good", () => {
      const card = buildSystemSummaryCard({ ...baseData, complianceScore: 85 });
      const json = JSON.stringify(card.body);
      expect(json).to.include('"85%"');
      // Score block should have Good color
      const body = card.body as any[];
      const scoreText = findNestedText(body, "85%");
      expect(scoreText?.color).to.equal("Good");
    });

    it("should color compliance score <60 as Attention", () => {
      const card = buildSystemSummaryCard({ ...baseData, complianceScore: 45 });
      const body = card.body as any[];
      const scoreText = findNestedText(body, "45%");
      expect(scoreText?.color).to.equal("Attention");
    });

    it("should show Active status for active systems", () => {
      const card = buildSystemSummaryCard({ ...baseData, isActive: true });
      const json = JSON.stringify(card.body);
      expect(json).to.include("Active");
    });

    it("should show Inactive status for inactive systems", () => {
      const card = buildSystemSummaryCard({ ...baseData, isActive: false });
      const json = JSON.stringify(card.body);
      expect(json).to.include("Inactive");
    });

    it("should include RMF step with icon", () => {
      const card = buildSystemSummaryCard(baseData);
      const json = JSON.stringify(card.body);
      expect(json).to.include("🔧");
      expect(json).to.include("Implement");
    });

    it("should include action buttons", () => {
      const card = buildSystemSummaryCard(baseData);
      const actions = card.actions as any[];
      expect(actions.length).to.be.at.least(3);
      const titles = actions.map((a: any) => a.title);
      expect(titles).to.include("View Compliance Details");
      expect(titles).to.include("Check RMF Progress");
      expect(titles).to.include("View Authorization Status");
    });

    it("should include agent attribution when provided", () => {
      const card = buildSystemSummaryCard({ ...baseData, agentUsed: "ComplianceAgent" });
      const json = JSON.stringify(card.body);
      expect(json).to.include("Processed by: ComplianceAgent");
    });

    it("should include suggestion buttons when provided", () => {
      const card = buildSystemSummaryCard({
        ...baseData,
        suggestions: ["Check controls", "View findings"],
        conversationId: "conv-123",
      });
      const actions = card.actions as any[];
      const suggestionActions = actions.filter((a: any) => a.data?.message);
      expect(suggestionActions).to.have.length(2);
    });
  });

  describe("Categorization Card (US13, T169)", () => {
    const baseData: CategorizationData = {
      systemName: "ACME Portal",
      fipsCategory: "Moderate",
      impactLevel: "IL4",
      confidentialityImpact: "Moderate",
      integrityImpact: "Moderate",
      availabilityImpact: "Low",
      overallImpact: "Moderate",
    };

    it("should produce a valid Adaptive Card v1.5", () => {
      const card = buildCategorizationCard(baseData);
      expect(card.type).to.equal("AdaptiveCard");
      expect(card.version).to.equal("1.5");
      expect(card.body).to.be.an("array").that.is.not.empty;
    });

    it("should display the system name", () => {
      const card = buildCategorizationCard(baseData);
      const json = JSON.stringify(card.body);
      expect(json).to.include("ACME Portal");
    });

    it("should display FIPS 199 category", () => {
      const card = buildCategorizationCard(baseData);
      const body = card.body as any[];
      const fipsText = findNestedText(body, "Moderate");
      expect(fipsText).to.exist;
    });

    it("should display DoD Impact Level", () => {
      const card = buildCategorizationCard(baseData);
      const json = JSON.stringify(card.body);
      expect(json).to.include("IL4");
    });

    it("should display C/I/A impact levels with icons", () => {
      const card = buildCategorizationCard(baseData);
      const json = JSON.stringify(card.body);
      // Moderate → 🟡, Low → 🟢
      expect(json).to.include("🟡");
      expect(json).to.include("🟢");
    });

    it("should render information types table when provided", () => {
      const card = buildCategorizationCard({
        ...baseData,
        informationTypes: [
          { name: "Financial Data", confidentiality: "High", integrity: "Moderate", availability: "Low" },
          { name: "PII", confidentiality: "High", integrity: "High", availability: "Moderate" },
        ],
      });
      const json = JSON.stringify(card.body);
      expect(json).to.include("Financial Data");
      expect(json).to.include("PII");
      expect(json).to.include("Information Types");
    });

    it("should truncate information types at 10 with overflow message", () => {
      const types = Array.from({ length: 12 }, (_, i) => ({
        name: `Type ${i + 1}`,
        confidentiality: "Low",
        integrity: "Low",
        availability: "Low",
      }));
      const card = buildCategorizationCard({ ...baseData, informationTypes: types });
      const json = JSON.stringify(card.body);
      expect(json).to.include("+ 2 more information types");
    });

    it("should include Select Control Baseline action", () => {
      const card = buildCategorizationCard(baseData);
      const actions = card.actions as any[];
      const titles = actions.map((a: any) => a.title);
      expect(titles).to.include("Select Control Baseline");
    });

    it("should display justification when provided", () => {
      const card = buildCategorizationCard({
        ...baseData,
        justification: "System processes sensitive DoD data",
      });
      const json = JSON.stringify(card.body);
      expect(json).to.include("System processes sensitive DoD data");
    });
  });

  describe("Authorization Card (US13, T170)", () => {
    const baseData: AuthorizationData = {
      systemName: "ACME Portal",
      decisionType: "ATO",
      status: "Active",
      riskLevel: "Moderate",
      authorizedDate: "2024-01-15",
      expirationDate: "2027-01-15",
      daysUntilExpiration: 900,
      authorizingOfficialName: "Col. Smith",
    };

    it("should produce a valid Adaptive Card v1.5", () => {
      const card = buildAuthorizationCard(baseData);
      expect(card.type).to.equal("AdaptiveCard");
      expect(card.version).to.equal("1.5");
      expect(card.body).to.be.an("array").that.is.not.empty;
    });

    it("should display ATO decision type with label", () => {
      const card = buildAuthorizationCard(baseData);
      const json = JSON.stringify(card.body);
      expect(json).to.include("Authority to Operate");
      expect(json).to.include("ATO");
    });

    it("should color ATO as Good", () => {
      const card = buildAuthorizationCard(baseData);
      const body = card.body as any[];
      const atoText = findNestedText(body, "Authority to Operate (ATO)");
      expect(atoText?.color).to.equal("Good");
    });

    it("should color DATO as Attention", () => {
      const card = buildAuthorizationCard({ ...baseData, decisionType: "DATO" });
      const body = card.body as any[];
      const datoText = findNestedText(body, "Denial of Authority to Operate (DATO)");
      expect(datoText?.color).to.equal("Attention");
    });

    it("should show expiration countdown in Good for distant dates", () => {
      const card = buildAuthorizationCard({ ...baseData, daysUntilExpiration: 900 });
      const body = card.body as any[];
      const expText = findNestedText(body, "900 days remaining");
      expect(expText).to.exist;
      expect(expText?.color).to.equal("Good");
    });

    it("should show expiration countdown in Attention for ≤30 days", () => {
      const card = buildAuthorizationCard({ ...baseData, daysUntilExpiration: 15 });
      const body = card.body as any[];
      const expText = findNestedText(body, "15 days remaining");
      expect(expText).to.exist;
      expect(expText?.color).to.equal("Attention");
    });

    it("should show EXPIRED for negative days", () => {
      const card = buildAuthorizationCard({ ...baseData, daysUntilExpiration: -10 });
      const json = JSON.stringify(card.body);
      expect(json).to.include("EXPIRED");
      expect(json).to.include("10 days ago");
    });

    it("should display authorizing official name", () => {
      const card = buildAuthorizationCard(baseData);
      const json = JSON.stringify(card.body);
      expect(json).to.include("Col. Smith");
    });

    it("should render conditions when provided", () => {
      const card = buildAuthorizationCard({
        ...baseData,
        conditions: [
          { description: "Must implement MFA within 90 days", status: "in progress" },
          { description: "Annual pen test required", status: "met" },
        ],
      });
      const json = JSON.stringify(card.body);
      expect(json).to.include("Must implement MFA within 90 days");
      expect(json).to.include("Annual pen test required");
      expect(json).to.include("🔄"); // in progress
      expect(json).to.include("✅"); // met
    });

    it("should include authorization package and ConMon actions", () => {
      const card = buildAuthorizationCard(baseData);
      const actions = card.actions as any[];
      const titles = actions.map((a: any) => a.title);
      expect(titles).to.include("View Authorization Package");
      expect(titles).to.include("Generate ConMon Report");
    });
  });

  describe("Dashboard Card (US13, T171)", () => {
    const baseData: DashboardData = {
      systems: [
        {
          systemName: "ACME Portal",
          acronym: "AP",
          currentRmfStep: "Monitor",
          complianceScore: 92,
          atoStatus: "Active",
          activeAlerts: 1,
        },
        {
          systemName: "HR System",
          acronym: "HRS",
          currentRmfStep: "Implement",
          complianceScore: 68,
          atoStatus: "IATT",
          activeAlerts: 5,
        },
        {
          systemName: "Finance App",
          currentRmfStep: "Categorize",
          complianceScore: 0,
          atoStatus: "Pending",
          activeAlerts: 0,
        },
      ],
      criticalAlerts: 2,
      expiringAtos: 1,
    };

    it("should produce a valid Adaptive Card v1.5", () => {
      const card = buildDashboardCard(baseData);
      expect(card.type).to.equal("AdaptiveCard");
      expect(card.version).to.equal("1.5");
      expect(card.body).to.be.an("array").that.is.not.empty;
    });

    it("should display total system count", () => {
      const card = buildDashboardCard(baseData);
      const json = JSON.stringify(card.body);
      expect(json).to.include("3 registered systems");
    });

    it("should calculate average compliance score", () => {
      const card = buildDashboardCard(baseData);
      const json = JSON.stringify(card.body);
      // (92 + 68 + 0) / 3 ≈ 53
      expect(json).to.include("53%");
    });

    it("should use provided average score when given", () => {
      const card = buildDashboardCard({ ...baseData, averageComplianceScore: 75 });
      const json = JSON.stringify(card.body);
      expect(json).to.include("75%");
    });

    it("should display critical alerts count", () => {
      const card = buildDashboardCard(baseData);
      const body = card.body as any[];
      const alertText = findNestedText(body, "2");
      expect(alertText).to.exist;
    });

    it("should display expiring ATOs count", () => {
      const card = buildDashboardCard(baseData);
      const json = JSON.stringify(card.body);
      // expiring ATOs = 1
      const body = card.body as any[];
      const found = findNestedText(body, "1");
      expect(found).to.exist;
    });

    it("should render RMF distribution when provided", () => {
      const card = buildDashboardCard({
        ...baseData,
        rmfDistribution: [
          { step: "Monitor", count: 1 },
          { step: "Implement", count: 1 },
          { step: "Categorize", count: 1 },
        ],
      });
      const json = JSON.stringify(card.body);
      expect(json).to.include("RMF Step Distribution");
      expect(json).to.include("📊"); // Monitor icon
      expect(json).to.include("🔧"); // Implement icon
      expect(json).to.include("🏷️"); // Categorize icon
    });

    it("should render per-system rows with names and scores", () => {
      const card = buildDashboardCard(baseData);
      const json = JSON.stringify(card.body);
      expect(json).to.include("ACME Portal (AP)");
      expect(json).to.include("HR System (HRS)");
      expect(json).to.include("Finance App");
      expect(json).to.include("92%");
      expect(json).to.include("68%");
    });

    it("should show ATO status icons per system", () => {
      const card = buildDashboardCard(baseData);
      const json = JSON.stringify(card.body);
      expect(json).to.include("🟢"); // Active
      expect(json).to.include("🟡"); // IATT
      expect(json).to.include("⏳"); // Pending
    });

    it("should truncate at 15 systems with overflow message", () => {
      const systems = Array.from({ length: 18 }, (_, i) => ({
        systemName: `System ${i + 1}`,
        complianceScore: 80,
        atoStatus: "Active",
      }));
      const card = buildDashboardCard({ systems });
      const json = JSON.stringify(card.body);
      expect(json).to.include("+ 3 more systems");
    });

    it("should include View All Systems action", () => {
      const card = buildDashboardCard(baseData);
      const actions = card.actions as any[];
      const titles = actions.map((a: any) => a.title);
      expect(titles).to.include("View All Systems");
      expect(titles).to.include("Compliance Summary");
      expect(titles).to.include("Expiring ATOs");
    });
  });
});

/** Helper: recursively find a TextBlock with the given text in a nested body structure. */
function findNestedText(items: any[], text: string): any | undefined {
  for (const item of items) {
    if (item.type === "TextBlock" && item.text === text) return item;
    if (item.columns) {
      for (const col of item.columns) {
        if (col.items) {
          const found = findNestedText(col.items, text);
          if (found) return found;
        }
      }
    }
    if (item.items) {
      const found = findNestedText(item.items, text);
      if (found) return found;
    }
  }
  return undefined;
}
