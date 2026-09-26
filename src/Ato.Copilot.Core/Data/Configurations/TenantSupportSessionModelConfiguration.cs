using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

/// <summary>Register before tenant filters; support authorization is evaluated before tenant binding.</summary>
public static class TenantSupportSessionModelConfiguration
{
    public static void ConfigureTenantSupportSessions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantSupportSession>(entity =>
        {
            entity.ToTable("TenantSupportSessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RevocationReason).HasMaxLength(64);
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Reference).HasMaxLength(100);
            entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.DirectoryTenantId, e.ObjectId, e.ExpiresAt });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TargetTenantId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
