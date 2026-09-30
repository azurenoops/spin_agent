using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.ProviderAuthorizations;

[ProviderScoped]
public sealed class ProviderMonitoringRule : ProviderOwnedRow
{
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(40)] public string Signal { get; set; } = "";
    public Guid SourceId { get; set; }
    [MaxLength(4000)] public string ConditionJson { get; set; } = "";
    [MaxLength(254)] public string OwnerId { get; set; } = "";
    [MaxLength(40)] public string Response { get; set; } = "CreateProviderImpactReview";
    public int CadenceMinutes { get; set; } = 60;
    public bool IsEnabled { get; set; } = true;
    public string BaselineJson { get; set; } = "{}";
    [MaxLength(64)] public string BaselineHash { get; set; } = "";
    public long NextEvaluationUtcTicks { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    [MaxLength(254)] public string UpdatedBy { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderMonitoringEvaluation : ProviderOwnedRow
{
    public Guid RuleId { get; set; }
    public long RuleRevision { get; set; }
    [MaxLength(64)] public string ObservationHash { get; set; } = "";
    [MaxLength(32)] public string Outcome { get; set; } = "";
    [MaxLength(32)] public string CollectionHealth { get; set; } = "";
    public string RuleSnapshotJson { get; set; } = "{}";
    public string SourceSnapshotJson { get; set; } = "{}";
    public Guid? ImpactReviewId { get; set; }
}
