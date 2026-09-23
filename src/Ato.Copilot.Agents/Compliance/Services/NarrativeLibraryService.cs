using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed record NarrativeReferenceResponse(
    Guid Id, Guid ReferenceKey, string Title, string Scope, string ScopeId,
    string SourceName, string SourceSha256, int Version, int Revision, bool IsPublished,
    DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? PublishedAt, string? PublishedBy,
    IReadOnlyList<NarrativeReferencePassage> Passages);

public sealed record NarrativeScopeCapability(string Id, string Name);
public sealed record NarrativeAccessResponse(Guid TenantId, string SystemName, bool CanAuthor,
    bool CanPublishShared, IReadOnlyList<NarrativeScopeCapability> Capabilities, bool CanGenerate = false);

public sealed partial class NarrativeLibraryService(AtoCopilotContext db, ITenantContext tenant,
    ISystemWorkspaceAccessService? systemAccess = null)
{
    private Guid TenantId => tenant.EffectiveTenantId;

    public async Task<NarrativeAccessResponse> GetAccessAsync(string systemId, string actor, CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, false, cancellationToken);
        var shared = await HasSharedAuthorityAsync(actor, cancellationToken);
        var canAuthor = tenant.IsWorkspaceRequest
            ? (await WorkspaceRolesAsync(systemId, cancellationToken)).Any(IsReferenceAuthor)
            : shared || await db.RmfRoleAssignments.AnyAsync(assignment => assignment.TenantId == TenantId &&
            assignment.RegisteredSystemId == systemId && assignment.UserId == actor && assignment.IsActive &&
            (assignment.RmfRole == RmfRole.MissionOwner || assignment.RmfRole == RmfRole.SystemOwner ||
             assignment.RmfRole == RmfRole.Isso || assignment.RmfRole == RmfRole.Issm), cancellationToken);
        var name = await db.RegisteredSystems.Where(system => system.TenantId == TenantId && system.Id == systemId)
            .Select(system => system.Name).SingleAsync(cancellationToken);
        var capabilities = shared ? await db.SecurityCapabilities.AsNoTracking().Where(capability => capability.TenantId == TenantId)
            .OrderBy(capability => capability.Name).Select(capability => new NarrativeScopeCapability(capability.Id, capability.Name))
            .ToListAsync(cancellationToken) : [];
        var canGenerate = tenant.IsWorkspaceRequest
            ? (await WorkspaceAccessAsync(systemId, cancellationToken)).Permissions.CanAuthorNarratives : canAuthor;
        return new(TenantId, name, canAuthor, shared, capabilities, canGenerate);
    }

    public async Task<IReadOnlyList<NarrativeReferenceResponse>> ListAsync(
        string systemId, string actor, CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, false, cancellationToken);
        var rows = await ReferencesForSystem(systemId).Where(item =>
            item.IsPublished || item.ImportedForSystemId == systemId && item.CreatedBy == actor)
            .OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(ToResponse).ToList();
    }

    internal async Task<IReadOnlyList<NarrativeReferenceResponse>> PublishedForSystemAsync(string systemId, CancellationToken ct) =>
        (await ReferencesForSystem(systemId).Where(item => item.IsPublished).ToListAsync(ct)).Select(ToResponse).ToArray();

    private IQueryable<NarrativeReference> ReferencesForSystem(string systemId)
    {
        var systemCapabilities = db.SystemCapabilityLinks.Where(item => item.TenantId == TenantId && item.RegisteredSystemId == systemId)
            .Select(item => item.SecurityCapabilityId);
        var capabilityIds = db.CapabilityControlMappings.Where(item => item.TenantId == TenantId &&
            (item.RegisteredSystemId == systemId || item.RegisteredSystemId == null && systemCapabilities.Contains(item.SecurityCapabilityId)))
            .Select(item => item.SecurityCapabilityId);
        return db.NarrativeReferences.AsNoTracking().Where(item => item.TenantId == TenantId &&
            (item.Scope == "Organization" || item.Scope == "System" && item.ScopeId == systemId ||
             item.Scope == "Capability" && capabilityIds.Contains(item.ScopeId)));
    }

    public async Task<NarrativeReferenceResponse> ImportAsync(
        string systemId, string actor, string title, string scope, string scopeId,
        string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, true, cancellationToken);
        await RequireScopeAsync(systemId, actor, scope, scopeId, cancellationToken);
        return await ImportCoreAsync(systemId, actor, title, scope, scopeId, fileName, content, cancellationToken);
    }

    private async Task<NarrativeReferenceResponse> ImportCoreAsync(
        string? systemId, string actor, string title, string scope, string scopeId,
        string fileName, Stream content, CancellationToken cancellationToken)
    {
        title = title.Trim();
        if (title.Length is 0 or > 200) throw new ArgumentException("Reference title must contain 1-200 characters.");
        var imported = await NarrativeReferenceContent.ReadAsync(fileName, content, cancellationToken);
        if (systemId is null) await RequireOrganizationPublisherAsync(actor, cancellationToken);
        else await RequireSystemAsync(systemId, actor, true, cancellationToken);
        await RequireScopeAsync(systemId, actor, scope, scopeId, cancellationToken);
        var previous = await db.NarrativeReferences.AsNoTracking().Where(item => item.TenantId == TenantId &&
            item.Scope == scope && item.ScopeId == scopeId && item.Title == title)
            .OrderByDescending(item => item.Version).FirstOrDefaultAsync(cancellationToken);
        var row = new NarrativeReference
        {
            TenantId = TenantId, Title = title, Scope = scope, ScopeId = scopeId, ImportedForSystemId = systemId,
            SourceName = imported.SourceName, SourceSha256 = imported.SourceSha256,
            CreatedBy = actor, PassagesJson = imported.PassagesJson, OriginalPassagesJson = imported.PassagesJson,
            ReferenceKey = previous?.ReferenceKey ?? Guid.NewGuid(), Version = (previous?.Version ?? 0) + 1,
        };
        db.NarrativeReferences.Add(row);
        await NarrativePersistence.SaveAsync(db, cancellationToken);
        return ToResponse(row);
    }

    public async Task<NarrativeReferenceResponse> PublishAsync(
        string systemId, Guid id, string actor, int expectedRevision,
        IReadOnlyList<NarrativeReferencePassage> passages, bool reviewed,
        CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, true, cancellationToken);
        var row = await db.NarrativeReferences.FirstOrDefaultAsync(item => item.Id == id && item.TenantId == TenantId &&
            item.ImportedForSystemId == systemId, cancellationToken) ?? throw new KeyNotFoundException("Reference not found.");
        await RequireDraftOwnerAsync(row, actor, cancellationToken);
        await RequireScopeAsync(systemId, actor, row.Scope, row.ScopeId, cancellationToken);
        return await PublishCoreAsync(systemId, row, actor, expectedRevision, passages, reviewed, cancellationToken);
    }

    private async Task<NarrativeReferenceResponse> PublishCoreAsync(
        string? systemId, NarrativeReference row, string actor, int expectedRevision,
        IReadOnlyList<NarrativeReferencePassage> passages, bool reviewed, CancellationToken cancellationToken)
    {
        if (row.IsPublished || row.Revision != expectedRevision)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Reference was published or changed. Reload before reviewing.");
        if (!reviewed) throw new ArgumentException("Acknowledge review before publishing.");
        var normalized = await ValidatePassagesAsync(systemId, passages, cancellationToken);
        var previous = await db.NarrativeReferences.AsNoTracking().Where(item => item.TenantId == TenantId &&
            item.ReferenceKey == row.ReferenceKey && item.Id != row.Id && item.IsPublished)
            .OrderByDescending(item => item.Version).FirstOrDefaultAsync(cancellationToken);
        var publication = NarrativeReferencePublicationPayloadBuilder.Build(
            row.Id, previous?.Id, row.ReferenceKey, row.Version, row.Scope, row.ScopeId, row.SourceSha256, normalized, previous?.PassagesJson);
        db.Set<NarrativeReferencePublication>().Add(new()
        {
            Id = row.Id, TenantId = TenantId, ReferenceId = row.Id, RecordedBy = actor,
            SourceRevision = publication.Revision, PayloadJson = publication.Payload
        });
        row.PassagesJson = JsonSerializer.Serialize(normalized);
        row.IsPublished = true;
        row.PublishedBy = actor;
        row.PublishedAt = DateTime.UtcNow;
        row.Revision++;
        await NarrativePersistence.SaveAsync(db, cancellationToken);
        return ToResponse(row);
    }

    public async Task<NarrativeReferenceResponse> UpdateDraftAsync(
        string systemId, Guid id, string actor, int expectedRevision, string scope, string scopeId,
        IReadOnlyList<NarrativeReferencePassage> passages, CancellationToken cancellationToken = default)
    {
        await RequireSystemAsync(systemId, actor, true, cancellationToken);
        var row = await db.NarrativeReferences.SingleOrDefaultAsync(item => item.Id == id && item.TenantId == TenantId &&
            item.ImportedForSystemId == systemId, cancellationToken) ?? throw new KeyNotFoundException("Reference not found.");
        await RequireDraftOwnerAsync(row, actor, cancellationToken);
        await RequireScopeAsync(systemId, actor, row.Scope, row.ScopeId, cancellationToken);
        await RequireScopeAsync(systemId, actor, scope, scopeId, cancellationToken);
        return await UpdateDraftCoreAsync(systemId, row, expectedRevision, scope, scopeId, passages, cancellationToken);
    }

    private async Task<NarrativeReferenceResponse> UpdateDraftCoreAsync(
        string? systemId, NarrativeReference row, int expectedRevision, string scope, string scopeId,
        IReadOnlyList<NarrativeReferencePassage> passages, CancellationToken cancellationToken)
    {
        if (row.IsPublished || row.Revision != expectedRevision)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Reference was published or changed. Reload before editing.");
        var normalized = await ValidatePassagesAsync(systemId, passages, cancellationToken, requireComplete: false);
        if (row.Scope != scope || row.ScopeId != scopeId)
        {
            var previous = await db.NarrativeReferences.AsNoTracking().Where(item => item.TenantId == TenantId &&
                item.Scope == scope && item.ScopeId == scopeId && item.Title == row.Title)
                .OrderByDescending(item => item.Version).FirstOrDefaultAsync(cancellationToken);
            row.ReferenceKey = previous?.ReferenceKey ?? Guid.NewGuid();
            row.Version = (previous?.Version ?? 0) + 1;
        }
        row.Scope = scope;
        row.ScopeId = scopeId;
        row.PassagesJson = JsonSerializer.Serialize(normalized);
        row.Revision++;
        await NarrativePersistence.SaveAsync(db, cancellationToken);
        return ToResponse(row);
    }

    internal async Task<IReadOnlyList<NarrativeReferencePassage>> ValidatePassagesAsync(
        string? systemId, IReadOnlyList<NarrativeReferencePassage> passages, CancellationToken cancellationToken,
        bool requireComplete = true)
    {
        if (passages is null || passages.Count is 0 or > 500 ||
            passages.Any(passage => passage is null || string.IsNullOrWhiteSpace(passage.Content) ||
                passage.Content.Length > 20_000 ||
                (passage.NarrativeType is not (null or "Policy" or "Technical")) ||
                requireComplete && (passage.NarrativeType is null || string.IsNullOrWhiteSpace(passage.ControlId)) ||
                passage.ControlId?.Length > 20) ||
            passages.Sum(passage => passage.Content.Length) > 500_000)
            throw new ArgumentException("Review all passages and resolve their control and narrative-type mappings before publishing.");
        var normalized = passages.Select(passage => passage with
        { ControlId = string.IsNullOrWhiteSpace(passage.ControlId) ? null : passage.ControlId.Trim().ToUpperInvariant(),
            Content = passage.Content.Trim() }).ToList();
        foreach (var controlId in normalized.Where(_ => requireComplete).Select(passage => passage.ControlId!).Distinct())
        {
            if (!await db.NistControls.AnyAsync(control => control.Id.ToUpper() == controlId, cancellationToken) &&
                (systemId is null || !await db.ControlImplementations.AnyAsync(item => item.TenantId == TenantId &&
                    item.RegisteredSystemId == systemId && item.ControlId.ToUpper() == controlId, cancellationToken)))
                throw new ArgumentException($"Unknown control mapping: {controlId}.");
        }
        return normalized;
    }

    private async Task RequireDraftOwnerAsync(NarrativeReference row, string actor, CancellationToken ct)
    {
        if (row.CreatedBy != actor && !await HasSharedAuthorityAsync(actor, ct))
            throw new UnauthorizedAccessException("Only the draft owner or an authorized shared-reference publisher may change this draft.");
    }

    internal async Task RequireSystemAsync(string systemId, string actor, bool write, CancellationToken cancellationToken)
    {
        if (TenantId == Guid.Empty || !await db.RegisteredSystems.AnyAsync(
            system => system.TenantId == TenantId && system.Id == systemId && system.IsActive, cancellationToken))
            throw new KeyNotFoundException("System not found.");
        if (string.IsNullOrWhiteSpace(actor)) throw new UnauthorizedAccessException("An authenticated identity is required.");
        if (tenant.IsWorkspaceRequest)
        {
            var access = await WorkspaceAccessAsync(systemId, cancellationToken);
            if (access.Permissions.CanRead && (!write || access.Roles.Any(role =>
                Enum.TryParse<RmfRole>(role, out var rmfRole) && IsReferenceAuthor(rmfRole)))) return;
            throw new UnauthorizedAccessException("An applicable system assignment is required.");
        }
        if (await HasSharedAuthorityAsync(actor, cancellationToken)) return;
        var assignments = db.RmfRoleAssignments.Where(assignment => assignment.TenantId == TenantId &&
            assignment.RegisteredSystemId == systemId && assignment.UserId == actor && assignment.IsActive);
        if (write) assignments = assignments.Where(assignment => assignment.RmfRole == RmfRole.MissionOwner ||
            assignment.RmfRole == RmfRole.SystemOwner || assignment.RmfRole == RmfRole.Isso || assignment.RmfRole == RmfRole.Issm);
        if (!await assignments.AnyAsync(cancellationToken))
            throw new UnauthorizedAccessException("An active system assignment is required.");
    }

    internal async Task RequireNarrativeAuthorAsync(string systemId, string actor, CancellationToken ct)
    {
        await RequireSystemAsync(systemId, actor, !tenant.IsWorkspaceRequest, ct);
        if (tenant.IsWorkspaceRequest && !(await WorkspaceAccessAsync(systemId, ct)).Permissions.CanAuthorNarratives)
            throw new UnauthorizedAccessException("An applicable narrative author assignment is required.");
    }

    internal async Task<bool> HasSharedAuthorityAsync(string actor, CancellationToken cancellationToken)
    {
        if (tenant.IsWorkspaceRequest)
            return tenant.PersonId.HasValue && await db.OrganizationRoleAssignments.AnyAsync(assignment =>
                assignment.TenantId == TenantId && assignment.PersonId == tenant.PersonId && assignment.RemovedAt == null
                && (assignment.Role == OrganizationRole.Issm || assignment.Role == OrganizationRole.Administrator), cancellationToken);
        if (!Guid.TryParse(actor, out var actorId)) return false;
        return await db.OrganizationRoleAssignments.AnyAsync(assignment => assignment.TenantId == TenantId &&
            assignment.RemovedAt == null && (assignment.Role == OrganizationRole.Issm || assignment.Role == OrganizationRole.Administrator) &&
            assignment.Person != null && assignment.Person.TenantId == TenantId &&
            (assignment.Person.Id == actorId || assignment.Person.EntraObjectId == actorId), cancellationToken);
    }

    internal async Task<bool> CanReviewProposalAsync(string systemId, string actor, CancellationToken ct)
    {
        if (tenant.IsWorkspaceRequest)
            return (await WorkspaceAccessAsync(systemId, ct)).Permissions.CanReviewNarratives;
        var tenantIssm = Guid.TryParse(actor, out var id) && await db.OrganizationRoleAssignments.AnyAsync(a =>
            a.TenantId == TenantId && a.RemovedAt == null && a.Role == OrganizationRole.Issm && a.Person != null
            && a.Person.TenantId == TenantId && (a.Person.Id == id || a.Person.EntraObjectId == id), ct);
        return tenantIssm || await db.RmfRoleAssignments.AnyAsync(a => a.TenantId == TenantId
            && a.RegisteredSystemId == systemId && a.UserId == actor && a.IsActive && a.RmfRole == RmfRole.Issm, ct);
    }

    internal async Task<HashSet<string>> SelfApprovalAliasesAsync(string actor, CancellationToken ct)
    {
        var isGuid = Guid.TryParse(actor, out var actorId);
        var aliases = new HashSet<string>(tenant.IsWorkspaceRequest || isGuid ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal) { actor };
        if (!tenant.IsWorkspaceRequest && !isGuid) return aliases;
        // Older proposals stored an oid rather than the canonical Person ID. Aliases may
        // deny self-approval, but must never grant private-reference read access.
        var people = await db.Persons.AsNoTracking().Where(person => person.TenantId == TenantId &&
            (tenant.IsWorkspaceRequest ? person.Id == tenant.PersonId :
                person.Id == actorId || person.EntraObjectId == actorId)).ToListAsync(ct);
        foreach (var person in people)
        {
            aliases.Add(person.Id.ToString());
            aliases.Add(person.Email);
            if (person.EntraObjectId.HasValue) aliases.Add(person.EntraObjectId.Value.ToString());
        }
        var personIds = people.Select(person => person.Id).ToArray();
        var identities = await db.OrganizationMemberships.AsNoTracking()
            .Where(m => m.TenantId == TenantId && personIds.Contains(m.PersonId))
            .Select(m => m.ObjectId).ToListAsync(ct);
        foreach (var identity in identities) aliases.Add(identity.ToString());
        return aliases;
    }

    private async Task<HashSet<RmfRole>> WorkspaceRolesAsync(string systemId, CancellationToken ct)
    {
        var access = await WorkspaceAccessAsync(systemId, ct);
        return Enum.GetValues<RmfRole>().Where(role => access.Roles.Contains(role.ToString())).ToHashSet();
    }

    private Task<SystemWorkspaceAccessResponse> WorkspaceAccessAsync(string systemId, CancellationToken ct)
    {
        if (systemAccess is null || tenant.PersonId is null)
            throw new UnauthorizedAccessException("An authenticated system workspace access decision is required.");
        return systemAccess.GetAccessAsync(TenantId, tenant.PersonId, systemId, false, ct);
    }

    private static bool IsReferenceAuthor(RmfRole role) =>
        role is RmfRole.MissionOwner or RmfRole.SystemOwner or RmfRole.Isso or RmfRole.Issm;

    private async Task RequireScopeAsync(string? systemId, string actor, string scope, string scopeId, CancellationToken cancellationToken)
    {
        if (systemId is not null && scope == "System" && scopeId == systemId) return;
        if (scope is not ("Organization" or "Capability")) throw new ArgumentException("Invalid reference scope.");
        if (!await HasSharedAuthorityAsync(actor, cancellationToken))
            throw new UnauthorizedAccessException("Shared references require tenant ISSM or Administrator authority.");
        if (scope == "Organization" && scopeId != TenantId.ToString() || scope == "Capability" &&
            !await db.SecurityCapabilities.AnyAsync(capability => capability.TenantId == TenantId && capability.Id == scopeId, cancellationToken))
            throw new ArgumentException("Reference scope target is not available in this tenant.");
    }

    private static NarrativeReferenceResponse ToResponse(NarrativeReference row) => new(
        row.Id, row.ReferenceKey, row.Title, row.Scope, row.ScopeId, row.SourceName, row.SourceSha256,
        row.Version, row.Revision, row.IsPublished, new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)), row.CreatedBy,
        row.PublishedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.PublishedAt.Value, DateTimeKind.Utc)) : null, row.PublishedBy,
        JsonSerializer.Deserialize<List<NarrativeReferencePassage>>(row.PassagesJson) ?? []);
}