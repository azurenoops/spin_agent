import { expect } from "chai";
import {
  IacCodeActionProvider,
  parseSuggestedFix,
} from "../../src/codeActions/iacCodeActionProvider";
import * as vscode from "vscode";
import type { IacFinding } from "../../src/diagnostics/iacDiagnosticsProvider";

/**
 * Tests for IaC code action provider (T088).
 */
describe("IacCodeActionProvider", () => {
  // ─── parseSuggestedFix ────────────────────────────────────────────

  describe("parseSuggestedFix", () => {
    it("should parse a valid unified diff", () => {
      const diff =
        "--- original\n+++ fixed\n@@ -1 +1 @@\n-  endpoint: 'http://example.com'\n+  endpoint: 'https://example.com'";
      const result = parseSuggestedFix(diff);
      expect(result).to.not.be.null;
      expect(result!.original).to.equal("  endpoint: 'http://example.com'");
      expect(result!.replacement).to.equal(
        "  endpoint: 'https://example.com'"
      );
    });

    it("should return null for empty string", () => {
      expect(parseSuggestedFix("")).to.be.null;
    });

    it("should return null for malformed diff without + line", () => {
      const diff = "--- original\n+++ fixed\n@@ -1 +1 @@\n-old line only";
      expect(parseSuggestedFix(diff)).to.be.null;
    });

    it("should return null for diff without - line", () => {
      const diff = "--- original\n+++ fixed\n@@ -1 +1 @@\n+new line only";
      expect(parseSuggestedFix(diff)).to.be.null;
    });

    it("should not treat --- header as original line", () => {
      const diff =
        "--- original\n+++ fixed\n@@ -1 +1 @@\n-actual old\n+actual new";
      const result = parseSuggestedFix(diff);
      expect(result!.original).to.equal("actual old");
      expect(result!.replacement).to.equal("actual new");
    });

    it("should not treat +++ header as replacement line", () => {
      const diff =
        "--- original\n+++ fixed\n@@ -1 +1 @@\n-old\n+new";
      const result = parseSuggestedFix(diff);
      expect(result!.replacement).to.equal("new");
    });
  });

  // ─── providedCodeActionKinds ──────────────────────────────────────

  describe("providedCodeActionKinds", () => {
    it("should include QuickFix", () => {
      expect(IacCodeActionProvider.providedCodeActionKinds).to.include(
        vscode.CodeActionKind.QuickFix
      );
    });
  });

  // ─── provideCodeActions ───────────────────────────────────────────

  describe("provideCodeActions", () => {
    const provider = new IacCodeActionProvider();

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
          "--- original\n+++ fixed\n@@ -1 +1 @@\n-http://\n+https://",
        framework: "nist-800-53-r5",
        ...overrides,
      };
    }

    function makeDiagnostic(
      finding: IacFinding,
      range?: vscode.Range
    ): vscode.Diagnostic {
      const r =
        range ??
        new vscode.Range(
          new vscode.Position(4, 0),
          new vscode.Position(4, 40)
        );
      const diag = new vscode.Diagnostic(
        r,
        finding.title,
        vscode.DiagnosticSeverity.Error
      );
      diag.source = "Security Posture Intelligence Navigator";
      (diag as any)._iacFinding = finding;
      return diag;
    }

    function mockDocument(): vscode.TextDocument {
      return {
        uri: vscode.Uri.parse("file:///test/main.bicep"),
        getText: (_range?: vscode.Range) =>
          "  endpoint: 'http://example.com'",
      } as unknown as vscode.TextDocument;
    }

    function mockContext(
      diagnostics: vscode.Diagnostic[]
    ): vscode.CodeActionContext {
      return {
        diagnostics,
        triggerKind: vscode.CodeActionTriggerKind.Invoke,
        only: undefined,
      };
    }

    it("should return empty array when no ATO diagnostics", () => {
      const otherDiag = new vscode.Diagnostic(
        new vscode.Range(0, 0, 0, 10),
        "other issue",
        vscode.DiagnosticSeverity.Warning
      );
      otherDiag.source = "Other Linter";

      const actions = provider.provideCodeActions(
        mockDocument(),
        new vscode.Range(0, 0, 0, 10),
        mockContext([otherDiag]),
        new vscode.CancellationTokenSource().token
      );

      expect(actions).to.be.an("array").that.is.empty;
    });

    it("should skip non-auto-remediable findings", () => {
      const finding = makeFinding({
        autoRemediable: false,
        suggestedFix: null,
      });
      const diag = makeDiagnostic(finding);

      const actions = provider.provideCodeActions(
        mockDocument(),
        new vscode.Range(4, 0, 4, 40),
        mockContext([diag]),
        new vscode.CancellationTokenSource().token
      );

      expect(actions).to.be.an("array").that.is.empty;
    });

    it("should create a Quick Fix action for auto-remediable finding", () => {
      const finding = makeFinding();
      const diag = makeDiagnostic(finding);

      const actions = provider.provideCodeActions(
        mockDocument(),
        new vscode.Range(4, 0, 4, 40),
        mockContext([diag]),
        new vscode.CancellationTokenSource().token
      );

      expect(actions.length).to.be.greaterThanOrEqual(1);
      const fixAction = actions[0];
      expect(fixAction.title).to.include("Fix:");
      expect(fixAction.kind).to.deep.equal(vscode.CodeActionKind.QuickFix);
      expect(fixAction.isPreferred).to.be.true;
    });

    it("should attach a workspace edit to the fix action", () => {
      const finding = makeFinding();
      const diag = makeDiagnostic(finding);

      const actions = provider.provideCodeActions(
        mockDocument(),
        new vscode.Range(4, 0, 4, 40),
        mockContext([diag]),
        new vscode.CancellationTokenSource().token
      );

      const fixAction = actions[0];
      expect(fixAction.edit).to.exist;
    });

    it("should create Apply All Fixes when multiple fixable diagnostics", () => {
      const finding1 = makeFinding({
        ruleId: "SC-8-01",
        title: "HTTP detected",
        lineNumber: 5,
      });
      const finding2 = makeFinding({
        ruleId: "AC-3-01",
        title: "Public access enabled",
        lineNumber: 8,
        suggestedFix:
          "--- original\n+++ fixed\n@@ -1 +1 @@\n-Enabled\n+Disabled",
      });

      const diag1 = makeDiagnostic(
        finding1,
        new vscode.Range(4, 0, 4, 40)
      );
      const diag2 = makeDiagnostic(
        finding2,
        new vscode.Range(7, 0, 7, 40)
      );

      const actions = provider.provideCodeActions(
        mockDocument(),
        new vscode.Range(4, 0, 7, 40),
        mockContext([diag1, diag2]),
        new vscode.CancellationTokenSource().token
      );

      // Should have 2 individual fixes + 1 "Apply All" action
      expect(actions.length).to.equal(3);
      const applyAllAction = actions.find((a) =>
        a.title.includes("Apply All Fixes")
      );
      expect(applyAllAction).to.exist;
      expect(applyAllAction!.title).to.include("2 issues");
    });

    it("should not create Apply All when only one fixable diagnostic", () => {
      const finding = makeFinding();
      const diag = makeDiagnostic(finding);

      const actions = provider.provideCodeActions(
        mockDocument(),
        new vscode.Range(4, 0, 4, 40),
        mockContext([diag]),
        new vscode.CancellationTokenSource().token
      );

      const applyAllAction = actions.find((a) =>
        a.title.includes("Apply All")
      );
      expect(applyAllAction).to.be.undefined;
    });

    it("should include finding title in action label", () => {
      const finding = makeFinding({ title: "HTTP endpoint not encrypted" });
      const diag = makeDiagnostic(finding);

      const actions = provider.provideCodeActions(
        mockDocument(),
        new vscode.Range(4, 0, 4, 40),
        mockContext([diag]),
        new vscode.CancellationTokenSource().token
      );

      expect(actions[0].title).to.include("HTTP endpoint not encrypted");
    });
  });
});
