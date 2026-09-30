using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class ScopedMonitoringModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ScopedMonitoringObservation>(e =>
        {
            e.ToTable("ScopedMonitoringObservations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.RegisteredSystemId, x.SourceId, x.Fingerprint }).IsUnique();
        });
        model.Entity<AlertRule>(e =>
        {
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.RegisteredSystemId).HasMaxLength(36);
            e.Property(x => x.BoundaryDefinitionId).HasMaxLength(36);
            e.Property(x => x.OwnerId).HasMaxLength(200);
            e.Property(x => x.LastModifiedBy).HasMaxLength(200);
            e.Property(x => x.Signal).HasMaxLength(32);
            e.Property(x => x.Response).HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.RegisteredSystemId, x.IsEnabled });
        });
        model.Entity<MonitoringRuleEvaluation>(e =>
        {
            e.ToTable("MonitoringRuleEvaluations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.RuleId, x.RuleVersion, x.InputFingerprint }).IsUnique();
        });
        model.Entity<MonitoringImpactReview>(e =>
        {
            e.ToTable("MonitoringImpactReviews");
            e.HasKey(x => x.Id);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.EvaluationId }).IsUnique();
        });
    }
}
