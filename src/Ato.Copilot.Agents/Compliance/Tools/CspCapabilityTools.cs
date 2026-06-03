using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ═══════════════════════════════════════════════════════════════════════════════
// CSP Capability Tools (Epic #124 — Feature 050, Issue #161)
// MCP tool for remapping CSP capability parent relationships.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// MCP tool: csp_remap_capability_parent
/// Remaps a CSP capability to a new parent (or makes it a root by omitting new_parent_id).
/// Records a ParentChanged history event.
/// </summary>
public class CspRemapCapabilityParentTool : BaseTool
{
    private readonly ICspCapabilityService _capabilityService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public CspRemapCapabilityParentTool(
        ICspCapabilityService capabilityService,
        ILogger<CspRemapCapabilityParentTool> logger) : base(logger)
    {
        _capabilityService = capabilityService;
    }

    /// <inheritdoc />
    public override string Name => "csp_remap_capability_parent";

    /// <inheritdoc />
    public override string Description =>
        "Remap a CSP capability to a new parent capability, or make it a root capability by " +
        "omitting new_parent_id. The remap is recorded in the capability history as a " +
        "ParentChanged event. Requires confirmation before executing in production flows.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["capability_id"] = new()
        {
            Name = "capability_id",
            Description = "ID of the capability to remap",
            Type = "string",
            Required = true
        },
        ["new_parent_id"] = new()
        {
            Name = "new_parent_id",
            Description = "ID of the new parent capability. Omit or pass null to make this capability a root.",
            Type = "string",
            Required = false
        },
        ["remapped_by"] = new()
        {
            Name = "remapped_by",
            Description = "User ID performing the remap (defaults to 'system')",
            Type = "string",
            Required = false
        }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var capabilityId = GetArg<string>(arguments, "capability_id");
        var newParentId = GetArg<string>(arguments, "new_parent_id");
        var remappedBy = GetArg<string>(arguments, "remapped_by") ?? "system";

        if (string.IsNullOrWhiteSpace(capabilityId))
            return Error("INVALID_INPUT", "The 'capability_id' parameter is required.");

        // Validate capability exists
        var existing = await _capabilityService.GetCapabilityAsync(capabilityId, cancellationToken);
        if (existing is null)
            return Error("NOT_FOUND", $"Capability '{capabilityId}' was not found.");

        var previousParent = existing.ParentCapabilityId;

        // Perform remap — records ParentChanged history event
        var updated = await _capabilityService.RemapParentAsync(
            capabilityId,
            string.IsNullOrWhiteSpace(newParentId) ? null : newParentId,
            remappedBy,
            cancellationToken);

        var result = new
        {
            capabilityId = updated.Id,
            name = updated.Name,
            previousParentId = previousParent,
            newParentId = updated.ParentCapabilityId,
            updatedAt = updated.UpdatedAt,
            message = updated.ParentCapabilityId is null
                ? $"Capability '{updated.Name}' is now a root capability."
                : $"Capability '{updated.Name}' remapped to parent '{updated.ParentCapabilityId}'.",
            historyRecorded = true
        };

        return JsonSerializer.Serialize(result, JsonOpts);
    }
}
