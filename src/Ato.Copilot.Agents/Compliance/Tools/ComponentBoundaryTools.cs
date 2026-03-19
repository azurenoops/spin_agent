using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Services;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ─── T040-1: compliance_discover_azure_resources ─────────────────────────────

/// <summary>
/// Discover Azure resources for component import.
/// </summary>
public class DiscoverAzureComponentsTool : BaseTool
{
    private readonly AzureResourceDiscoveryService _discoveryService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public DiscoverAzureComponentsTool(
        AzureResourceDiscoveryService discoveryService,
        ILogger<DiscoverAzureComponentsTool> logger) : base(logger)
    {
        _discoveryService = discoveryService;
    }

    public override string Name => "compliance_discover_azure_resources";

    public override string Description =>
        "Discover Azure resources from a subscription for import as system components. " +
        "Returns a paginated list with already-imported flags and partial failure tracking.";

    public override PimTier RequiredPimTier => PimTier.Read;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID/name/acronym. If provided, scopes to system's subscription. If omitted, user must provide subscription_id.", Type = "string", Required = false },
        ["subscription_id"] = new() { Name = "subscription_id", Description = "Azure subscription ID. Required if system_id not provided.", Type = "string", Required = false },
        ["resource_group"] = new() { Name = "resource_group", Description = "Filter by resource group name", Type = "string", Required = false },
        ["resource_type"] = new() { Name = "resource_type", Description = "Filter by Azure resource type", Type = "string", Required = false },
        ["search"] = new() { Name = "search", Description = "Text search on resource name", Type = "string", Required = false },
        ["cursor"] = new() { Name = "cursor", Description = "Pagination cursor from previous response", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var subscriptionId = GetArg<string>(arguments, "subscription_id");
        var resourceGroup = GetArg<string>(arguments, "resource_group");
        var resourceType = GetArg<string>(arguments, "resource_type");
        var search = GetArg<string>(arguments, "search");
        var cursor = GetArg<string>(arguments, "cursor");

        if (string.IsNullOrWhiteSpace(subscriptionId) && string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "Either 'system_id' or 'subscription_id' is required.");

        try
        {
            var result = await _discoveryService.DiscoverForComponentsAsync(
                subscriptionId ?? "", systemId, resourceGroup, resourceType, search, cursor, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    resources = result.Resources.Select(r => new
                    {
                        resource_id = r.ResourceId,
                        name = r.Name,
                        type = r.Type,
                        resource_group = r.ResourceGroup,
                        location = r.Location,
                        already_imported = r.AlreadyImported,
                        exists_in_org_library = r.ExistsInOrgLibrary,
                    }),
                    next_cursor = result.NextCursor,
                    total_count = result.TotalCount,
                    failed_resource_groups = result.FailedResourceGroups,
                },
                metadata = Meta(sw),
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_discover_azure_resources failed");
            return Error("DISCOVERY_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        execution_time_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O"),
    };
}

// ─── T040-2: compliance_import_azure_components ──────────────────────────────

/// <summary>
/// Import discovered Azure resources as SystemComponent records.
/// </summary>
public class ImportAzureComponentsTool : BaseTool
{
    private readonly ComponentService _componentService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ImportAzureComponentsTool(
        ComponentService componentService,
        ILogger<ImportAzureComponentsTool> logger) : base(logger)
    {
        _componentService = componentService;
    }

    public override string Name => "compliance_import_azure_components";

    public override string Description =>
        "Import discovered Azure resources as SystemComponent records. " +
        "Creates org-wide or system-scoped 'Thing' components from discovery results.";

    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID/name/acronym. If provided, creates system-scoped components. If omitted, creates org-wide components.", Type = "string", Required = false },
        ["resources"] = new() { Name = "resources", Description = "Array of objects with resource_id, name, type, resource_group, location", Type = "array", Required = true },
        ["assign_existing"] = new() { Name = "assign_existing", Description = "Array of existing org component GUIDs to assign to the system instead of creating duplicates", Type = "array", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var resourcesRaw = GetArg<List<AzureImportResource>>(arguments, "resources");
        var assignExisting = GetArg<List<string>>(arguments, "assign_existing");

        if (resourcesRaw == null || resourcesRaw.Count == 0)
            return Error("INVALID_INPUT", "The 'resources' parameter is required and must be non-empty.");

        try
        {
            ImportAzureComponentsResult result;

            if (!string.IsNullOrWhiteSpace(systemId))
            {
                result = await _componentService.ImportSystemAzureComponentsAsync(
                    systemId, resourcesRaw, assignExisting, "mcp-user", cancellationToken);
            }
            else
            {
                result = await _componentService.ImportAzureComponentsAsync(
                    resourcesRaw, "mcp-user", cancellationToken);
            }

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    imported = result.Imported,
                    assigned_from_org = result.AssignedFromOrg,
                    skipped = result.Skipped,
                    components = result.Components.Select(c => new
                    {
                        id = c.Id,
                        name = c.Name,
                        component_type = c.ComponentType,
                        azure_resource_id = c.BoundaryDefinitionId,
                    }),
                },
                metadata = Meta(sw),
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_import_azure_components failed");
            return Error("IMPORT_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        execution_time_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O"),
    };
}

// ─── T040-3: compliance_assign_component_to_boundary ─────────────────────────

public class AssignComponentToBoundaryTool : BaseTool
{
    private readonly ComponentService _componentService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AssignComponentToBoundaryTool(ComponentService componentService, ILogger<AssignComponentToBoundaryTool> logger) : base(logger) => _componentService = componentService;

    public override string Name => "compliance_assign_component_to_boundary";
    public override string Description => "Assign a component to a boundary definition with scope status.";
    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID/name/acronym", Type = "string", Required = true },
        ["boundary_id"] = new() { Name = "boundary_id", Description = "AuthorizationBoundaryDefinition GUID", Type = "string", Required = true },
        ["component_id"] = new() { Name = "component_id", Description = "SystemComponent GUID", Type = "string", Required = true },
        ["is_in_scope"] = new() { Name = "is_in_scope", Description = "Default true. Set false to exclude.", Type = "boolean", Required = false },
        ["exclusion_rationale"] = new() { Name = "exclusion_rationale", Description = "Required when is_in_scope is false", Type = "string", Required = false },
        ["inheritance_provider"] = new() { Name = "inheritance_provider", Description = "CSP/common control provider name", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var boundaryId = GetArg<string>(arguments, "boundary_id");
        var componentId = GetArg<string>(arguments, "component_id");
        var isInScope = GetArg<bool?>(arguments, "is_in_scope") ?? true;
        var rationale = GetArg<string>(arguments, "exclusion_rationale");
        var provider = GetArg<string>(arguments, "inheritance_provider");

        if (string.IsNullOrWhiteSpace(boundaryId) || string.IsNullOrWhiteSpace(componentId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "boundary_id and component_id are required." }, JsonOpts);

        var (dto, error) = await _componentService.AssignComponentToBoundaryAsync(boundaryId, componentId, isInScope, rationale, provider, "mcp-user", cancellationToken);
        sw.Stop();

        if (error != null)
            return JsonSerializer.Serialize(new { status = "error", errorCode = error, message = error switch { "DUPLICATE_ASSIGNMENT" => "Component already assigned to this boundary.", "RATIONALE_REQUIRED" => "is_in_scope is false but exclusion_rationale is empty.", "NOT_FOUND" => "System, boundary, or component not found.", _ => error } }, JsonOpts);

        return JsonSerializer.Serialize(new { status = "success", data = new { assignment_id = dto!.AssignmentId, component_id = dto.ComponentId, component_name = dto.ComponentName, boundary_id = boundaryId, is_in_scope = dto.IsInScope, exclusion_rationale = dto.ExclusionRationale, inheritance_provider = dto.InheritanceProvider }, metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") } }, JsonOpts);
    }
}

// ─── T040-4: compliance_list_boundary_components ─────────────────────────────

public class ListBoundaryComponentsTool : BaseTool
{
    private readonly ComponentService _componentService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ListBoundaryComponentsTool(ComponentService componentService, ILogger<ListBoundaryComponentsTool> logger) : base(logger) => _componentService = componentService;

    public override string Name => "compliance_list_boundary_components";
    public override string Description => "List components assigned to a boundary with scope details.";
    public override PimTier RequiredPimTier => PimTier.Read;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID/name/acronym", Type = "string", Required = true },
        ["boundary_id"] = new() { Name = "boundary_id", Description = "AuthorizationBoundaryDefinition GUID", Type = "string", Required = true },
        ["scope_filter"] = new() { Name = "scope_filter", Description = "Filter: in_scope, excluded, or omit for all", Type = "string", Required = false },
        ["type_filter"] = new() { Name = "type_filter", Description = "Filter by ComponentType: Person, Place, Thing", Type = "string", Required = false },
        ["page"] = new() { Name = "page", Description = "1-based page number (default: 1)", Type = "integer", Required = false },
        ["page_size"] = new() { Name = "page_size", Description = "Results per page (default: 50, max: 200)", Type = "integer", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var boundaryId = GetArg<string>(arguments, "boundary_id");
        if (string.IsNullOrWhiteSpace(boundaryId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "boundary_id is required." }, JsonOpts);

        var scopeFilter = GetArg<string>(arguments, "scope_filter");
        var scopeValue = scopeFilter?.Replace("_", "").ToLowerInvariant() switch { "inscope" => "InScope", "excluded" => "Excluded", _ => null };

        var query = new BoundaryComponentQuery { ScopeFilter = scopeValue, TypeFilter = GetArg<string>(arguments, "type_filter"), Page = GetArg<int?>(arguments, "page") ?? 1, PageSize = GetArg<int?>(arguments, "page_size") ?? 50 };
        var result = await _componentService.ListBoundaryComponentsAsync(boundaryId, query, cancellationToken);
        sw.Stop();

        var inScopeCount = result.Items.Count(i => i.IsInScope);
        return JsonSerializer.Serialize(new { status = "success", data = new { components = result.Items.Select(i => new { assignment_id = i.AssignmentId, component_id = i.ComponentId, component_name = i.ComponentName, component_type = i.ComponentType, is_in_scope = i.IsInScope, exclusion_rationale = i.ExclusionRationale, inheritance_provider = i.InheritanceProvider, azure_resource_id = i.AzureResourceId }), pagination = new { page = result.Page, page_size = result.PageSize, total_count = result.TotalCount }, summary = new { in_scope_count = inScopeCount, excluded_count = result.Items.Count - inScopeCount, total = result.TotalCount } }, metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") } }, JsonOpts);
    }
}

// ─── T040-5: compliance_update_component_scope ───────────────────────────────

public class UpdateComponentScopeTool : BaseTool
{
    private readonly ComponentService _componentService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public UpdateComponentScopeTool(ComponentService componentService, ILogger<UpdateComponentScopeTool> logger) : base(logger) => _componentService = componentService;

    public override string Name => "compliance_update_component_scope";
    public override string Description => "Toggle a component's scope status within a boundary.";
    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID/name/acronym", Type = "string", Required = true },
        ["assignment_id"] = new() { Name = "assignment_id", Description = "BoundaryComponentAssignment GUID", Type = "string", Required = true },
        ["is_in_scope"] = new() { Name = "is_in_scope", Description = "New scope status", Type = "boolean", Required = true },
        ["exclusion_rationale"] = new() { Name = "exclusion_rationale", Description = "Required when is_in_scope is false", Type = "string", Required = false },
        ["inheritance_provider"] = new() { Name = "inheritance_provider", Description = "CSP/common control provider name", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var assignmentId = GetArg<string>(arguments, "assignment_id");
        var isInScope = GetArg<bool?>(arguments, "is_in_scope") ?? true;
        var rationale = GetArg<string>(arguments, "exclusion_rationale");
        var provider = GetArg<string>(arguments, "inheritance_provider");

        if (string.IsNullOrWhiteSpace(assignmentId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "assignment_id is required." }, JsonOpts);

        var (dto, error) = await _componentService.UpdateBoundaryAssignmentAsync(assignmentId, isInScope, rationale, provider, "mcp-user", cancellationToken);
        sw.Stop();

        if (error != null)
            return JsonSerializer.Serialize(new { status = "error", errorCode = error, message = error switch { "RATIONALE_REQUIRED" => "is_in_scope is false but exclusion_rationale is empty.", "NOT_FOUND" => "Assignment not found.", _ => error } }, JsonOpts);

        return JsonSerializer.Serialize(new { status = "success", data = new { assignment_id = dto!.AssignmentId, component_id = dto.ComponentId, component_name = dto.ComponentName, is_in_scope = dto.IsInScope, exclusion_rationale = dto.ExclusionRationale, inheritance_provider = dto.InheritanceProvider }, metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") } }, JsonOpts);
    }
}

// ─── T040-6: compliance_remove_component_from_boundary ───────────────────────

public class RemoveComponentFromBoundaryTool : BaseTool
{
    private readonly ComponentService _componentService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RemoveComponentFromBoundaryTool(ComponentService componentService, ILogger<RemoveComponentFromBoundaryTool> logger) : base(logger) => _componentService = componentService;

    public override string Name => "compliance_remove_component_from_boundary";
    public override string Description => "Remove a component from a boundary (keeps component in library).";
    public override PimTier RequiredPimTier => PimTier.Write;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID/name/acronym", Type = "string", Required = true },
        ["assignment_id"] = new() { Name = "assignment_id", Description = "BoundaryComponentAssignment GUID", Type = "string", Required = true },
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var assignmentId = GetArg<string>(arguments, "assignment_id");
        if (string.IsNullOrWhiteSpace(assignmentId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "assignment_id is required." }, JsonOpts);

        var removed = await _componentService.RemoveComponentFromBoundaryAsync(assignmentId, cancellationToken);
        sw.Stop();

        if (!removed)
            return JsonSerializer.Serialize(new { status = "error", errorCode = "NOT_FOUND", message = "Assignment not found." }, JsonOpts);

        return JsonSerializer.Serialize(new { status = "success", data = new { removed = true, component_retained = true, message = "Component removed from boundary. The component remains in the library." }, metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") } }, JsonOpts);
    }
}

// ─── T040-7: compliance_component_risk_summary ─────────────────────────────

/// <summary>
/// Get per-component risk summary for a system's assessment findings.
/// </summary>
public class ComponentRiskSummaryTool : BaseTool
{
    private readonly ComponentService _componentService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ComponentRiskSummaryTool(ComponentService componentService, ILogger<ComponentRiskSummaryTool> logger) : base(logger) => _componentService = componentService;

    public override string Name => "compliance_component_risk_summary";
    public override string Description => "Get per-component risk summary for a system's assessment findings. Shows open finding counts, highest severity, and overdue remediations per component.";
    public override PimTier RequiredPimTier => PimTier.Read;

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID/name/acronym", Type = "string", Required = true },
        ["assessment_id"] = new() { Name = "assessment_id", Description = "Specific assessment. If omitted, aggregates across all completed assessments.", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return JsonSerializer.Serialize(new { status = "error", errorCode = "INVALID_INPUT", message = "system_id is required." }, JsonOpts);

        var assessmentId = GetArg<string>(arguments, "assessment_id");

        var result = await _componentService.GetComponentRiskSummaryAsync(systemId, assessmentId, cancellationToken);
        sw.Stop();

        return JsonSerializer.Serialize(new
        {
            status = "success",
            data = new
            {
                component_risks = result.ComponentRisks.Select(r => new
                {
                    component_id = r.ComponentId,
                    component_name = r.ComponentName,
                    component_type = r.ComponentType,
                    open_finding_count = r.OpenFindingCount,
                    highest_severity = r.HighestSeverity,
                    overdue_remediation_count = r.OverdueRemediationCount,
                }),
                unlinked_finding_count = result.UnlinkedFindingCount,
                total_finding_count = result.TotalFindingCount,
            },
            metadata = new { tool = Name, execution_time_ms = sw.ElapsedMilliseconds, timestamp = DateTime.UtcNow.ToString("O") }
        }, JsonOpts);
    }
}
