using System.Data;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService
{
    private static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web);

    private async Task<Guid> RequireDraftProviderAsync(AtoCopilotContext db, CancellationToken ct)
    {
        if (systemTenant is not { IsCspAdmin: true, ImpersonatedTenantId: null })
            throw new UnauthorizedAccessException("Ordinary provider administrator authority is required.");
        return await db.CspProfiles.Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Hosting provider profile is unavailable.");
    }

    private static void CheckDraftPage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Page must be positive and page size between 1 and 100.");
    }

    private static OrganizationDraftValues NormalizeDraft(OrganizationDraftValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.OrganizationChoice is not ("create" or "existing")
            || values.AdministratorChoice is not (null or "existing" or "other" or "deferred"))
            throw new ArgumentException("Unsupported organization or administrator choice.");
        if (values.Discovery is { Source: not ("directory" or "manual") })
            throw new ArgumentException("Unsupported identity discovery source.");
        static string? Field(string? value, int max)
        {
            value = NormalizeOptional(value);
            if (value?.Length > max) throw new ArgumentException($"Draft field exceeds {max} characters.");
            return value;
        }
        var normalized = values with
        {
            DisplayName = Field(values.DisplayName, 200), LegalEntityName = Field(values.LegalEntityName, 300),
            PrimaryPocName = Field(values.PrimaryPocName, 200), PrimaryPocEmail = Field(values.PrimaryPocEmail, 254),
            DeferralReason = Field(values.DeferralReason, 1000)
        };
        if (values.ExistingTenantId == Guid.Empty || values.Administrator is { } identity
            && (identity.DirectoryTenantId == Guid.Empty || identity.ObjectId == Guid.Empty || identity.PersonId == Guid.Empty))
            throw new ArgumentException("An identity or organization ID cannot be the empty GUID.");
        if (values.Administrator?.NewPerson is { } person)
            normalized = normalized with { Administrator = values.Administrator with
            {
                NewPerson = new(Field(person.DisplayName, 256) ?? "", Field(person.Email, 320) ?? "")
            } };
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(normalized, DraftJson)) > 32768)
            throw new ArgumentException("Draft exceeds 32 KiB.");
        return normalized;
    }

    private static OrganizationOnboardingDraftResult ProjectDraft(OrganizationOnboardingDraft row)
    {
        var values = JsonSerializer.Deserialize<OrganizationDraftValues>(row.ValuesJson, DraftJson)
            ?? throw new InvalidOperationException("Saved organization draft is invalid.");
        var route = row.TenantId is { } tenant
            ? row.OperationId is { } operation
                ? $"/organizations/{tenant}/provisioning?key={Uri.EscapeDataString(row.ProvisioningKey ?? row.CreationKey)}&operationId={operation}"
                : $"/organizations/{tenant}/provisioning"
            : $"/organizations/new?draft={row.Id}";
        return new(row.Id, row.Revision, row.State, row.UpdatedAt, values.DisplayName ?? "Organization draft",
            row.CurrentStep, values, row.CreationKey, row.TenantId, row.OperationId, route, row.SchemaVersion);
    }

    public async Task<OrganizationOnboardingDraftResult> SaveOrganizationDraftAsync(
        Guid id, SaveOrganizationDraftRequest request, string actor, CancellationToken ct)
    {
        if (id == Guid.Empty || request.SchemaVersion != 1 || request.ExpectedRevision < 0
            || request.CurrentStep is not ("details" or "administrator" or "review"))
            throw new ArgumentException("Invalid draft identity, version or step.");
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var values = NormalizeDraft(request.Values);
        await using var db = await factory.CreateDbContextAsync(ct);
        var provider = await RequireDraftProviderAsync(db, ct);
        if (values.ExistingTenantId is { } existing) await RequireActiveProvisioningTenantAsync(db, existing, ct);
        var row = await db.Set<OrganizationOnboardingDraft>().SingleOrDefaultAsync(x => x.Id == id && x.ProviderId == provider, ct);
        if (row is null)
        {
            if (request.ExpectedRevision != 0) throw new DbUpdateConcurrencyException("Draft revision changed. Reload before saving.");
            row = new() { Id = id, ProviderId = provider, CreatedBy = actor };
            db.Set<OrganizationOnboardingDraft>().Add(row);
        }
        else
        {
            if (row.State != "Draft") throw new InvalidOperationException($"Draft is {row.State} and cannot be edited.");
            if (row.Revision != request.ExpectedRevision) throw new DbUpdateConcurrencyException("Draft revision changed. Your edits have not been applied.");
            row.Revision++;
        }
        row.ValuesJson = JsonSerializer.Serialize(values, DraftJson);
        row.CurrentStep = request.CurrentStep;
        TouchDraft(row, actor);
        AuditDraft(db, row, actor, "Saved");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            await using var check = await factory.CreateDbContextAsync(ct);
            if (await check.Set<OrganizationOnboardingDraft>().AnyAsync(x => x.Id == id && x.ProviderId == provider, ct))
                throw new DbUpdateConcurrencyException("Draft was saved concurrently. Reload before saving.", ex);
            throw;
        }
        return ProjectDraft(row);
    }

    private static void TouchDraft(OrganizationOnboardingDraft row, string actor)
    {
        row.UpdatedBy = actor;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedAtTicks = row.UpdatedAt.UtcTicks;
    }

    private void AuditDraft(AtoCopilotContext db, OrganizationOnboardingDraft row, string actor, string action)
        => db.AuditLogs.Add(new AuditLogEntry
        {
            TenantId = systemTenant!.EffectiveTenantId, ActorTenantId = systemTenant.EffectiveTenantId,
            UserId = actor, UserRole = "CSP.Admin", Action = $"OrganizationOnboarding.{action}",
            Details = JsonSerializer.Serialize(new { draftId = row.Id, providerId = row.ProviderId, revision = row.Revision })
        });

    public async Task<OrganizationOnboardingDraftResult?> GetOrganizationDraftAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var provider = await RequireDraftProviderAsync(db, ct);
        var row = await db.Set<OrganizationOnboardingDraft>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ProviderId == provider, ct);
        return row is null ? null : ProjectDraft(row);
    }

    public async Task<PagedResult<OrganizationOnboardingDraftResult>> ListOrganizationDraftsAsync(int page, int pageSize, CancellationToken ct)
    {
        CheckDraftPage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var provider = await RequireDraftProviderAsync(db, ct);
        var query = db.Set<OrganizationOnboardingDraft>().AsNoTracking().Where(x => x.ProviderId == provider && x.State != "Discarded");
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.UpdatedAtTicks).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(ProjectDraft).ToArray(), page, pageSize, total);
    }

    public async Task<OrganizationOnboardingDraftResult> DiscardOrganizationDraftAsync(Guid id, long expectedRevision, string actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var provider = await RequireDraftProviderAsync(db, ct);
        var row = await db.Set<OrganizationOnboardingDraft>().SingleOrDefaultAsync(x => x.Id == id && x.ProviderId == provider, ct)
            ?? throw new KeyNotFoundException("Organization draft was not found.");
        if (row.Revision != expectedRevision) throw new DbUpdateConcurrencyException("Draft revision changed.");
        if (row.State != "Draft") throw new InvalidOperationException("Only an unconfirmed draft can be discarded.");
        row.State = "Discarded"; row.Revision++; TouchDraft(row, actor);
        AuditDraft(db, row, actor, "Discarded");
        await db.SaveChangesAsync(ct);
        return ProjectDraft(row);
    }

    public async Task<OrganizationOnboardingDraftResult> ConfirmOrganizationDraftAsync(
        Guid id, ConfirmOrganizationDraftRequest request, string actor, CancellationToken ct)
    {
        if (!request.Confirmed) throw new ArgumentException("Explicit confirmation is required.");
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var provider = await RequireDraftProviderAsync(db, ct);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var row = await db.Set<OrganizationOnboardingDraft>().SingleOrDefaultAsync(x => x.Id == id && x.ProviderId == provider, ct)
                ?? throw new KeyNotFoundException("Organization draft was not found.");
            if (row.State == "Confirmed" && row.ConfirmedRevision == request.ExpectedRevision)
            {
                await RequireActiveProvisioningTenantAsync(db, row.TenantId!.Value, ct);
                return ProjectDraft(row);
            }
            if (row.State != "Draft") throw new InvalidOperationException($"Draft is {row.State}.");
            if (row.Revision != request.ExpectedRevision) throw new DbUpdateConcurrencyException("Draft revision changed; review the latest saved values.");
            var values = NormalizeDraft(ProjectDraft(row).Values);
            if (values.AdministratorChoice is null) throw new ArgumentException("Choose an administrator outcome before confirming.");
            UpdateProvisioningRequest? administrator = null;
            if (values.AdministratorChoice == "other")
            {
                var selection = values.Administrator ?? throw new ArgumentException("Administrator identity is required.");
                administrator = NormalizeAdministrator(new(selection.DirectoryTenantId ?? Guid.Empty,
                    selection.ObjectId ?? Guid.Empty, selection.PersonId, selection.NewPerson));
            }
            if (values.OrganizationChoice == "create")
            {
                if (values.AdministratorChoice == "existing") throw new ArgumentException("A new organization has no existing administrator.");
                var created = await CreateOrganizationCoreAsync(db, new(values.DisplayName ?? "", values.LegalEntityName,
                    values.PrimaryPocName, values.PrimaryPocEmail, administrator), row.CreationKey, actor, ct);
                row.TenantId = created.TenantId; row.OperationId = created.OperationId;
            }
            else
            {
                var target = await RequireActiveProvisioningTenantAsync(db,
                    values.ExistingTenantId ?? throw new ArgumentException("Choose an existing organization."), ct);
                row.TenantId = target.Id;
                values = values with { DisplayName = target.DisplayName };
                if (values.AdministratorChoice == "existing" && !await LiveAdministrators(db, target.Id).AnyAsync(ct))
                    throw new InvalidOperationException("No active organization administrator is available for reuse.");
                if (administrator is not null)
                {
                    if (await LiveAdministrators(db, target.Id).AnyAsync(ct))
                        throw new InvalidOperationException("Use the existing administrator role-management workflow to select another administrator.");
                    var operations = await db.OrganizationProvisioningOperations.Where(x => x.TenantId == target.Id).ToListAsync(ct);
                    var operation = operations.OrderByDescending(x => x.CreationIntentHash is not null).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefault();
                    if (operation is null)
                    {
                        operation = new() { TenantId = target.Id, IdempotencyKey = row.CreationKey, InitialAdministratorJson = JsonSerializer.Serialize(administrator) };
                        db.OrganizationProvisioningOperations.Add(operation);
                    }
                    else if (ReadAdministrator(operation) is { } existing && existing != administrator)
                        throw new InvalidOperationException("An existing enrollment request must be reviewed and resumed separately.");
                    else if (!operation.AdministratorBoundAt.HasValue)
                    {
                        var intentJson = JsonSerializer.Serialize(administrator);
                        if (!string.Equals(operation.InitialAdministratorJson, intentJson, StringComparison.Ordinal))
                        {
                            operation.InitialAdministratorJson = intentJson;
                            operation.Revision++;
                            operation.UpdatedAt = DateTimeOffset.UtcNow;
                            db.AuditLogs.Add(new AuditLogEntry
                            {
                                TenantId = target.Id, ActorTenantId = systemTenant!.EffectiveTenantId,
                                UserId = actor, UserRole = "CSP.Admin", Action = "OrganizationOnboarding.IntentUpdated",
                                Details = JsonSerializer.Serialize(new
                                {
                                    draftId = row.Id, operationId = operation.Id, revision = operation.Revision
                                })
                            });
                        }
                    }
                    row.OperationId = operation.Id;
                    row.ProvisioningKey = operation.IdempotencyKey;
                }
            }
            row.ValuesJson = JsonSerializer.Serialize(values, DraftJson);
            row.ConfirmedIntentHash = Hash(row.ValuesJson);
            row.ConfirmedRevision = row.Revision;
            row.Revision++; row.State = "Confirmed"; TouchDraft(row, actor);
            AuditDraft(db, row, actor, "Confirmed");
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return ProjectDraft(row);
        });
    }

    private sealed class LiveAdministratorRow
    {
        public Guid PersonId { get; init; }
        public string DisplayName { get; init; } = "";
        public Guid MembershipId { get; init; }
        public Guid DirectoryTenantId { get; init; }
        public Guid ObjectId { get; init; }
        public Guid AssignmentId { get; init; }
    }

    private static IQueryable<LiveAdministratorRow> LiveAdministrators(AtoCopilotContext db, Guid tenantId)
        => (from person in db.Persons.IgnoreQueryFilters().AsNoTracking()
            join member in db.OrganizationMemberships.AsNoTracking() on person.Id equals member.PersonId
            join role in db.OrganizationRoleAssignments.IgnoreQueryFilters().AsNoTracking() on person.Id equals role.PersonId
            where person.TenantId == tenantId && member.TenantId == tenantId && role.TenantId == tenantId
                && member.RevokedAt == null && role.RemovedAt == null && role.Role == OrganizationRole.Administrator
            select new LiveAdministratorRow
            {
                PersonId = person.Id, DisplayName = person.DisplayName, MembershipId = member.Id,
                DirectoryTenantId = member.DirectoryTenantId, ObjectId = member.ObjectId, AssignmentId = role.Id
            });

    public async Task<OrganizationSetupSummary> GetOrganizationSetupSummaryAsync(
        Guid tenantId, Guid? operationId, int page, int pageSize, Guid directoryId, Guid objectId, CancellationToken ct)
    {
        CheckDraftPage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireDraftProviderAsync(db, ct);
        var tenant = await RequireActiveProvisioningTenantAsync(db, tenantId, ct);
        var administrators = LiveAdministrators(db, tenantId);
        var total = await administrators.CountAsync(ct);
        var items = await administrators.OrderBy(x => x.DisplayName).ThenBy(x => x.PersonId)
            .ThenBy(x => x.AssignmentId).ThenBy(x => x.MembershipId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var memberCount = await db.OrganizationMemberships.CountAsync(x => x.TenantId == tenantId && x.RevokedAt == null, ct);
        OrganizationProvisioningOperation? operation;
        if (operationId is { } exact)
            operation = await db.OrganizationProvisioningOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == exact && x.TenantId == tenantId, ct)
                ?? throw new KeyNotFoundException("The requested enrollment operation was not found.");
        else
            operation = (await db.OrganizationProvisioningOperations.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(ct))
                .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault();
        var requested = operation is null ? null : await ProjectProvisioningAsync(db, operation, ct);
        var intent = operation is null ? null : ReadAdministrator(operation);
        var expectedPerson = operation?.PersonId ?? intent?.PersonId;
        var same = intent is not null && expectedPerson.HasValue && await administrators.AnyAsync(x => x.DirectoryTenantId == intent.DirectoryTenantId
            && x.ObjectId == intent.ObjectId && x.PersonId == expectedPerson.Value, ct);
        var actorCanEnter = await (from membership in db.OrganizationMemberships.AsNoTracking()
                                  join person in db.Persons.IgnoreQueryFilters().AsNoTracking() on membership.PersonId equals person.Id
                                  where membership.TenantId == tenantId && person.TenantId == tenantId
                                      && membership.RevokedAt == null && membership.DirectoryTenantId == directoryId && membership.ObjectId == objectId
                                  select membership.Id).AnyAsync(ct);
        return new(new(tenant.Id, tenant.DisplayName, tenant.Status.ToString(), tenant.OnboardingState.ToString()),
            DateTimeOffset.UtcNow, new(total > 0 ? "Available" : "Missing", memberCount,
                new(items.Select(x => new OrganizationLiveAdministrator(x.PersonId, x.DisplayName, x.MembershipId,
                    x.DirectoryTenantId, x.ObjectId, x.AssignmentId)).ToArray(), page, pageSize, total)),
            requested, same ? "SameIdentity" : intent is not null && total > 0 ? "DifferentIdentity" : operation?.PersonId is null ? "Unbound" : "None",
            new(true, requested?.OverallState != "Completed" && (total == 0 || same), actorCanEnter));
    }
}
