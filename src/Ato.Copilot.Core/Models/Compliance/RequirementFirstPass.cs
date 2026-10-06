namespace Ato.Copilot.Core.Models.Compliance;

public sealed record RequirementDraftSource(string Id, string Kind, string Title, string Version,
    string ContentHash, string ReviewState, string Content, IReadOnlyList<string> RecordedValues);
public sealed record RequirementFirstPassContext(string ControlId, string Title, string Kind,
    IReadOnlyList<CatalogRequirement> Requirements, IReadOnlyList<CatalogParameter> Parameters,
    IReadOnlyList<RequirementDraftSource> Sources);
public sealed record RequirementResponseDraft(string StatementId, string Response, IReadOnlyList<string> SourceIds, string Explanation);
public sealed record RequirementParameterDraft(string ParameterId, string Value, IReadOnlyList<string> SourceIds, string Explanation);
public sealed record RequirementFirstPassSuggestion(IReadOnlyList<RequirementResponseDraft> Responses,
    IReadOnlyList<RequirementParameterDraft> Parameters, IReadOnlyList<string> Questions, IReadOnlyList<string> Conflicts);
public sealed record RequirementFirstPassSourceReference(string Id, string Kind, string Title, string Version, string ContentHash, string ReviewState);
public sealed record RequirementFirstPassProvenance(string ContextHash, string Kind, DateTimeOffset GeneratedAt,
    IReadOnlyList<RequirementFirstPassSourceReference> Sources);
