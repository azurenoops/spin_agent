using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class OscalSspExportService
{
    private static async Task ResolveRecordedRolesAsync(IServiceProvider services, RegisteredSystem system,
        List<RmfRoleAssignment> roles, CancellationToken ct)
    {
        var reader = services.GetService<IUnifiedRoleReader>();
        if (reader == null) return;
        var snapshot = await reader.GetSystemRolesAsync(system.TenantId, system.Id, ct);
        foreach (var resolved in snapshot.Roles.Where(r => r.Source != RoleAssignmentSource.Legacy))
        {
            roles.RemoveAll(r => r.RmfRole == resolved.Role);
            if (resolved.PersonId is not Guid personId || resolved.Source == RoleAssignmentSource.NotAssigned) continue;
            roles.Add(new RmfRoleAssignment
            {
                RegisteredSystemId = system.Id, TenantId = system.TenantId,
                RmfRole = resolved.Role, UserId = personId.ToString(), UserDisplayName = resolved.PersonDisplayName,
                IsActive = true
            });
        }
    }

    private static Task<List<SystemComponent>> LoadModernComponentsAsync(AtoCopilotContext db, RegisteredSystem system, CancellationToken ct) =>
        db.SystemComponents.AsNoTracking()
            .Where(c => c.TenantId == system.TenantId && c.Status == ComponentStatus.Active
                && (c.RegisteredSystemId == system.Id
                    || c.SystemAssignments.Any(a => a.RegisteredSystemId == system.Id && a.TenantId == system.TenantId)
                    || c.BoundaryAssignments.Any(a => a.IsInScope && a.TenantId == system.TenantId
                        && a.AuthorizationBoundaryDefinition.RegisteredSystemId == system.Id))
                && (!c.BoundaryAssignments.Any(a => a.AuthorizationBoundaryDefinition.RegisteredSystemId == system.Id)
                    || c.BoundaryAssignments.Any(a => a.IsInScope && a.AuthorizationBoundaryDefinition.RegisteredSystemId == system.Id)))
            .OrderBy(c => c.Id).ToListAsync(ct);

    private static void AppendModernComponents(Dictionary<string, object> implementation,
        List<SystemComponent> components, Dictionary<string, string> ids)
    {
        if (components.Count == 0) return;
        var output = implementation["components"] as List<Dictionary<string, object>> ?? [];
        foreach (var component in components)
            output.Add(new()
            {
                ["uuid"] = ids[component.Id],
                ["type"] = component.ComponentType.ToString().ToLowerInvariant(),
                ["title"] = component.Name,
                ["description"] = component.Description ?? component.Name,
                ["status"] = new Dictionary<string, string> { ["state"] = "operational" },
                ["props"] = new[]
                {
                    new Dictionary<string, string> { ["name"] = "asset-id", ["value"] = component.Id }
                }
            });
        implementation["components"] = output;
    }
}
