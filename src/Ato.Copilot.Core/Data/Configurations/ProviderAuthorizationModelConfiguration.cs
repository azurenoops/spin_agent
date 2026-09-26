using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

/// <summary>Independent provider aggregates with restrictive, ownership-qualified references.</summary>
public static class ProviderAuthorizationModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var types = typeof(ProviderOwnedRow).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ProviderOwnedRow)) && !t.IsAbstract);
        foreach (var type in types)
        {
            var entity = modelBuilder.Entity(type);
            entity.HasBaseType((Type?)null);
            entity.ToTable(type.Name + "s");
            entity.HasKey(nameof(ProviderOwnedRow.Id));
            entity.Property(nameof(ProviderOwnedRow.Revision)).IsConcurrencyToken();
            entity.HasAlternateKey("ProviderId", "OfferingId", "Id");
            entity.HasIndex("ProviderId", "OfferingId");
            entity.HasOne(typeof(CspProfile)).WithMany().HasForeignKey("ProviderId")
                .OnDelete(DeleteBehavior.Restrict);
            if (type != typeof(ProviderOffering))
                entity.HasOne(typeof(ProviderOffering)).WithMany().HasForeignKey("ProviderId", "OfferingId")
                    .HasPrincipalKey("ProviderId", "Id").OnDelete(DeleteBehavior.Restrict);
        }

        modelBuilder.Entity<ProviderBoundaryRevision>()
            .HasIndex(x => new { x.ProviderId, x.OfferingId, x.Revision }).IsUnique();
        modelBuilder.Entity<ProviderHostingScopeRevision>()
            .HasIndex(x => new { x.ProviderId, x.OfferingId, x.Revision }).IsUnique();
        modelBuilder.Entity<ProviderAuthorizationRevision>()
            .HasIndex(x => new { x.ProviderId, x.OfferingId, x.RecordId, x.Revision }).IsUnique();
        modelBuilder.Entity<ProviderPackageVersion>()
            .HasIndex(x => new { x.ProviderId, x.OfferingId, x.SeriesId, x.Version }).IsUnique();
        modelBuilder.Entity<ProviderPackageVersion>().HasIndex(x => x.PackageId).IsUnique();
        modelBuilder.Entity<ProviderAuthorizationOperation>()
            .HasIndex(x => new { x.ProviderId, x.OperationScope, x.IdempotencyKey }).IsUnique();
        modelBuilder.Entity<ProviderAuthorizationImpactReview>().HasIndex(x => x.PreviewId).IsUnique();
        modelBuilder.Entity<ProviderClaimReview>()
            .HasIndex(x => new { x.ProviderId, x.OfferingId, x.CandidateId, x.CandidateRevision });
        modelBuilder.Entity<ProviderPackageEnrichment>()
            .HasIndex(x => new { x.ProviderId, x.OfferingId, x.PackageId, x.TargetProfileVersion });

        Reference<ProviderBoundaryRevision, ProviderBoundaryRevision>("PredecessorId");
        Reference<ProviderHostingScopeRevision, ProviderHostingScopeRevision>("PredecessorId");
        Reference<ProviderAuthorizationRevision, ProviderAuthorizationRecord>("RecordId");
        Reference<ProviderAuthorizationRevision, ProviderBoundaryRevision>("BoundaryRevisionId");
        Reference<ProviderAuthorizationLifecycleEvent, ProviderAuthorizationRecord>("RecordId");
        Reference<ProviderAuthorizationLifecycleEvent, ProviderAuthorizationRevision>("AuthorizationRevisionId");
        Reference<ProviderAuthorizationLifecycleEvent, ProviderAuthorizationRevision>("ReplacementRevisionId");
        Reference<ProviderPackageVersion, ProviderBoundaryRevision>("BoundaryRevisionId");
        Reference<ProviderPackageVersion, ProviderPackageVersion>("PreviousVersionId");
        Reference<ProviderCatalogContextSnapshot, ProviderAuthorizationImpactReview>("ImpactReviewId");
        Reference<ProviderHostingAssignment, ProviderHostingScopeRevision>("HostingScopeRevisionId");
        Reference<MissionProviderRelationshipReview, ProviderAuthorizationRevision>("AuthorizationRevisionId");
        Reference<MissionProviderRelationshipReview, ProviderBoundaryRevision>("BoundaryRevisionId");
        Reference<CapabilityAdoptionSnapshot, ProviderCatalogContextSnapshot>("ContextSnapshotId");
        Reference<ProviderFindingEvidence, ProviderFinding>("FindingId");
        Reference<ProviderFindingReview, ProviderFinding>("FindingId");

        // Current pointers are transactionally checked by the service: enforcing both directions
        // would make the non-null authorization record/revision cycle impossible to insert.
        foreach (var type in new[] { typeof(ProviderPackageVersion), typeof(ProviderClaimReview), typeof(ProviderPackageEnrichment) })
            modelBuilder.Entity(type).HasOne(typeof(CspPackage)).WithMany().HasForeignKey("PackageId")
                .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProviderClaimReview>().HasOne<CspPackageCandidate>().WithMany()
            .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProviderHostingAssignment>().HasOne<Tenant>().WithMany()
            .HasForeignKey(x => x.TargetTenantId).OnDelete(DeleteBehavior.Restrict);
        foreach (var type in new[] { typeof(MissionProviderRelationshipReview), typeof(CapabilityAdoptionSnapshot) })
        {
            var entity = modelBuilder.Entity(type);
            entity.HasIndex("TenantId", "SystemId");
            entity.HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(typeof(ProviderHostingAssignment)).WithMany()
                .HasForeignKey("ProviderId", "OfferingId", "TenantId", "SystemId", "AssignmentId")
                .HasPrincipalKey("ProviderId", "OfferingId", "TargetTenantId", "SystemId", "Id")
                .OnDelete(DeleteBehavior.Restrict);
        }

        void Reference<TDependent, TPrincipal>(string property)
            where TDependent : ProviderOwnedRow
            where TPrincipal : ProviderOwnedRow =>
            modelBuilder.Entity<TDependent>().HasOne<TPrincipal>().WithMany()
                .HasForeignKey("ProviderId", "OfferingId", property)
                .HasPrincipalKey("ProviderId", "OfferingId", "Id").OnDelete(DeleteBehavior.Restrict);
    }
}
