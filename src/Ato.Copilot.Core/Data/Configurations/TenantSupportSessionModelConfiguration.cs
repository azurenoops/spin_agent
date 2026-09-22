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
            entity.HasIndex(e => new { e.DirectoryTenantId, e.ObjectId, e.ExpiresAt });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TargetTenantId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
