using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Organization reference publication permission and organization-owned capability choices.</summary>
public sealed record NarrativeOrganizationAccessResponse(
    Guid TenantId, bool CanPublishShared, IReadOnlyList<NarrativeScopeCapability> Capabilities);

public sealed partial class NarrativeLibraryService
{
    public async Task<NarrativeOrganizationAccessResponse> GetOrganizationAccessAsync(string actor, CancellationToken ct = default)
    {
        await RequireOrganizationPublisherAsync(actor, ct);
        var capabilities = await db.SecurityCapabilities.AsNoTracking().Where(item => item.TenantId == TenantId)
            .OrderBy(item => item.Name).Select(item => new NarrativeScopeCapability(item.Id, item.Name)).ToListAsync(ct);
        return new(TenantId, true, capabilities);
    }

    public async Task<IReadOnlyList<NarrativeReferenceResponse>> ListOrganizationAsync(string actor, CancellationToken ct = default)
    {
        await RequireOrganizationPublisherAsync(actor, ct);
        var rows = await db.NarrativeReferences.AsNoTracking().Where(item => item.TenantId == TenantId &&
            (item.Scope == "Organization" || item.Scope == "Capability")).OrderByDescending(item => item.CreatedAt).ToListAsync(ct);
        return rows.Select(ToResponse).ToArray();
    }

    public async Task<NarrativeReferenceResponse> ImportOrganizationAsync(
        string actor, string title, string scope, string scopeId, string fileName, Stream content, CancellationToken ct = default)
    {
        await RequireOrganizationPublisherAsync(actor, ct);
        await RequireScopeAsync(null, actor, scope, scopeId, ct);
        return await ImportCoreAsync(null, actor, title, scope, scopeId, fileName, content, ct);
    }

    public async Task<NarrativeReferenceResponse> UpdateOrganizationDraftAsync(
        Guid id, string actor, int expectedRevision, string scope, string scopeId,
        IReadOnlyList<NarrativeReferencePassage> passages, CancellationToken ct = default)
    {
        await RequireOrganizationPublisherAsync(actor, ct);
        var row = await SharedReferenceAsync(id, ct);
        await RequireScopeAsync(null, actor, row.Scope, row.ScopeId, ct);
        await RequireScopeAsync(null, actor, scope, scopeId, ct);
        return await UpdateDraftCoreAsync(null, row, expectedRevision, scope, scopeId, passages, ct);
    }

    public async Task<NarrativeReferenceResponse> PublishOrganizationAsync(
        Guid id, string actor, int expectedRevision, IReadOnlyList<NarrativeReferencePassage> passages, bool reviewed,
        CancellationToken ct = default)
    {
        await RequireOrganizationPublisherAsync(actor, ct);
        var row = await SharedReferenceAsync(id, ct);
        await RequireScopeAsync(null, actor, row.Scope, row.ScopeId, ct);
        return await PublishCoreAsync(null, row, actor, expectedRevision, passages, reviewed, ct);
    }

    private async Task<Ato.Copilot.Core.Models.Compliance.NarrativeReference> SharedReferenceAsync(Guid id, CancellationToken ct) =>
        await db.NarrativeReferences.SingleOrDefaultAsync(item => item.TenantId == TenantId && item.Id == id &&
            (item.Scope == "Organization" || item.Scope == "Capability"), ct)
            ?? throw new KeyNotFoundException("Organization reference not found.");

    private async Task RequireOrganizationPublisherAsync(string actor, CancellationToken ct)
    {
        if (TenantId == Guid.Empty || string.IsNullOrWhiteSpace(actor) || !await HasSharedAuthorityAsync(actor, ct))
            throw new UnauthorizedAccessException("Organization references require tenant ISSM or Administrator authority.");
        if (tenant.IsWorkspaceRequest && !await db.OrganizationMemberships.AnyAsync(item => item.TenantId == TenantId &&
            item.PersonId == tenant.PersonId && item.RevokedAt == null, ct))
            throw new UnauthorizedAccessException("An active organization membership is required.");
    }
}
