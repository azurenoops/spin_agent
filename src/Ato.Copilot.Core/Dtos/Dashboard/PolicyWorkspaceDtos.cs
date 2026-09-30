using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record PolicySourceDto(string Id, string Name, string? Description, string? SubType,
    string Status, string? Owner, string Revision, string VersionLabel, DateTime? ModifiedAt,
    bool AlreadyLinked, IReadOnlyList<string> RelatedControls);

public sealed record PolicyReferenceDto(string Id, string PolicyId, string Name, string? Rationale,
    string? RetainedVersionLabel, string? SourceStatus, bool SourceChanged, string Retention,
    int Revision, bool CanEdit, bool CanRemove, string? ActionReason);

public sealed record PolicyWorkspacePermissions(bool CanAssign, string? AssignReason,
    bool CanCreateLibrary, string? CreateReason);

public sealed record PolicyWorkspaceDto(string SystemId, string SystemName, IReadOnlyList<PolicyReferenceDto> Items,
    int TotalCount, int UnfilteredTotal, int Page, int PageSize, PolicyWorkspacePermissions Permissions);

public sealed record PolicyLibraryDto(string SystemId, IReadOnlyList<PolicySourceDto> Items,
    int TotalCount, int Page, int PageSize);

public sealed record PolicyReferenceHistoryDto(string Id, string Action, string? Actor, DateTime At, string Description);

public sealed record PolicyReferenceDetailDto(string SystemId, string SystemName, PolicyReferenceDto Reference,
    PolicySourceDto? RetainedSource, PolicySourceDto? CurrentSource, IReadOnlyList<string> RelatedControls,
    string ReviewMessage, IReadOnlyList<PolicyReferenceHistoryDto> History, IReadOnlyList<string> RemovalImpact);

public sealed record CreatePolicySourceRequest(string Name, string? Description = null, string? SubType = null);
public sealed record CreatePolicyReferenceRequest(string PolicyId, string ExpectedSourceRevision, string Rationale);
public sealed record UpdatePolicyReferenceRequest([property: JsonRequired] int ExpectedRevision, string Rationale);
