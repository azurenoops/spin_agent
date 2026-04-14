using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ────────────────────────────────────────────────────────────────────────────
// T022: RegisterSystemTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_register_system — Register a new information system for RMF processing.
/// RBAC: Compliance.Administrator or Compliance.PlatformEngineer
/// </summary>
public class RegisterSystemTool : BaseTool
{
    private readonly IRmfLifecycleService _lifecycleService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RegisterSystemTool(
        IRmfLifecycleService lifecycleService,
        ILogger<RegisterSystemTool> logger) : base(logger)
    {
        _lifecycleService = lifecycleService;
    }

    public override string Name => "compliance_register_system";

    public override string Description =>
        "Register a new information system for RMF processing. Returns the system with ID and initial step 'Prepare'.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["name"] = new() { Name = "name", Description = "System name", Type = "string", Required = true },
        ["system_type"] = new() { Name = "system_type", Description = "System type: MajorApplication | Enclave | PlatformIt", Type = "string", Required = true },
        ["mission_criticality"] = new() { Name = "mission_criticality", Description = "Mission criticality: MissionCritical | MissionEssential | MissionSupport", Type = "string", Required = true },
        ["hosting_environment"] = new() { Name = "hosting_environment", Description = "Hosting environment: AzureGovernment | AzureCommercial | OnPremises | Hybrid", Type = "string", Required = true },
        ["acronym"] = new() { Name = "acronym", Description = "System acronym", Type = "string", Required = false },
        ["description"] = new() { Name = "description", Description = "System description", Type = "string", Required = false },
        ["cloud_environment"] = new() { Name = "cloud_environment", Description = "Azure cloud type: Commercial | Government | GovernmentAirGappedIl5 | GovernmentAirGappedIl6", Type = "string", Required = false },
        ["subscription_ids"] = new() { Name = "subscription_ids", Description = "Azure subscription IDs in boundary", Type = "array", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var name = GetArg<string>(arguments, "name");
        var systemTypeStr = GetArg<string>(arguments, "system_type");
        var missionStr = GetArg<string>(arguments, "mission_criticality");
        var hosting = GetArg<string>(arguments, "hosting_environment");
        var acronym = GetArg<string>(arguments, "acronym");
        var description = GetArg<string>(arguments, "description");
        var cloudEnvStr = GetArg<string>(arguments, "cloud_environment");
        var subscriptionIds = GetArg<List<string>>(arguments, "subscription_ids");

        if (string.IsNullOrWhiteSpace(name))
            return Error("INVALID_INPUT", "The 'name' parameter is required.");

        if (!Enum.TryParse<SystemType>(systemTypeStr, true, out var systemType))
            return Error("INVALID_INPUT", $"Invalid system_type '{systemTypeStr}'. Use: MajorApplication, Enclave, PlatformIt.");

        if (!Enum.TryParse<MissionCriticality>(missionStr, true, out var mission))
            return Error("INVALID_INPUT", $"Invalid mission_criticality '{missionStr}'. Use: MissionCritical, MissionEssential, MissionSupport.");

        if (string.IsNullOrWhiteSpace(hosting))
            return Error("INVALID_INPUT", "The 'hosting_environment' parameter is required.");

        try
        {
            AzureEnvironmentProfile? azureProfile = null;
            if (!string.IsNullOrWhiteSpace(cloudEnvStr) &&
                Enum.TryParse<AzureCloudEnvironment>(cloudEnvStr, true, out var cloudEnv))
            {
                azureProfile = new AzureEnvironmentProfile
                {
                    CloudEnvironment = cloudEnv,
                    SubscriptionIds = subscriptionIds ?? new List<string>()
                };
            }

            var system = await _lifecycleService.RegisterSystemAsync(
                name, systemType, mission, hosting, "mcp-user",
                acronym, description, azureProfile, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatSystem(system),
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_register_system failed for '{Name}'", name);
            return Error("REGISTRATION_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private static object FormatSystem(RegisteredSystem s) => new
    {
        id = s.Id,
        name = s.Name,
        acronym = s.Acronym,
        system_type = s.SystemType.ToString(),
        mission_criticality = s.MissionCriticality.ToString(),
        hosting_environment = s.HostingEnvironment,
        current_rmf_step = s.CurrentRmfStep.ToString(),
        is_active = s.IsActive,
        created_at = s.CreatedAt.ToString("O"),
        created_by = s.CreatedBy
    };

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        execution_time_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// T023: ListSystemsTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_list_systems — List registered systems with pagination.
/// </summary>
public class ListSystemsTool : BaseTool
{
    private readonly IRmfLifecycleService _lifecycleService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ListSystemsTool(
        IRmfLifecycleService lifecycleService,
        ILogger<ListSystemsTool> logger) : base(logger)
    {
        _lifecycleService = lifecycleService;
    }

    public override string Name => "compliance_list_systems";

    public override string Description =>
        "List all registered information systems visible to the current user with pagination.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["active_only"] = new() { Name = "active_only", Description = "Filter to active systems only (default: true)", Type = "boolean", Required = false },
        ["page"] = new() { Name = "page", Description = "1-based page number", Type = "integer", Required = false },
        ["page_size"] = new() { Name = "page_size", Description = "Results per page (default: 20, max: 100)", Type = "integer", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var activeOnly = GetArg<bool?>(arguments, "active_only") ?? true;
        var page = GetArg<int?>(arguments, "page") ?? 1;
        var pageSize = GetArg<int?>(arguments, "page_size") ?? 20;

        try
        {
            var (systems, totalCount) = await _lifecycleService.ListSystemsAsync(
                activeOnly, page, pageSize, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    systems = systems.Select(s => new
                    {
                        id = s.Id,
                        name = s.Name,
                        acronym = s.Acronym,
                        system_type = s.SystemType.ToString(),
                        current_rmf_step = s.CurrentRmfStep.ToString(),
                        is_active = s.IsActive,
                        created_at = s.CreatedAt.ToString("O")
                    }),
                    pagination = new
                    {
                        page,
                        page_size = pageSize,
                        total_count = totalCount,
                        total_pages = (int)Math.Ceiling((double)totalCount / pageSize)
                    }
                },
                metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_list_systems failed");
            return JsonSerializer.Serialize(new { status = "error", errorCode = "LIST_FAILED", message = ex.Message }, JsonOpts);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T024: GetSystemTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_get_system — Get full details of a registered system.
/// </summary>
public class GetSystemTool : BaseTool
{
    private readonly IRmfLifecycleService _lifecycleService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GetSystemTool(
        IRmfLifecycleService lifecycleService,
        ILogger<GetSystemTool> logger) : base(logger)
    {
        _lifecycleService = lifecycleService;
    }

    public override string Name => "compliance_get_system";

    public override string Description =>
        "Get full details of a registered system including categorization, baseline, current RMF step, and role assignments.";

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
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'system_id' parameter is required." }, JsonOpts);

        try
        {
            var system = await _lifecycleService.GetSystemAsync(systemId, cancellationToken);
            sw.Stop();

            if (system == null)
                return JsonSerializer.Serialize(new { status = "error", errorCode = "NOT_FOUND", message = $"System '{systemId}' not found." }, JsonOpts);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    id = system.Id,
                    name = system.Name,
                    acronym = system.Acronym,
                    system_type = system.SystemType.ToString(),
                    mission_criticality = system.MissionCriticality.ToString(),
                    hosting_environment = system.HostingEnvironment,
                    description = system.Description,
                    current_rmf_step = system.CurrentRmfStep.ToString(),
                    rmf_step_updated_at = system.RmfStepUpdatedAt.ToString("O"),
                    is_active = system.IsActive,
                    created_at = system.CreatedAt.ToString("O"),
                    created_by = system.CreatedBy,
                    security_categorization = system.SecurityCategorization != null ? new
                    {
                        id = system.SecurityCategorization.Id,
                        overall = system.SecurityCategorization.OverallCategorization.ToString(),
                        confidentiality = system.SecurityCategorization.ConfidentialityImpact.ToString(),
                        integrity = system.SecurityCategorization.IntegrityImpact.ToString(),
                        availability = system.SecurityCategorization.AvailabilityImpact.ToString(),
                        dod_impact_level = system.SecurityCategorization.DoDImpactLevel,
                        nist_baseline = system.SecurityCategorization.NistBaseline,
                        formal_notation = system.SecurityCategorization.FormalNotation,
                        information_types_count = system.SecurityCategorization.InformationTypes.Count
                    } : (object?)null,
                    control_baseline = system.ControlBaseline != null ? new
                    {
                        id = system.ControlBaseline.Id,
                        baseline_level = system.ControlBaseline.BaselineLevel,
                        total_controls = system.ControlBaseline.TotalControls,
                        overlay_applied = system.ControlBaseline.OverlayApplied
                    } : (object?)null,
                    boundary_resource_count = system.AuthorizationBoundaries.Count(b => b.IsInBoundary),
                    role_assignments = system.RmfRoleAssignments
                        .Where(r => r.IsActive)
                        .Select(r => new
                        {
                            role = r.RmfRole.ToString(),
                            user_id = r.UserId,
                            user_display_name = r.UserDisplayName
                        })
                },
                metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_get_system failed for '{SystemId}'", systemId);
            return JsonSerializer.Serialize(new { status = "error", errorCode = "GET_FAILED", message = ex.Message }, JsonOpts);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T025: AdvanceRmfStepTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_advance_rmf_step — Advance a system to the next RMF step with gate validation.
/// RBAC: Compliance.Administrator or Compliance.SecurityLead
/// </summary>
public class AdvanceRmfStepTool : BaseTool
{
    private readonly IRmfLifecycleService _lifecycleService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AdvanceRmfStepTool(
        IRmfLifecycleService lifecycleService,
        ILogger<AdvanceRmfStepTool> logger) : base(logger)
    {
        _lifecycleService = lifecycleService;
    }

    public override string Name => "compliance_advance_rmf_step";

    public override string Description =>
        "Advance a system to the next RMF step. Validates gate conditions and optionally forces through with audit logging.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["target_step"] = new() { Name = "target_step", Description = "Target RMF step: Prepare | Categorize | Select | Implement | Assess | Authorize | Monitor", Type = "string", Required = true },
        ["force"] = new() { Name = "force", Description = "Override gate failures (will be audit-logged)", Type = "boolean", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var targetStepStr = GetArg<string>(arguments, "target_step");
        var force = GetArg<bool?>(arguments, "force") ?? false;

        if (string.IsNullOrWhiteSpace(systemId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'system_id' parameter is required." }, JsonOpts);

        if (!Enum.TryParse<RmfPhase>(targetStepStr, true, out var targetStep))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = $"Invalid target_step '{targetStepStr}'. Use: Prepare, Categorize, Select, Implement, Assess, Authorize, Monitor." }, JsonOpts);

        try
        {
            var result = await _lifecycleService.AdvanceRmfStepAsync(
                systemId, targetStep, force, "mcp-user", cancellationToken);

            sw.Stop();

            return JsonSerializer.Serialize(new
            {
                status = result.Success ? "success" : "error",
                data = new
                {
                    success = result.Success,
                    previous_step = result.PreviousStep.ToString(),
                    new_step = result.NewStep.ToString(),
                    was_forced = result.WasForced,
                    system_id = result.System?.Id,
                    system_name = result.System?.Name,
                    gate_results = result.GateResults.Select(g => new
                    {
                        gate = g.GateName,
                        passed = g.Passed,
                        message = g.Message,
                        severity = g.Severity
                    })
                },
                errorCode = result.Success ? (string?)null : "GATE_CHECK_FAILED",
                message = result.ErrorMessage,
                metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_advance_rmf_step failed for '{SystemId}'", systemId);
            return JsonSerializer.Serialize(new { status = "error", errorCode = "ADVANCE_FAILED", message = ex.Message }, JsonOpts);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T026: DefineBoundaryTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_define_boundary — Define or update the authorization boundary.
/// </summary>
public class DefineBoundaryTool : BaseTool
{
    private readonly IBoundaryService _boundaryService;
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public DefineBoundaryTool(
        IBoundaryService boundaryService,
        IServiceScopeFactory scopeFactory,
        ILogger<DefineBoundaryTool> logger) : base(logger)
    {
        _boundaryService = boundaryService;
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_define_boundary";

    public override string Description =>
        "Define or update the authorization boundary for a system by adding resources.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["resources"] = new() { Name = "resources", Description = "Array of resources: [{resourceId, resourceType, resourceName?, inheritanceProvider?}]", Type = "array", Required = true },
        ["boundary_definition_name"] = new() { Name = "boundary_definition_name", Description = "Optional boundary definition name to assign resources to (e.g., 'Dev/Test'). If omitted, resources are unassigned.", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var resources = GetArg<List<BoundaryResourceInput>>(arguments, "resources");

        // The LLM sometimes sends resources as a flat array of resource ID strings
        // instead of an array of objects. Handle both formats.
        if ((resources == null || resources.Count == 0 || resources.All(r => string.IsNullOrWhiteSpace(r.ResourceId)))
            && arguments.TryGetValue("resources", out var rawRes) && rawRes is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var stringIds = GetArg<List<string>>(arguments, "resources");
            if (stringIds != null && stringIds.Count > 0)
            {
                resources = stringIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id =>
                    {
                        var trimmed = id.Trim();
                        // Extract resource type from ARM path: .../providers/{provider}/{type}/{name}
                        var resourceType = "Unknown";
                        var providerIdx = trimmed.IndexOf("/providers/", StringComparison.OrdinalIgnoreCase);
                        if (providerIdx >= 0)
                        {
                            var afterProvider = trimmed[(providerIdx + "/providers/".Length)..];
                            var segments = afterProvider.Split('/', StringSplitOptions.RemoveEmptyEntries);
                            if (segments.Length >= 2)
                                resourceType = $"{segments[0]}/{segments[1]}";
                        }
                        // Extract resource name (last path segment)
                        var resourceName = trimmed.Split('/').LastOrDefault() ?? trimmed;

                        return new BoundaryResourceInput
                        {
                            ResourceId = trimmed,
                            ResourceType = resourceType,
                            ResourceName = resourceName
                        };
                    })
                    .ToList();

                Logger.LogInformation("compliance_define_boundary: converted {Count} string IDs to BoundaryResourceInput objects", resources.Count);
            }
        }

        if (string.IsNullOrWhiteSpace(systemId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'system_id' parameter is required." }, JsonOpts);

        if (resources == null || resources.Count == 0)
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "At least one resource is required in the 'resources' array." }, JsonOpts);

        try
        {
            var entries = await _boundaryService.DefineBoundaryAsync(
                systemId, resources, "mcp-user", cancellationToken);

            // Assign resources to a specific boundary definition if name provided
            var boundaryDefName = GetArg<string>(arguments, "boundary_definition_name");
            string? assignedBoundaryName = null;
            if (!string.IsNullOrWhiteSpace(boundaryDefName))
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                var boundaryDef = await db.AuthorizationBoundaryDefinitions
                    .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId && b.Name == boundaryDefName, cancellationToken);

                if (boundaryDef != null)
                {
                    foreach (var entry in entries)
                    {
                        var tracked = await db.AuthorizationBoundaries
                            .FirstOrDefaultAsync(b => b.Id == entry.Id, cancellationToken);
                        if (tracked != null)
                            tracked.AuthorizationBoundaryDefinitionId = boundaryDef.Id;
                    }
                    await db.SaveChangesAsync(cancellationToken);
                    assignedBoundaryName = boundaryDef.Name;
                }
            }

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    resources_added = entries.Count,
                    assigned_boundary = assignedBoundaryName,
                    boundary = entries.Select(b => new
                    {
                        id = b.Id,
                        resource_id = b.ResourceId,
                        resource_type = b.ResourceType,
                        resource_name = b.ResourceName,
                        is_in_boundary = b.IsInBoundary,
                        inheritance_provider = b.InheritanceProvider
                    })
                },
                metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new { status = "error", errorCode = "NOT_FOUND", message = ex.Message }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_define_boundary failed for '{SystemId}'", systemId);
            return JsonSerializer.Serialize(new { status = "error", errorCode = "BOUNDARY_FAILED", message = ex.Message }, JsonOpts);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T027: ExcludeFromBoundaryTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_exclude_from_boundary — Remove a resource from the boundary.
/// </summary>
public class ExcludeFromBoundaryTool : BaseTool
{
    private readonly IBoundaryService _boundaryService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ExcludeFromBoundaryTool(
        IBoundaryService boundaryService,
        ILogger<ExcludeFromBoundaryTool> logger) : base(logger)
    {
        _boundaryService = boundaryService;
    }

    public override string Name => "compliance_exclude_from_boundary";

    public override string Description =>
        "Exclude a resource from the authorization boundary with documented rationale.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["resource_id"] = new() { Name = "resource_id", Description = "Azure resource ID to exclude", Type = "string", Required = true },
        ["rationale"] = new() { Name = "rationale", Description = "Exclusion justification", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var resourceId = GetArg<string>(arguments, "resource_id");
        var rationale = GetArg<string>(arguments, "rationale");

        if (string.IsNullOrWhiteSpace(systemId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'system_id' parameter is required." }, JsonOpts);
        if (string.IsNullOrWhiteSpace(resourceId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'resource_id' parameter is required." }, JsonOpts);
        if (string.IsNullOrWhiteSpace(rationale))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'rationale' parameter is required." }, JsonOpts);

        try
        {
            var boundary = await _boundaryService.ExcludeResourceAsync(
                systemId, resourceId, rationale, "mcp-user", cancellationToken);

            sw.Stop();

            if (boundary == null)
                return JsonSerializer.Serialize(new { status = "error", errorCode = "NOT_FOUND", message = $"Resource '{resourceId}' not found in boundary for system '{systemId}'." }, JsonOpts);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    resource_id = boundary.ResourceId,
                    resource_type = boundary.ResourceType,
                    is_in_boundary = boundary.IsInBoundary,
                    exclusion_rationale = boundary.ExclusionRationale
                },
                metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_exclude_from_boundary failed for '{SystemId}/{ResourceId}'", systemId, resourceId);
            return JsonSerializer.Serialize(new { status = "error", errorCode = "EXCLUDE_FAILED", message = ex.Message }, JsonOpts);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T028: AssignRmfRoleTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_assign_rmf_role — Assign an RMF role to a user for a system.
/// RBAC: Compliance.Administrator or Compliance.SecurityLead
/// </summary>
public class AssignRmfRoleTool : BaseTool
{
    private readonly IBoundaryService _boundaryService;
    private readonly Services.IProfileNotificationService _notificationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AssignRmfRoleTool(
        IBoundaryService boundaryService,
        Services.IProfileNotificationService notificationService,
        ILogger<AssignRmfRoleTool> logger) : base(logger)
    {
        _boundaryService = boundaryService;
        _notificationService = notificationService;
    }

    public override string Name => "compliance_assign_rmf_role";

    public override string Description =>
        "Assign an RMF role (AuthorizingOfficial, Issm, Isso, Sca, SystemOwner, MissionOwner) to a user for a specific registered system.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["role"] = new() { Name = "role", Description = "RMF role: AuthorizingOfficial | Issm | Isso | Sca | SystemOwner | MissionOwner", Type = "string", Required = true },
        ["user_id"] = new() { Name = "user_id", Description = "User identity", Type = "string", Required = true },
        ["user_display_name"] = new() { Name = "user_display_name", Description = "Display name of the user", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var roleStr = GetArg<string>(arguments, "role");
        var userId = GetArg<string>(arguments, "user_id");
        var displayName = GetArg<string>(arguments, "user_display_name");

        if (string.IsNullOrWhiteSpace(systemId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'system_id' parameter is required." }, JsonOpts);
        if (string.IsNullOrWhiteSpace(userId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'user_id' parameter is required." }, JsonOpts);

        if (!Enum.TryParse<RmfRole>(roleStr, true, out var role))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = $"Invalid role '{roleStr}'. Use: AuthorizingOfficial, Issm, Isso, Sca, SystemOwner, MissionOwner." }, JsonOpts);

        try
        {
            var assignment = await _boundaryService.AssignRmfRoleAsync(
                systemId, role, userId, displayName, "mcp-user", cancellationToken);

            // Notify Mission Owner on assignment (FR-049)
            if (role == RmfRole.MissionOwner)
            {
                try
                {
                    await _notificationService.NotifyMissionOwnerAssignedAsync(
                        assignment.RegisteredSystemId, assignment.UserId, cancellationToken);
                }
                catch (Exception notifyEx)
                {
                    Logger.LogWarning(notifyEx, "Failed to send MO assignment notification for system '{SystemId}'", systemId);
                }
            }

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    id = assignment.Id,
                    system_id = assignment.RegisteredSystemId,
                    role = assignment.RmfRole.ToString(),
                    user_id = assignment.UserId,
                    user_display_name = assignment.UserDisplayName,
                    assigned_by = assignment.AssignedBy,
                    is_active = assignment.IsActive,
                    assigned_at = assignment.AssignedAt.ToString("O")
                },
                metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new { status = "error", errorCode = "NOT_FOUND", message = ex.Message }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_assign_rmf_role failed for '{SystemId}'", systemId);
            return JsonSerializer.Serialize(new { status = "error", errorCode = "ASSIGN_FAILED", message = ex.Message }, JsonOpts);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T029: ListRmfRolesTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_list_rmf_roles — List all RMF role assignments for a system.
/// </summary>
public class ListRmfRolesTool : BaseTool
{
    private readonly IBoundaryService _boundaryService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ListRmfRolesTool(
        IBoundaryService boundaryService,
        ILogger<ListRmfRolesTool> logger) : base(logger)
    {
        _boundaryService = boundaryService;
    }

    public override string Name => "compliance_list_rmf_roles";

    public override string Description =>
        "List all active RMF role assignments for a registered system.";

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
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "The 'system_id' parameter is required." }, JsonOpts);

        try
        {
            var roles = await _boundaryService.ListRmfRolesAsync(systemId, cancellationToken);
            sw.Stop();

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    total_roles = roles.Count,
                    roles = roles.Select(r => new
                    {
                        id = r.Id,
                        role = r.RmfRole.ToString(),
                        user_id = r.UserId,
                        user_display_name = r.UserDisplayName,
                        assigned_by = r.AssignedBy,
                        assigned_at = r.AssignedAt.ToString("O")
                    })
                },
                metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_list_rmf_roles failed for '{SystemId}'", systemId);
            return JsonSerializer.Serialize(new { status = "error", errorCode = "LIST_ROLES_FAILED", message = ex.Message }, JsonOpts);
        }
    }
}
