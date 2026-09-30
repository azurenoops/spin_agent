using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.PackageImports;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class ProviderSetupModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var draft = modelBuilder.Entity<ProviderSetupDraft>();
        draft.ToTable("ProviderSetupDrafts");
        draft.HasIndex(x => x.ProviderId).IsUnique();
        draft.HasAlternateKey(x => new { x.ProviderId, x.Id });
        draft.HasOne<CspProfile>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
        var command = modelBuilder.Entity<ProviderSetupCommand>();
        command.ToTable("ProviderSetupCommands");
        command.HasIndex(x => new { x.ProviderId, x.Operation, x.IdempotencyKey }).IsUnique();
        command.HasOne<ProviderSetupDraft>().WithMany().HasForeignKey(x => new { x.ProviderId, x.DraftId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var intent = modelBuilder.Entity<CspPackageUploadIntent>();
        intent.ToTable("CspPackageUploadIntents");
        intent.HasOne<CspProfile>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
        intent.HasIndex(x => new { x.ProviderId, x.IdempotencyKey }).IsUnique();
        intent.HasIndex(x => new { x.ProviderId, x.EntryPoint, x.OfferingHintId, x.Id });
        modelBuilder.Entity<CspPackage>().HasIndex(x => x.UploadIntentId).IsUnique()
            .HasFilter("[UploadIntentId] IS NOT NULL");
        intent.HasOne<ProviderSetupDraft>().WithMany().HasForeignKey(x => new { x.ProviderId, x.DraftId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
