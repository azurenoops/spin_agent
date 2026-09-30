using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class OrganizationOnboardingModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganizationOnboardingDraft>(entity =>
        {
            entity.ToTable("OrganizationOnboardingDrafts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.CreationKey).HasMaxLength(100);
            entity.Property(x => x.ProvisioningKey).HasMaxLength(100);
            entity.Property(x => x.CurrentStep).HasMaxLength(32);
            entity.Property(x => x.State).HasMaxLength(24);
            entity.Property(x => x.ConfirmedIntentHash).HasMaxLength(64);
            entity.Property(x => x.CreatedBy).HasMaxLength(200);
            entity.Property(x => x.UpdatedBy).HasMaxLength(200);
            entity.HasIndex(x => new { x.ProviderId, x.CreationKey }).IsUnique();
            entity.HasIndex(x => new { x.ProviderId, x.State, x.UpdatedAtTicks, x.Id });
            entity.HasOne<CspProfile>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Tenant>().Property(x => x.OnboardingDraftRevision).IsConcurrencyToken();
    }
}
