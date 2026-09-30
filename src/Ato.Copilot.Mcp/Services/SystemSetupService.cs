using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Roles;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Services;

public sealed class SystemSetupService(
    IDbContextFactory<AtoCopilotContext> factory, ITenantContext tenant,
    IWorkspaceService workspace, ISystemWorkspaceAccessService access, IUnifiedRoleReader roles)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Screens = ["s-details", "s-team", "s-sources", "s-hosting", "s-connect", "s-review", "s-ready"];
    private sealed record Command(string Key, string Hash, string Action, Guid Actor, long BaseRevision, long AppliedRevision);

    private Guid Actor(Guid tenantId)
    {
        if (!tenant.IsWorkspaceRequest || tenant.EffectiveTenantId != tenantId ||
            tenant.IsCspAdmin || tenant.ImpersonatedTenantId is not null ||
            tenant.PersonId is not { } person || tenant.Status != TenantStatus.Active ||
            workspace.Current is not { Kind: "organization", Mode: "ordinary" })
            throw new WorkspaceException(403, "WORKSPACE_OPERATION_NOT_AUTHORIZED", "Use an active ordinary organization workspace.");
        return person;
    }

    public bool CanCreate(Guid tenantId)
    {
        Actor(tenantId);
        return workspace.Current?.Permissions.CanCreateSystem == true;
    }

    public async Task<object> ContextAsync(Guid tenantId, CancellationToken ct)
    {
        var canCreate = CanCreate(tenantId);
        await using var db = await factory.CreateDbContextAsync(ct);
        return new { organizationName = await OrganizationName(db, tenantId, ct), canCreate,
            contacts = canCreate ? await Contacts(db, tenantId, ct) : [] };
    }

    public async Task<(SystemSetupView View, bool Created)> CreateAsync(
        Guid tenantId, SystemSetupDraft input, string key, CancellationToken ct)
    {
        var actor = Actor(tenantId);
        if (!CanCreate(tenantId)) throw Denied();
        ValidateKey(key);
        var draft = Normalize(input);
        var hash = Hash(new { tenantId, actor, draft });
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var existing = await db.Set<RegisteredSystem>().SingleOrDefaultAsync(x =>
                x.TenantId == tenantId && x.SetupActorPersonId == actor && x.SetupRequestKey == key, ct);
            if (existing is not null)
            {
                if (existing.SetupRequestHash != hash) throw Conflict("SETUP_REQUEST_CONFLICT", "This request key belongs to a different saved intent.");
                await transaction.CommitAsync(ct);
                return (await ProjectAsync(db, existing, ct), false);
            }
            await ValidateContact(db, tenantId, draft.Contact, ct);
            var intent = Intent(draft, ["systemType", "missionCriticality", "hostingEnvironment"]);
            var system = new RegisteredSystem
            {
                TenantId = tenantId, Name = draft.Name, Acronym = EmptyNull(draft.Acronym),
                Description = EmptyNull(draft.MissionPurpose),
                SystemType = SystemType.MajorApplication, MissionCriticality = MissionCriticality.MissionSupport,
                HostingEnvironment = "Undetermined", CurrentRmfStep = RmfPhase.Prepare,
                CreatedBy = actor.ToString(), SetupActorPersonId = actor,
                SetupRequestKey = key, SetupRequestHash = hash, SetupRevision = 1,
                SetupDraftJson = JsonSerializer.Serialize(intent, Json),
            };
            db.Set<RegisteredSystem>().Add(system);
            AddActivity(db, system, actor, "SystemSetupDraftSaved", "Saved a system preparation draft; no authorization or roles were granted.");
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                var winner = await db.Set<RegisteredSystem>().SingleOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.SetupActorPersonId == actor && x.SetupRequestKey == key, ct);
                if (winner is null) throw;
                if (winner.SetupRequestHash != hash) throw Conflict("SETUP_REQUEST_CONFLICT", "This request key belongs to another intent.");
                return (await ProjectAsync(db, winner, ct), false);
            }
            await transaction.CommitAsync(ct);
            return (await ProjectAsync(db, system, ct), true);
        });
    }

    public async Task<SystemSetupView> GetAsync(Guid tenantId, string systemId, CancellationToken ct)
    {
        Actor(tenantId);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ProjectAsync(db, await Find(db, tenantId, systemId, ct), ct);
    }

    public async Task<SystemSetupView> RecoverAsync(Guid tenantId, string key, CancellationToken ct)
    {
        var actor = Actor(tenantId);
        ValidateKey(key);
        await using var db = await factory.CreateDbContextAsync(ct);
        var system = await db.Set<RegisteredSystem>().SingleOrDefaultAsync(x =>
            x.TenantId == tenantId && x.SetupActorPersonId == actor && x.SetupRequestKey == key && x.IsActive, ct)
            ?? throw Missing();
        return await ProjectAsync(db, system, ct);
    }

    public async Task<object> ListAsync(Guid tenantId, string? cursor, int pageSize, CancellationToken ct)
    {
        Actor(tenantId);
        if (pageSize is < 1 or > 100) throw Invalid("pageSize must be between 1 and 100.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Set<RegisteredSystem>().AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.IsActive && x.SetupDraftJson != null);
        if (cursor is not null) query = query.Where(x => string.Compare(x.Id, cursor) > 0);
        var systems = await query.OrderBy(x => x.Id).Take(pageSize + 1).ToListAsync(ct);
        var items = new List<SystemSetupSummary>();
        foreach (var system in systems.Take(pageSize))
        {
            var permission = await access.GetAccessAsync(tenantId, tenant.PersonId, system.Id, false, ct);
            if (!permission.Permissions.CanRead) continue;
            items.Add(new(system.Id, system.Name, system.SetupRevision, SavedAt(system),
                system.SetupCompletedAt is null ? "draft" : "confirmed",
                new($"/systems/{Uri.EscapeDataString(system.Id)}/setup")));
        }
        return new { items, nextCursor = systems.Count > pageSize ? systems[pageSize - 1].Id : null };
    }

    public Task<SystemSetupView> SaveAsync(Guid tenantId, string systemId, SystemSetupDraft input,
        string key, long revision, CancellationToken ct) =>
        MutateAsync(tenantId, systemId, Normalize(input), key, revision, false, input.ExpectedIdentityRevision, ct);

    public async Task<SystemSetupView> ConfirmAsync(Guid tenantId, string systemId,
        SystemSetupConfirmRequest request, string key, long revision, CancellationToken ct)
    {
        if (!request.Confirmed || request.ReviewRevision != revision || string.IsNullOrWhiteSpace(request.IdentityRevision))
            throw Invalid("Confirm the exact displayed setup and identity revisions.");
        return await MutateAsync(tenantId, systemId, null, key, revision, true, request.IdentityRevision, ct);
    }

    private async Task<SystemSetupView> MutateAsync(Guid tenantId, string systemId, SystemSetupDraft? input,
        string key, long revision, bool confirm, string? reviewedIdentityRevision, CancellationToken ct)
    {
        var actor = Actor(tenantId);
        ValidateKey(key);
        var permission = await access.GetAccessAsync(tenantId, actor, systemId, false, ct);
        if (!permission.Permissions.CanRead) throw Missing();
        if (!permission.Permissions.CanManageSystem) throw Denied();
        var action = confirm ? "Confirm" : "Save";
        var hash = Hash(new { tenantId, systemId, actor, action, revision, reviewedIdentityRevision, input });
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var system = await Find(db, tenantId, systemId, ct);
            var previous = system.SetupLastCommandJson is null ? null : JsonSerializer.Deserialize<Command>(system.SetupLastCommandJson, Json);
            if (previous?.Key == key)
            {
                if (previous.Hash != hash || previous.Actor != actor) throw Conflict("SETUP_REQUEST_CONFLICT", "This key belongs to another setup command.");
                var replay = await ProjectAsync(db, system, ct);
                await transaction.CommitAsync(ct);
                return replay;
            }
            if (system.SetupRevision != revision) throw Conflict("STALE_SETUP", "Setup changed. Reload the saved draft before reviewing another change.");
            var stored = ReadIntent(system);
            var draft = input ?? ReadDraft(system, stored);
            if (reviewedIdentityRevision != IdentityRevision(system, stored))
                throw Conflict("STALE_SETUP", "System identity changed. Reload the saved draft.");
            await ValidateContact(db, tenantId, draft.Contact, ct);
            if (confirm && (string.IsNullOrWhiteSpace(draft.Objective) || draft.Contact is null))
                throw Invalid("Choose a preparation objective and an active accountable contact before confirming setup.");
            var updatedIntent = Intent(draft with { LastScreen = confirm ? "s-ready" : draft.LastScreen }, stored.UnconfirmedFields);
            var json = JsonSerializer.Serialize(updatedIntent, Json);
            var receipt = JsonSerializer.Serialize(new Command(key, hash, action, actor, revision, revision + 1), Json);
            var now = DateTime.UtcNow;
            var completed = confirm ? DateTimeOffset.UtcNow : system.SetupCompletedAt;
            var name = draft.Name;
            var acronym = EmptyNull(draft.Acronym);
            var purpose = EmptyNull(draft.MissionPurpose);
            var count = await db.Set<RegisteredSystem>().Where(x => x.Id == systemId && x.TenantId == tenantId && x.IsActive &&
                    x.SetupRevision == revision && x.Name == system.Name && x.Acronym == system.Acronym && x.Description == system.Description)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Name, name).SetProperty(x => x.Acronym, acronym).SetProperty(x => x.Description, purpose)
                    .SetProperty(x => x.SetupDraftJson, json).SetProperty(x => x.SetupRevision, revision + 1)
                    .SetProperty(x => x.SetupLastCommandJson, receipt).SetProperty(x => x.SetupCompletedAt, completed)
                    .SetProperty(x => x.ModifiedAt, now), ct);
            if (count != 1) throw Conflict("STALE_SETUP", "A concurrent system edit requires a fresh review.");
            db.Entry(system).State = EntityState.Detached;
            system = await Find(db, tenantId, systemId, ct);
            AddActivity(db, system, actor, confirm ? "SystemSetupConfirmed" : "SystemSetupDraftSaved",
                confirm ? "Confirmed preparation choices; documentation and authorization gates remain separate." : "Saved system preparation choices.");
            await db.SaveChangesAsync(ct);
            var result = await ProjectAsync(db, system, ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    public async Task EnsureManageAsync(Guid tenantId, string systemId, CancellationToken ct)
    {
        var actor = Actor(tenantId);
        var permission = await access.GetAccessAsync(tenantId, actor, systemId, false, ct);
        if (!permission.Permissions.CanRead) throw Missing();
        if (!permission.Permissions.CanManageSystem) throw Denied();
    }

    private async Task<SystemSetupView> ProjectAsync(AtoCopilotContext db, RegisteredSystem system, CancellationToken ct)
    {
        var actor = Actor(system.TenantId);
        var permission = await access.GetAccessAsync(system.TenantId, actor, system.Id, false, ct);
        if (!permission.Permissions.CanRead || !system.IsActive) throw Missing();
        var intent = ReadIntent(system);
        var snapshot = await roles.GetSystemRolesAsync(system.TenantId, system.Id, ct);
        var contacts = await Contacts(db, system.TenantId, ct);
        if (!permission.Permissions.CanManageSystem)
            contacts = contacts.Where(x => x.PersonId == intent.Contact?.PersonId).ToArray();
        var path = $"/systems/{Uri.EscapeDataString(system.Id)}";
        var sources = await SystemSourceReadProjection.ListAsync(db, system.TenantId, system.Id, ct);
        var tasks = SystemSetupPreparationProjection.WithSources(
            SystemSetupPreparationProjection.Build(system, permission.Permissions), sources, permission.Permissions);
        return new(system.Id, system.TenantId, system.Name, await OrganizationName(db, system.TenantId, ct),
            system.SetupRevision, IdentityRevision(system, intent), SavedAt(system),
            system.SetupDraftJson is null ? "legacy" : system.SetupCompletedAt is null ? "draft" : "confirmed",
            system.SetupCompletedAt, ReadDraft(system, intent), permission.Permissions.CanManageSystem,
            contacts, snapshot.Roles.Select(x => new SystemSetupTeamRole(x.Role.ToString(), x.PersonDisplayName, x.Source.ToString())).ToArray(),
            sources.Cast<object>().ToArray(),
            tasks, new(system.AzureProfile is null ? "absent" : "present", "notChecked", "unsupported", "unknown", "unknown"),
            new($"{path}/documents", permission.Permissions.CanEditProfile ? $"{path}/profile/EnvironmentAndDeployment/hosting" : null,
                permission.Permissions.CanManageSystem || permission.Permissions.CanAuthorNarratives ? $"{path}/security-capabilities" : null,
                $"{path}/conmon"), intent.UnconfirmedFields);
    }

    private static Task<RegisteredSystem> Find(AtoCopilotContext db, Guid tenantId, string id, CancellationToken ct) =>
        FindCore(db, tenantId, id, ct);
    private static async Task<RegisteredSystem> FindCore(AtoCopilotContext db, Guid tenantId, string id, CancellationToken ct) =>
        await db.Set<RegisteredSystem>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id && x.IsActive, ct) ?? throw Missing();
    private static async Task<string> OrganizationName(AtoCopilotContext db, Guid id, CancellationToken ct) =>
        await db.Tenants.Where(x => x.Id == id).Select(x => x.DisplayName).SingleAsync(ct);
    private static async Task<IReadOnlyList<SystemSetupContact>> Contacts(AtoCopilotContext db, Guid id, CancellationToken ct) =>
        await db.Persons.Where(p => p.TenantId == id && db.OrganizationMemberships.Any(m =>
            m.TenantId == id && m.PersonId == p.Id && m.RevokedAt == null))
            .OrderBy(p => p.DisplayName).Select(p => new SystemSetupContact(p.Id, p.DisplayName)).ToListAsync(ct);
    private static async Task ValidateContact(AtoCopilotContext db, Guid tenantId, SystemSetupContactSelection? contact, CancellationToken ct)
    {
        if (contact is null) return;
        if (contact.Responsibility != "preparationContact" || !await db.Persons.AnyAsync(p =>
                p.Id == contact.PersonId && p.TenantId == tenantId && db.OrganizationMemberships.Any(m =>
                    m.TenantId == tenantId && m.PersonId == p.Id && m.RevokedAt == null), ct))
            throw Invalid("Select an active contact from this organization; this selection does not grant access.");
    }
    private static SystemSetupDraft Normalize(SystemSetupDraft input)
    {
        var result = input with { Name = (input.Name ?? "").Trim(), Acronym = (input.Acronym ?? "").Trim(),
            MissionPurpose = (input.MissionPurpose ?? "").Trim() };
        if (result.Name.Length is < 1 or > 200 || result.Acronym.Length > 20 || result.MissionPurpose.Length > 2000 ||
            !Screens.Contains(result.LastScreen) || result.LastScreen == "s-ready" ||
            !new[] { "", "initialAto", "continuePackage", "maintainSystem" }.Contains(result.Objective) ||
            !new[] { "blank", "sspPdf", "emass", "deferred" }.Contains(result.SourceChoice) ||
            !new[] { "allocatedService", "organizationManaged", "deferred" }.Contains(result.HostingChoice) ||
            !new[] { "configureLater", "reviewAzureConnection" }.Contains(result.MonitoringChoice))
            throw Invalid("Supply a valid system name and supported setup choices.");
        return result;
    }
    private static SystemSetupIntent Intent(SystemSetupDraft draft, IReadOnlyList<string> unconfirmed) =>
        new(1, draft.Objective, draft.Contact, draft.SourceChoice, draft.HostingChoice, draft.MonitoringChoice, draft.LastScreen, unconfirmed);
    private static SystemSetupIntent ReadIntent(RegisteredSystem system) =>
        system.SetupDraftJson is null ? Intent(new SystemSetupDraft(), []) :
        JsonSerializer.Deserialize<SystemSetupIntent>(system.SetupDraftJson, Json) ?? throw new InvalidOperationException("Saved setup state is unreadable.");
    private static SystemSetupDraft ReadDraft(RegisteredSystem system, SystemSetupIntent intent) => new()
    {
        Name = system.Name, Acronym = system.Acronym ?? "", MissionPurpose = system.Description ?? "",
        Objective = intent.Objective, Contact = intent.Contact, SourceChoice = intent.SourceChoice,
        HostingChoice = intent.HostingChoice, MonitoringChoice = intent.MonitoringChoice, LastScreen = intent.LastScreen,
    };
    private static string IdentityRevision(RegisteredSystem system, SystemSetupIntent intent) =>
        Hash(new { system.Name, system.Acronym, system.Description, intent.UnconfirmedFields });
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));
    private static string? EmptyNull(string value) => string.IsNullOrEmpty(value) ? null : value;
    private static DateTimeOffset SavedAt(RegisteredSystem system) => new(DateTime.SpecifyKind(system.ModifiedAt ?? system.CreatedAt, DateTimeKind.Utc));
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Any(char.IsControl)) throw Invalid("A nonempty Idempotency-Key of at most 100 characters is required.");
    }
    private static void AddActivity(AtoCopilotContext db, RegisteredSystem system, Guid actor, string action, string summary) =>
        db.DashboardActivities.Add(new() { RegisteredSystemId = system.Id, TenantId = system.TenantId,
            EventType = action, Actor = actor.ToString(), Summary = summary,
            RelatedEntityType = nameof(RegisteredSystem), RelatedEntityId = system.Id });
    private static WorkspaceException Missing() => new(404, "SYSTEM_NOT_FOUND", "This system is not accessible in the selected workspace.");
    private static WorkspaceException Denied() => new(403, "WORKSPACE_OPERATION_NOT_AUTHORIZED", "Your current assignments do not authorize system setup changes.");
    private static WorkspaceException Invalid(string message) => new(400, "INVALID_SETUP", message);
    private static WorkspaceException Conflict(string code, string message) => new(409, code, message);
}
