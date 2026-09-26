namespace Ato.Copilot.Core.Models.PackageImports;

/// <summary>Unconfirmed source assertions, never persisted authority or workflow state.</summary>
public sealed record CspPackageClaim
{
    public CspAuthorizationDecisionClaim? AuthorizationDecision { get; init; }
    public CspBoundaryClaim? Boundary { get; init; }
    public CspAssessmentFindingClaim? AssessmentFinding { get; init; }
    public CspPoamItemClaim? PoamItem { get; init; }
    public IReadOnlyList<CspClaimFieldSource> FieldSources { get; init; } = [];
    public IReadOnlyList<CspClaimRelationship> Relationships { get; init; } = [];
    public IReadOnlyList<string> SourceAliases { get; init; } = [];
    public IReadOnlyList<string> Qualifications { get; init; } = [];
}

public sealed record CspAuthorizationDecisionClaim
{
    public string SubjectKind { get; init; } = "Unspecified";
    public string? Subject { get; init; }
    public string? Reference { get; init; }
    public string? Authority { get; init; }
    public string? DecisionType { get; init; }
    public string? StatusAsStated { get; init; }
    public string? DecisionDate { get; init; }
    public string? ExpirationDate { get; init; }
    public string? Scope { get; init; }
    public IReadOnlyList<string> Conditions { get; init; } = [];
    public IReadOnlyList<string> Exclusions { get; init; } = [];
}

public sealed record CspBoundaryClaim
{
    public string? Subject { get; init; }
    public string Relationship { get; init; } = "Undetermined";
    public string? Scope { get; init; }
    public string? Environment { get; init; }
    public IReadOnlyList<string> ResourceIds { get; init; } = [];
    public IReadOnlyList<string> Responsibilities { get; init; } = [];
    public string? DecisionReference { get; init; }
}

public sealed record CspAssessmentFindingClaim
{
    public string? SourceFindingId { get; init; }
    public string? Observation { get; init; }
    public string? SeverityAsStated { get; init; }
    public string? StatusAsStated { get; init; }
    public string? Assessor { get; init; }
    public string? AssessmentDate { get; init; }
    public IReadOnlyList<string> ControlIds { get; init; } = [];
    public IReadOnlyList<string> EvidenceReferences { get; init; } = [];
}

public sealed record CspPoamItemClaim
{
    public string? SourcePoamId { get; init; }
    public string? CorrectiveAction { get; init; }
    public string? OwnerAsStated { get; init; }
    public string? StatusAsStated { get; init; }
    public IReadOnlyList<CspClaimMilestone> Milestones { get; init; } = [];
    public IReadOnlyList<string> RequiredClosureEvidence { get; init; } = [];
    public IReadOnlyList<string> SubmittedEvidenceReferences { get; init; } = [];
}

public sealed record CspClaimMilestone(string? Description, string? DueDate);
public sealed record CspClaimFieldSource(string Field, IReadOnlyList<int> CitationIndexes);
public sealed record CspClaimRelationship(
    string Kind, string TargetSourceId, string? TargetEntryId = null, string Resolution = "Unresolved");

public enum CspPackageClaimFamily { Inventory, AuthorizationDecision, Boundary, AssessmentFinding, PoamItem }
public enum CspPackageFamilyCoverageStatus { Analyzed, NoDeclarations, Unsupported, Unavailable, Failed }
public sealed record CspPackageFamilyCoverage(
    CspPackageClaimFamily Family, CspPackageFamilyCoverageStatus Status, string? Reason = null);
