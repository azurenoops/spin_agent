import * as vscode from "vscode";
import { McpClient } from "../services/mcpClient";

/**
 * IaC compliance finding from the MCP `iac_compliance_scan` tool (T081/T082).
 */
export interface IacFinding {
  findingId: string;
  ruleId: string;
  controlId: string;
  controlFamily: string;
  severity: "Critical" | "High" | "Medium" | "Low";
  catSeverity: "CAT I" | "CAT II" | "CAT III";
  title: string;
  description: string;
  lineNumber: number;
  lineContent: string;
  remediation: string;
  autoRemediable: boolean;
  suggestedFix: string | null;
  framework: string;
}

/**
 * Response shape from the IaC compliance scan MCP tool.
 */
export interface IacScanResponse {
  success: boolean;
  filePath: string;
  fileType: string;
  framework: string;
  totalFindings: number;
  findings: IacFinding[];
  scannedAt: string;
  error?: string;
  message?: string;
}

/** Supported IaC file extensions and their types. */
const IAC_LANGUAGE_MAP: Record<string, string> = {
  bicep: "bicep",
  tf: "terraform",
  json: "arm", // ARM templates — matched by content heuristic
};

/**
 * Maps STIG CAT severity to VS Code DiagnosticSeverity.
 * CAT I/II → Error, CAT III → Warning.
 */
export function mapSeverity(
  catSeverity: string
): vscode.DiagnosticSeverity {
  switch (catSeverity) {
    case "CAT I":
      return vscode.DiagnosticSeverity.Error;
    case "CAT II":
      return vscode.DiagnosticSeverity.Error;
    case "CAT III":
      return vscode.DiagnosticSeverity.Warning;
    default:
      return vscode.DiagnosticSeverity.Warning;
  }
}

/**
 * Detects the IaC file type from language ID or file extension.
 * Returns `null` if the file is not a recognized IaC format.
 */
export function detectFileType(
  document: vscode.TextDocument
): string | null {
  const langId = document.languageId;

  // Direct language ID matches
  if (langId === "bicep") return "bicep";
  if (langId === "terraform" || langId === "hcl") return "terraform";

  // Extension-based detection
  const ext = document.fileName.split(".").pop()?.toLowerCase() ?? "";
  if (ext === "bicep") return "bicep";
  if (ext === "tf" || ext === "tfvars") return "terraform";

  // ARM template detection: JSON files with "$schema" containing "deploymentTemplate"
  if ((langId === "json" || langId === "jsonc" || ext === "json") && document.lineCount > 0) {
    const firstChunk = document.getText(
      new vscode.Range(0, 0, Math.min(10, document.lineCount), 0)
    );
    if (
      firstChunk.includes("deploymentTemplate") ||
      firstChunk.includes("deploymentParameters")
    ) {
      return "arm";
    }
  }

  return null;
}

/**
 * Creates a VS Code Diagnostic from an IaC compliance finding.
 * Includes STIG rule ID, control info, and remediation in the message.
 */
export function createDiagnostic(
  finding: IacFinding,
  document: vscode.TextDocument
): vscode.Diagnostic {
  // LineNumber is 1-based from the scanner; VS Code ranges are 0-based
  const lineIndex = Math.max(0, finding.lineNumber - 1);
  const line = document.lineAt(
    Math.min(lineIndex, document.lineCount - 1)
  );

  const range = new vscode.Range(
    line.range.start,
    line.range.end
  );

  const severity = mapSeverity(finding.catSeverity);

  const message = `[${finding.ruleId}] ${finding.catSeverity}: ${finding.title} (${finding.controlId} — ${finding.controlFamily})`;

  const diagnostic = new vscode.Diagnostic(range, message, severity);
  diagnostic.code = {
    value: finding.ruleId,
    target: vscode.Uri.parse(
      `https://csrc.nist.gov/projects/cprt/catalog#/cprt/framework/version/SP_800_53_5_1_1/home?element=${finding.controlId}`
    ),
  };
  diagnostic.source = "Security Posture Intelligence Navigator";

  // Store finding data for code action provider access
  (diagnostic as any)._iacFinding = finding;

  return diagnostic;
}

/**
 * Provides inline IaC compliance diagnostics in VS Code (T083).
 *
 * Listens for document open/save/change events on IaC files (Bicep, Terraform, ARM),
 * calls the MCP `iac_compliance_scan` tool, and maps findings to VS Code diagnostics.
 * CAT I/II findings are shown as Errors, CAT III as Warnings.
 */
export class IacDiagnosticsProvider implements vscode.Disposable {
  private readonly diagnosticCollection: vscode.DiagnosticCollection;
  private readonly mcpClient: McpClient;
  private readonly disposables: vscode.Disposable[] = [];
  private debounceTimers = new Map<string, ReturnType<typeof setTimeout>>();

  /** Debounce delay (ms) for on-change scans to avoid excessive calls. */
  private static readonly DEBOUNCE_MS = 1500;

  constructor(mcpClient: McpClient) {
    this.mcpClient = mcpClient;
    this.diagnosticCollection =
      vscode.languages.createDiagnosticCollection("ato-iac-compliance");

    // Scan on document open
    this.disposables.push(
      vscode.workspace.onDidOpenTextDocument((doc) => this.scanDocument(doc))
    );

    // Scan on document save
    this.disposables.push(
      vscode.workspace.onDidSaveTextDocument((doc) => this.scanDocument(doc))
    );

    // Debounced scan on document change
    this.disposables.push(
      vscode.workspace.onDidChangeTextDocument((e) =>
        this.debounceScan(e.document)
      )
    );

    // Clear diagnostics when document is closed
    this.disposables.push(
      vscode.workspace.onDidCloseTextDocument((doc) => {
        this.diagnosticCollection.delete(doc.uri);
        this.debounceTimers.delete(doc.uri.toString());
      })
    );

    // Scan all currently open IaC files
    vscode.workspace.textDocuments.forEach((doc) => this.scanDocument(doc));
  }

  /**
   * Returns the underlying diagnostic collection (for test access).
   */
  get collection(): vscode.DiagnosticCollection {
    return this.diagnosticCollection;
  }

  /**
   * Debounces a scan to avoid excessive API calls during typing.
   */
  private debounceScan(document: vscode.TextDocument): void {
    const key = document.uri.toString();
    const existing = this.debounceTimers.get(key);
    if (existing) clearTimeout(existing);

    this.debounceTimers.set(
      key,
      setTimeout(() => {
        this.debounceTimers.delete(key);
        this.scanDocument(document);
      }, IacDiagnosticsProvider.DEBOUNCE_MS)
    );
  }

  /**
   * Scans a document for IaC compliance findings and publishes diagnostics.
   */
  async scanDocument(document: vscode.TextDocument): Promise<void> {
    const fileType = detectFileType(document);
    if (!fileType) return; // Not an IaC file

    try {
      const response = await this.mcpClient.sendMessage({
        conversationId: "iac-scan",
        message: "",
        conversationHistory: [],
        context: {
          source: "vscode-copilot",
          platform: "VSCode",
          targetAgent: "compliance",
          metadata: {
            routingHint: "iac-scan",
            fileName: document.fileName,
            language: fileType,
          },
        },
        action: "iacComplianceScan",
        actionContext: {
          filePath: document.fileName,
          fileContent: document.getText(),
          fileType,
          framework: "nist-800-53-r5",
        },
      });

      // Parse the nested scan result from the MCP response
      let scanResult: IacScanResponse | null = null;

      if (response.data && typeof response.data === "object") {
        scanResult = response.data as unknown as IacScanResponse;
      } else if (response.response) {
        try {
          scanResult = JSON.parse(response.response) as IacScanResponse;
        } catch {
          // Response wasn't JSON — ignore
          return;
        }
      }

      if (!scanResult?.success || !scanResult.findings) return;

      const diagnostics = scanResult.findings.map((finding) =>
        createDiagnostic(finding, document)
      );

      this.diagnosticCollection.set(document.uri, diagnostics);
    } catch {
      // Silently ignore scan failures — don't interrupt the user's editing
    }
  }

  /**
   * Directly sets diagnostics from a pre-parsed scan result (for testing).
   */
  setDiagnosticsFromFindings(
    uri: vscode.Uri,
    findings: IacFinding[],
    document: vscode.TextDocument
  ): void {
    const diagnostics = findings.map((f) => createDiagnostic(f, document));
    this.diagnosticCollection.set(uri, diagnostics);
  }

  dispose(): void {
    this.diagnosticCollection.dispose();
    for (const timer of this.debounceTimers.values()) {
      clearTimeout(timer);
    }
    this.debounceTimers.clear();
    for (const d of this.disposables) {
      d.dispose();
    }
  }
}
