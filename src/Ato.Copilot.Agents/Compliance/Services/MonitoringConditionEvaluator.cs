using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Monitoring;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Compatibility entry point; provider and system rules execute the same Core evaluator.</summary>
public static class MonitoringConditionEvaluator
{
    public static MonitoringCondition Parse(string json) => Core.Services.Monitoring.MonitoringConditionEvaluator.Parse(json);
    public static bool Matches(string? json, ComplianceAlert alert) => Core.Services.Monitoring.MonitoringConditionEvaluator.Matches(json, alert);
}
