using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>Immutable collection identity; reconciliation never replaces this original pin.</summary>
public sealed record AssessmentPlanPin(string Id, long Revision, string Hash, string Title,
    string Status, string[] IncludedControlIds, string[] ExcludedControlIds);

public sealed record ResultReview(string ControlId, string Determination, string Method,
    string Notes, string[] EvidenceIds, string? CatSeverity, string Actor, DateTime At,
    string SourceRevision, string SnapshotId, ResultEvidenceSnapshot[]? Evidence = null, AssessmentPlanPin? ReviewPlan = null);
public sealed record ResultEvidenceSnapshot(string Id, string Name, string ContentHash);

public sealed record ResultReconciliation(AssessmentPlanPin Plan, string Actor, DateTime At);

/// <summary>Retained on the existing assessment or import, never in a parallel run store.</summary>
public sealed class AssessmentResultProvenance
{
    public AssessmentPlanPin? Plan { get; set; }
    public string? RequestIntentHash { get; set; }
    public string? ExecutionToken { get; set; }
    public DateTime? ExecutionLeaseExpiresAt { get; set; }
    public List<ResultReview> Reviews { get; set; } = [];
    public List<ResultReconciliation> Reconciliations { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public string[]? ObservedControlIds { get; set; }
    public string[] EvidenceIds { get; set; } = [];
    public string? SourceResultId { get; set; }
    public string? SystemId { get; set; }
    public List<ResultFindingSnapshot>? Findings { get; set; }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static AssessmentPlanPin Pin(SecurityAssessmentPlan plan) => new(plan.Id, plan.Revision,
        Hash(plan.Content), plan.Title, plan.Status.ToString(),
        plan.ControlEntries.Where(x => !x.IsExcluded).Select(x => x.ControlId).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
        plan.ControlEntries.Where(x => x.IsExcluded).Select(x => x.ControlId).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray());
    public static AssessmentResultProvenance Read(string? json) => json is null ? new() :
        JsonSerializer.Deserialize<AssessmentResultProvenance>(json) ?? throw new InvalidOperationException("Invalid retained result provenance.");
    public string Serialize() => JsonSerializer.Serialize(this);
}

public sealed record ResultFindingSnapshot(string FindingId, string? ControlId, string ControlFamily,
    string Title, string Description, string Severity, string Status, string? ResourceType,
    string? ResourceId, string? RemediationGuidance, DateTime DiscoveredAt, string? DeviationId,
    string? DeviationType)
{
    public static ResultFindingSnapshot From(ComplianceFinding f) => new(f.Id, f.ControlId,
        f.ControlFamily, f.Title, f.Description, f.Severity.ToString(), f.Status.ToString(),
        f.ResourceType, f.ResourceId, f.RemediationGuidance, f.DiscoveredAt, f.DeviationId, null);
}

public sealed record RetainedSarSource(string Id, string Revision, AssessmentPlanPin? OriginalPlan,
    string[] ObservedControls, ResultReview[] Reviews, ResultFindingSnapshot[] Findings,
    string[] EvidenceIds, string[] Warnings, ResultEvidenceSnapshot[]? Evidence = null);

public sealed record ScopedSarInput(string OperationKey, string IntentHash, string Title,
    AssessmentPlanPin? Plan, RetainedSarSource[] Sources, string[] Warnings);

/// <summary>Current per-source reviews, with unresolved cross-source conflicts excluded from assessed coverage.</summary>
public sealed record SelectedAssessmentReviewSummary(ResultReview[] Reviews, string[] Warnings)
{
    public static SelectedAssessmentReviewSummary Build(
        IEnumerable<(string Id, IEnumerable<string> Observed, IEnumerable<ResultReview> Reviews)> sourceValues,
        string[]? includedControlIds)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        bool InScope(string controlId) => includedControlIds is null || includedControlIds.Contains(controlId, comparer);
        var sources = sourceValues.GroupBy(x => x.Id, StringComparer.Ordinal).Select(x => x.First()).ToArray();
        var warnings = new List<string>();
        var duplicates = sources.SelectMany(s => s.Observed.Where(InScope).Distinct(comparer))
            .GroupBy(x => x, comparer).Where(g => g.Count() > 1).Select(g => g.Key).Order(comparer).ToArray();
        if (duplicates.Length > 0)
            warnings.Add($"Duplicate observations across selected results for controls: {string.Join(", ", duplicates)}. Coverage counts each control once.");

        var current = sources.SelectMany(s => s.Reviews.Where(r => InScope(r.ControlId))
            .GroupBy(r => r.ControlId, comparer)
            .Select(g => new { SourceId = s.Id, Review = g.OrderBy(r => r.At).Last() }));
        var resolved = new List<ResultReview>();
        foreach (var control in current.GroupBy(x => x.Review.ControlId, comparer).OrderBy(g => g.Key, comparer))
        {
            if (control.Select(x => x.Review.Determination).Distinct(comparer).Count() > 1)
            {
                var provenance = string.Join("; ", control.OrderBy(x => x.SourceId, StringComparer.Ordinal)
                    .Select(x => $"{x.SourceId} = {x.Review.Determination}"));
                warnings.Add($"Conflicting current determinations for {control.Key}: {provenance}. This control remains pending.");
            }
            else resolved.Add(control.OrderBy(x => x.Review.At).Last().Review);
        }
        return new(resolved.ToArray(), warnings.ToArray());
    }
}
