using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record PackageReadinessSelection(PackagePurpose Purpose = PackagePurpose.Legacy,
    RetainedPackageSelection? RetainedContext = null);
public sealed record PackageReadinessAction(bool CanView, bool CanEdit, string? Path, string? Label, string? Reason);
public sealed record PackageReadinessSource(string Kind, string RecordId, string? Revision, string? ContentHash, string Label);
public sealed record PackageReadinessOwner(string PersonId, string DisplayName, string Role, string AssignmentId, string Scope);
public sealed record PackageReadinessCheck(
    string Id, string RuleId, string Title, string Outcome, string Category, bool Required,
    string Applicability, string Why, string? MissingSource, IReadOnlyList<PackageReadinessSource> Sources,
    IReadOnlyList<string> NextSteps, string? ExpectedRole, PackageReadinessOwner? RecordedOwner, PackageReadinessAction Action);
public sealed record PackageReadinessCounts(int Total, int Passed, int Blocking, int FollowUp,
    int NotApplicable, int Unavailable, int RequiredUnavailable)
{
    public static PackageReadinessCounts From(IReadOnlyCollection<PackageReadinessCheck> checks) =>
        new(checks.Count, checks.Count(x => x.Outcome == "Passed"), checks.Count(x => x.Outcome == "Blocking"),
            checks.Count(x => x.Outcome == "FollowUp"), checks.Count(x => x.Outcome == "NotApplicable"),
            checks.Count(x => x.Outcome == "Unavailable"), checks.Count(x => x.Outcome == "Unavailable" && x.Required));
}
public sealed record PackageReadinessFailure(string Code, string Message);
public sealed record PackageReadinessFreshness(string State, DateTime CheckedAt, string? CurrentSourceHash, string? Reason);
public sealed record PackageReadinessRunDto(string Id, string Outcome, DateTime StartedAt, DateTime EvaluatedAt,
    string EvaluatedBy, string? SourceHash, string? SourceHashAfter, string RuleVersion,
    PackageReadinessCounts Counts, string? RecommendedCheckId, PackageReadinessFailure? Failure, PackageReadinessFreshness Freshness);
public sealed record PackageReadinessPage<T>(IReadOnlyList<T> Items, int TotalCount, int Limit, int Offset);
public sealed record PackageReadinessRecord(string Kind, string Id, string? Status, DateTime? RecordedAt,
    PackagePurpose? Purpose, string? SourceHash, string SourceRelationship, PackageReadinessAction Action);
public sealed record PackageReadinessProgress(string Id, string State, string Description,
    IReadOnlyList<PackageReadinessRecord> Records, int TotalCount, PackageReadinessAction Action);
public sealed record PackageReadinessDocument(string Kind, string Title, string Presence, string? Status,
    string? ReviewState, string? SourceState, string? ValidationOutcome, int? RecordCount,
    IReadOnlyList<PackageReadinessRecord> Records, PackageReadinessAction Action);
public sealed record PackageReadinessTransition(string Id, string FromPhase, string ToPhase, DateTime OccurredAt, string Actor);
public sealed record PackageReadinessRmf(string Phase, IReadOnlyList<PackageReadinessTransition> Transitions, int TotalCount);
