using System.Text.Json;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Workspaces;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed record WorkspaceCatalogQuery(
    int Page = 1,
    int PageSize = 50,
    string? Search = null,
    string? Grouping = null,
    string? Sort = null,
    string? Direction = null,
    string? Lifecycle = null,
    string? Review = null,
    string? ComponentId = null)
{
    public WorkspaceCatalogQuery Normalize() => this with
    {
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, 200),
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Grouping = Grouping is "capability" ? "capability"
            : Grouping is "component" or "normalized-component" ? "component" : "component",
        Sort = Sort is "updatedAt" or "status" ? Sort : "name",
        Direction = Direction?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true ? "desc" : "asc",
        Lifecycle = string.IsNullOrWhiteSpace(Lifecycle) ? null : Lifecycle.Trim(),
        Review = string.IsNullOrWhiteSpace(Review) ? null : Review.Trim(),
        ComponentId = string.IsNullOrWhiteSpace(ComponentId) ? null : ComponentId.Trim(),
    };
}

public sealed record OrganizationCatalogQuery(
    int Page = 1,
    int PageSize = 50,
    string? Search = null,
    string? Lifecycle = null,
    string? Onboarding = null,
    string? Review = null)
{
    public OrganizationCatalogQuery Normalize() => this with
    {
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, 200),
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
    };
}

public sealed record SaveWorkingRevisionRequest(
    long ExpectedRevision,
    string Classification,
    string ServiceCategory,
    IReadOnlyList<string> Contributors,
    IReadOnlyDictionary<string, string> ControlDuties);

public sealed record PublishWorkingRevisionRequest(
    long Revision,
    long ApprovedRevision,
    Guid PreviewId,
    string PreviewHash,
    string IdempotencyKey);

public sealed record ApproveWorkingRevisionRequest(
    long Revision,
    Guid PreviewId,
    string PreviewHash);

public sealed record StartSupportAccessRequest(string Reason, string? Reference, bool Acknowledged);

public readonly record struct WorkspaceRecordIdentity(string Source, string RecordId);

public sealed record ProviderReleaseImpactState(
    string DeliveryState,
    string CustomerReviewState,
    string NarrativeState)
{
    public bool IsCustomerComplete =>
        DeliveryState == "Delivered" && CustomerReviewState == "Accepted" && NarrativeState == "Accepted";
}

public sealed record ResponsibilityDetailState(
    string? ConfirmedDesignation,
    string? ConfirmedBy,
    DateTimeOffset? ConfirmedAt)
{
    public string Designation => string.IsNullOrWhiteSpace(ConfirmedDesignation)
        ? "Undesignated"
        : ConfirmedDesignation;
}

public sealed record CapabilitySetupPlan(
    string IdempotencyKey,
    IReadOnlyList<string> Writes,
    bool Subscribe,
    bool CompensationDeletesSharedComponents)
{
    public static CapabilitySetupPlan Create(
        string idempotencyKey,
        IEnumerable<string> componentIds,
        bool subscribe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return new(idempotencyKey,
            componentIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            subscribe,
            false);
    }
}

public static class WorkspaceContractValidator
{
    public static IReadOnlyList<string> Validate(SaveWorkingRevisionRequest request)
    {
        var errors = new List<string>();
        if (request.ExpectedRevision < 1) errors.Add("expected revision must be positive");
        if (string.IsNullOrWhiteSpace(request.Classification)) errors.Add("classification is required");
        if (string.IsNullOrWhiteSpace(request.ServiceCategory)) errors.Add("service category is required");
        if (request.Contributors.Any(string.IsNullOrWhiteSpace)) errors.Add("contributors are required");
        var contributors = request.Contributors.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).ToArray();
        if (contributors.Distinct(StringComparer.OrdinalIgnoreCase).Count() != contributors.Length)
            errors.Add("contributors must be unique");
        if (request.ControlDuties.Keys.Any(string.IsNullOrWhiteSpace))
            errors.Add("control duty IDs are required");
        var controlIds = request.ControlDuties.Keys.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).ToArray();
        if (controlIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != controlIds.Length)
            errors.Add("control duty IDs must be unique");
        if (request.ControlDuties.Values.Any(v => v is not ("Provider" or "Shared" or "Customer")))
            errors.Add("control duties must be Provider, Shared, or Customer");
        return errors;
    }

    public static IReadOnlyList<string> Validate(PublishWorkingRevisionRequest request)
    {
        var errors = new List<string>();
        if (request.Revision != request.ApprovedRevision)
            errors.Add("approved revision must match the requested revision");
        if (string.IsNullOrWhiteSpace(request.PreviewHash))
            errors.Add("preview hash is required");
        if (request.PreviewId == Guid.Empty)
            errors.Add("preview ID is required");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            errors.Add("idempotency key is required and limited to 100 characters");
        return errors;
    }

    public static IReadOnlyList<string> Validate(StartSupportAccessRequest request)
    {
        var errors = new List<string>();
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 500) errors.Add("reason must contain 3-500 characters");
        if (request.Reference?.Trim().Length > 100) errors.Add("reference is limited to 100 characters");
        if (!request.Acknowledged) errors.Add("acknowledgement is required");
        return errors;
    }
}

public static class CapabilitySetupOutcomeReader
{
    public static IReadOnlyList<SetupWriteOutcome> Read(CapabilitySetupOperation row)
    {
        try
        {
            var values = JsonSerializer.Deserialize<List<SetupWriteOutcome>>(row.OutcomesJson);
            if (values is not null)
                return values.Select(x => x.WriteKind == "record"
                    ? x with { WriteId = CanonicalProviderRecordId(row, x.WriteId) }
                    : x).ToArray();
        }

        catch (JsonException)
        {
            // Legacy rows used string outcomes and are normalized on read.
        }

        var legacy = JsonSerializer.Deserialize<string[]>(row.OutcomesJson) ?? [];
        return legacy.Select(value =>
        {
            var segments = value.Split(':', 3);
            var legacyKind = segments.ElementAtOrDefault(0) ?? "legacy";
            var writeKind = legacyKind == "component" ? "component-link" : legacyKind;
            var writeId = segments.Length == 3 ? segments[1]
                : writeKind == "subscription" ? row.RegisteredSystemId ?? string.Empty
                : CanonicalProviderRecordId(row, row.SourceRecordId);
            var state = segments.ElementAtOrDefault(segments.Length - 1) ?? "Pending";
            return new SetupWriteOutcome(writeKind, writeId,
                string.Equals(state, "completed", StringComparison.OrdinalIgnoreCase) ? "Completed" : "Pending",
                null, row.UpdatedAt);
        }).ToArray();
    }

    private static string CanonicalProviderRecordId(CapabilitySetupOperation row, string recordId) =>
        row.SourceKind == "provider" && Guid.TryParse(recordId, out var providerId)
            ? providerId.ToString("D")
            : recordId;
}

internal static class CapabilitySetupRetentionPolicy
{
    internal static readonly TimeSpan AbandonedPreparationRetention = TimeSpan.FromDays(7);
}
