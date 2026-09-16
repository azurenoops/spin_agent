using System.Text;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools;

public sealed class EmassGetWorkflowStatusTool : BaseTool
{
    private readonly IEmassWorkflowStatusService _service;

    public EmassGetWorkflowStatusTool(
        IEmassWorkflowStatusService service,
        ILogger<EmassGetWorkflowStatusTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "emass_get_workflow_status";
    public override string Description => "Show eMASS export status, pending records, and unresolved conflicts for a system.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new()
        {
            Name = "system_id",
            Description = "Registered system ID, name, or acronym",
            Type = "string",
            Required = true,
        },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "system_id is required.");

        try
        {
            var status = await _service.GetStatusAsync(systemId, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    workflowStatus = status,
                    markdown = FormatStatus(status),
                },
                metadata = new { tool = Name, timestamp = DateTimeOffset.UtcNow },
            });
        }
        catch (InvalidOperationException exception)
        {
            return Error("SYSTEM_NOT_FOUND", exception.Message);
        }
    }

    private static string FormatStatus(EmassWorkflowStatus status)
    {
        var markdown = new StringBuilder()
            .AppendLine($"### eMASS workflow: {status.OverallStatus}")
            .AppendLine()
            .AppendLine("| Category | Exported | Pending | Last exported |")
            .AppendLine("|---|---:|---:|---|");

        foreach (var category in status.ExportSummary)
        {
            markdown.AppendLine(
                $"| {category.Category} | {category.ExportedCount} | {category.PendingCount} | {FormatDate(category.LastExportedAt)} |");
        }

        markdown.AppendLine()
            .AppendLine($"Unresolved conflicts: {status.UnresolvedConflictCount}")
            .AppendLine($"Last synced: {FormatDate(status.LastSyncedAt)}");
        return markdown.ToString().TrimEnd();
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("u") ?? "Never";

    private static string Error(string code, string message) => JsonSerializer.Serialize(new
    {
        status = "error",
        errorCode = code,
        message,
    });
}

public sealed class EmassCheckExportReadinessTool : BaseTool
{
    private readonly IEmassExportReadinessService _service;

    public EmassCheckExportReadinessTool(
        IEmassExportReadinessService service,
        ILogger<EmassCheckExportReadinessTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "emass_check_export_readiness";
    public override string Description => "Check whether a system is ready for eMASS export and list actionable gaps.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new()
        {
            Name = "system_id",
            Description = "Registered system ID, name, or acronym",
            Type = "string",
            Required = true,
        },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "system_id is required.");

        try
        {
            var readiness = await _service.CheckReadinessAsync(systemId, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    readiness,
                    markdown = FormatReadiness(readiness),
                },
                metadata = new { tool = Name, timestamp = DateTimeOffset.UtcNow },
            });
        }
        catch (InvalidOperationException exception)
        {
            return Error("SYSTEM_NOT_FOUND", exception.Message);
        }
    }

    private static string FormatReadiness(EmassExportReadinessResult readiness)
    {
        var markdown = new StringBuilder()
            .AppendLine(readiness.IsReady ? "### Ready for eMASS export" : "### Not ready for eMASS export");

        if (readiness.Gaps.Count == 0)
            return markdown.AppendLine().Append("No readiness gaps found.").ToString();

        markdown.AppendLine();
        foreach (var gap in readiness.Gaps)
        {
            var text = $"{gap.FieldName}: {gap.Description}";
            if (gap.Severity == ReadinessGapSeverity.Blocking)
                text = $"**{text}**";
            markdown.Append("- ").Append(text);
            if (!string.IsNullOrWhiteSpace(gap.FixUrl))
                markdown.Append(" [Fix]").Append('(').Append(gap.FixUrl).Append(')');
            markdown.AppendLine();
        }

        return markdown.ToString().TrimEnd();
    }

    private static string Error(string code, string message) => JsonSerializer.Serialize(new
    {
        status = "error",
        errorCode = code,
        message,
    });
}