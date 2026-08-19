import * as vscode from "vscode";
import { ComplianceFinding } from "../commands/analyzeFile";
import { McpClient, McpChatResponse, ToolExecution } from "../services/mcpClient";
import { isEditorToHostEvent } from "../bridge/editorBridgeGuard";
import type { OpenCitationSourcePanelEvent } from "../bridge/editorBridgeEvents";

/**
 * Severity configuration: color, label, CSS class, sort order.
 */
interface SeverityConfig {
  label: string;
  cssClass: string;
  color: string;
  order: number;
}

const SEVERITY_MAP: Record<string, SeverityConfig> = {
  critical: { label: "CRITICAL", cssClass: "critical", color: "#9c27b0", order: 0 },
  high: { label: "HIGH", cssClass: "high", color: "#d32f2f", order: 1 },
  medium: { label: "MEDIUM", cssClass: "medium", color: "#f57c00", order: 2 },
  low: { label: "LOW", cssClass: "low", color: "#fbc02d", order: 3 },
  informational: { label: "INFO", cssClass: "informational", color: "#1976d2", order: 4 },
};

/**
 * Finding status lifecycle configuration.
 */
const STATUS_MAP: Record<string, { label: string; icon: string }> = {
  open: { label: "Open", icon: "🔴" },
  acknowledged: { label: "Acknowledged", icon: "🟡" },
  remediated: { label: "Remediated", icon: "🔵" },
  verified: { label: "Verified", icon: "🟢" },
};

/**
 * Status transition targets for finding lifecycle (FR-018b).
 */
const STATUS_TRANSITIONS: Record<string, string> = {
  open: "acknowledged",
  acknowledged: "remediated",
  remediated: "verified",
};

/**
 * Create a webview panel displaying compliance analysis findings (FR-015 through FR-020).
 * Groups findings by control family with 5-level severity badges, framework reference,
 * resource context, auto-remediation badges, and interactive actions.
 */
export function createAnalysisPanel(
  title: string,
  findings: ComplianceFinding[],
  source: string,
  mcpClient?: McpClient,
  mcpResponse?: McpChatResponse
): vscode.WebviewPanel {
  const panel = vscode.window.createWebviewPanel(
    "atoComplianceAnalysis",
    title,
    vscode.ViewColumn.Beside,
    {
      enableScripts: true,
      retainContextWhenHidden: true,
    }
  );

  panel.webview.html = buildHtml(title, findings, source, mcpResponse);

  // Handle messages from the webview (FR-014c, FR-018a, FR-018b, FR-018e, #2343)
  if (mcpClient) {
    // Debounce state for openCitationSourcePanel (AC3) — keyed on citationId,
    // suppresses duplicate emits arriving within 300 ms (e.g. double-click).
    const citationDebounce = new Map<string, number>();

    panel.webview.onDidReceiveMessage(
      async (message: unknown) => {
        if (!isEditorToHostEvent(message)) {
          // Malformed payload — warn without crashing (AC4)
          console.warn("[EditorBridge] Rejected malformed message:", message);
          return;
        }
        switch (message.command) {
          case "drillDown":
            await handleDrillDown(
              panel,
              mcpClient,
              message.controlId,
              message.conversationId
            );
            break;
          case "applyFix":
            await handleApplyFix(message as unknown as { [key: string]: unknown });
            break;
          case "confirmRemediation":
            await handleConfirmRemediation(mcpClient, message as unknown as { [key: string]: unknown });
            break;
          case "updateStatus":
            await handleStatusUpdate(
              mcpClient,
              message.findingId,
              message.newStatus,
              message.conversationId
            );
            break;
          case "checkPim":
            await handlePimCheck(mcpClient, message.conversationId);
            break;
          case "openCitationSourcePanel":
            await handleOpenCitationSourcePanel(message, citationDebounce);
            break;
        }
      }
    );
  }

  return panel;
}

/**
 * Handle drill-down on a control ID — sends action to MCP and shows detail inline (FR-014c).
 */
async function handleDrillDown(
  panel: vscode.WebviewPanel,
  mcpClient: McpClient,
  controlId: string,
  conversationId?: string
): Promise<void> {
  try {
    const response = await mcpClient.sendAction(
      conversationId ?? `drilldown-${Date.now()}`,
      "drillDown",
      { controlId }
    );
    // Send detail data back to webview
    panel.webview.postMessage({
      command: "drillDownResult",
      controlId,
      data: response.data,
      response: response.response,
    });
  } catch {
    vscode.window.showErrorMessage(`Failed to load details for ${controlId}`);
  }
}

/**
 * Handle "Apply Fix" — opens diff preview with remediation script (FR-018a).
 */
async function handleApplyFix(message: { [key: string]: unknown }): Promise<void> {
  const script = message.remediationScript as string | undefined;
  const findingTitle = message.title as string | undefined;

  if (!script) {
    vscode.window.showWarningMessage("No remediation script available");
    return;
  }

  // Open a read-only diff preview
  const doc = await vscode.workspace.openTextDocument({
    content: script,
    language: "bicep",
  });
  await vscode.window.showTextDocument(doc, {
    preview: true,
    viewColumn: vscode.ViewColumn.Beside,
  });
  vscode.window.showInformationMessage(
    `Remediation preview for: ${findingTitle ?? "finding"}. Review and use "Confirm & Apply" to execute.`
  );
}

/**
 * Handle "Confirm & Apply" — sends remediation action to MCP (FR-018a, FR-018e).
 */
async function handleConfirmRemediation(
  mcpClient: McpClient,
  message: { [key: string]: unknown }
): Promise<void> {
  const confirmed = await vscode.window.showWarningMessage(
    `Apply remediation for ${message.findingId ?? "this finding"}? This will modify cloud resources.`,
    { modal: true },
    "Confirm & Apply"
  );

  if (confirmed !== "Confirm & Apply") {
    return;
  }

  try {
    await mcpClient.sendAction(
      (message.conversationId as string) ?? `remediate-${Date.now()}`,
      "remediate",
      {
        findingId: message.findingId,
        controlId: message.controlId,
        confirmed: "true",
      }
    );
    vscode.window.showInformationMessage("Remediation applied successfully");
  } catch {
    vscode.window.showErrorMessage("Remediation failed — check output for details");
  }
}

/**
 * Handle finding status update (FR-018b).
 */
async function handleStatusUpdate(
  mcpClient: McpClient,
  findingId: string,
  newStatus: string,
  conversationId?: string
): Promise<void> {
  try {
    await mcpClient.sendAction(
      conversationId ?? `status-${Date.now()}`,
      "updateFindingStatus",
      { findingId, status: newStatus }
    );
    vscode.window.showInformationMessage(
      `Finding ${findingId} status updated to ${newStatus}`
    );
  } catch {
    vscode.window.showErrorMessage(`Failed to update status for ${findingId}`);
  }
}

/**
 * Handle PIM pre-flight check (FR-018c-i).
 */
async function handlePimCheck(
  mcpClient: McpClient,
  conversationId?: string
): Promise<void> {
  try {
    const response = await mcpClient.sendAction(
      conversationId ?? `pim-${Date.now()}`,
      "checkPimStatus",
      {}
    );
    if (response.data?.pimActive) {
      vscode.window.showInformationMessage("PIM role is active");
    } else {
      const activate = await vscode.window.showWarningMessage(
        "PIM role is not active. Activate Privileged Identity Management role to proceed.",
        "Activate PIM"
      );
      if (activate === "Activate PIM") {
        await mcpClient.sendAction(
          conversationId ?? `pim-activate-${Date.now()}`,
          "activatePim",
          {}
        );
        vscode.window.showInformationMessage("PIM activation requested");
      }
    }
  } catch {
    vscode.window.showErrorMessage("PIM check failed");
  }
}

/**
 * Handle openCitationSourcePanel (#2343, AC3).
 *
 * Opens/focuses the source-evidence panel for the given citation.
 * - Debounces rapid repeat emits keyed on citationId (300 ms window).
 * - Resolves sourceId from citationId when absent (placeholder — source panel
 *   UI is out of scope for this ticket; resolution logic lives in the panel).
 * - Shows a graceful empty-state notification when no source can be resolved.
 */
async function handleOpenCitationSourcePanel(
  msg: OpenCitationSourcePanelEvent,
  debounce: Map<string, number>
): Promise<void> {
  const now = Date.now();
  const last = debounce.get(msg.citationId) ?? 0;
  if (now - last < 300) {
    return; // debounce: suppress rapid repeat
  }
  debounce.set(msg.citationId, now);

  // Resolve sourceId — when the editor knows only the citationId, the host is
  // responsible for looking up the associated source. Until the source panel
  // UI lands, we surface the citation identifier to the user directly.
  const resolvedSourceId = msg.sourceId ?? msg.citationId;

  if (!resolvedSourceId) {
    // Graceful empty-state: no source available for this citation (AC3)
    vscode.window.showInformationMessage(
      `No source available for citation "${msg.citationId}".`
    );
    return;
  }

  // Open/focus the source panel via VS Code command.
  // The ato.openSourcePanel command is registered by the source-panel feature;
  // if not yet registered (pre-Wave 14), fall back to a user-facing notice.
  try {
    await vscode.commands.executeCommand("ato.openSourcePanel", {
      citationId: msg.citationId,
      sourceId: resolvedSourceId,
      anchor: msg.anchor,
      requestId: msg.requestId,
    });
  } catch {
    // Source panel not yet installed — graceful empty-state (AC3)
    vscode.window.showInformationMessage(
      `Source panel not available. Citation: "${msg.citationId}" (source: ${resolvedSourceId}).`
    );
  }
}

/**
 * Group findings by control family (FR-016).
 */
export function groupByControlFamily(
  findings: ComplianceFinding[]
): Map<string, ComplianceFinding[]> {
  const groups = new Map<string, ComplianceFinding[]>();
  for (const finding of findings) {
    const family = finding.controlFamily ?? (finding.controlId.replace(/[-.]?\d+.*$/, "") || "Other");
    if (!groups.has(family)) {
      groups.set(family, []);
    }
    groups.get(family)!.push(finding);
  }
  return groups;
}

/**
 * Get severity config with fallback.
 */
export function getSeverityConfig(severity: string): SeverityConfig {
  return SEVERITY_MAP[severity] ?? SEVERITY_MAP["informational"];
}

// ─── HTML Generation ───────────────────────────────────────────

function buildHtml(
  title: string,
  findings: ComplianceFinding[],
  source: string,
  mcpResponse?: McpChatResponse
): string {
  const framework = findings[0]?.frameworkReference ?? "NIST 800-53 Rev 5";
  const summarySection = buildSummarySection(findings);
  const toolsSection = mcpResponse?.toolsExecuted?.length
    ? buildToolsSection(mcpResponse.toolsExecuted)
    : "";
  const familyGroups = groupByControlFamily(findings);
  const groupsHtml = buildFamilyGroups(familyGroups, mcpResponse?.conversationId);

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>${escapeHtml(title)}</title>
  <style>${getStyles()}</style>
</head>
<body>
  <div class="header">
    <h1>${escapeHtml(title)}</h1>
    <div class="header-badges">
      <span class="framework-badge">${escapeHtml(framework)}</span>
      <span class="scope-badge">${escapeHtml(source)}</span>
    </div>
    <div class="meta">Generated: ${new Date().toISOString()}</div>
  </div>
  ${summarySection}
  ${toolsSection}
  ${
    findings.length === 0
      ? '<div class="no-findings">No compliance findings detected</div>'
      : groupsHtml
  }
  <script>${getScript()}</script>
</body>
</html>`;
}

function buildSummarySection(findings: ComplianceFinding[]): string {
  const counts: Record<string, number> = {
    critical: 0,
    high: 0,
    medium: 0,
    low: 0,
    informational: 0,
  };

  for (const f of findings) {
    const sev = f.severity ?? "informational";
    counts[sev] = (counts[sev] ?? 0) + 1;
  }

  const badges = Object.entries(SEVERITY_MAP)
    .map(
      ([key, config]) =>
        `<td class="badge ${config.cssClass}">${counts[key]} ${config.label}</td>`
    )
    .join("");

  return `
    <table class="summary">
      <tr>
        ${badges}
        <td><strong>${findings.length} Total</strong></td>
      </tr>
    </table>
  `;
}

function buildToolsSection(tools: ToolExecution[]): string {
  const rows = tools.map(
    (t) => `
    <tr>
      <td><code>${escapeHtml(t.toolName)}</code></td>
      <td>${t.executionTimeMs}ms</td>
      <td class="${t.success ? "tool-success" : "tool-failure"}">${t.success ? "✓" : "✗"}</td>
      ${t.resultSummary ? `<td>${escapeHtml(t.resultSummary)}</td>` : "<td>—</td>"}
    </tr>
  `
  );

  return `
    <details class="tools-section">
      <summary>🔧 Tools Executed (${tools.length})</summary>
      <table class="tools-table">
        <thead><tr><th>Tool</th><th>Time</th><th>Status</th><th>Summary</th></tr></thead>
        <tbody>${rows.join("")}</tbody>
      </table>
    </details>
  `;
}

function buildFamilyGroups(
  groups: Map<string, ComplianceFinding[]>,
  conversationId?: string
): string {
  const sortedEntries = Array.from(groups.entries()).sort(([a], [b]) =>
    a.localeCompare(b)
  );

  return sortedEntries
    .map(([family, familyFindings]) => {
      const sortedFindings = familyFindings.sort(
        (a, b) =>
          (SEVERITY_MAP[a.severity]?.order ?? 99) -
          (SEVERITY_MAP[b.severity]?.order ?? 99)
      );

      const findingsHtml = sortedFindings
        .map((f) => buildFindingCard(f, conversationId))
        .join("");

      return `
      <details class="family-group" open>
        <summary>
          <strong>${escapeHtml(family)}</strong>
          <span class="family-count">${familyFindings.length} finding(s)</span>
        </summary>
        ${findingsHtml}
      </details>
    `;
    })
    .join("");
}

function buildFindingCard(
  finding: ComplianceFinding,
  conversationId?: string
): string {
  const sevConfig = getSeverityConfig(finding.severity);
  const statusInfo = STATUS_MAP[finding.findingStatus ?? "open"] ?? STATUS_MAP["open"];
  const nextStatus = STATUS_TRANSITIONS[finding.findingStatus ?? "open"];

  const autoRemediableBadge = finding.autoRemediable
    ? '<span class="badge auto-remediable">🔧 Auto-Remediate</span>'
    : "";

  const resourceInfo =
    finding.resourceId || finding.resourceType
      ? `<div class="resource-info">
          ${finding.resourceType ? `<span class="resource-type">${escapeHtml(finding.resourceType)}</span>` : ""}
          ${finding.resourceId ? `<code class="resource-id">${escapeHtml(finding.resourceId)}</code>` : ""}
        </div>`
      : "";

  const riskBadge = finding.riskLevel
    ? `<span class="badge risk-${finding.riskLevel}">${escapeHtml(finding.riskLevel.toUpperCase())} Risk</span>`
    : "";

  const statusBadge = `<span class="status-badge status-${finding.findingStatus ?? "open"}">${statusInfo.icon} ${statusInfo.label}</span>`;

  // Action buttons
  const actions: string[] = [];

  if (finding.autoRemediable && finding.remediationScript) {
    actions.push(
      `<button class="action-btn apply-fix" onclick="applyFix('${escapeAttr(finding.findingId ?? "")}', '${escapeAttr(finding.title)}')">Apply Fix</button>`
    );
    actions.push(
      `<button class="action-btn confirm-btn" onclick="confirmRemediation('${escapeAttr(finding.findingId ?? "")}', '${escapeAttr(finding.controlId)}', '${escapeAttr(conversationId ?? "")}')">Confirm &amp; Apply</button>`
    );
  }

  if (nextStatus) {
    actions.push(
      `<button class="action-btn status-btn" onclick="updateStatus('${escapeAttr(finding.findingId ?? "")}', '${escapeAttr(nextStatus)}', '${escapeAttr(conversationId ?? "")}')">${escapeHtml(nextStatus.charAt(0).toUpperCase() + nextStatus.slice(1))}</button>`
    );
  }

  const actionsHtml = actions.length > 0
    ? `<div class="actions">${actions.join("")}</div>`
    : "";

  return `
    <div class="finding" data-finding-id="${escapeAttr(finding.findingId ?? "")}">
      <div class="finding-header">
        <span class="badge ${sevConfig.cssClass}">${sevConfig.label}</span>
        <a class="control-link" href="#" onclick="drillDown('${escapeAttr(finding.controlId)}', '${escapeAttr(conversationId ?? "")}'); return false;">
          <code>${escapeHtml(finding.controlId)}</code>
        </a>
        <strong>${escapeHtml(finding.title)}</strong>
        ${autoRemediableBadge}
        ${riskBadge}
        ${statusBadge}
      </div>
      ${resourceInfo}
      <p class="description">${escapeHtml(finding.description)}</p>
      <div class="recommendation">
        <strong>Recommendation:</strong> ${escapeHtml(finding.recommendation)}
      </div>
      <div class="drill-down-container" id="detail-${escapeAttr(finding.controlId)}"></div>
      ${actionsHtml}
    </div>
  `;
}

// ─── Styles ────────────────────────────────────────────────────

function getStyles(): string {
  return `
    body { font-family: var(--vscode-font-family, sans-serif); padding: 16px; color: var(--vscode-foreground); background: var(--vscode-editor-background); }
    h1 { font-size: 1.4em; margin-bottom: 4px; }
    h2 { font-size: 1.1em; margin-top: 24px; }
    .header { margin-bottom: 16px; }
    .header-badges { display: flex; gap: 8px; margin: 8px 0; }
    .framework-badge { display: inline-block; padding: 3px 10px; border-radius: 4px; font-size: 0.85em; font-weight: bold; background: #1565c0; color: #fff; }
    .scope-badge { display: inline-block; padding: 3px 10px; border-radius: 4px; font-size: 0.85em; background: var(--vscode-badge-background); color: var(--vscode-badge-foreground); }
    .meta { color: var(--vscode-descriptionForeground); font-size: 0.85em; }
    .summary { margin-bottom: 16px; }
    .summary td { padding: 4px 12px; }
    .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 0.85em; font-weight: bold; color: #fff; }
    .badge.critical { background: #9c27b0; }
    .badge.high { background: #d32f2f; }
    .badge.medium { background: #f57c00; }
    .badge.low { background: #fbc02d; color: #333; }
    .badge.informational { background: #1976d2; }
    .badge.auto-remediable { background: #00897b; }
    .badge.risk-critical { background: #9c27b0; }
    .badge.risk-high { background: #d32f2f; }
    .badge.risk-medium { background: #f57c00; }
    .badge.risk-low { background: #388e3c; }
    .status-badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 0.8em; background: var(--vscode-badge-background); color: var(--vscode-badge-foreground); margin-left: 4px; }
    .status-open { border-left: 3px solid #d32f2f; }
    .status-acknowledged { border-left: 3px solid #fbc02d; }
    .status-remediated { border-left: 3px solid #1976d2; }
    .status-verified { border-left: 3px solid #388e3c; }
    .family-group { border: 1px solid var(--vscode-panel-border); border-radius: 8px; margin-bottom: 16px; padding: 4px; }
    .family-group > summary { cursor: pointer; padding: 8px 12px; font-size: 1.05em; display: flex; align-items: center; gap: 8px; }
    .family-count { font-size: 0.85em; color: var(--vscode-descriptionForeground); }
    .finding { border: 1px solid var(--vscode-panel-border); border-radius: 6px; padding: 12px; margin: 8px; }
    .finding-header { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; flex-wrap: wrap; }
    .finding-header code { font-family: var(--vscode-editor-font-family, monospace); background: var(--vscode-textCodeBlock-background); padding: 2px 6px; border-radius: 3px; }
    .control-link { text-decoration: none; cursor: pointer; }
    .control-link:hover code { text-decoration: underline; color: var(--vscode-textLink-foreground); }
    .resource-info { display: flex; gap: 8px; align-items: center; margin-bottom: 6px; font-size: 0.9em; color: var(--vscode-descriptionForeground); }
    .resource-type { font-style: italic; }
    .resource-id { font-family: var(--vscode-editor-font-family, monospace); background: var(--vscode-textCodeBlock-background); padding: 1px 4px; border-radius: 3px; font-size: 0.85em; }
    .description { margin: 4px 0; }
    .recommendation { background: rgba(56, 142, 60, 0.1); padding: 8px 12px; border-radius: 4px; margin-top: 8px; }
    .actions { display: flex; gap: 8px; margin-top: 8px; }
    .action-btn { padding: 4px 12px; border-radius: 4px; border: 1px solid var(--vscode-button-border, transparent); cursor: pointer; font-size: 0.85em; }
    .apply-fix { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
    .confirm-btn { background: var(--vscode-button-background); color: var(--vscode-button-foreground); }
    .status-btn { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
    .action-btn:hover { opacity: 0.85; }
    .no-findings { text-align: center; padding: 32px; color: var(--vscode-descriptionForeground); }
    .drill-down-container { margin-top: 8px; padding: 8px; display: none; border-left: 3px solid var(--vscode-textLink-foreground); }
    .drill-down-container.active { display: block; }
    .tools-section { border: 1px solid var(--vscode-panel-border); border-radius: 8px; padding: 8px; margin-bottom: 16px; }
    .tools-section > summary { cursor: pointer; padding: 4px 8px; }
    .tools-table { width: 100%; border-collapse: collapse; margin-top: 8px; }
    .tools-table th, .tools-table td { text-align: left; padding: 4px 8px; border-bottom: 1px solid var(--vscode-panel-border); }
    .tool-success { color: #388e3c; }
    .tool-failure { color: #d32f2f; }
    .progress-container { margin: 16px 0; }
    .progress-bar { height: 4px; background: var(--vscode-progressBar-background); border-radius: 2px; transition: width 0.3s ease; }
    .progress-label { font-size: 0.85em; color: var(--vscode-descriptionForeground); }
  `;
}

// ─── Script ────────────────────────────────────────────────────

function getScript(): string {
  return `
    const vscode = acquireVsCodeApi();

    function drillDown(controlId, conversationId) {
      vscode.postMessage({ command: 'drillDown', controlId, conversationId });
    }

    function applyFix(findingId, title) {
      const finding = document.querySelector('[data-finding-id="' + findingId + '"]');
      const script = finding ? finding.dataset.remediationScript : '';
      vscode.postMessage({ command: 'applyFix', findingId, title, remediationScript: script });
    }

    function confirmRemediation(findingId, controlId, conversationId) {
      vscode.postMessage({ command: 'confirmRemediation', findingId, controlId, conversationId });
    }

    function updateStatus(findingId, newStatus, conversationId) {
      vscode.postMessage({ command: 'updateStatus', findingId, newStatus, conversationId });
    }

    function checkPim(conversationId) {
      vscode.postMessage({ command: 'checkPim', conversationId });
    }

    /**
     * openCitationSourcePanel — editor→host bridge dispatch (#2343, AC2).
     *
     * @param {string} citationId  - stable citation identifier
     * @param {string} origin      - 'click' | 'keyboard' | 'programmatic'
     * @param {string} [sourceId]  - optional pre-resolved source identifier
     */
    function openCitationSourcePanel(citationId, origin, sourceId) {
      const requestId = crypto.randomUUID();
      const msg = {
        command: 'openCitationSourcePanel',
        citationId,
        origin,
        requestId,
      };
      if (sourceId) {
        msg.sourceId = sourceId;
      }
      vscode.postMessage(msg);
    }

    // Delegated citation interaction listener (AC2).
    // Fires openCitationSourcePanel for any element carrying [data-citation-id].
    // Handles both click and keyboard (Enter / Space) activation.
    document.addEventListener('click', function(event) {
      const target = event.target && event.target.closest
        ? event.target.closest('[data-citation-id]')
        : null;
      if (!target) return;
      const citationId = target.getAttribute('data-citation-id');
      const sourceId = target.getAttribute('data-source-id') || undefined;
      if (citationId) {
        openCitationSourcePanel(citationId, 'click', sourceId);
      }
    });

    document.addEventListener('keydown', function(event) {
      if (event.key !== 'Enter' && event.key !== ' ') return;
      const target = event.target && event.target.closest
        ? event.target.closest('[data-citation-id]')
        : null;
      if (!target) return;
      event.preventDefault();
      const citationId = target.getAttribute('data-citation-id');
      const sourceId = target.getAttribute('data-source-id') || undefined;
      if (citationId) {
        openCitationSourcePanel(citationId, 'keyboard', sourceId);
      }
    });

    // Handle messages from extension host
    window.addEventListener('message', event => {
      const message = event.data;
      if (message.command === 'drillDownResult') {
        const container = document.getElementById('detail-' + message.controlId);
        if (container) {
          container.innerHTML = '<p>' + (message.response || 'No additional details available.') + '</p>';
          container.classList.add('active');
        }
      }
      if (message.command === 'progressUpdate') {
        updateProgress(message.percentage, message.label);
      }
    });

    function updateProgress(percentage, label) {
      let container = document.getElementById('progress-container');
      if (!container) {
        container = document.createElement('div');
        container.id = 'progress-container';
        container.className = 'progress-container';
        container.innerHTML = '<div class="progress-bar" id="progress-bar" style="width:0%"></div><div class="progress-label" id="progress-label"></div>';
        document.body.insertBefore(container, document.body.firstChild.nextSibling);
      }
      const bar = document.getElementById('progress-bar');
      const lbl = document.getElementById('progress-label');
      if (bar) bar.style.width = percentage + '%';
      if (lbl) lbl.textContent = label || (percentage + '%');
    }
  `;
}

// ─── Utilities ─────────────────────────────────────────────────

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function escapeAttr(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/'/g, "\\'")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}
