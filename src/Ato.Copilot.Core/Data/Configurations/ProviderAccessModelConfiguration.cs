using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class ProviderAccessModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var principal = model.Entity<ProviderPrincipal>();
        principal.ToTable("ProviderPrincipals");
        principal.HasAlternateKey(x => new { x.ProviderId, x.Id });
        principal.HasOne<CspProfile>().WithMany().HasForeignKey(x => x.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        var match = model.Entity<ProviderDirectoryMatch>();
        match.ToTable("ProviderDirectoryMatches");
        match.HasAlternateKey(x => new { x.ProviderId, x.Id });
        match.HasIndex(x => new { x.DirectoryTenantId, x.ObjectId }).IsUnique()
            .HasFilter("[State] = 'Verified'");
        match.HasOne<ProviderPrincipal>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.PrincipalId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var membership = model.Entity<ProviderMembership>();
        membership.ToTable("ProviderMemberships");
        membership.HasAlternateKey(x => new { x.ProviderId, x.Id });
        membership.HasIndex(x => new { x.ProviderId, x.DirectoryMatchId })
            .IsUnique().HasFilter("[State] = 'Active' AND [RevokedAt] IS NULL");
        membership.HasOne<ProviderPrincipal>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.PrincipalId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        membership.HasOne<ProviderDirectoryMatch>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.DirectoryMatchId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var role = model.Entity<ProviderRoleAssignment>();
        role.ToTable("ProviderRoleAssignments");
        role.HasIndex(x => new { x.ProviderId, x.MembershipId, x.Role, x.PortfolioId, x.OfferingId })
            .IsUnique().HasFilter("[RemovedAt] IS NULL");
        role.HasOne<ProviderMembership>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.MembershipId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        role.HasOne<ServicePortfolio>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.PortfolioId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        role.HasOne<ProviderOffering>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.OfferingId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);

        var invitation = model.Entity<ProviderInvitation>();
        invitation.ToTable("ProviderInvitations");
        invitation.HasIndex(x => x.TokenHash).IsUnique();
        invitation.HasIndex(x => new { x.ProviderId, x.Status, x.ExpiresAt });
        invitation.HasOne<CspProfile>().WithMany().HasForeignKey(x => x.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        var request = model.Entity<ProviderAccessRequest>();
        request.ToTable("ProviderAccessRequests");
        request.HasIndex(x => new { x.DirectoryTenantId, x.ObjectId })
            .IsUnique().HasFilter("[Status] = 'Pending'");
        request.HasIndex(x => new { x.ProviderId, x.Status, x.CreatedAt });
        request.HasOne<CspProfile>().WithMany().HasForeignKey(x => x.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        var contact = model.Entity<ProviderContact>();
        contact.ToTable("ProviderContacts");
        contact.HasIndex(x => new { x.ProviderId, x.Category, x.State });
        contact.HasOne<CspProfile>().WithMany().HasForeignKey(x => x.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);
        contact.HasOne<ProviderPrincipal>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.PrincipalId })
            .HasPrincipalKey(x => new { x.ProviderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
