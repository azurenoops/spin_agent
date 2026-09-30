using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>Only existing inventory metadata is editable through the register.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SaveInventoryRegisterItem : InventoryItemInput;

/// <summary>Explicit reason retained when retiring a hardware/software item.</summary>
public sealed record DecommissionInventoryRequest(string Rationale);

public static partial class DashboardEndpoints
{
    private static void MapInventoryRegisterRoutes(IEndpointRouteBuilder group)
    {
        const string root = "/systems/{systemId}/inventory-items";
        group.MapGet(root, async (string systemId, int? page, int? pageSize, AtoCopilotContext db,
            IInventoryService service, HttpContext http, CancellationToken ct) =>
        {
            var number = page ?? 1;
            var size = pageSize ?? 50;
            if (number < 1 || size is < 1 or > 200 || (long)(number - 1) * size > int.MaxValue)
                return InventoryInvalid("Use page >= 1 and pageSize between 1 and 200.");
            if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId, ct)) return Results.NotFound();
            var items = await service.ListItemsAsync(systemId, new() { PageNumber = number, PageSize = size }, ct);
            var total = await db.InventoryItems.CountAsync(x => x.RegisteredSystemId == systemId && x.Status == InventoryItemStatus.Active, ct);
            var canManage = (await ReadinessPermissions(http, systemId, ct)).CanManageSystem;
            return Results.Ok(new { systemId, items = items.Select(InventoryItemView), totalCount = total,
                page = number, pageSize = size, canManage });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapGet(root + "/completeness", async (string systemId, IInventoryService service, CancellationToken ct) =>
            Results.Ok(await service.CheckCompletenessAsync(systemId, ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapGet(root + "/export", async (string systemId, IInventoryService service, CancellationToken ct) =>
            Results.File(await service.ExportToExcelAsync(systemId, cancellationToken: ct),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "hardware-software-inventory.xlsx"))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapGet(root + "/{itemId}", async (string systemId, string itemId, AtoCopilotContext db, CancellationToken ct) =>
        {
            var item = await db.InventoryItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == itemId && x.RegisteredSystemId == systemId, ct);
            return item is null ? Results.NotFound() : Results.Ok(InventoryItemView(item));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        group.MapPost(root, (string systemId, SaveInventoryRegisterItem body, IInventoryService service,
            AtoCopilotContext db, ICurrentUserService user, CancellationToken ct) =>
                SaveInventoryItem(systemId, null, body, service, db, user, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);
        group.MapPut(root + "/{itemId}", (string systemId, string itemId, SaveInventoryRegisterItem body,
            IInventoryService service, AtoCopilotContext db, ICurrentUserService user, CancellationToken ct) =>
                SaveInventoryItem(systemId, itemId, body, service, db, user, ct))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);
        group.MapPost(root + "/{itemId}/decommission", async (string systemId, string itemId, DecommissionInventoryRequest body,
            AtoCopilotContext db, IInventoryService service, ICurrentUserService user, CancellationToken ct) =>
        {
            if (!await db.InventoryItems.AnyAsync(x => x.Id == itemId && x.RegisteredSystemId == systemId, ct)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Rationale) || body.Rationale.Length > 2000)
                return InventoryInvalid("A retirement rationale of 1-2000 characters is required.");
            try { return Results.Ok(InventoryItemView(await service.DecommissionItemAsync(itemId, body.Rationale.Trim(), user.CurrentUserId, ct))); }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ALREADY_DECOMMISSIONED:", StringComparison.Ordinal))
            {
                return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "INVENTORY_ALREADY_RETIRED" });
            }
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);
    }

    private static async Task<IResult> SaveInventoryItem(string systemId, string? itemId, SaveInventoryRegisterItem body,
        IInventoryService service, AtoCopilotContext db, ICurrentUserService user, CancellationToken ct)
    {
        if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId, ct)) return Results.NotFound();
        InventoryItemType? itemType = body.Type;
        if (itemId is not null)
        {
            var prior = await db.InventoryItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == itemId && x.RegisteredSystemId == systemId, ct);
            if (prior is null) return Results.NotFound();
            itemType ??= prior.Type;
            if (body.Type.HasValue && body.Type != prior.Type) return InventoryInvalid("An existing inventory item's type cannot be changed.");
            if (prior.Status == InventoryItemStatus.Decommissioned) return InventoryInvalid("Retired items cannot be edited.");
        }
        if (body.Type.HasValue && !Enum.IsDefined(body.Type.Value)
            || body.HardwareFunction.HasValue && !Enum.IsDefined(body.HardwareFunction.Value)
            || body.SoftwareFunction.HasValue && !Enum.IsDefined(body.SoftwareFunction.Value))
            return InventoryInvalid("Select a defined inventory type and function.");
        if (body.ItemName != null && string.IsNullOrWhiteSpace(body.ItemName)
            || itemType == InventoryItemType.Software && (body.Vendor != null && string.IsNullOrWhiteSpace(body.Vendor)
                || body.Version != null && string.IsNullOrWhiteSpace(body.Version))
            || itemType == InventoryItemType.Hardware && body.Manufacturer != null && string.IsNullOrWhiteSpace(body.Manufacturer))
            return InventoryInvalid("Required item name, vendor/version or manufacturer fields cannot be cleared.");
        foreach (var property in typeof(InventoryItem).GetProperties())
        {
            var maximum = property.GetCustomAttributes(typeof(MaxLengthAttribute), true).OfType<MaxLengthAttribute>().FirstOrDefault();
            var value = typeof(InventoryItemInput).GetProperty(property.Name)?.GetValue(body) as string;
            if (maximum != null && value?.Length > maximum.Length) return InventoryInvalid($"{property.Name} exceeds {maximum.Length} characters.");
        }
        if (!string.IsNullOrEmpty(body.ParentHardwareId) && !await db.InventoryItems.AnyAsync(x =>
            x.Id == body.ParentHardwareId && x.RegisteredSystemId == systemId && x.Type == InventoryItemType.Hardware
                && x.Status == InventoryItemStatus.Active, ct))
            return InventoryInvalid("Parent hardware must be an active hardware item in this system.");
        try
        {
            var result = itemId is null ? await service.AddItemAsync(systemId, body, user.CurrentUserId, ct)
                : await service.UpdateItemAsync(itemId, body, user.CurrentUserId, ct);
            return Results.Ok(InventoryItemView(result));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("VALIDATION_FAILED:", StringComparison.Ordinal)
            || ex.Message.StartsWith("DUPLICATE_IP:", StringComparison.Ordinal) || ex.Message.StartsWith("PARENT_NOT_FOUND:", StringComparison.Ordinal))
        {
            return InventoryInvalid(ex.Message);
        }
    }

    private static IResult InventoryInvalid(string message) =>
        Results.BadRequest(new ErrorResponse { Error = message, ErrorCode = "INVALID_INVENTORY_INPUT" });

    private static object InventoryItemView(InventoryItem item) => new
    {
        item.Id, systemId = item.RegisteredSystemId, item.ItemName, type = item.Type.ToString(),
        hardwareFunction = item.HardwareFunction?.ToString(), softwareFunction = item.SoftwareFunction?.ToString(),
        item.Manufacturer, item.Model, item.SerialNumber, item.IpAddress, item.MacAddress, item.Location,
        item.Vendor, item.Version, item.PatchLevel, item.LicenseType, item.ParentHardwareId, item.BoundaryResourceId,
        status = item.Status.ToString(), item.CreatedAt, item.CreatedBy, item.ModifiedAt, item.ModifiedBy,
        item.DecommissionDate, item.DecommissionRationale
    };
}
