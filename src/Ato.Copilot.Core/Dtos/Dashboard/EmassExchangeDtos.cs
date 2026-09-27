namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record RecordEmassExchangeRequest(
    string PackageId, string PackageHash, DateTimeOffset ExportGeneratedAt,
    string Outcome, string ReceivingWorkflow, string ExternalReference,
    DateTimeOffset OccurredAt, string Notes, string IdempotencyKey,
    long ExpectedVersion, string? SupersedesId = null);

public sealed record EmassExchangeDto(
    string Id, long Version, string PackageId, string PackageHash, DateTimeOffset ExportGeneratedAt,
    string Outcome, string ReceivingWorkflow, string ExternalReference,
    DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, string RecordedBy,
    string Notes, string? SupersedesId);

public sealed record EmassExchangeHistory(
    long Version, bool CanRecord, IReadOnlyList<EmassExchangeDto> Items);

public sealed record EmassExchangeExport(
    string PackageId, string PackageHash, DateTimeOffset ExportGeneratedAt, string Purpose);
