using Ato.Copilot.Core.Interfaces.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ato.Copilot.Mcp.Extensions;

/// <summary>
/// Registers CSP Capability HTTP endpoints on the MCP API bridge.
/// Issue #161: PATCH /csp/capabilities/{id}/parent — remap parent with confirmation dialog support.
/// </summary>
public static class CspCapabilityEndpoints
{
    /// <summary>
    /// Maps CSP capability endpoints onto the given route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapCspCapabilityEndpoints(this IEndpointRouteBuilder app)
    {
        // PATCH /csp/capabilities/{id}/parent
        // Remaps a capability to a new parent. Null newParentId makes it a root.
        // The remap is recorded in CapabilityHistoryEvents as ParentChanged.
        app.MapMethods("csp/capabilities/{id}/parent", new[] { "PATCH" },
            async (string id, RemapParentRequest request, ICspCapabilityService service) =>
            {
                var capability = await service.GetCapabilityAsync(id);
                if (capability is null)
                    return Results.NotFound(new { error = "NOT_FOUND", message = $"Capability '{id}' not found." });

                var updated = await service.RemapParentAsync(
                    id,
                    request.NewParentId,
                    request.RemappedBy ?? "system");

                return Results.Ok(new
                {
                    id = updated.Id,
                    name = updated.Name,
                    parentCapabilityId = updated.ParentCapabilityId,
                    status = updated.Status.ToString(),
                    updatedAt = updated.UpdatedAt,
                    message = updated.ParentCapabilityId is null
                        ? $"Capability '{updated.Name}' is now a root capability."
                        : $"Capability '{updated.Name}' remapped to parent '{updated.ParentCapabilityId}'."
                });
            })
            .WithName("RemapCapabilityParent")
            .WithTags("CSP Capabilities")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}

/// <summary>
/// Request body for the PATCH /csp/capabilities/{id}/parent endpoint.
/// </summary>
public record RemapParentRequest(
    /// <summary>The new parent capability ID. Null makes the capability a root.</summary>
    string? NewParentId,
    /// <summary>User ID performing the remap (defaults to 'system' if not provided).</summary>
    string? RemappedBy = null
);
