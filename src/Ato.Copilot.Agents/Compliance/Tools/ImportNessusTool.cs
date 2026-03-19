// ═══════════════════════════════════════════════════════════════════════════
// Feature 026 — ACAS/Nessus Scan Import: MCP Tool
// Tool for importing .nessus vulnerability scan files.
// See specs/026-acas-nessus-import/contracts/mcp-tools.md for contracts.
// ═══════════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

/// <summary>
/// MCP tool for importing an ACAS/Nessus .nessus vulnerability scan file.
/// Accepts base64-encoded file content, decodes to bytes, validates size,
/// checks for duplicate files, and delegates to <see cref="IScanImportService"/>.
/// </summary>
public class ImportNessusTool : BaseTool
{
    private static readonly JsonSerializerOptions s_jsonOpts = new() { WriteIndented = true };
    private const int MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly IScanImportService _importService;
    private readonly IServiceScopeFactory _scopeFactory;

    public ImportNessusTool(
        IScanImportService importService,
        IServiceScopeFactory scopeFactory,
        ILogger<ImportNessusTool> logger)
        : base(logger)
    {
        _importService = importService;
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_import_nessus";

    public override string Description =>
        "Import an ACAS/Nessus .nessus vulnerability scan file for a registered system. " +
        "Parses NessusClientData_v2 XML, maps vulnerabilities to NIST 800-53 controls, " +
        "creates compliance findings and control effectiveness records. " +
        "Accepts base64-encoded file content (max 5 MB after decoding).";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new()
        {
            Name = "system_id",
            Description = "System GUID, name, or acronym (required)",
            Type = "string",
            Required = true
        },
        ["file_content"] = new()
        {
            Name = "file_content",
            Description = "Base64-encoded .nessus file content (required, max 5 MB after decoding).",
            Type = "string",
            Required = true
        },
        ["file_name"] = new()
        {
            Name = "file_name",
            Description = "Original file name (required, must end with .nessus).",
            Type = "string",
            Required = true
        },
        ["conflict_resolution"] = new()
        {
            Name = "conflict_resolution",
            Description = "How to handle duplicate findings: 'Skip' (default), 'Overwrite', or 'Merge'.",
            Type = "string",
            Required = false
        },
        ["dry_run"] = new()
        {
            Name = "dry_run",
            Description = "If true, preview results without persisting changes (default: false).",
            Type = "boolean",
            Required = false
        },
        ["assessment_id"] = new()
        {
            Name = "assessment_id",
            Description = "Optional assessment ID. If omitted, auto-resolves or creates one.",
            Type = "string",
            Required = false
        },
        ["user_role"] = new()
        {
            Name = "user_role",
            Description = "Caller's compliance role (auto-populated by middleware).",
            Type = "string",
            Required = false
        }
    };

    // RBAC: ISSO, SCA, SystemAdmin only
    private static readonly HashSet<string> AllowedImportRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Compliance.Analyst",      // ISSO/SCA
        "Compliance.SecurityLead", // ISSO senior
        "Compliance.Administrator",
        "Compliance.PlatformEngineer"
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        // RBAC check (T035)
        var userRole = GetArg<string>(arguments, "user_role") ?? string.Empty;
        if (!string.IsNullOrEmpty(userRole) && !AllowedImportRoles.Contains(userRole))
            return ErrorJson("INSUFFICIENT_PERMISSIONS",
                "Nessus import requires ISSO, SCA, or SystemAdmin role.");

        var systemId = GetArg<string>(arguments, "system_id");
        var fileContentBase64 = GetArg<string>(arguments, "file_content");
        var fileName = GetArg<string>(arguments, "file_name");
        var conflictStr = GetArg<string>(arguments, "conflict_resolution") ?? "Skip";
        var dryRun = GetArg<bool>(arguments, "dry_run");
        var assessmentId = GetArg<string>(arguments, "assessment_id");

        // Validate required parameters
        if (string.IsNullOrWhiteSpace(systemId))
            return ErrorJson("INVALID_INPUT", "The 'system_id' parameter is required.");

        if (string.IsNullOrWhiteSpace(fileContentBase64))
            return ErrorJson("INVALID_INPUT", "The 'file_content' parameter is required (base64-encoded .nessus data).");

        if (string.IsNullOrWhiteSpace(fileName))
            return ErrorJson("INVALID_INPUT", "The 'file_name' parameter is required.");

        // Validate file extension
        if (!fileName.EndsWith(".nessus", StringComparison.OrdinalIgnoreCase))
            return ErrorJson("INVALID_FILE_TYPE", "The file must have a .nessus extension.");

        // Decode base64
        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(fileContentBase64);
        }
        catch (FormatException)
        {
            return ErrorJson("INVALID_BASE64", "The 'file_content' parameter is not valid base64. Ensure the .nessus file is base64-encoded.");
        }

        // Validate file size
        if (fileBytes.Length > MaxFileSizeBytes)
        {
            return ErrorJson("FILE_TOO_LARGE",
                $"File is {fileBytes.Length / 1024.0 / 1024.0:F1} MB, exceeding the 5 MB limit.");
        }

        // Parse conflict resolution
        if (!Enum.TryParse<ImportConflictResolution>(conflictStr, ignoreCase: true, out var resolution))
            resolution = ImportConflictResolution.Skip;

        // Check for duplicate file (same SHA-256 hash + system)
        var warnings = new List<string>();
        var hash = ComputeSha256(fileBytes);
        await CheckDuplicateFile(systemId, hash, warnings, cancellationToken);

        try
        {
            var result = await _importService.ImportNessusAsync(
                systemId, assessmentId, fileBytes, fileName,
                resolution, dryRun, "mcp-user", cancellationToken);

            // Merge duplicate warnings
            var allWarnings = warnings.Concat(result.Warnings).ToList();

            return JsonSerializer.Serialize(new
            {
                status = result.Status == ScanImportStatus.Failed ? "error" : "success",
                data = new
                {
                    import_record_id = result.ImportRecordId,
                    import_status = result.Status.ToString(),
                    dry_run = dryRun,
                    report_name = result.ReportName,
                    severity_breakdown = new
                    {
                        critical = result.CriticalCount,
                        high = result.HighCount,
                        medium = result.MediumCount,
                        low = result.LowCount,
                        informational = result.InformationalCount
                    },
                    host_count = result.HostCount,
                    total_plugins = result.TotalPluginResults,
                    credentialed_scan = result.CredentialedScan,
                    changes = new
                    {
                        findings_created = result.FindingsCreated,
                        findings_updated = result.FindingsUpdated,
                        skipped = result.SkippedCount,
                        poam_weaknesses_created = result.PoamWeaknessesCreated,
                        poam_items_created = result.PoamItemsCreated,
                        poam_items_deduplicated = result.PoamItemsDeduplicated,
                        component_links_created = result.ComponentLinksCreated,
                        effectiveness_created = result.EffectivenessRecordsCreated,
                        effectiveness_updated = result.EffectivenessRecordsUpdated,
                        nist_controls_affected = result.NistControlsAffected
                    },
                    warnings = allWarnings,
                    error_message = result.ErrorMessage
                },
                metadata = new
                {
                    tool = Name,
                    timestamp = DateTime.UtcNow.ToString("O")
                }
            }, s_jsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Nessus import failed for system {SystemId}", systemId);
            return ErrorJson("IMPORT_FAILED", $"Nessus import failed: {ex.Message}");
        }
    }

    private async Task CheckDuplicateFile(string systemId, string hash, List<string> warnings, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var existing = await ctx.ScanImportRecords
                .Where(r => r.RegisteredSystemId == systemId && r.FileHash == hash && !r.IsDryRun)
                .OrderByDescending(r => r.ImportedAt)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                warnings.Add($"File previously imported on {existing.ImportedAt:yyyy-MM-dd HH:mm} UTC (import ID: {existing.Id}).");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not check for duplicate file");
        }
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private string ErrorJson(string code, string message)
    {
        return JsonSerializer.Serialize(new
        {
            status = "error",
            errorCode = code,
            message,
            metadata = new { tool = Name, timestamp = DateTime.UtcNow.ToString("O") }
        }, s_jsonOpts);
    }
}
