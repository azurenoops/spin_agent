namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>Selected-system provenance on existing import sessions; no separate receipt store.</summary>
public interface ISystemSourceSession
{
    Guid Id { get; set; }
    Guid TenantId { get; set; }
    string OriginalFileName { get; set; }
    string StorageBlobKey { get; set; }
    string ContentChecksumSha256 { get; set; }
    long FileSizeBytes { get; set; }
    string? TargetSystemId { get; set; }
    string? RequestKey { get; set; }
    string? RequestPayloadHash { get; set; }
    Guid? RequestActorPersonId { get; set; }
    long ReviewRevision { get; set; }
    string? ReviewProposalJson { get; set; }
    string? ReviewSnapshotJson { get; set; }
    string? ApplyReceiptJson { get; set; }
    DateTimeOffset CreatedAt { get; set; }
    Guid CreatedBy { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    Guid UpdatedBy { get; set; }
}
