using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

[TenantScoped]
public sealed class ResponsibilityDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = "";
    [MaxLength(20)] public string ControlId { get; set; } = "";
    [MaxLength(36)] public string ScopeKey { get; set; } = "";
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    [MaxLength(64)] public string SourceHash { get; set; } = "";
    public string SourceJson { get; set; } = "{}";
    public string ValuesJson { get; set; } = "{}";
    public string SuggestionJson { get; set; } = "{}";
    [MaxLength(24)] public string Status { get; set; } = "Proposed";
    [MaxLength(32)] public string GenerationState { get; set; } = "NotRequested";
    [MaxLength(1000)] public string? GenerationError { get; set; }
    public DateTimeOffset PreparedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? GeneratedAt { get; set; }
    [MaxLength(200)] public string PreparedBy { get; set; } = "";
    public DateTimeOffset? ReviewedAt { get; set; }
    [MaxLength(200)] public string? ReviewedBy { get; set; }
}

[TenantScoped]
public sealed class ResponsibilityDraftHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DraftId { get; set; }
    public long Revision { get; set; }
    [MaxLength(32)] public string Action { get; set; } = "";
    [MaxLength(200)] public string Actor { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(64)] public string SourceHash { get; set; } = "";
    public string ValuesJson { get; set; } = "{}";
    public string? SourceJson { get; set; }
    [MaxLength(2000)] public string? ReviewNotes { get; set; }
}

public sealed record ResponsibilityDraftValue(string Value, string Origin, IReadOnlyList<string> SourceIds,
    string Explanation = "", bool UserEdited = false, string SourceHash = "");
public sealed record ResponsibilityDraftSource(string Id, string Title, string Origin, string Version,
    string Content, string? Href = null);
public sealed record ResponsibilityDraftSuggestion(Dictionary<string, ResponsibilityDraftValue> Values,
    IReadOnlyList<string> Questions, IReadOnlyList<string> Conflicts);
public sealed record ResponsibilityDraftScope(Guid Id, string Name, string? Provider, bool ReviewRequired);
public sealed record ResponsibilityDraftResponse(Guid Id, long Revision, string Status, string SourceHash,
    bool IsStale, string GenerationState, string? GenerationError, DateTimeOffset PreparedAt,
    DateTimeOffset? GeneratedAt, string PreparedBy, string? ReviewedBy, DateTimeOffset? ReviewedAt,
    Dictionary<string, ResponsibilityDraftValue> Values, ResponsibilityDraftSuggestion Suggestion,
    IReadOnlyList<ResponsibilityDraftSource> Sources, System.Text.Json.JsonElement History);
public sealed record ResponsibilityDraftContext(string SystemId, string ControlId, string BaselineId,
    Guid? ScopeId, bool CanPrepare, string SourceHash, IReadOnlyList<ResponsibilityDraftScope> Scopes,
    IReadOnlyList<ResponsibilityDraftSource> Sources, Dictionary<string, ResponsibilityDraftValue> SourceValues,
    IReadOnlyList<string> Questions, IReadOnlyList<string> Conflicts, ResponsibilityDraftResponse? Draft);
public sealed record PrepareResponsibilityDraftRequest(Guid? ScopeId, long ExpectedRevision, bool Generate = true);
public sealed record SaveResponsibilityDraftRequest(long ExpectedRevision, Dictionary<string, string> Values,
    bool ApplySuggestion = false);
public sealed record ConfirmResponsibilityDraftRequest(long ExpectedRevision, string SourceHash,
    bool ProviderCoverageVerified, bool CustomerDutiesReviewed, string ReviewNotes);
