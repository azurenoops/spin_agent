using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

/// <summary>Production model registration; call before the context installs tenant filters.</summary>
public static class CapabilityResponsibilityModelConfiguration
{
    public static void ConfigureCapabilityResponsibilities(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CapabilitySubscription>().HasIndex(e => new { e.RoutingCapabilityId, e.IsActive, e.Id })
            .HasDatabaseName("IX_CapabilitySubscription_Routing");
        modelBuilder.Entity<CspResponsibilitySourceEvent>(entity =>
        {
            entity.ToTable("CspResponsibilitySourceEvents");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.CapabilityId, e.Sequence }).IsUnique();
            entity.Property(e => e.ExpansionRevision).IsConcurrencyToken();
            entity.HasIndex(e => new { e.FanoutCompleted, e.NextExpansionUtcTicks, e.Id });
        });
        modelBuilder.Entity<CapabilityResponsibilityDelivery>(entity =>
        {
            entity.ToTable("CapabilityResponsibilityDeliveries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Revision).IsConcurrencyToken();
            entity.HasIndex(e => new { e.CompletedAt, e.NextAttemptUtcTicks, e.LeaseUntilUtcTicks, e.Id });
            entity.HasIndex(e => new { e.TenantId, e.RegisteredSystemId, e.CompletedAt });
            entity.HasIndex(e => e.ImpactId).IsUnique().HasFilter("[ImpactId] IS NOT NULL");
        });
        modelBuilder.Entity<CapabilityResponsibilityConfirmation>(entity =>
        {
            entity.ToTable("CapabilityResponsibilityConfirmations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InheritanceType).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.TenantId, e.RegisteredSystemId, e.SubscriptionId, e.ControlId })
                .IsUnique().HasFilter("[IsCurrent] = 1");
            entity.HasOne<RegisteredSystem>().WithMany().HasForeignKey(e => e.RegisteredSystemId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CapabilityResponsibilityProjection>(entity =>
        {
            entity.ToTable("CapabilityResponsibilityProjections");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Revision).IsConcurrencyToken();
            entity.HasIndex(e => new { e.TenantId, e.RegisteredSystemId, e.ControlBaselineId, e.ControlId }).IsUnique();
            entity.HasOne<RegisteredSystem>().WithMany().HasForeignKey(e => e.RegisteredSystemId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CapabilityResponsibilityImpact>(entity =>
        {
            entity.ToTable("CapabilityResponsibilityImpacts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ProjectionId, e.Revision }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.RegisteredSystemId, e.AcknowledgedAt });
            entity.HasOne<RegisteredSystem>().WithMany().HasForeignKey(e => e.RegisteredSystemId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
