using System.Net.Mail;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService
{
    public async Task<CreateWorkspaceOrganizationResult?> RecoverOrganizationCreationAsync(
        string idempotencyKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 100) throw new ArgumentException("Idempotency key is limited to 100 characters.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var operation = await db.OrganizationProvisioningOperations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey && x.CreationIntentHash != null, ct);
        if (operation?.CreationIntentHash is not { } intentHash) return null;
        return await ProjectOrganizationCreationReplayAsync(db, operation, intentHash, ct);
    }

    private static UpdateProvisioningRequest NormalizeAdministrator(UpdateProvisioningRequest request)
    {
        if (request.DirectoryTenantId == Guid.Empty || request.ObjectId == Guid.Empty)
            throw new ArgumentException("Directory tenant and object IDs must be nonempty GUIDs.");
        if (request.PersonId == Guid.Empty || request.PersonId.HasValue == (request.NewPerson is not null))
            throw new ArgumentException("Exactly one nonempty personId or newPerson is required.");
        if (request.NewPerson is not { } person) return request;
        var name = person.DisplayName?.Trim() ?? "";
        var email = person.Email?.Trim() ?? "";
        if (name.Length is < 1 or > 256 || email.Length is < 1 or > 320
            || !MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new ArgumentException("New Person requires a display name (maximum 256) and valid email (maximum 320).");
        return request with { NewPerson = new(name, email) };
    }

    private static async Task<Tenant> RequireActiveProvisioningTenantAsync(
        AtoCopilotContext db, Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || tenantId == Tenancy.TenantBootstrapService.SystemTenantId
            || tenantId == Tenancy.TenantBootstrapService.DefaultTenantId)
            throw new UnauthorizedAccessException("An active customer organization is required.");
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, ct)
            ?? throw new KeyNotFoundException("Organization was not found.");
        if (tenant.Status != TenantStatus.Active)
            throw new UnauthorizedAccessException("The organization is not active.");
        return tenant;
    }

    private static UpdateProvisioningRequest? ReadAdministrator(OrganizationProvisioningOperation row)
        => row.InitialAdministratorJson is { } json
            ? JsonSerializer.Deserialize<UpdateProvisioningRequest>(json)
                ?? throw new InvalidOperationException("Persisted administrator intent is invalid.")
            : row.DirectoryTenantId.HasValue && row.ObjectId.HasValue && row.PersonId.HasValue
                ? new(row.DirectoryTenantId.Value, row.ObjectId.Value, row.PersonId) : null;

    private static async Task<OrganizationProvisioningResult> ProjectProvisioningAsync(
        AtoCopilotContext db, OrganizationProvisioningOperation row, CancellationToken ct)
        => (await ProjectProvisioningBatchAsync(db, [row], ct))[row.Id];

    private static async Task<Dictionary<Guid, OrganizationProvisioningResult>> ProjectProvisioningBatchAsync(
        AtoCopilotContext db, IReadOnlyCollection<OrganizationProvisioningOperation> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var tenants = rows.Select(x => x.TenantId).Distinct().ToArray();
        var people = rows.Where(x => x.PersonId.HasValue).Select(x => x.PersonId!.Value).Distinct().ToArray();
        var localPeople = (await db.Persons.IgnoreQueryFilters().AsNoTracking()
            .Where(x => tenants.Contains(x.TenantId) && people.Contains(x.Id))
            .Select(x => new { x.TenantId, x.Id }).ToListAsync(ct)).Select(x => (x.TenantId, x.Id)).ToHashSet();
        var members = (await db.OrganizationMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => tenants.Contains(x.TenantId) && people.Contains(x.PersonId) && x.RevokedAt == null)
            .Select(x => new { x.TenantId, x.PersonId, x.DirectoryTenantId, x.ObjectId }).ToListAsync(ct))
            .Select(x => (x.TenantId, x.PersonId, x.DirectoryTenantId, x.ObjectId)).ToHashSet();
        var admins = (await db.OrganizationRoleAssignments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => tenants.Contains(x.TenantId) && people.Contains(x.PersonId)
                && x.Role == OrganizationRole.Administrator && x.RemovedAt == null)
            .Select(x => new { x.TenantId, x.PersonId }).ToListAsync(ct)).Select(x => (x.TenantId, x.PersonId)).ToHashSet();
        return rows.ToDictionary(x => x.Id, row =>
        {
            var intent = ReadAdministrator(row);
            var personId = row.PersonId.GetValueOrDefault();
            var validPerson = localPeople.Contains((row.TenantId, personId));
            var member = validPerson && members.Contains((row.TenantId, personId,
                row.DirectoryTenantId.GetValueOrDefault(), row.ObjectId.GetValueOrDefault()));
            var admin = validPerson && admins.Contains((row.TenantId, personId));
            return new OrganizationProvisioningResult(row.Id, row.TenantId, "Completed",
                admin ? "Completed" : "Pending", member ? "Completed" : "Pending",
                member && admin ? null : row.LastError, row.IdempotencyKey, intent,
                intent?.NewPerson is null ? "NotRequested" : validPerson ? "Completed" : "Pending",
                !row.AdministratorBoundAt.HasValue && !validPerson, row.PersonId);
        });
    }

    private static async Task<Dictionary<Guid, (string SetupState, int MemberCount)>> OrganizationSetupAsync(
        AtoCopilotContext db, Guid[] tenantIds, CancellationToken ct)
    {
        var operations = await db.OrganizationProvisioningOperations.AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId)).ToListAsync(ct);
        var latest = operations.GroupBy(x => x.TenantId).Select(x => x.OrderByDescending(y => y.UpdatedAt)
            .ThenByDescending(y => y.CreatedAt).ThenByDescending(y => y.Id).First()).ToArray();
        var states = await ProjectProvisioningBatchAsync(db, latest, ct);
        var byTenant = latest.ToDictionary(x => x.TenantId, x => states[x.Id].OverallState);
        var counts = await db.OrganizationMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId) && x.RevokedAt == null).GroupBy(x => x.TenantId)
            .Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return tenantIds.ToDictionary(x => x, x => (
            byTenant.GetValueOrDefault(x, "NotStarted") is "InProgress" ? "Pending" : byTenant.GetValueOrDefault(x, "NotStarted"),
            counts.GetValueOrDefault(x)));
    }
}
