using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Real provider identity and capability choices for provider-owned reference management.</summary>
public sealed record ProviderNarrativeAccessResponse(
    Guid CspProfileId, string DisplayName, bool CanPublish, IReadOnlyList<NarrativeScopeCapability> Capabilities);

public sealed class ProviderNarrativeLibraryService(
    AtoCopilotContext db, ITenantContext tenant, NarrativeLibraryService library)
{
    public async Task<ProviderNarrativeAccessResponse> GetAccessAsync(string actor, CancellationToken ct = default)
    {
        var profile = await RequireProviderAsync(actor, ct);
        var capabilities = await db.CspInheritedCapabilities.AsNoTracking()
            .Where(item => item.CspInheritedComponent.CspProfileId == profile.Id &&
                item.CspInheritedComponent.Status != CspInheritedComponentStatus.Archived)
            .OrderBy(item => item.Name).Select(item => new { item.Id, item.Name }).ToListAsync(ct);
        return new(profile.Id, profile.DisplayName, true,
            capabilities.Select(item => new NarrativeScopeCapability(item.Id.ToString(), item.Name)).ToArray());
    }

    public async Task<IReadOnlyList<NarrativeReferenceResponse>> ListAsync(string actor, CancellationToken ct = default)
    {
        var profile = await RequireProviderAsync(actor, ct);
        var rows = await db.Set<ProviderNarrativeReference>().AsNoTracking().Where(item => item.CspProfileId == profile.Id)
            .OrderByDescending(item => item.CreatedAt).ToListAsync(ct);
        return rows.Select(ToResponse).ToArray();
    }

    public async Task<NarrativeReferenceResponse> ImportAsync(
        string actor, string title, string scope, string scopeId, string fileName, Stream content, CancellationToken ct = default)
    {
        var profile = await RequireProviderAsync(actor, ct);
        var target = await RequireScopeAsync(profile.Id, scope, scopeId, ct);
        title = title.Trim();
        if (title.Length is 0 or > 200) throw new ArgumentException("Reference title must contain 1-200 characters.");
        var imported = await NarrativeReferenceContent.ReadAsync(fileName, content, ct);
        await RequireProviderAsync(actor, ct);
        await RequireScopeAsync(profile.Id, scope, scopeId, ct);
        var previous = await PreviousRevisionAsync(profile.Id, scope, target, title, ct);
        var row = new ProviderNarrativeReference
        {
            CspProfileId = profile.Id, Scope = scope, ScopeId = target, Title = title, CreatedBy = actor,
            SourceName = imported.SourceName, SourceSha256 = imported.SourceSha256,
            OriginalPassagesJson = imported.PassagesJson, PassagesJson = imported.PassagesJson,
            ReferenceKey = previous?.ReferenceKey ?? Guid.NewGuid(), Version = (previous?.Version ?? 0) + 1
        };
        db.Set<ProviderNarrativeReference>().Add(row);
        await NarrativePersistence.SaveAsync(db, ct);
        return ToResponse(row);
    }

    public async Task<NarrativeReferenceResponse> UpdateDraftAsync(
        Guid id, string actor, int expectedRevision, string scope, string scopeId,
        IReadOnlyList<NarrativeReferencePassage> passages, CancellationToken ct = default)
    {
        var profile = await RequireProviderAsync(actor, ct);
        var row = await ReferenceAsync(profile.Id, id, ct);
        EnsureDraft(row, expectedRevision);
        var target = await RequireScopeAsync(profile.Id, scope, scopeId, ct);
        var normalized = await library.ValidatePassagesAsync(null, passages, ct, requireComplete: false);
        if (row.Scope != scope || row.ScopeId != target)
        {
            var previous = await PreviousRevisionAsync(profile.Id, scope, target, row.Title, ct);
            row.ReferenceKey = previous?.ReferenceKey ?? Guid.NewGuid();
            row.Version = (previous?.Version ?? 0) + 1;
        }
        row.Scope = scope;
        row.ScopeId = target;
        row.PassagesJson = JsonSerializer.Serialize(normalized);
        row.Revision++;
        await NarrativePersistence.SaveAsync(db, ct);
        return ToResponse(row);
    }

    public async Task<NarrativeReferenceResponse> PublishAsync(
        Guid id, string actor, int expectedRevision, IReadOnlyList<NarrativeReferencePassage> passages, bool reviewed,
        CancellationToken ct = default)
    {
        var profile = await RequireProviderAsync(actor, ct);
        var row = await ReferenceAsync(profile.Id, id, ct);
        EnsureDraft(row, expectedRevision);
        if (!reviewed) throw new ArgumentException("Acknowledge reference review before publishing.");
        await RequireScopeAsync(profile.Id, row.Scope, row.ScopeId.ToString(), ct);
        var normalized = await library.ValidatePassagesAsync(null, passages, ct);
        if (row.Scope == "ProviderCapability")
        {
            var capability = await db.CspInheritedCapabilities.AsNoTracking().SingleAsync(item => item.Id == row.ScopeId, ct);
            if (capability.Status != CspInheritedCapabilityStatus.Mapped || normalized.Any(passage =>
                !capability.MappedNistControlIds.Contains(passage.ControlId!, StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("Published passages must map to reviewed controls of the selected provider capability.");
        }
        var previous = await db.Set<ProviderNarrativeReference>().AsNoTracking().Where(item => item.CspProfileId == profile.Id &&
            item.ReferenceKey == row.ReferenceKey && item.Id != row.Id && item.IsPublished)
            .OrderByDescending(item => item.Version).FirstOrDefaultAsync(ct);
        var publication = NarrativeReferencePublicationPayloadBuilder.Build(
            row.Id, previous?.Id, row.ReferenceKey, row.Version, row.Scope, row.ScopeId.ToString(),
            row.SourceSha256, normalized, previous?.PassagesJson);
        db.Set<ProviderNarrativeReferencePublication>().Add(new()
        {
            Id = row.Id, CspProfileId = profile.Id, ReferenceId = row.Id, RecordedBy = actor,
            SourceRevision = publication.Revision, PayloadJson = publication.Payload
        });
        row.PassagesJson = JsonSerializer.Serialize(normalized);
        row.IsPublished = true;
        row.PublishedAt = DateTime.UtcNow;
        row.PublishedBy = actor;
        row.Revision++;
        await NarrativePersistence.SaveAsync(db, ct);
        return ToResponse(row);
    }

    public async Task<IReadOnlyList<NarrativeReferenceResponse>> GetApplicableAsync(
        string systemId, string controlId, string actor, CancellationToken ct = default)
    {
        await library.RequireSystemAsync(systemId, actor, false, ct);
        return await ReadApplicableAsync(db, tenant.EffectiveTenantId, systemId, controlId, ct);
    }

    internal static async Task<IReadOnlyList<NarrativeReferenceResponse>> ReadApplicableAsync(
        AtoCopilotContext db, Guid tenantId, string systemId, string controlId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || !await db.RegisteredSystems.AnyAsync(item => item.TenantId == tenantId &&
            item.Id == systemId && item.IsActive, ct)) throw new KeyNotFoundException("System not found.");
        var subscriptions = await db.CapabilitySubscriptions.AsNoTracking()
            .Where(item => item.RegisteredSystemId == systemId && item.IsActive)
            .Select(item => item.CspInheritedCapabilityId).ToListAsync(ct);
        var capabilityIds = ParseCapabilityIds(subscriptions);
        var capabilities = await db.CspInheritedCapabilities.AsNoTracking().Where(item => capabilityIds.Contains(item.Id) &&
            item.Status == CspInheritedCapabilityStatus.Mapped && item.CspInheritedComponent.Status == CspInheritedComponentStatus.Published)
            .Select(item => new { item.Id, item.MappedNistControlIds, item.CspInheritedComponent.CspProfileId }).ToListAsync(ct);
        var applicable = capabilities.Where(item => item.MappedNistControlIds.Contains(controlId, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (applicable.Length == 0) return [];
        var ids = applicable.Select(item => item.Id).ToArray();
        var profiles = applicable.Select(item => item.CspProfileId).Distinct().ToArray();
        var references = await db.Set<ProviderNarrativeReference>().AsNoTracking().Where(item => item.IsPublished &&
            profiles.Contains(item.CspProfileId) && (item.Scope == "Provider" && item.ScopeId == item.CspProfileId ||
                item.Scope == "ProviderCapability" && ids.Contains(item.ScopeId))).ToListAsync(ct);
        return references.GroupBy(item => item.ReferenceKey).Select(group => group.MaxBy(item => item.Version)!)
            .Select(ToResponse).Select(item => item with
            { Passages = item.Passages.Where(passage => string.Equals(passage.ControlId, controlId, StringComparison.OrdinalIgnoreCase)).ToArray() })
            .Where(item => item.Passages.Count > 0).ToArray();
    }

    internal static Guid[] ParseCapabilityIds(IEnumerable<string> ids) =>
        ids.Select(id => Guid.TryParse(id, out var value) && value != Guid.Empty ? value :
            throw new InvalidOperationException("SOURCE_STATE_INVALID: A stored subscription has an invalid provider capability identity."))
            .Distinct().ToArray();

    private async Task<CspProfile> RequireProviderAsync(string actor, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin || tenant.EffectiveTenantId != Guid.Empty || tenant.ImpersonatedTenantId.HasValue ||
            string.IsNullOrWhiteSpace(actor))
            throw new UnauthorizedAccessException("Provider references require an authorized provider workspace.");
        return await db.CspProfiles.AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Provider profile not found.");
    }

    private async Task<Guid> RequireScopeAsync(Guid profileId, string scope, string scopeId, CancellationToken ct)
    {
        if (!Guid.TryParse(scopeId, out var target) || target == Guid.Empty)
            throw new ArgumentException("A real provider or capability identity is required.");
        if (scope == "Provider" && target == profileId) return target;
        if (scope == "ProviderCapability" && await db.CspInheritedCapabilities.AnyAsync(item => item.Id == target &&
            item.CspInheritedComponent.CspProfileId == profileId &&
            item.CspInheritedComponent.Status != CspInheritedComponentStatus.Archived, ct)) return target;
        throw new ArgumentException("Reference target is not available in this provider workspace.");
    }

    private async Task<ProviderNarrativeReference> ReferenceAsync(Guid profileId, Guid id, CancellationToken ct) =>
        await db.Set<ProviderNarrativeReference>().SingleOrDefaultAsync(item => item.CspProfileId == profileId && item.Id == id, ct)
            ?? throw new KeyNotFoundException("Provider reference not found.");

    private Task<ProviderNarrativeReference?> PreviousRevisionAsync(Guid profileId, string scope, Guid scopeId, string title, CancellationToken ct) =>
        db.Set<ProviderNarrativeReference>().AsNoTracking().Where(item => item.CspProfileId == profileId &&
            item.Scope == scope && item.ScopeId == scopeId && item.Title == title).OrderByDescending(item => item.Version).FirstOrDefaultAsync(ct);

    private static void EnsureDraft(ProviderNarrativeReference row, int expectedRevision)
    {
        if (row.IsPublished || row.Revision != expectedRevision)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: Provider reference was published or changed.");
    }

    private static NarrativeReferenceResponse ToResponse(ProviderNarrativeReference row) => new(
        row.Id, row.ReferenceKey, row.Title, row.Scope, row.ScopeId.ToString(), row.SourceName, row.SourceSha256,
        row.Version, row.Revision, row.IsPublished, new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)), row.CreatedBy,
        row.PublishedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.PublishedAt.Value, DateTimeKind.Utc)) : null, row.PublishedBy,
        JsonSerializer.Deserialize<List<NarrativeReferencePassage>>(row.PassagesJson)
            ?? throw new InvalidDataException("Provider reference passages are invalid."));
}
