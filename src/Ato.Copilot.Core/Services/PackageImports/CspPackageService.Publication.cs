using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Workspaces;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ato.Copilot.Core.Services.PackageImports;

public sealed partial class CspPackageService
{
    public Task<PackagePublicationResponse> PublishAsync(Guid id, PackageDecisionRequest request, string key, string actor, CancellationToken ct)
    {
        Authorize();
        ValidateKey(key);
        return ChangeAsync(id, "Published", actor, async (db, package) =>
        {
            var previous = await db.CspPackageApprovals.SingleOrDefaultAsync(x => x.PackageId == id && x.PublicationKey == key, ct);
            if (previous is not null)
            {
                if (previous.Id != request.PreviewId || previous.PreviewHash != request.PreviewHash || previous.Revision != request.Revision)
                    throw new DbUpdateConcurrencyException("Publication key was already used for a different approved set.");
                if (previous.PublicationJson is not null) return Read<PackagePublicationResponse>(previous.PublicationJson) with { Existing = true };
            }
            Editable(package);
            if (!await db.CspProfiles.AnyAsync(x => x.Id == package.ProviderId && x.OnboardingState == OnboardingState.Active, ct))
                throw new PackageStateException("Complete provider onboarding before publishing approved records.");
            var approval = await DecisionAsync(db, package, request, ct);
            if (approval.State != "Approved" || approval.ApprovedBy is null)
                throw new DbUpdateConcurrencyException("Explicit approval of the exact preview is required before publication.");
            var selection = Read<PackageSelection[]>(approval.SelectionJson);
            var material = await ValidateSelectionAsync(db, package, selection, ct, PackageImpactIds(approval.SnapshotJson));
            RequireEligible(approval, material);
            var rows = await db.CspPackageCandidates.Where(x => x.PackageId == id).ToListAsync(ct);
            if (material.Candidates.Any(x => rows.Single(r => r.Id == x.CandidateId).ReviewState != "Approved"))
                throw new DbUpdateConcurrencyException("Every selected candidate must still be explicitly approved.");
            if (!db.Database.IsRelational() || db.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Publication requires a transactional relational database.");
            package.PublicationState = "Publishing";
            Touch(package, false);
            await db.SaveChangesAsync(ct);
            var workspace = new WorkspaceOperationsService(new PublicationContextFactory(db));
            var results = new List<PackagePublishedRecord>();
            foreach (var component in material.Candidates.Where(x => x.Type == "Component"))
            {
                var recordId = component.DuplicateResolution == "ReusePublished" ? component.ContributorIds.Single() : component.CandidateId;
                if (component.DuplicateResolution != "ReusePublished")
                    db.CspInheritedComponents.Add(new CspInheritedComponent
                    {
                        Id = recordId, CspProfileId = package.ProviderId, Name = component.Name, Description = component.Description,
                        ComponentType = Enum.Parse<CspComponentType>(component.ComponentType),
                        Status = CspInheritedComponentStatus.Draft, SourceFormat = SourceFormat.Package,
                        SourceFileName = component.Citations[0].ArchivePath,
                        SourceArtifactReference = $"package:{id:D}/artifact:{component.Citations[0].ArtifactId:D}",
                        ImportedBy = package.CreatedBy, UpdatedBy = actor, UpdatedAt = DateTimeOffset.UtcNow
                    });
                results.Add(new(component.CandidateId, recordId, null, "Component"));
                ProviderPublicationGuard.Stage(db, material.Binding, actor, componentId: recordId, approvalId: approval.Id);
            }
            await db.SaveChangesAsync(ct);
            foreach (var capability in material.Candidates.Where(x => x.Type == "Capability"))
            {
                var contributors = capability.ContributorIds.Select(contributor =>
                    results.SingleOrDefault(x => x.CandidateId == contributor)?.RecordId ?? contributor).ToArray();
                var capabilityId = capability.CandidateId;
                db.CspInheritedCapabilities.Add(new CspInheritedCapability
                {
                    Id = capabilityId, CspInheritedComponentId = contributors[0], Name = capability.Name, Description = capability.Description,
                    MappedNistControlIds = capability.ControlDuties.Keys.ToList(), Status = CspInheritedCapabilityStatus.NeedsReview,
                    MappedBy = MappedBy.User, CreatedBy = package.CreatedBy, ReviewedBy = actor, ReviewedAt = DateTimeOffset.UtcNow,
                    ReviewerNote = $"Approved package {id:D} preview {approval.Id:D}"
                });
                ProviderPublicationGuard.Stage(db, material.Binding, actor, capabilityId: capabilityId, approvalId: approval.Id);
                await db.SaveChangesAsync(ct);
                var working = await workspace.SaveWorkingRevisionAsync(capabilityId, new SaveWorkingRevisionRequest(
                    1, capability.Classification, capability.ServiceCategory, contributors.Select(x => x.ToString("D")).ToArray(),
                    capability.ControlDuties), actor, ct);
                var preview = await workspace.GeneratePublicationPreviewAsync(capabilityId, working.Revision, ct);
                await workspace.ApproveWorkingRevisionAsync(capabilityId,
                    new ApproveWorkingRevisionRequest(working.Revision, preview.PreviewId, preview.PreviewHash), actor, ct);
                var release = await workspace.PublishAsync(capabilityId, new PublishWorkingRevisionRequest(
                    working.Revision, working.Revision, preview.PreviewId, preview.PreviewHash,
                    $"package-{approval.Id:N}-{capabilityId:N}"), actor, ct);
                results.Add(new(capability.CandidateId, capabilityId, release.ReleaseId, "Capability"));
            }
            foreach (var component in material.Candidates.Where(x => x.Type == "Component" && x.DuplicateResolution != "ReusePublished"))
            {
                var row = await db.CspInheritedComponents.SingleAsync(x => x.Id == component.CandidateId && x.CspProfileId == package.ProviderId, ct);
                row.Status = CspInheritedComponentStatus.Published;
            }
            foreach (var result in results)
            {
                var row = rows.Single(x => x.Id == result.CandidateId);
                row.ReviewState = "Published";
                row.PayloadJson = Json(Candidate(row) with { PublishedRecordId = result.RecordId });
            }
            package.PublicationState = rows.Any(x => x.ReviewState != "Rejected" && x.Type is "Component" or "Capability"
                && !Candidate(x).PublishedRecordId.HasValue) ? "PartiallyPublished" : "Published";
            approval.State = "Published";
            approval.PublishedVersion = package.Version;
            approval.PublicationKey = key;
            var response = new PackagePublicationResponse(id, package.PublicationState, results, false);
            approval.PublicationJson = Json(response);
            if (material.Binding.Reviews.Count != 0)
                Audit(db, package, "OfferingInventoryPublished", actor, Json(new
                {
                    ApprovalId = approval.Id, Records = results,
                    ImpactReviewIds = material.Binding.Reviews.Select(x => x.Id).ToArray(),
                    ContextSnapshotHash = material.Binding.ContextHash
                }));
            // All catalog writes, canonical releases, audit and replay checkpoints commit together.
            // A lost commit response replays this ledger; a crash before commit leaves no visible drafts.
            return response;
        }, ct);
    }

    private sealed class PublicationContextFactory(AtoCopilotContext parent) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext()
        {
            var original = (DbContextOptions<AtoCopilotContext>)parent.GetService<IDbContextOptions>();
            var options = new DbContextOptionsBuilder<AtoCopilotContext>(original);
            if (parent.Database.IsSqlite()) options.UseSqlite(parent.Database.GetDbConnection());
            else options.UseSqlServer(parent.Database.GetDbConnection());
            var context = new AtoCopilotContext(options.Options);
            context.Database.UseTransaction(parent.Database.CurrentTransaction!.GetDbTransaction());
            return context;
        }
        public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
