using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class SystemSetupModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        var entity = builder.Entity<RegisteredSystem>();
        entity.Property(x => x.SetupRevision).HasDefaultValue(0L).IsConcurrencyToken();
        entity.HasIndex(x => new { x.TenantId, x.SetupActorPersonId, x.SetupRequestKey })
            .IsUnique().HasDatabaseName("UX_RegisteredSystem_SetupRequest")
            .HasFilter("[SetupRequestKey] IS NOT NULL");
        ConfigureSource<EmassImportSession>(builder);
        ConfigureSource<SspPdfImportSession>(builder);
    }

    private static void ConfigureSource<T>(ModelBuilder builder) where T : class, ISystemSourceSession
    {
        var entity = builder.Entity<T>();
        entity.Property(x => x.TargetSystemId).HasMaxLength(36);
        entity.Property(x => x.RequestKey).HasMaxLength(100);
        entity.Property(x => x.RequestPayloadHash).HasMaxLength(64);
        entity.Property(x => x.ReviewRevision).HasDefaultValue(0L).IsConcurrencyToken();
        entity.HasIndex(x => new { x.TenantId, x.TargetSystemId, x.RequestActorPersonId, x.RequestKey })
            .IsUnique().HasDatabaseName($"UX_{typeof(T).Name}_SystemSourceRequest")
            .HasFilter("[RequestKey] IS NOT NULL");
    }
}
