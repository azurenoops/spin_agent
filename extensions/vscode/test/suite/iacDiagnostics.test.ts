import { expect } from "chai";
import {
  mapSeverity,
  detectFileType,
  createDiagnostic,
  IacFinding,
} from "../../src/diagnostics/iacDiagnosticsProvider";
import * as vscode from "vscode";

/**
 * Tests for IaC diagnostics provider utilities (T087).
 */
describe("IacDiagnosticsProvider", () => {
  // ─── mapSeverity ──────────────────────────────────────────────────

  describe("mapSeverity", () => {
    it("should map CAT I to Error", () => {
      expect(mapSeverity("CAT I")).to.equal(vscode.DiagnosticSeverity.Error);
    });

    it("should map CAT II to Error", () => {
      expect(mapSeverity("CAT II")).to.equal(vscode.DiagnosticSeverity.Error);
    });

    it("should map CAT III to Warning", () => {
      expect(mapSeverity("CAT III")).to.equal(
        vscode.DiagnosticSeverity.Warning
      );
    });

    it("should default unknown severity to Warning", () => {
      expect(mapSeverity("unknown")).to.equal(
        vscode.DiagnosticSeverity.Warning
      );
    });
  });

  // ─── detectFileType ───────────────────────────────────────────────

  describe("detectFileType", () => {
    function mockDocument(overrides: {
      languageId?: string;
      fileName?: string;
      lineCount?: number;
      getText?: () => string;
    }): vscode.TextDocument {
      return {
        languageId: overrides.languageId ?? "plaintext",
        fileName: overrides.fileName ?? "file.txt",
        lineCount: overrides.lineCount ?? 1,
        getText: overrides.getText ?? (() => ""),
      } as unknown as vscode.TextDocument;
    }

    it("should detect bicep by languageId", () => {
      const doc = mockDocument({ languageId: "bicep" });
      expect(detectFileType(doc)).to.equal("bicep");
    });

    it("should detect terraform by languageId", () => {
      const doc = mockDocument({ languageId: "terraform" });
      expect(detectFileType(doc)).to.equal("terraform");
    });

    it("should detect hcl language as terraform", () => {
      const doc = mockDocument({ languageId: "hcl" });
      expect(detectFileType(doc)).to.equal("terraform");
    });

    it("should detect bicep by file extension", () => {
      const doc = mockDocument({ fileName: "main.bicep" });
      expect(detectFileType(doc)).to.equal("bicep");
    });

    it("should detect terraform by .tf extension", () => {
      const doc = mockDocument({ fileName: "main.tf" });
      expect(detectFileType(doc)).to.equal("terraform");
    });

    it("should detect terraform by .tfvars extension", () => {
      const doc = mockDocument({ fileName: "variables.tfvars" });
      expect(detectFileType(doc)).to.equal("terraform");
    });

    it("should detect ARM template by JSON content", () => {
      const doc = mockDocument({
        languageId: "json",
        fileName: "azuredeploy.json",
        lineCount: 5,
        getText: () =>
          '{\n  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json"\n}',
      });
      expect(detectFileType(doc)).to.equal("arm");
    });

    it("should return null for non-IaC files", () => {
      const doc = mockDocument({
        languageId: "markdown",
        fileName: "readme.md",
      });
      expect(detectFileType(doc)).to.be.null;
    });

    it("should return null for regular JSON without ARM schema", () => {
      const doc = mockDocument({
        languageId: "json",
        fileName: "package.json",
        lineCount: 3,
        getText: () => '{\n  "name": "my-package"\n}',
      });
      expect(detectFileType(doc)).to.be.null;
    });
  });

  // ─── createDiagnostic ────────────────────────────────────────────

  describe("createDiagnostic", () => {
    function makeFinding(overrides?: Partial<IacFinding>): IacFinding {
      return {
        findingId: "IAC-SC-8-01-5",
        ruleId: "SC-8-01",
        controlId: "SC-8",
        controlFamily: "System and Communications Protection",
        severity: "High",
        catSeverity: "CAT I",
        title: "Unencrypted HTTP protocol detected",
        description: "Resources should use HTTPS.",
        lineNumber: 5,
        lineContent: "  endpoint: 'http://example.com'",
        remediation: "Change http:// to https://.",
        autoRemediable: true,
        suggestedFix:
          "--- original\n+++ fixed\n@@ -1 +1 @@\n-  endpoint: 'http://example.com'\n+  endpoint: 'https://example.com'",
        framework: "nist-800-53-r5",
        ...overrides,
      };
    }

    function mockDocumentForDiag(lineCount: number = 10): vscode.TextDocument {
      return {
        lineCount,
        lineAt: (line: number) => ({
          range: new vscode.Range(
            new vscode.Position(line, 0),
            new vscode.Position(line, 40)
          ),
        }),
      } as unknown as vscode.TextDocument;
    }

    it("should create a diagnostic with correct severity", () => {
      const finding = makeFinding({ catSeverity: "CAT I" });
      const doc = mockDocumentForDiag();
      const diag = createDiagnostic(finding, doc);
      expect(diag.severity).to.equal(vscode.DiagnosticSeverity.Error);
    });

    it("should create a warning for CAT III", () => {
      const finding = makeFinding({ catSeverity: "CAT III", severity: "Low" });
      const doc = mockDocumentForDiag();
      const diag = createDiagnostic(finding, doc);
      expect(diag.severity).to.equal(vscode.DiagnosticSeverity.Warning);
    });

    it("should set source to Security Posture Intelligence Navigator", () => {
      const diag = createDiagnostic(makeFinding(), mockDocumentForDiag());
      expect(diag.source).to.equal("Security Posture Intelligence Navigator");
    });

    it("should include rule ID in message", () => {
      const diag = createDiagnostic(makeFinding(), mockDocumentForDiag());
      expect(diag.message).to.include("SC-8-01");
    });

    it("should include CAT severity in message", () => {
      const diag = createDiagnostic(makeFinding(), mockDocumentForDiag());
      expect(diag.message).to.include("CAT I");
    });

    it("should include control family in message", () => {
      const diag = createDiagnostic(makeFinding(), mockDocumentForDiag());
      expect(diag.message).to.include("System and Communications Protection");
    });

    it("should set code with NIST link", () => {
      const diag = createDiagnostic(makeFinding(), mockDocumentForDiag());
      const code = diag.code as { value: string; target: vscode.Uri };
      expect(code.value).to.equal("SC-8-01");
      expect(code.target.toString()).to.include("csrc.nist.gov");
      expect(code.target.toString()).to.include("SC-8");
    });

    it("should store finding on diagnostic for code action provider", () => {
      const finding = makeFinding();
      const diag = createDiagnostic(finding, mockDocumentForDiag());
      const stored = (diag as any)._iacFinding as IacFinding;
      expect(stored).to.exist;
      expect(stored.ruleId).to.equal("SC-8-01");
      expect(stored.suggestedFix).to.include("--- original");
    });

    it("should clamp lineNumber to valid range", () => {
      const finding = makeFinding({ lineNumber: 100 });
      const doc = mockDocumentForDiag(10);
      // Should not throw — lineAt should receive at most lineCount - 1
      const diag = createDiagnostic(finding, doc);
      expect(diag).to.exist;
    });

    it("should handle lineNumber 0 gracefully", () => {
      const finding = makeFinding({ lineNumber: 0 });
      const doc = mockDocumentForDiag(5);
      const diag = createDiagnostic(finding, doc);
      expect(diag).to.exist;
    });
  });
});
