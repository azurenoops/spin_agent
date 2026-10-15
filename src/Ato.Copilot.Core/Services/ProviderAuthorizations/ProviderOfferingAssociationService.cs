using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed class ProviderOfferingAssociationService(
    AtoCopilotContext db, ProviderAuthorizationStore store) : IProviderOfferingAssociationService
{
    private async Task<Guid> ProviderAsync(CancellationToken ct) => await store.ProviderAsync(db, ct);

    private async Task<ProviderOfferingAssociation> RequireAsync(Guid id, CancellationToken ct)
    {
        var provider = await ProviderAsync(ct);
        return await db.Set<ProviderOfferingAssociation>().SingleOrDefaultAsync(x =>
            x.Id == id && x.ProviderId == provider, ct)
            ?? throw new KeyNotFoundException("Offering association was not found.");
    }

    public async Task<IReadOnlyList<OfferingAssociationResponse>> ListAsync(Guid offeringId, bool includeDrafts,
        CancellationToken ct)
    {
        var provider = await ProviderAsync(ct);
        var query = db.Set<ProviderOfferingAssociationRevision>().AsNoTracking()
            .Where(x => x.ProviderId == provider && x.OfferingId == offeringId);
        if (!includeDrafts) query = query.Where(x => x.Status == "Published");
        var rows = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return rows.Select(ToResponse).ToArray();
    }

    public async Task<OfferingAssociationResponse> CreateAsync(Guid offeringId,
        CreateOfferingAssociationRequest request, string actor, CancellationToken ct)
    {
        var provider = await ProviderAsync(ct);
        var offering = await db.Set<ProviderOffering>().SingleOrDefaultAsync(x =>
            x.Id == offeringId && x.ProviderId == provider, ct)
            ?? throw new KeyNotFoundException("Offering was not found.");
        var (capability, release) = await RequirePublishedCapabilityAsync(provider, request, ct);
        if (offering.Revision != request.OfferingRevision)
            throw new DbUpdateConcurrencyException("The offering or capability release revision is stale.");
        Validate(request);
        await store.CitationsAsync(db, provider, request.SourceProvenance, ct, required: true);
        if (await db.Set<ProviderOfferingAssociationRevision>().AnyAsync(x =>
                x.ProviderId == provider && x.OfferingId == offeringId
                && x.ComponentId == request.ComponentId && x.CapabilityId == request.CapabilityId
                && x.CapabilityReleaseId == request.CapabilityReleaseId
                && (x.Status == "Draft" || x.Status == "Reviewed" || x.Status == "Published"), ct))
            throw new InvalidOperationException("An association already exists for this offering and capability release.");
        var association = new ProviderOfferingAssociation
        {
            ProviderId = provider, OfferingId = offeringId, ComponentId = capability.CspInheritedComponentId,
            CapabilityId = capability.Id, CreatedBy = actor
        };
        var revision = BuildRevision(association, release, offering.Revision, request, actor, null);
        association.CurrentRevisionId = revision.Id;
        db.Add(association);
        db.Add(revision);
        await db.SaveChangesAsync(ct);
        return ToResponse(revision);
    }

    public async Task<OfferingAssociationResponse> UpdateAsync(Guid associationId,
        CreateOfferingAssociationRequest request, long expectedRevision, string actor, CancellationToken ct)
    {
        var association = await RequireAsync(associationId, ct);
        Expected(association, expectedRevision);
        if (association.Lifecycle is "Withdrawn" or "Superseded")
            throw new InvalidOperationException("Withdrawn or superseded associations cannot be edited.");
        var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == association.OfferingId, ct);
        if (offering.Revision != request.OfferingRevision)
            throw new DbUpdateConcurrencyException("The offering revision is stale.");
        if (request.ComponentId != association.ComponentId || request.CapabilityId != association.CapabilityId)
            throw new InvalidOperationException("An association revision cannot change its component or capability.");
        var (_, release) = await RequirePublishedCapabilityAsync(association.ProviderId, request, ct);
        Validate(request);
        await store.CitationsAsync(db, association.ProviderId, request.SourceProvenance, ct, required: true);
        var prior = await db.Set<ProviderOfferingAssociationRevision>().SingleAsync(x =>
            x.Id == association.CurrentRevisionId && x.AssociationId == association.Id, ct);
        var revision = BuildRevision(association, release, offering.Revision, request, actor, prior.Id);
        association.Revision++;
        revision.Revision = association.Revision;
        association.CurrentRevisionId = revision.Id;
        association.Lifecycle = "Draft";
        db.Add(revision);
        await db.SaveChangesAsync(ct);
        return ToResponse(revision);
    }

    private async Task<(CspInheritedCapability Capability, ProviderCapabilityRelease Release)> RequirePublishedCapabilityAsync(
        Guid provider, CreateOfferingAssociationRequest request, CancellationToken ct)
    {
        var release = await db.ProviderCapabilityReleases.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == request.CapabilityReleaseId && x.CapabilityId == request.CapabilityId, ct)
            ?? throw new KeyNotFoundException("The exact published capability release was not found.");
        var capability = await db.CspInheritedCapabilities.Include(x => x.CspInheritedComponent)
            .SingleOrDefaultAsync(x => x.Id == request.CapabilityId
                && x.CspInheritedComponentId == request.ComponentId
                && x.CspInheritedComponent.CspProfileId == provider
                && x.Status == CspInheritedCapabilityStatus.Mapped
                && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published, ct)
            ?? throw new InvalidOperationException("The component and capability must be reviewed and published first.");
        if (release.Revision < 1 || string.IsNullOrWhiteSpace(release.PublishedBy))
            throw new InvalidOperationException("Only a published capability release can be associated with an offering.");
        return (capability, release);
    }

    public async Task<OfferingAssociationResponse> ReviewAsync(Guid associationId, long expectedRevision,
        bool approve, string rationale, string actor, CancellationToken ct)
    {
        var association = await RequireAsync(associationId, ct);
        Expected(association, expectedRevision);
        var revision = await db.Set<ProviderOfferingAssociationRevision>().SingleAsync(x =>
            x.Id == association.CurrentRevisionId && x.AssociationId == association.Id, ct);
        if (revision.Status is not ("Draft" or "Rejected"))
            throw new InvalidOperationException("Only a draft association can be reviewed.");
        revision.Status = approve ? "Reviewed" : "Rejected";
        revision.Reviewer = actor;
        revision.ReviewedAt = DateTimeOffset.UtcNow;
        revision.ReviewRationale = Text(rationale, "review rationale", 4000);
        association.Revision++;
        revision.Revision = association.Revision;
        association.Lifecycle = revision.Status;
        await db.SaveChangesAsync(ct);
        return ToResponse(revision);
    }

    public async Task<OfferingAssociationResponse> PublishAsync(Guid associationId, long expectedRevision,
        string actor, CancellationToken ct)
    {
        var association = await RequireAsync(associationId, ct);
        Expected(association, expectedRevision);
        var revision = await db.Set<ProviderOfferingAssociationRevision>().SingleAsync(x =>
            x.Id == association.CurrentRevisionId && x.AssociationId == association.Id, ct);
        if (revision.Status != "Reviewed" || revision.Reviewer is null)
            throw new InvalidOperationException("Only a reviewed association can be published.");
        var published = await db.Set<ProviderOfferingAssociationRevision>().Where(x =>
            x.AssociationId == association.Id && x.Status == "Published").ToListAsync(ct);
        foreach (var prior in published) prior.Status = "Superseded";
        revision.Status = "Published";
        association.Revision++;
        revision.Revision = association.Revision;
        association.Lifecycle = "Published";
        var capability = await db.CspInheritedCapabilities.Include(x => x.CspInheritedComponent)
            .SingleAsync(x => x.Id == association.CapabilityId && x.CspInheritedComponentId == association.ComponentId, ct);
        await CspResponsibilitySourceTracker.StageAssociationAsync(
            db, capability, revision.ContentHash, true, actor, ct);
        await db.SaveChangesAsync(ct);
        return ToResponse(revision);
    }

    public async Task<OfferingAssociationResponse> WithdrawAsync(Guid associationId, long expectedRevision,
        string rationale, string actor, CancellationToken ct)
    {
        var association = await RequireAsync(associationId, ct);
        Expected(association, expectedRevision);
        var revision = await db.Set<ProviderOfferingAssociationRevision>().SingleAsync(x =>
            x.Id == association.CurrentRevisionId && x.AssociationId == association.Id, ct);
        if (revision.Status != "Published")
            throw new InvalidOperationException("Only a published association can be withdrawn.");
        revision.Status = "Withdrawn";
        revision.Reviewer = actor;
        revision.ReviewedAt = DateTimeOffset.UtcNow;
        revision.ReviewRationale = Text(rationale, "withdrawal rationale", 4000);
        association.Revision++;
        revision.Revision = association.Revision;
        association.Lifecycle = "Withdrawn";
        var capability = await db.CspInheritedCapabilities.Include(x => x.CspInheritedComponent)
            .SingleAsync(x => x.Id == association.CapabilityId && x.CspInheritedComponentId == association.ComponentId, ct);
        await CspResponsibilitySourceTracker.StageAssociationAsync(
            db, capability, Hash($"withdrawn:{revision.ContentHash}"), false, actor, ct);
        await db.SaveChangesAsync(ct);
        return ToResponse(revision);
    }

    private static ProviderOfferingAssociationRevision BuildRevision(ProviderOfferingAssociation association,
        ProviderCapabilityRelease release, long offeringRevision, CreateOfferingAssociationRequest request,
        string actor, Guid? supersedes)
    {
        var source = JsonSerializer.Serialize(request.SourceProvenance, JsonOptions);
        var coverage = JsonSerializer.Serialize(request.ProviderCoverage, JsonOptions);
        var obligations = JsonSerializer.Serialize(request.CustomerObligations, JsonOptions);
        var material = JsonSerializer.Serialize(new { association.OfferingId, association.ComponentId, association.CapabilityId,
            release.Id, release.Revision, offeringRevision, source, coverage, obligations, request.ContributionRole }, JsonOptions);
        return new()
        {
            AssociationId = association.Id, ProviderId = association.ProviderId, OfferingId = association.OfferingId,
            ComponentId = association.ComponentId, CapabilityId = association.CapabilityId, CapabilityReleaseId = release.Id,
            CapabilityReleaseRevision = release.Revision, OfferingRevision = offeringRevision, SourceProvenanceJson = source,
            ProviderCoverageJson = coverage, CustomerObligationsJson = obligations,
            ContributionRole = request.ContributionRole, ContentHash = Hash(material), Author = actor,
            SupersedesRevisionId = supersedes
        };
    }

    private static void Validate(CreateOfferingAssociationRequest request)
    {
        Bounded(request.SourceProvenance, "source provenance", 1);
        Bounded(request.ProviderCoverage, "provider coverage", 1);
        Bounded(request.CustomerObligations, "customer obligations");
        foreach (var coverage in request.ProviderCoverage) Text(coverage, "provider coverage statement", 4000);
        foreach (var obligation in request.CustomerObligations) Text(obligation, "customer obligation", 4000);
        if (request.ContributionRole is not ("Primary" or "Supporting"))
            throw new ArgumentException("Contribution role must be Primary or Supporting.");
    }

    private static OfferingAssociationResponse ToResponse(ProviderOfferingAssociationRevision x) =>
        new(x.AssociationId, x.Id, x.OfferingId, x.ComponentId, x.CapabilityId, x.CapabilityReleaseId,
            x.CapabilityReleaseRevision, x.OfferingRevision, x.Revision, x.Status,
            Read<IReadOnlyList<ProviderCitation>>(x.SourceProvenanceJson),
            Read<IReadOnlyList<string>>(x.ProviderCoverageJson),
            Read<IReadOnlyList<string>>(x.CustomerObligationsJson), x.ContributionRole, x.Author, x.Reviewer,
            x.CreatedAt, x.ReviewedAt, x.ReviewRationale, x.ContentHash);
}
