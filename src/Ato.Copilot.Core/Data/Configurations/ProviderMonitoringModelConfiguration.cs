using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class ProviderMonitoringModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        // ProviderAuthorizationModelConfiguration maps the ProviderOwnedRow base columns and ownership FKs.
        model.Entity<ProviderMonitoringRule>().HasIndex(x => new { x.ProviderId, x.IsEnabled, x.NextEvaluationUtcTicks });
        model.Entity<ProviderMonitoringEvaluation>()
            .HasIndex(x => new { x.ProviderId, x.OfferingId, x.RuleId, x.RuleRevision, x.ObservationHash }).IsUnique();
        model.Entity<ProviderMonitoringEvaluation>().HasOne<ProviderMonitoringRule>().WithMany()
            .HasForeignKey(x => new { x.ProviderId, x.OfferingId, x.RuleId })
            .HasPrincipalKey(x => new { x.ProviderId, x.OfferingId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
