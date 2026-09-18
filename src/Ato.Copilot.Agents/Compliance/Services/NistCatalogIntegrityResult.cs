namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Describes schema and baseline validation of a NIST OSCAL catalog.</summary>
public sealed record NistCatalogIntegrityResult
{
    public bool IsValid => SchemaValid && Violations.Count == 0;
    public bool SchemaValid { get; init; }
    public string OscalVersion { get; init; } = string.Empty;
    public string CatalogVersion { get; init; } = string.Empty;
    public int GroupCount { get; init; }
    public int BaseControlCount { get; init; }
    public int EnhancementCount { get; init; }
    public int TotalControlCount => BaseControlCount + EnhancementCount;
    public IReadOnlyList<string> Violations { get; init; } = [];
}