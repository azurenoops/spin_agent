import * as vscode from "vscode";
import { IacFinding } from "../diagnostics/iacDiagnosticsProvider";

/**
 * Parses a unified diff suggested fix into {original, replacement} line pairs.
 * Expected format:
 * ```
 * --- original
 * +++ fixed
 * @@ -1 +1 @@
 * -{originalLine}
 * +{replacementLine}
 * ```
 */
export function parseSuggestedFix(
  suggestedFix: string
): { original: string; replacement: string } | null {
  const lines = suggestedFix.split("\n");
  let original: string | null = null;
  let replacement: string | null = null;

  for (const line of lines) {
    if (line.startsWith("-") && !line.startsWith("---")) {
      original = line.substring(1);
    } else if (line.startsWith("+") && !line.startsWith("+++")) {
      replacement = line.substring(1);
    }
  }

  if (original !== null && replacement !== null) {
    return { original, replacement };
  }
  return null;
}

/**
 * Extracts the IaC finding from a diagnostic (stored by IacDiagnosticsProvider).
 */
function getFinding(
  diagnostic: vscode.Diagnostic
): IacFinding | undefined {
  return (diagnostic as any)._iacFinding as IacFinding | undefined;
}

/**
 * Provides Quick Fix code actions for IaC compliance findings (T084).
 *
 * When a diagnostic has an auto-remediable finding with a `suggestedFix`,
 * this provider offers:
 * - "Fix: {title}" — apply the specific fix
 * - "Apply All Fixes" — apply all non-conflicting auto-remediable fixes in the file
 */
export class IacCodeActionProvider implements vscode.CodeActionProvider {
  static readonly providedCodeActionKinds = [
    vscode.CodeActionKind.QuickFix,
  ];

  provideCodeActions(
    document: vscode.TextDocument,
    range: vscode.Range | vscode.Selection,
    context: vscode.CodeActionContext,
    _token: vscode.CancellationToken
  ): vscode.CodeAction[] {
    const actions: vscode.CodeAction[] = [];

    // Individual fix actions for each fixable diagnostic in range
    const fixableDiagnostics: vscode.Diagnostic[] = [];

    for (const diagnostic of context.diagnostics) {
      if (diagnostic.source !== "Security Posture Intelligence Navigator") continue;

      const finding = getFinding(diagnostic);
      if (!finding?.autoRemediable || !finding.suggestedFix) continue;

      fixableDiagnostics.push(diagnostic);

      const fix = parseSuggestedFix(finding.suggestedFix);
      if (!fix) continue;

      const action = new vscode.CodeAction(
        `Fix: ${finding.title}`,
        vscode.CodeActionKind.QuickFix
      );
      action.diagnostics = [diagnostic];
      action.isPreferred = true;

      const edit = new vscode.WorkspaceEdit();
      edit.replace(
        document.uri,
        diagnostic.range,
        this.applyFix(
          document.getText(diagnostic.range),
          fix.original,
          fix.replacement
        )
      );
      action.edit = edit;
      actions.push(action);
    }

    // "Apply All Fixes" action when there are multiple fixable diagnostics
    if (fixableDiagnostics.length > 1) {
      const allFixAction = this.createApplyAllAction(
        document,
        fixableDiagnostics
      );
      if (allFixAction) {
        actions.push(allFixAction);
      }
    }

    return actions;
  }

  /**
   * Applies a fix by replacing the original pattern with the replacement.
   * Falls back to returning the replacement directly if exact match fails.
   */
  private applyFix(
    lineText: string,
    original: string,
    replacement: string
  ): string {
    if (lineText.includes(original)) {
      return lineText.replace(original, replacement);
    }
    // Fall back to a case-insensitive replace
    const idx = lineText.toLowerCase().indexOf(original.toLowerCase());
    if (idx >= 0) {
      return (
        lineText.substring(0, idx) +
        replacement +
        lineText.substring(idx + original.length)
      );
    }
    // Direct replacement as last resort
    return replacement;
  }

  /**
   * Creates an "Apply All Fixes" code action that applies all non-conflicting fixes.
   * Processes fixes from bottom to top to preserve line numbers.
   */
  private createApplyAllAction(
    document: vscode.TextDocument,
    diagnostics: vscode.Diagnostic[]
  ): vscode.CodeAction | null {
    const edit = new vscode.WorkspaceEdit();
    let appliedCount = 0;

    // Sort diagnostics by line number descending to avoid line shift issues
    const sorted = [...diagnostics].sort(
      (a, b) => b.range.start.line - a.range.start.line
    );

    // Track lines already modified to avoid conflicts
    const modifiedLines = new Set<number>();

    for (const diagnostic of sorted) {
      const finding = getFinding(diagnostic);
      if (!finding?.suggestedFix) continue;

      const fix = parseSuggestedFix(finding.suggestedFix);
      if (!fix) continue;

      const lineNum = diagnostic.range.start.line;
      if (modifiedLines.has(lineNum)) continue; // Skip conflicting fix

      edit.replace(
        document.uri,
        diagnostic.range,
        this.applyFix(
          document.getText(diagnostic.range),
          fix.original,
          fix.replacement
        )
      );
      modifiedLines.add(lineNum);
      appliedCount++;
    }

    if (appliedCount === 0) return null;

    const action = new vscode.CodeAction(
      `Apply All Fixes (${appliedCount} issues)`,
      vscode.CodeActionKind.QuickFix
    );
    action.diagnostics = diagnostics;
    action.edit = edit;

    return action;
  }
}
