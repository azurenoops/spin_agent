using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ═══════════════════════════════════════════════════════════════════════════════
// compliance_validate_oscal_schema
// ═══════════════════════════════════════════════════════════════════════════════

public class ValidateOscalSchemaTool : BaseTool
{
    private readonly IOscalSchemaValidationService _service;

    public ValidateOscalSchemaTool(
        IOscalSchemaValidationService service,
        ILogger<ValidateOscalSchemaTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_validate_oscal_schema";

    public override string Description =>
        "Validate an OSCAL JSON artifact against the official NIST OSCAL 1.1.2 JSON schema. " +
        "Generates the artifact for the given system, then runs schema validation. " +
        "Supported models: ssp, poam, assessment-results, assessment-plan. " +
        "RBAC: ISSM, SCA, AO.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["model"] = new() { Name = "model", Description = "OSCAL model type: 'ssp', 'poam', 'assessment-results', or 'assessment-plan'", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var model = GetArg<string>(arguments, "model");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");
        if (string.IsNullOrWhiteSpace(model))
            return Error("INVALID_INPUT", "The 'model' parameter is required.");

        try
        {
            var result = await _service.ValidateForSystemAsync(systemId, model, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = result.IsValid ? "success" : "validation_failed",
                data = new
                {
                    system_id = systemId,
                    model,
                    is_valid = result.IsValid,
                    schema_version = result.SchemaVersion,
                    violation_count = result.Violations.Count,
                    violations = result.Violations.Select(v => new
                    {
                        json_path = v.JsonPath,
                        message = v.Message,
                        expected_format = v.ExpectedFormat,
                        actual_value = v.ActualValue
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("VALIDATION_FAILED", ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ═══════════════════════════════════════════════════════════════════════════════
// compliance_validate_package
// ═══════════════════════════════════════════════════════════════════════════════

public class ValidatePackageTool : BaseTool
{
    private readonly IPackageValidationService _service;

    public ValidatePackageTool(
        IPackageValidationService service,
        ILogger<ValidatePackageTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_validate_package";

    public override string Description =>
        "Run a pre-submission readiness check for an authorization package. " +
        "Validates artifact presence, SSP completeness, SAR status, OSCAL schema conformance, " +
        "cross-artifact control ID consistency, and evidence coverage. " +
        "Returns a readiness checklist with errors (blocking) and warnings (non-blocking). " +
        "RBAC: ISSM, SCA, AO.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var result = await _service.ValidateAsync(systemId, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = result.IsValid ? "success" : "validation_failed",
                data = new
                {
                    system_id = systemId,
                    is_valid = result.IsValid,
                    error_count = result.ErrorCount,
                    warning_count = result.WarningCount,
                    findings = result.Findings.Select(f => new
                    {
                        severity = f.Severity.ToString().ToLowerInvariant(),
                        category = f.Category,
                        artifact_type = f.ArtifactType,
                        description = f.Description,
                        remediation = f.Remediation
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("VALIDATION_FAILED", ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ═══════════════════════════════════════════════════════════════════════════════
// compliance_generate_package
// ═══════════════════════════════════════════════════════════════════════════════

public class GeneratePackageTool : BaseTool
{
    private readonly IAuthorizationPackageService _service;

    public GeneratePackageTool(
        IAuthorizationPackageService service,
        ILogger<GeneratePackageTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_generate_package";

    public override string Description =>
        "Generate a complete eMASS authorization package (OSCAL SSP, POA&M, Assessment Results, " +
        "Assessment Plan, SAR, and evidence) as a ZIP archive. Runs readiness validation first. " +
        "Returns immediately with package ID — generation runs in background. " +
        "RBAC: ISSM, AO.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["evidence_mode"] = new() { Name = "evidence_mode", Description = "Evidence bundling: 'embedded' (default) or 'manifest_only'", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var evidenceModeStr = GetArg<string>(arguments, "evidence_mode");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        var evidenceMode = EvidenceMode.Embedded;
        if (!string.IsNullOrWhiteSpace(evidenceModeStr) &&
            evidenceModeStr.Equals("manifest_only", StringComparison.OrdinalIgnoreCase))
        {
            evidenceMode = EvidenceMode.ManifestOnly;
        }

        try
        {
            var package = await _service.EnqueuePackageAsync(systemId, evidenceMode, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    package_id = package.Id,
                    system_id = package.RegisteredSystemId,
                    package_status = package.Status.ToString(),
                    evidence_mode = package.EvidenceMode.ToString(),
                    message = "Package generation has been queued. Use compliance_package_status to track progress."
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("READINESS_CHECK_FAILED", ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ═══════════════════════════════════════════════════════════════════════════════
// compliance_package_status
// ═══════════════════════════════════════════════════════════════════════════════

public class PackageStatusTool : BaseTool
{
    private readonly IAuthorizationPackageService _service;

    public PackageStatusTool(
        IAuthorizationPackageService service,
        ILogger<PackageStatusTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_package_status";

    public override string Description =>
        "Get the current status of an authorization package generation job. " +
        "Returns status, artifacts generated, validation results, and download link if complete. " +
        "RBAC: ISSM, SCA, AO.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["package_id"] = new() { Name = "package_id", Description = "Package ID returned from compliance_generate_package", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var packageId = GetArg<string>(arguments, "package_id");

        if (string.IsNullOrWhiteSpace(packageId))
            return Error("INVALID_INPUT", "The 'package_id' parameter is required.");

        try
        {
            var package = await _service.GetPackageAsync(packageId, cancellationToken);
            if (package == null)
                return Error("NOT_FOUND", $"Package '{packageId}' not found.");

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    package_id = package.Id,
                    system_id = package.RegisteredSystemId,
                    package_status = package.Status.ToString(),
                    evidence_mode = package.EvidenceMode.ToString(),
                    artifact_count = package.TotalArtifactCount,
                    artifacts = (package.Artifacts ?? []).Select(a => new
                    {
                        type = a.ArtifactType.ToString(),
                        file_name = a.FileName,
                        format = a.Format,
                        file_size = a.FileSize,
                        schema_valid = a.SchemaValid
                    }),
                    validation_passed = package.ValidationPassed,
                    file_size = package.FileSize,
                    failure_reason = package.FailureReason,
                    failed_artifact = package.FailedArtifactType,
                    generated_by = package.GeneratedBy,
                    generated_at = package.GeneratedAt.ToString("O"),
                    completed_at = package.CompletedAt?.ToString("O"),
                    download_url = package.Status == PackageStatus.Completed
                        ? $"/api/v1/systems/{package.RegisteredSystemId}/packages/{package.Id}/download"
                        : null
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("STATUS_CHECK_FAILED", ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}

// ═══════════════════════════════════════════════════════════════════════════════
// compliance_list_packages
// ═══════════════════════════════════════════════════════════════════════════════

public class ListPackagesTool : BaseTool
{
    private readonly IAuthorizationPackageService _service;

    public ListPackagesTool(
        IAuthorizationPackageService service,
        ILogger<ListPackagesTool> logger) : base(logger)
    {
        _service = service;
    }

    public override string Name => "compliance_list_packages";

    public override string Description =>
        "List authorization packages for a system with pagination. " +
        "Returns package history sorted by most recent first, with status, artifact count, and validation results. " +
        "RBAC: ISSM, SCA, AO.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["limit"] = new() { Name = "limit", Description = "Max results to return (default: 10)", Type = "integer", Required = false },
        ["include_failed"] = new() { Name = "include_failed", Description = "Include failed packages (default: false)", Type = "boolean", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var limitStr = GetArg<string>(arguments, "limit");
            var limit = 10;
            if (!string.IsNullOrWhiteSpace(limitStr) && int.TryParse(limitStr, out var parsed))
                limit = Math.Clamp(parsed, 1, 50);

            var includeFailedStr = GetArg<string>(arguments, "include_failed");
            var includeFailed = string.Equals(includeFailedStr, "true", StringComparison.OrdinalIgnoreCase);

            var result = await _service.ListPackagesAsync(systemId, limit, 0, includeFailed, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    total_count = result.TotalCount,
                    packages = result.Items.Select(p => new
                    {
                        package_id = p.PackageId,
                        package_status = p.Status,
                        artifact_count = p.ArtifactCount,
                        validation_passed = p.ValidationPassed,
                        file_size = p.FileSize,
                        generated_by = p.GeneratedBy,
                        generated_at = p.GeneratedAt.ToString("O"),
                        expires_at = p.ExpiresAt.ToString("O")
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return Error("LIST_PACKAGES_FAILED", ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, new JsonSerializerOptions { WriteIndented = true });

    private object Meta(Stopwatch sw) => new { tool = Name, duration_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") };
}
