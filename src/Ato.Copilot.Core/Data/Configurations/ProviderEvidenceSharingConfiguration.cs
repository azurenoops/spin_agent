using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class ProviderEvidenceSharingConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var share = modelBuilder.Entity<ProviderEvidenceShare>();
        share.HasIndex(x => new { x.ProviderId, x.OfferingId, x.EvidenceId, x.AssignmentId, x.Version }).IsUnique();
        share.HasIndex(x => new { x.TargetTenantId, x.SystemId });
        share.HasOne<ProviderFindingEvidence>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.OfferingId, x.EvidenceId })
            .HasPrincipalKey(x => new { x.ProviderId, x.OfferingId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        share.HasOne<ProviderHostingAssignment>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.OfferingId, x.TargetTenantId, x.SystemId, x.AssignmentId })
            .HasPrincipalKey(x => new { x.ProviderId, x.OfferingId, x.TargetTenantId, x.SystemId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        share.HasOne<ProviderEvidenceShare>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.OfferingId, x.PreviousVersionId })
            .HasPrincipalKey(x => new { x.ProviderId, x.OfferingId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
