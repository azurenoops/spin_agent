using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ────────────────────────────────────────────────────────────────────────────
// T047: ListBoundaryDefinitionsTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_list_boundary_definitions — List boundary definitions for a system.
/// </summary>
public class ListBoundaryDefinitionsTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ListBoundaryDefinitionsTool(
        IServiceScopeFactory scopeFactory,
        ILogger<ListBoundaryDefinitionsTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_list_boundary_definitions";

    public override string Description =>
        "List all authorization boundary definitions for a registered system.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        if (string.IsNullOrWhiteSpace(systemId))
            return JsonSerializer.Serialize(new { status = "error", message = "system_id is required" }, JsonOpts);

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BoundaryDefinitionService>();

        var items = await service.ListAsync(systemId, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            data = new
            {
                system_id = systemId,
                boundary_count = items.Count,
                boundaries = items.Select(b => new
                {
                    id = b.Id,
                    name = b.Name,
                    boundary_type = b.BoundaryType,
                    is_primary = b.IsPrimary,
                    description = b.Description,
                    resource_count = b.ResourceCount,
                    component_count = b.ComponentCount,
                    coverage_percent = b.CoveragePercent,
                }),
            },
        }, JsonOpts);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T048: CreateBoundaryDefinitionTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_create_boundary_definition — Create a new boundary definition.
/// </summary>
public class CreateBoundaryDefinitionTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public CreateBoundaryDefinitionTool(
        IServiceScopeFactory scopeFactory,
        ILogger<CreateBoundaryDefinitionTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_create_boundary_definition";

    public override string Description =>
        "Create a new authorization boundary definition for a system (e.g., Dev/Test, DMZ, Production).";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["name"] = new() { Name = "name", Description = "Boundary name (e.g., 'Dev/Test', 'DMZ')", Type = "string", Required = true },
        ["boundary_type"] = new() { Name = "boundary_type", Description = "Type: Physical, Logical, or Hybrid", Type = "string", Required = true },
        ["description"] = new() { Name = "description", Description = "Optional description of the boundary", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        var name = GetArg<string>(arguments, "name");
        var boundaryType = GetArg<string>(arguments, "boundary_type");
        var description = GetArg<string>(arguments, "description");

        if (string.IsNullOrWhiteSpace(systemId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(boundaryType))
            return JsonSerializer.Serialize(new { status = "error", message = "system_id, name, and boundary_type are required" }, JsonOpts);

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BoundaryDefinitionService>();

        try
        {
            var request = new CreateBoundaryDefinitionRequest(name, boundaryType, description);
            var result = await service.CreateAsync(systemId, request, "mcp-user", cancellationToken);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    id = result.Id,
                    name = result.Name,
                    boundary_type = result.BoundaryType,
                    is_primary = result.IsPrimary,
                    description = result.Description,
                },
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return JsonSerializer.Serialize(new { status = "error", errorCode = "DUPLICATE_NAME", message = ex.Message }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message }, JsonOpts);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T049: DeleteBoundaryDefinitionTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_delete_boundary_definition — Delete a boundary definition with orphan reassignment.
/// </summary>
public class DeleteBoundaryDefinitionTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public DeleteBoundaryDefinitionTool(
        IServiceScopeFactory scopeFactory,
        ILogger<DeleteBoundaryDefinitionTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_delete_boundary_definition";

    public override string Description =>
        "Delete a boundary definition. Orphaned resources, components, and mappings are reassigned to the Primary boundary.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["boundary_id"] = new() { Name = "boundary_id", Description = "Boundary definition GUID to delete", Type = "string", Required = true },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var boundaryId = GetArg<string>(arguments, "boundary_id");
        if (string.IsNullOrWhiteSpace(boundaryId))
            return JsonSerializer.Serialize(new { status = "error", message = "boundary_id is required" }, JsonOpts);

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BoundaryDefinitionService>();

        try
        {
            var result = await service.DeleteAsync(boundaryId, "mcp-user", cancellationToken);

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    deleted_id = result.DeletedId,
                    primary_boundary_id = result.PrimaryBoundaryId,
                    reassigned_resources = result.ReassignedResources,
                    reassigned_components = result.ReassignedComponents,
                    reassigned_mappings = result.ReassignedMappings,
                },
            }, JsonOpts);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Primary"))
        {
            return JsonSerializer.Serialize(new { status = "error", errorCode = "PRIMARY_PROTECTED", message = ex.Message }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message }, JsonOpts);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────
// T050: BoundaryGapAnalysisTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_boundary_gap_analysis — Run gap analysis scoped to a specific boundary.
/// </summary>
public class BoundaryGapAnalysisTool : BaseTool
{
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BoundaryGapAnalysisTool(
        IServiceScopeFactory scopeFactory,
        ILogger<BoundaryGapAnalysisTool> logger) : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Name => "compliance_boundary_gap_analysis";

    public override string Description =>
        "Run gap analysis scoped to a specific authorization boundary, or compare coverage across all boundaries.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["boundary_id"] = new() { Name = "boundary_id", Description = "Optional boundary definition GUID. Omit to compare all boundaries.", Type = "string", Required = false },
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var systemId = GetArg<string>(arguments, "system_id");
        var boundaryId = GetArg<string>(arguments, "boundary_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return JsonSerializer.Serialize(new { status = "error", message = "system_id is required" }, JsonOpts);

        using var scope = _scopeFactory.CreateScope();
        var capService = scope.ServiceProvider.GetRequiredService<CapabilityService>();

        var result = await capService.GetGapAnalysisAsync(systemId, boundaryId, cancellationToken);
        if (result is null)
            return JsonSerializer.Serialize(new { status = "error", errorCode = "NOT_FOUND", message = "System or baseline not found" }, JsonOpts);

        return JsonSerializer.Serialize(new
        {
            status = "success",
            data = new
            {
                system_id = result.SystemId,
                baseline_level = result.BaselineLevel,
                total_controls = result.TotalBaselineControls,
                covered_controls = result.CoveredControls,
                gap_count = result.GapCount,
                coverage_percent = result.CoveragePercent,
                boundary_filter = boundaryId,
                boundary_comparison = result.BoundaryComparison?.Select(b => new
                {
                    boundary_id = b.BoundaryId,
                    boundary_name = b.BoundaryName,
                    boundary_type = b.BoundaryType,
                    is_primary = b.IsPrimary,
                    total_controls = b.TotalControls,
                    covered_controls = b.CoveredControls,
                    gap_count = b.GapCount,
                    coverage_percent = b.CoveragePercent,
                }),
                families_below_50 = result.FamilyBreakdown
                    .Where(f => f.IsBelow50)
                    .Select(f => new { family = f.FamilyCode, name = f.FamilyName, coverage = f.CoveragePercent }),
            },
        }, JsonOpts);
    }
}
