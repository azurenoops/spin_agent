using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class ProviderSetupModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CspProfile>().Property(x => x.SetupRevision).HasDefaultValue(1L);
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

        var portfolio = modelBuilder.Entity<ServicePortfolio>();
        portfolio.ToTable("ServicePortfolios");
        portfolio.HasAlternateKey(x => new { x.ProviderId, x.Id });
        portfolio.HasIndex(x => new { x.ProviderId, x.Name });
        portfolio.HasOne<CspProfile>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);

        var membership = modelBuilder.Entity<ServicePortfolioOfferingRevision>();
        membership.ToTable("ServicePortfolioOfferingRevisions");
        membership.HasAlternateKey(x => new { x.ProviderId, x.Id });
        membership.HasIndex(x => new { x.ProviderId, x.OfferingId, x.Revision }).IsUnique();
        membership.HasIndex(x => new { x.ProviderId, x.OfferingId })
            .IsUnique().HasFilter("[IsPrimary] = 1 AND [State] = 'Active'");
        membership.HasOne<ServicePortfolio>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.PortfolioId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        membership.HasOne<ProviderOffering>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.OfferingId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        membership.HasOne<ServicePortfolioOfferingRevision>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.PredecessorId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var authorizationIntent = modelBuilder.Entity<ProviderOfferingAuthorizationIntentRevision>();
        authorizationIntent.ToTable("ProviderOfferingAuthorizationIntentRevisions");
        authorizationIntent.HasAlternateKey(x => new { x.ProviderId, x.Id });
        authorizationIntent.HasIndex(x => new { x.ProviderId, x.SetupId, x.Revision }).IsUnique();
        authorizationIntent.HasOne<ProviderSetupDraft>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.SetupId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        authorizationIntent.HasOne<ProviderOffering>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.OfferingId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        authorizationIntent.HasOne<ProviderOfferingAuthorizationIntentRevision>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.PredecessorId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var work = modelBuilder.Entity<ProviderSetupWorkItem>();
        work.ToTable("ProviderSetupWorkItems");
        work.HasIndex(x => new { x.ProviderId, x.IdempotencyKey }).IsUnique();
        work.HasIndex(x => new { x.ProviderId, x.State, x.OwnerRole });
        work.HasOne<ProviderSetupDraft>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.SetupId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        work.HasOne<ServicePortfolio>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.PortfolioId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        work.HasOne<ProviderOffering>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.OfferingId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
