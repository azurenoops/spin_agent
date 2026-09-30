namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record WorkingDocumentSourceGap(string Code, string Message);
public sealed record WorkingDocumentSourceRecord(string Kind, string RecordId, string? VersionId, string ContentHash);

public sealed record WorkingDocumentPreviewDto(
    string SystemId, string SystemName, string DocumentType, bool Available,
    string Format, string ContentType, string Content, string ContentHash,
    DateTimeOffset GeneratedAt, IReadOnlyList<WorkingDocumentSourceGap> SourceGaps,
    string DocumentStatus, IReadOnlyList<WorkingDocumentSourceRecord> SourceRecords)
{
    public bool IsPreview => true;
    public string SourceState => "CurrentWorkingData";
    public bool CanGenerate => false;
}

public sealed record MissingWorkingDocumentPreviewDto(
    string SystemId, string SystemName, string DocumentType, bool Available, string ReasonCode, string Message);
