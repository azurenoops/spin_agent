namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record SystemSourceField(string Field, string SourceField, string? ProposedValue, bool Supported);
public sealed record SystemSourceAnalysis(string State, string? Error, IReadOnlyList<SystemSourceField> Fields);
public sealed record SystemSourceReceipt(Guid SessionId, string Kind, string SystemId, string FileName, string Sha256,
    long SourceRevision, string ReceiptState, string AnalysisState, string ReviewState, string State,
    IReadOnlyList<SystemSourceField> Fields, string? Error);
public sealed record SystemSourcePreviewRequest(long ExpectedSourceRevision);
public sealed record SystemSourcePreviewField(string Field, string? CurrentValue, string? ProposedValue, bool Supported);
public sealed record SystemSourcePreview(Guid SessionId, string SystemId, long SourceRevision, string SourceHash,
    string IdentityRevision, string PreviewHash, IReadOnlyList<SystemSourcePreviewField> Fields);
public sealed record SystemSourceDecision(string Field, string Decision);
public sealed record SystemSourceApplyRequest(long ExpectedSourceRevision, string ExpectedSystemRevision,
    string PreviewHash, IReadOnlyList<SystemSourceDecision> Decisions);
public sealed record SystemSourceApplyReceipt(string Key, string IntentHash, Guid ActorPersonId, DateTimeOffset ReviewedAt,
    string SystemId, long SourceRevision, string SourceHash, string BeforeName, string? BeforeAcronym,
    string AfterName, string? AfterAcronym);
