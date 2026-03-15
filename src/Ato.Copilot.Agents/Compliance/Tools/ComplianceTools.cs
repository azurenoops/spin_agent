using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

/// <summary>
/// Tool for running NIST 800-53 compliance assessments.
/// Post-assessment: detects findings and offers Kanban board creation/update.
/// </summary>
public class ComplianceAssessmentTool : BaseTool
{
    private readonly IAtoComplianceEngine _complianceEngine;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Initializes a new instance of the <see cref="ComplianceAssessmentTool"/> class.</summary>
    /// <param name="complianceEngine">The compliance engine for running assessments.</param>
    /// <param name="scopeFactory">Service scope factory for resolving scoped services.</param>
    /// <param name="logger">Logger instance.</param>
    public ComplianceAssessmentTool(
        IAtoComplianceEngine complianceEngine,
        IServiceScopeFactory scopeFactory,
        ILogger<ComplianceAssessmentTool> logger) : base(logger)
    {
        _complianceEngine = complianceEngine;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public override string Name => "compliance_assess";
    /// <inheritdoc />
    public override string Description => "Run a NIST 800-53 compliance assessment against Azure resources. Supports scan types: quick, policy, full.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription ID", Type = "string", Required = true },
        ["framework"] = new() { Name = "framework", Description = "Compliance framework", Type = "string" },
        ["control_families"] = new() { Name = "control_families", Description = "Control families to assess", Type = "string" },
        ["resource_types"] = new() { Name = "resource_types", Description = "Resource types to assess", Type = "string" },
        ["scan_type"] = new() { Name = "scan_type", Description = "Scan type: quick, policy, full", Type = "string" },
        ["include_passed"] = new() { Name = "include_passed", Description = "Include passed controls", Type = "boolean" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var subscriptionId = GetArg<string>(arguments, "subscription_id");
        var framework = GetArg<string>(arguments, "framework");
        var controlFamilies = GetArg<string>(arguments, "control_families");
        var resourceTypes = GetArg<string>(arguments, "resource_types");
        var scanType = GetArg<string>(arguments, "scan_type") ?? "quick";
        var includePassed = GetArg<bool?>(arguments, "include_passed") ?? false;

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return "⚠️ No subscription configured. Use 'set my subscription to <id>' first.\n" +
                   "Error: SUBSCRIPTION_NOT_CONFIGURED";
        }

        Logger.LogInformation("Running compliance assessment | Sub: {Sub} | Type: {Type}", subscriptionId, scanType);

        var result = await _complianceEngine.RunComprehensiveAssessmentAsync(
            subscriptionId, resourceGroup: null, progress: null, cancellationToken);

        var output = $"## Compliance Assessment Results\n\n" +
               $"**Assessment ID**: {result.Id}\n" +
               $"**Subscription**: {result.SubscriptionId}\n" +
               $"**Framework**: {result.Framework}\n" +
               $"**Scan Type**: {result.ScanType}\n" +
               $"**Assessed**: {result.AssessedAt:yyyy-MM-dd HH:mm:ss UTC}\n\n" +
               $"### Score: {result.ComplianceScore:F1}%\n\n" +
               $"| Metric | Count |\n|--------|-------|\n" +
               $"| Total Controls | {result.TotalControls} |\n" +
               $"| ✅ Passed | {result.PassedControls} |\n" +
               $"| ❌ Failed | {result.FailedControls} |\n" +
               $"| ⚪ Not Assessed | {result.NotAssessedControls} |\n\n";

        // Group findings by resource type for resource scans, or by policy for policy scans
        if (result.Findings.Count > 0)
        {
            if (string.Equals(scanType, "resource", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scanType, "quick", StringComparison.OrdinalIgnoreCase))
            {
                var grouped = result.Findings
                    .GroupBy(f => f.ResourceType ?? "Unknown")
                    .OrderByDescending(g => g.Count());
                output += $"### Findings by Resource Type ({result.Findings.Count})\n\n";
                foreach (var group in grouped)
                {
                    output += $"#### {group.Key} ({group.Count()})\n";
                    foreach (var f in group.Take(5))
                        output += $"- **{f.Severity}** [{f.ControlId}] {f.Title}\n";
                    if (group.Count() > 5)
                        output += $"- ... and {group.Count() - 5} more\n";
                    output += "\n";
                }
            }
            else if (string.Equals(scanType, "policy", StringComparison.OrdinalIgnoreCase))
            {
                var grouped = result.Findings
                    .GroupBy(f => f.PolicyDefinitionId ?? f.ControlId ?? "Unknown")
                    .OrderByDescending(g => g.Count());
                output += $"### Findings by Policy ({result.Findings.Count})\n\n";
                foreach (var group in grouped)
                {
                    output += $"#### {group.Key} ({group.Count()})\n";
                    foreach (var f in group.Take(5))
                        output += $"- **{f.Severity}** [{f.ControlId}] {f.Title}\n";
                    if (group.Count() > 5)
                        output += $"- ... and {group.Count() - 5} more\n";
                    output += "\n";
                }
            }
            else
            {
                // Combined or other: show by control family
                var grouped = result.Findings
                    .GroupBy(f => f.ControlId?.Split('-').FirstOrDefault() ?? "Unknown")
                    .OrderByDescending(g => g.Count());
                output += $"### Findings by Control Family ({result.Findings.Count})\n\n";
                foreach (var group in grouped)
                {
                    output += $"#### {group.Key} ({group.Count()})\n";
                    foreach (var f in group.Take(5))
                        output += $"- **{f.Severity}** [{f.ControlId}] {f.Title}\n";
                    if (group.Count() > 5)
                        output += $"- ... and {group.Count() - 5} more\n";
                    output += "\n";
                }
            }
        }

        // Append executive summary if available
        if (!string.IsNullOrEmpty(result.ExecutiveSummary))
        {
            output += "\n---\n\n" + result.ExecutiveSummary + "\n";
        }

        // ── Post-assessment flow: suggest Kanban board creation / update ──
        if (result.Findings.Count > 0 && !string.IsNullOrWhiteSpace(subscriptionId))
        {
            output += await AppendKanbanSuggestionAsync(subscriptionId, result, cancellationToken);
        }

        return output;
    }

    /// <summary>
    /// Checks for existing remediation boards on the subscription and appends
    /// a suggestion to create or update a Kanban board if findings exist.
    /// </summary>
    private async Task<string> AppendKanbanSuggestionAsync(
        string subscriptionId,
        ComplianceAssessment assessment,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

            // Check for existing active boards on this subscription
            var existingBoard = await context.RemediationBoards
                .Where(b => b.SubscriptionId == subscriptionId && !b.IsArchived)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var openFindingsCount = assessment.Findings
                .Count(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"### 📋 Remediation Tracking ({openFindingsCount} open findings)");
            sb.AppendLine();

            if (existingBoard != null)
            {
                sb.AppendLine($"An active remediation board already exists for this subscription:");
                sb.AppendLine($"- **Board**: {existingBoard.Name} (ID: `{existingBoard.Id}`)");
                sb.AppendLine($"- **Created**: {existingBoard.CreatedAt:yyyy-MM-dd}");
                sb.AppendLine();
                sb.AppendLine("You can:");
                sb.AppendLine($"1. **Update existing board** — \"Update board {existingBoard.Id} with this assessment\" to sync new/resolved findings");
                sb.AppendLine($"2. **Create new board** — \"Create a new remediation board from this assessment\" for a fresh board");
            }
            else
            {
                sb.AppendLine("No active remediation board found for this subscription.");
                sb.AppendLine();
                sb.AppendLine("💡 **Create a remediation board** to track fixes as Kanban tasks:");
                sb.AppendLine("- Say: \"Create a remediation board from this assessment\"");
                sb.AppendLine($"- Each of the {openFindingsCount} findings will become a trackable task with SLA-based due dates");
            }

            sb.AppendLine();
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to check for existing Kanban boards post-assessment");
            return string.Empty;
        }
    }
}

/// <summary>
/// Tool for NIST control family details
/// </summary>
public class ControlFamilyTool : BaseTool
{
    private readonly INistControlsService _nistService;

    /// <summary>Initializes a new instance of the <see cref="ControlFamilyTool"/> class.</summary>
    /// <param name="nistService">NIST controls catalog service.</param>
    /// <param name="logger">Logger instance.</param>
    public ControlFamilyTool(INistControlsService nistService, ILogger<ControlFamilyTool> logger) : base(logger)
    {
        _nistService = nistService;
    }

    /// <inheritdoc />
    public override string Name => "compliance_get_control_family";
    /// <inheritdoc />
    public override string Description => "Get detailed information about a NIST 800-53 control family.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["family_id"] = new() { Name = "family_id", Description = "Control family (e.g., AC, AU, IA)", Type = "string", Required = true },
        ["include_controls"] = new() { Name = "include_controls", Description = "Include individual controls", Type = "boolean" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var familyId = GetArg<string>(arguments, "family_id") ?? "AC";
        var includeControls = GetArg<bool?>(arguments, "include_controls") ?? true;

        var controls = await _nistService.GetControlFamilyAsync(familyId, includeControls, cancellationToken);

        return $"## NIST 800-53 Control Family: {familyId}\n\n" +
               $"**Controls Found**: {controls.Count}\n\n" +
               string.Join("\n", controls.Select(c => $"- **{c.Id}** - {c.Title}\n  {c.Description}"));
    }
}

/// <summary>
/// Tool for generating compliance documents (SSP, POA&M, SAR).
/// When boardId is provided for POA&M, merges open Kanban tasks into the document.
/// </summary>
public class DocumentGenerationTool : BaseTool
{
    private readonly IDocumentGenerationService _documentService;
    private readonly IDocumentTemplateService _templateService;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Initializes a new instance of the <see cref="DocumentGenerationTool"/> class.</summary>
    /// <param name="documentService">Document generation service.</param>
    /// <param name="templateService">Template/rendering service for DOCX and PDF output.</param>
    /// <param name="scopeFactory">Service scope factory for resolving scoped services.</param>
    /// <param name="logger">Logger instance.</param>
    public DocumentGenerationTool(
        IDocumentGenerationService documentService,
        IDocumentTemplateService templateService,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentGenerationTool> logger) : base(logger)
    {
        _documentService = documentService;
        _templateService = templateService;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public override string Name => "compliance_generate_document";
    /// <inheritdoc />
    public override string Description => "Generate compliance documentation (SSP, POA&M, SAR, RAR). Supports markdown, DOCX, and PDF output formats. For POA&M, optionally provide a boardId to include open remediation tasks.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["document_type"] = new() { Name = "document_type", Description = "Document type: ssp, poam, sar, rar", Type = "string", Required = true },
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription for evidence", Type = "string" },
        ["framework"] = new() { Name = "framework", Description = "Compliance framework", Type = "string" },
        ["system_name"] = new() { Name = "system_name", Description = "System name for document", Type = "string" },
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym — required for DOCX/PDF output", Type = "string" },
        ["board_id"] = new() { Name = "board_id", Description = "Optional Kanban board ID — when provided for POA&M, includes open remediation tasks from the board", Type = "string" },
        ["format"] = new() { Name = "format", Description = "Output format: 'markdown' (default), 'docx', or 'pdf'", Type = "string" },
        ["template"] = new() { Name = "template", Description = "Template ID for custom DOCX template (optional, DOCX/PDF only)", Type = "string" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var documentType = GetArg<string>(arguments, "document_type") ?? "ssp";
        var subscriptionId = GetArg<string>(arguments, "subscription_id");
        var framework = GetArg<string>(arguments, "framework");
        var systemName = GetArg<string>(arguments, "system_name");
        var systemId = GetArg<string>(arguments, "system_id");
        var boardId = GetArg<string>(arguments, "board_id");
        var format = GetArg<string>(arguments, "format") ?? "markdown";
        var templateId = GetArg<string>(arguments, "template");

        // ── DOCX / PDF output (requires system_id) ──────────────────────
        if (format.Equals("docx", StringComparison.OrdinalIgnoreCase) ||
            format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(systemId))
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "error",
                    message = "system_id is required for DOCX/PDF output."
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            byte[] bytes;
            string mimeType;
            string extension;

            if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            {
                bytes = await _templateService.RenderPdfAsync(
                    systemId, documentType, progress: null, cancellationToken);
                mimeType = "application/pdf";
                extension = "pdf";
            }
            else
            {
                bytes = await _templateService.RenderDocxAsync(
                    systemId, documentType, templateId, cancellationToken);
                mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                extension = "docx";
            }

            var result = new
            {
                status = "success",
                data = new
                {
                    document_type = documentType,
                    format,
                    system_id = systemId,
                    template_id = templateId,
                    file_size_bytes = bytes.Length,
                    mime_type = mimeType,
                    filename = $"{documentType}_{systemId[..Math.Min(8, systemId.Length)]}.{extension}",
                    content_base64 = Convert.ToBase64String(bytes)
                }
            };

            return System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        // ── Markdown output (existing behavior) ─────────────────────────
        var doc = await _documentService.GenerateDocumentAsync(documentType, subscriptionId, framework, systemName, cancellationToken);
        var content = doc.Content;

        // For POA&M documents with a boardId, merge open Kanban tasks
        if (string.Equals(documentType, "poam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(boardId))
        {
            content += await AppendBoardTasksToPoamAsync(boardId, cancellationToken);
        }

        return content;
    }

    /// <summary>
    /// Fetches open tasks from a Kanban board and formats them as POA&M line items.
    /// </summary>
    private async Task<string> AppendBoardTasksToPoamAsync(string boardId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var kanbanService = scope.ServiceProvider.GetRequiredService<IKanbanService>();

            var poamItems = await kanbanService.GetOpenTasksForPoamAsync(boardId, cancellationToken);

            if (poamItems.Count == 0)
                return "\n\n_No open remediation tasks on the specified board._\n";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## POA&M Items from Remediation Board");
            sb.AppendLine();
            sb.AppendLine("| # | Control ID | Weakness | Status | Severity | Assignee | Due Date | Overdue |");
            sb.AppendLine("|---|-----------|----------|--------|----------|----------|----------|---------|");

            for (var i = 0; i < poamItems.Count; i++)
            {
                var item = poamItems[i];
                var isOverdue = item.DueDate < DateTime.UtcNow;
                var overdue = isOverdue ? "⚠️ Yes" : "No";
                sb.AppendLine($"| {i + 1} | {item.ControlId} | {item.Title} | {item.Status} | {item.Severity} | {item.AssigneeName ?? "Unassigned"} | {item.DueDate:yyyy-MM-dd} | {overdue} |");
            }

            sb.AppendLine();
            sb.AppendLine($"_Total open items: {poamItems.Count}_");
            sb.AppendLine();

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to fetch Kanban board tasks for POA&M generation");
            return "\n\n_⚠️ Could not load remediation board tasks. Board may not exist._\n";
        }
    }
}

/// <summary>
/// Tool for collecting compliance evidence
/// </summary>
public class EvidenceCollectionTool : BaseTool
{
    private readonly IEvidenceStorageService _evidenceService;

    /// <summary>Initializes a new instance of the <see cref="EvidenceCollectionTool"/> class.</summary>
    /// <param name="evidenceService">Evidence storage service.</param>
    /// <param name="logger">Logger instance.</param>
    public EvidenceCollectionTool(IEvidenceStorageService evidenceService, ILogger<EvidenceCollectionTool> logger) : base(logger)
    {
        _evidenceService = evidenceService;
    }

    /// <inheritdoc />
    public override string Name => "compliance_collect_evidence";
    /// <inheritdoc />
    public override string Description => "Collect compliance evidence from Azure resources for audit documentation.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["control_id"] = new() { Name = "control_id", Description = "NIST control ID", Type = "string", Required = true },
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription ID", Type = "string", Required = true },
        ["resource_group"] = new() { Name = "resource_group", Description = "Resource group filter", Type = "string" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var controlId = GetArg<string>(arguments, "control_id") ?? "";
        var subscriptionId = GetArg<string>(arguments, "subscription_id") ?? "";
        var resourceGroup = GetArg<string>(arguments, "resource_group");

        var evidence = await _evidenceService.CollectEvidenceAsync(controlId, subscriptionId, resourceGroup, cancellationToken);
        return $"## Evidence Collected\n\n**Control**: {evidence.ControlId}\n**Type**: {evidence.EvidenceType}\n\n{evidence.Content}";
    }
}

/// <summary>
/// Tool for remediating compliance findings
/// </summary>
public class RemediationExecuteTool : BaseTool
{
    private readonly IRemediationEngine _remediationEngine;

    /// <summary>Initializes a new instance of the <see cref="RemediationExecuteTool"/> class.</summary>
    /// <param name="remediationEngine">Remediation engine service.</param>
    /// <param name="logger">Logger instance.</param>
    public RemediationExecuteTool(IRemediationEngine remediationEngine, ILogger<RemediationExecuteTool> logger) : base(logger)
    {
        _remediationEngine = remediationEngine;
    }

    /// <inheritdoc />
    public override string Name => "compliance_remediate";
    /// <inheritdoc />
    public override string Description => "Remediate a compliance finding with guided or automated fixes. Supports single finding or batch remediation by severity/family.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["finding_id"] = new() { Name = "finding_id", Description = "Finding ID to remediate (for single remediation)", Type = "string" },
        ["apply_remediation"] = new() { Name = "apply_remediation", Description = "Apply fix automatically", Type = "boolean" },
        ["dry_run"] = new() { Name = "dry_run", Description = "Preview without applying", Type = "boolean" },
        ["batch"] = new() { Name = "batch", Description = "Set to true for batch remediation by severity or family", Type = "boolean" },
        ["severity"] = new() { Name = "severity", Description = "Severity filter for batch remediation (Critical, High, Medium, Low)", Type = "string" },
        ["family"] = new() { Name = "family", Description = "Control family filter for batch remediation (e.g., AC, IA, SC)", Type = "string" },
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Subscription ID for batch remediation", Type = "string" },
        ["require_approval"] = new() { Name = "require_approval", Description = "Require approval before execution (single-finding mode only)", Type = "boolean" },
        ["use_ai"] = new() { Name = "use_ai", Description = "Use AI-generated remediation scripts (single-finding mode only, default true)", Type = "boolean" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var batch = GetArg<bool?>(arguments, "batch") ?? false;

        if (batch)
        {
            var severity = GetArg<string>(arguments, "severity");
            var family = GetArg<string>(arguments, "family");
            var subscriptionId = GetArg<string>(arguments, "subscription_id") ?? "";
            var dryRun = GetArg<bool?>(arguments, "dry_run") ?? true;

            return await _remediationEngine.BatchRemediateAsync(subscriptionId, severity, family, dryRun, cancellationToken);
        }
        else
        {
            var findingId = GetArg<string>(arguments, "finding_id") ?? "";
            var applyRemediation = GetArg<bool?>(arguments, "apply_remediation") ?? false;
            var dryRun = GetArg<bool?>(arguments, "dry_run") ?? true;
            var requireApproval = GetArg<bool?>(arguments, "require_approval") ?? false;
            var useAi = GetArg<bool?>(arguments, "use_ai") ?? true;

            // When applying, use the enhanced typed overload for richer results
            if (applyRemediation && !dryRun)
            {
                var options = new RemediationExecutionOptions
                {
                    DryRun = false,
                    RequireApproval = requireApproval,
                    UseAiScript = useAi,
                    AutoValidate = true
                };

                var execution = await _remediationEngine.ExecuteRemediationAsync(findingId, options, cancellationToken);
                return FormatExecutionResult(execution);
            }

            return await _remediationEngine.ExecuteRemediationAsync(findingId, applyRemediation, dryRun, cancellationToken);
        }
    }

    private static string FormatExecutionResult(RemediationExecution execution)
    {
        var status = execution.Status == RemediationExecutionStatus.Completed ? "success" : "error";
        return JsonSerializer.Serialize(new
        {
            status,
            data = new
            {
                mode = "executed",
                findingId = execution.FindingId,
                executionId = execution.Id,
                executionStatus = execution.Status.ToString(),
                tierUsed = execution.TierUsed,
                stepsExecuted = execution.StepsExecuted,
                changesApplied = execution.ChangesApplied,
                dryRun = execution.DryRun,
                duration = execution.Duration?.TotalSeconds,
                error = execution.Error,
                hasBackup = execution.BackupId != null
            }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

/// <summary>
/// Tool for validating remediations
/// </summary>
public class ValidateRemediationTool : BaseTool
{
    private readonly IRemediationEngine _remediationEngine;

    /// <summary>Initializes a new instance of the <see cref="ValidateRemediationTool"/> class.</summary>
    /// <param name="remediationEngine">Remediation engine service.</param>
    /// <param name="logger">Logger instance.</param>
    public ValidateRemediationTool(IRemediationEngine remediationEngine, ILogger<ValidateRemediationTool> logger) : base(logger)
    {
        _remediationEngine = remediationEngine;
    }

    /// <inheritdoc />
    public override string Name => "compliance_validate_remediation";
    /// <inheritdoc />
    public override string Description => "Validate that a remediation was successfully applied.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["finding_id"] = new() { Name = "finding_id", Description = "Finding ID to validate", Type = "string", Required = true },
        ["execution_id"] = new() { Name = "execution_id", Description = "Execution ID to validate (use for typed validation with detailed checks)", Type = "string" },
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription", Type = "string" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var findingId = GetArg<string>(arguments, "finding_id") ?? "";
        var executionId = GetArg<string>(arguments, "execution_id");
        var subscriptionId = GetArg<string>(arguments, "subscription_id");

        // When execution_id is provided, use the enhanced typed validation
        if (!string.IsNullOrEmpty(executionId))
        {
            var result = await _remediationEngine.ValidateRemediationAsync(executionId, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                status = result.IsValid ? "success" : "error",
                data = new
                {
                    executionId = result.ExecutionId,
                    isValid = result.IsValid,
                    failureReason = result.FailureReason,
                    validatedAt = result.ValidatedAt,
                    checks = result.Checks.Select(c => new
                    {
                        name = c.Name,
                        passed = c.Passed,
                        expected = c.ExpectedValue,
                        actual = c.ActualValue
                    })
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        // Backward-compatible path
        return await _remediationEngine.ValidateRemediationAsync(findingId, executionId, subscriptionId, cancellationToken);
    }
}

/// <summary>
/// Tool for generating remediation plans
/// </summary>
public class RemediationPlanTool : BaseTool
{
    private readonly IRemediationEngine _remediationEngine;

    /// <summary>Initializes a new instance of the <see cref="RemediationPlanTool"/> class.</summary>
    /// <param name="remediationEngine">Remediation engine service.</param>
    /// <param name="logger">Logger instance.</param>
    public RemediationPlanTool(IRemediationEngine remediationEngine, ILogger<RemediationPlanTool> logger) : base(logger)
    {
        _remediationEngine = remediationEngine;
    }

    /// <inheritdoc />
    public override string Name => "compliance_generate_plan";
    /// <inheritdoc />
    public override string Description => "Generate a prioritized remediation plan for compliance findings.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription", Type = "string" },
        ["resource_group_name"] = new() { Name = "resource_group_name", Description = "Resource group filter", Type = "string" },
        ["include_families"] = new() { Name = "include_families", Description = "Comma-separated control families to include (e.g., AC,IA,SC)", Type = "string" },
        ["exclude_families"] = new() { Name = "exclude_families", Description = "Comma-separated control families to exclude", Type = "string" },
        ["automatable_only"] = new() { Name = "automatable_only", Description = "Only include auto-remediable findings", Type = "boolean" },
        ["severity_filter"] = new() { Name = "severity_filter", Description = "Minimum severity to include (Critical, High, Medium, Low)", Type = "string" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var subscriptionId = GetArg<string>(arguments, "subscription_id");
        var resourceGroupName = GetArg<string>(arguments, "resource_group_name");
        var includeFamilies = GetArg<string>(arguments, "include_families");
        var excludeFamilies = GetArg<string>(arguments, "exclude_families");
        var automatableOnly = GetArg<bool?>(arguments, "automatable_only") ?? false;
        var severityFilter = GetArg<string>(arguments, "severity_filter");

        // Use enhanced overload when any filter option is provided
        var optionsProvided =
            !string.IsNullOrWhiteSpace(includeFamilies) ||
            !string.IsNullOrWhiteSpace(excludeFamilies) ||
            automatableOnly ||
            !string.IsNullOrWhiteSpace(severityFilter);

        static List<string>? SplitFamilies(string? csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? null
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var plan = optionsProvided
            ? await _remediationEngine.GenerateRemediationPlanAsync(
                subscriptionId ?? "",
                new RemediationPlanOptions
                {
                    IncludeFamilies = SplitFamilies(includeFamilies),
                    ExcludeFamilies = SplitFamilies(excludeFamilies),
                    AutomatableOnly = automatableOnly,
                    MinSeverity = severityFilter
                },
                cancellationToken)
            : await _remediationEngine.GeneratePlanAsync(subscriptionId ?? "", resourceGroupName, cancellationToken);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Remediation Plan\n");
        sb.AppendLine($"**Total Findings**: {plan.TotalFindings}");
        sb.AppendLine($"**Auto-Remediable**: {plan.AutoRemediableCount}");

        // Show enhanced details if available
        if (plan.ExecutiveSummary != null)
        {
            sb.AppendLine($"\n### Executive Summary");
            sb.AppendLine($"- **Critical**: {plan.ExecutiveSummary.CriticalCount}");
            sb.AppendLine($"- **High**: {plan.ExecutiveSummary.HighCount}");
            sb.AppendLine($"- **Medium**: {plan.ExecutiveSummary.MediumCount}");
            sb.AppendLine($"- **Low**: {plan.ExecutiveSummary.LowCount}");
        }

        if (plan.RiskMetrics != null)
        {
            sb.AppendLine($"\n### Risk Metrics");
            sb.AppendLine($"- **Current Risk Score**: {plan.RiskMetrics.CurrentRiskScore:F1}");
            sb.AppendLine($"- **Projected Risk Score**: {plan.RiskMetrics.ProjectedRiskScore:F1}");
            sb.AppendLine($"- **Risk Reduction**: {plan.RiskMetrics.RiskReductionPercentage:F1}%");
        }

        // Use Items (enhanced) if available, fall back to Steps
        var items = plan.Items?.Count > 0 ? plan.Items : null;
        if (items != null)
        {
            sb.AppendLine($"\n### Prioritized Items ({items.Count})");
            foreach (var (item, idx) in items.Select((item, i) => (item, i)))
            {
                sb.AppendLine($"{idx + 1}. [{item.Finding.ControlId}] {item.Finding.Title} (Priority: {item.Priority}, Auto: {item.IsAutoRemediable})");
            }
        }
        else
        {
            sb.AppendLine($"\n### Steps");
            sb.AppendLine(string.Join("\n", plan.Steps.Select((s, i) =>
                $"{i + 1}. [{s.ControlId}] {s.Description} (Priority: {s.Priority}, Effort: {s.Effort})")));
        }

        if (plan.Timeline != null)
        {
            sb.AppendLine($"\n### Timeline");
            foreach (var phase in plan.Timeline.Phases)
            {
                sb.AppendLine($"- **{phase.Name}**: {phase.Items.Count} items ({phase.EstimatedDuration.TotalHours:F0}h)");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Tool for viewing assessment audit logs
/// </summary>
public class AssessmentAuditLogTool : BaseTool
{
    private readonly IAssessmentAuditService _auditService;

    /// <summary>Initializes a new instance of the <see cref="AssessmentAuditLogTool"/> class.</summary>
    /// <param name="auditService">Assessment audit service.</param>
    /// <param name="logger">Logger instance.</param>
    public AssessmentAuditLogTool(IAssessmentAuditService auditService, ILogger<AssessmentAuditLogTool> logger) : base(logger)
    {
        _auditService = auditService;
    }

    /// <inheritdoc />
    public override string Name => "compliance_audit_log";
    /// <inheritdoc />
    public override string Description => "Get the audit trail of compliance assessments.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription", Type = "string" },
        ["days"] = new() { Name = "days", Description = "Number of days to look back", Type = "integer" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var subscriptionId = GetArg<string>(arguments, "subscription_id");
        var days = GetArg<int?>(arguments, "days") ?? 7;

        return await _auditService.GetAuditLogAsync(subscriptionId, days, cancellationToken);
    }
}

/// <summary>
/// Tool for viewing compliance history
/// </summary>
public class ComplianceHistoryTool : BaseTool
{
    private readonly IComplianceHistoryService _historyService;

    /// <summary>Initializes a new instance of the <see cref="ComplianceHistoryTool"/> class.</summary>
    /// <param name="historyService">Compliance history service.</param>
    /// <param name="logger">Logger instance.</param>
    public ComplianceHistoryTool(IComplianceHistoryService historyService, ILogger<ComplianceHistoryTool> logger) : base(logger)
    {
        _historyService = historyService;
    }

    /// <inheritdoc />
    public override string Name => "compliance_history";
    /// <inheritdoc />
    public override string Description => "Get compliance history and trends over time.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription", Type = "string" },
        ["days"] = new() { Name = "days", Description = "Number of days to look back", Type = "integer" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var subscriptionId = GetArg<string>(arguments, "subscription_id");
        var days = GetArg<int?>(arguments, "days") ?? 30;

        return await _historyService.GetHistoryAsync(subscriptionId, days, cancellationToken);
    }
}

/// <summary>
/// Tool for getting current compliance status
/// </summary>
public class ComplianceStatusTool : BaseTool
{
    private readonly IComplianceStatusService _statusService;

    /// <summary>Initializes a new instance of the <see cref="ComplianceStatusTool"/> class.</summary>
    /// <param name="statusService">Compliance status service.</param>
    /// <param name="logger">Logger instance.</param>
    public ComplianceStatusTool(IComplianceStatusService statusService, ILogger<ComplianceStatusTool> logger) : base(logger)
    {
        _statusService = statusService;
    }

    /// <inheritdoc />
    public override string Name => "compliance_status";
    /// <inheritdoc />
    public override string Description => "Get current compliance status and posture summary.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription", Type = "string" },
        ["framework"] = new() { Name = "framework", Description = "Compliance framework", Type = "string" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var subscriptionId = GetArg<string>(arguments, "subscription_id");
        var framework = GetArg<string>(arguments, "framework");

        return await _statusService.GetStatusAsync(subscriptionId, framework, cancellationToken);
    }
}

/// <summary>
/// Tool for continuous compliance monitoring
/// </summary>
public class ComplianceMonitoringTool : BaseTool
{
    private readonly IComplianceMonitoringService _monitoringService;

    /// <summary>Initializes a new instance of the <see cref="ComplianceMonitoringTool"/> class.</summary>
    /// <param name="monitoringService">Compliance monitoring service.</param>
    /// <param name="logger">Logger instance.</param>
    public ComplianceMonitoringTool(IComplianceMonitoringService monitoringService, ILogger<ComplianceMonitoringTool> logger) : base(logger)
    {
        _monitoringService = monitoringService;
    }

    /// <inheritdoc />
    public override string Name => "compliance_monitoring";
    /// <inheritdoc />
    public override string Description => "Query continuous compliance monitoring status, alerts, and trends.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["action"] = new() { Name = "action", Description = "Action: status, scan, alerts, acknowledge, trend, history", Type = "string", Required = true },
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription", Type = "string" },
        ["days"] = new() { Name = "days", Description = "Days to look back", Type = "integer" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var action = GetArg<string>(arguments, "action") ?? "status";
        var subscriptionId = GetArg<string>(arguments, "subscription_id");
        var days = GetArg<int?>(arguments, "days") ?? 30;

        return action switch
        {
            "status" => await _monitoringService.GetStatusAsync(subscriptionId, cancellationToken),
            "scan" => await _monitoringService.TriggerScanAsync(subscriptionId, cancellationToken),
            "alerts" => await _monitoringService.GetAlertsAsync(subscriptionId, days, cancellationToken),
            "trend" => await _monitoringService.GetTrendAsync(subscriptionId, days, cancellationToken),
            _ => await _monitoringService.GetStatusAsync(subscriptionId, cancellationToken)
        };
    }
}
