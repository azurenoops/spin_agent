using System.Text.Json.Serialization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>Explicit source metadata, independent of RMF phase or authorization.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveSystemOperationalStatus(string OperationalStatus);

public static partial class DashboardEndpoints
{
    private static void MapOperationalStatusRoutes(IEndpointRouteBuilder group)
    {
        const string route = "/systems/{systemId}/operational-status";
        group.MapGet(route, async (string systemId, AtoCopilotContext db, HttpContext http, CancellationToken ct) =>
        {
            var system = await db.RegisteredSystems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == systemId && x.IsActive, ct);
            if (system is null) return Results.NotFound();
            return Results.Ok(new { systemId, operationalStatus = system.OperationalStatus?.ToString(),
                canManage = (await ReadinessPermissions(http, systemId, ct)).CanManageSystem });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapPut(route, async (string systemId, SaveSystemOperationalStatus body, IRmfLifecycleService service,
            ICurrentUserService user, HttpContext http, CancellationToken ct) =>
        {
            if (!Enum.TryParse<OperationalStatus>(body.OperationalStatus, false, out var status)
                || !Enum.IsDefined(status) || body.OperationalStatus != status.ToString())
                return Results.BadRequest(new ErrorResponse { Error = "Select Operational, UnderDevelopment, Disposed or MajorModification.", ErrorCode = "INVALID_OPERATIONAL_STATUS" });
            var result = await service.UpdateOperationalStatusAsync(systemId, status, user.CurrentUserId, ct);
            return Results.Ok(new { systemId = result.Id, operationalStatus = result.OperationalStatus?.ToString(),
                canManage = (await ReadinessPermissions(http, systemId, ct)).CanManageSystem });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);
    }
}
