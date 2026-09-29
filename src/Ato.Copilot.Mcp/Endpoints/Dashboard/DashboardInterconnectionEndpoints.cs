using System.Text.Json.Serialization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>Editable registry details only; lifecycle and agreement mutations are separate workflows.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveInterconnectionRequest(
    string? TargetSystemName, string? TargetSystemOwner, string? TargetSystemAcronym,
    string? InterconnectionType, string? DataFlowDirection, string? DataClassification,
    string? DataDescription, List<string>? ProtocolsUsed, List<string>? PortsUsed,
    List<string>? SecurityMeasures, string? AuthenticationMethod,
    string? RemoteSystem, string? Direction, string? Type, string? Protocol, string? Port, string? Hostname);

public static partial class DashboardEndpoints
{
    private static void MapInterconnectionRoutes(IEndpointRouteBuilder group)
    {
        const string root = "/systems/{systemId}/interconnections";
        group.MapGet(root, async (string systemId, int? page, int? pageSize,
            AtoCopilotContext db, HttpContext http, CancellationToken ct) =>
        {
            var currentPage = page ?? 1;
            var size = pageSize ?? 50;
            if (currentPage < 1 || size < 1 || size > 200 || (long)(currentPage - 1) * size > int.MaxValue)
                return InvalidInterconnection("Use page >= 1 and pageSize between 1 and 200.");
            if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId, ct)) return Results.NotFound();
            var query = InterconnectionsForSystem(db, systemId);
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
            var canManage = await CanManageInterconnections(http, systemId, ct);
            return Results.Ok(new { items = items.Select(x => InterconnectionDetail(x, canManage)),
                total, page = currentPage, pageSize = size, canManageInterconnections = canManage });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        group.MapGet(root + "/{interconnectionId}", async (string systemId, string interconnectionId,
            AtoCopilotContext db, HttpContext http, CancellationToken ct) =>
        {
            if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId, ct)) return Results.NotFound();
            var item = await InterconnectionsForSystem(db, systemId).SingleOrDefaultAsync(x => x.Id == interconnectionId, ct);
            return item is null ? Results.NotFound()
                : Results.Ok(InterconnectionDetail(item, await CanManageInterconnections(http, systemId, ct)));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        group.MapPost(root, (string systemId, SaveInterconnectionRequest body, IInterconnectionService service,
            AtoCopilotContext db, ICurrentUserService user, CancellationToken ct) =>
                SaveInterconnection(systemId, null, body, service, db, user, ct))
            .WithName("AddInterconnection")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);
        group.MapPut(root + "/{interconnectionId}", (string systemId, string interconnectionId,
            SaveInterconnectionRequest body, IInterconnectionService service, AtoCopilotContext db,
            ICurrentUserService user, CancellationToken ct) =>
                SaveInterconnection(systemId, interconnectionId, body, service, db, user, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);
    }

    private static IQueryable<SystemInterconnection> InterconnectionsForSystem(AtoCopilotContext db, string systemId) =>
        db.SystemInterconnections.AsNoTracking().Include(x => x.Agreements).Where(x => x.RegisteredSystemId == systemId);

    private static async Task<bool> CanManageInterconnections(HttpContext http, string systemId, CancellationToken ct)
    {
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        if (tenant.IsWorkspaceRequest)
            return (await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct)).Permissions.CanManageSystem;
        return (await http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(http.User, null, Policies.ComplianceWriter)).Succeeded;
    }

    private static object InterconnectionDetail(SystemInterconnection item, bool canManage) => new
    {
        id = item.Id, interconnectionId = item.Id, systemId = item.RegisteredSystemId,
        item.TargetSystemName, item.TargetSystemOwner, item.TargetSystemAcronym,
        interconnectionType = item.InterconnectionType.ToString(), dataFlowDirection = item.DataFlowDirection.ToString(),
        item.DataClassification, item.DataDescription, item.ProtocolsUsed, item.PortsUsed,
        item.SecurityMeasures, item.AuthenticationMethod, status = item.Status.ToString(), item.StatusReason,
        item.AuthorizationToConnect, item.CreatedBy, item.CreatedAt, item.ModifiedAt,
        hasAgreement = item.Agreements.Count > 0, canManageInterconnections = canManage,
        agreements = item.Agreements.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new
        {
            x.Id, agreementType = x.AgreementType.ToString(), x.Title, status = x.Status.ToString(),
            x.DocumentReference, x.EffectiveDate, x.ExpirationDate, x.SignedByLocal, x.SignedByLocalDate,
            x.SignedByRemote, x.SignedByRemoteDate, x.ReviewNotes, x.CreatedAt, x.ModifiedAt
        })
    };

    private static async Task<IResult> SaveInterconnection(string systemId, string? id, SaveInterconnectionRequest body,
        IInterconnectionService service, AtoCopilotContext db, ICurrentUserService user, CancellationToken ct)
    {
        if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId, ct)) return Results.NotFound();
        if (id is not null && !await InterconnectionsForSystem(db, systemId).AnyAsync(x => x.Id == id, ct))
            return Results.NotFound();
        var normalized = NormalizeInterconnection(body, id is null);
        if (!ValidInterconnection(normalized, out var type, out var direction))
            return InvalidInterconnection("Provide a target name, defined connection type/direction, classification and valid detail fields.");
        InterconnectionResult result;
        if (id is null)
            result = await service.AddInterconnectionAsync(systemId, normalized.TargetSystemName!, type, direction,
                normalized.DataClassification!, user.CurrentUserId, normalized.TargetSystemOwner, normalized.TargetSystemAcronym,
                normalized.DataDescription, normalized.ProtocolsUsed, normalized.PortsUsed, normalized.SecurityMeasures,
                normalized.AuthenticationMethod, ct);
        else
            result = await service.UpdateInterconnectionAsync(id, targetSystemName: normalized.TargetSystemName,
                interconnectionType: type, dataFlowDirection: direction, dataClassification: normalized.DataClassification,
                dataDescription: normalized.DataDescription ?? "", protocolsUsed: normalized.ProtocolsUsed,
                portsUsed: normalized.PortsUsed, securityMeasures: normalized.SecurityMeasures,
                authenticationMethod: normalized.AuthenticationMethod ?? "", cancellationToken: ct,
                targetSystemOwner: normalized.TargetSystemOwner ?? "", targetSystemAcronym: normalized.TargetSystemAcronym ?? "");
        db.DashboardActivities.Add(new DashboardActivity
        {
            RegisteredSystemId = systemId, EventType = id is null ? "InterconnectionAdded" : "InterconnectionUpdated",
            Actor = user.CurrentUserId, Summary = id is null ? "External interconnection registered" : "External interconnection details updated",
            RelatedEntityType = "SystemInterconnection", RelatedEntityId = result.InterconnectionId
        });
        await db.SaveChangesAsync(ct);
        var persisted = await InterconnectionsForSystem(db, systemId).SingleAsync(x => x.Id == result.InterconnectionId, ct);
        return Results.Ok(InterconnectionDetail(persisted, true));
    }

    private static SaveInterconnectionRequest NormalizeInterconnection(SaveInterconnectionRequest body, bool create)
    {
        // Preserve the existing quick-add request without treating invalid enum values as defaults.
        var legacy = create && body.TargetSystemName is null && body.RemoteSystem is not null;
        return body with
        {
            TargetSystemName = body.TargetSystemName ?? (legacy ? body.RemoteSystem : null),
            InterconnectionType = body.InterconnectionType ?? (legacy ? body.Type ?? "Direct" : null),
            DataFlowDirection = body.DataFlowDirection ?? (legacy ? body.Direction : null),
            DataClassification = body.DataClassification ?? (legacy ? "CUI" : null),
            ProtocolsUsed = body.ProtocolsUsed ?? (legacy ? string.IsNullOrWhiteSpace(body.Protocol) ? [] : [body.Protocol] : null),
            PortsUsed = body.PortsUsed ?? (legacy ? string.IsNullOrWhiteSpace(body.Port) ? [] : [body.Port] : null),
            SecurityMeasures = body.SecurityMeasures ?? (legacy ? [] : null)
        };
    }

    private static bool ValidInterconnection(SaveInterconnectionRequest body, out InterconnectionType type,
        out DataFlowDirection direction)
    {
        var validType = Enum.TryParse(body.InterconnectionType, true, out type) && Enum.IsDefined(type);
        var validDirection = Enum.TryParse(body.DataFlowDirection, true, out direction) && Enum.IsDefined(direction);
        return validType && validDirection && !string.IsNullOrWhiteSpace(body.TargetSystemName)
            && body.TargetSystemName.Length <= 200 && !string.IsNullOrWhiteSpace(body.DataClassification)
            && body.DataClassification.Length <= 50 && (body.TargetSystemOwner?.Length ?? 0) <= 200
            && (body.TargetSystemAcronym?.Length ?? 0) <= 20 && (body.DataDescription?.Length ?? 0) <= 2000
            && (body.AuthenticationMethod?.Length ?? 0) <= 200
            && ValidInterconnectionValues(body.ProtocolsUsed) && ValidInterconnectionValues(body.PortsUsed)
            && ValidInterconnectionValues(body.SecurityMeasures);
    }

    private static bool ValidInterconnectionValues(List<string>? values) =>
        values is not null && values.Count <= 200 && values.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 200);

    private static IResult InvalidInterconnection(string message) => Results.BadRequest(new ErrorResponse
    {
        Error = message, ErrorCode = "INVALID_INPUT",
        Suggestion = "Reload the interconnection and correct its editable fields. Approval and lifecycle changes use their separate workflows."
    });
}
