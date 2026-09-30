using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class SystemEnvironmentConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<SystemProviderScopeSelection>().ToTable("SystemProviderScopeSelections")
            .HasIndex(x => new { x.TenantId, x.SystemId, x.AssignmentId }).IsUnique();
        model.Entity<SystemProviderScopeSelection>().HasOne<ProviderHostingAssignment>().WithMany()
            .HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SystemEnvironmentHostingLinkRecord>().ToTable("SystemEnvironmentHostingLinkRecords")
            .HasIndex(x => new { x.TenantId, x.SystemId, x.AttachmentId, x.AssignmentId }).IsUnique();
        model.Entity<SystemEnvironmentHostingLinkRecord>().HasOne<SystemEnvironmentAttachmentRecord>().WithMany()
            .HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SystemEnvironmentHostingLinkRecord>().HasOne<ProviderHostingAssignment>().WithMany()
            .HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SystemEnvironmentWorkspace>().ToTable("SystemEnvironmentWorkspaces")
            .HasIndex(x => new { x.TenantId, x.SystemId }).IsUnique();
        model.Entity<SystemEnvironmentAttachmentRecord>().ToTable("SystemEnvironmentAttachmentRecords")
            .HasIndex(x => new { x.TenantId, x.SystemId, x.RegistrationId }).IsUnique();
        model.Entity<SystemEnvironmentAttachmentRecord>().HasOne<AzureSubscriptionRegistration>().WithMany()
            .HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SystemEnvironmentPendingOperation>().ToTable("SystemEnvironmentPendingOperations")
            .HasIndex(x => new { x.TenantId, x.SystemId });
        model.Entity<SystemEnvironmentReplay>().ToTable("SystemEnvironmentReplays")
            .HasIndex(x => new { x.TenantId, x.SystemId, x.Key }).IsUnique();
        model.Entity<ProviderEnvironmentAllocationRecord>().HasOne<AzureSubscriptionRegistration>().WithMany()
            .HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProviderEnvironmentAllocationRecord>().HasIndex(x => new { x.ConsumerTenantId, x.RegistrationId });
    }
}
