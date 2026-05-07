using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>Unified row for the cross-kind imports management view (FR-093, SC-013).</summary>
public sealed record WizardArtifactInventoryRow(
    Guid Id,
    Guid TenantId,
    ArtifactSourceKind Kind,
    string Label,
    string? VersionTag,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int DependentsCount,
    int StaleDependentsCount,
    string? StatusLabel);

/// <summary>Paginated result envelope.</summary>
public sealed record WizardArtifactInventoryPage(
    IReadOnlyList<WizardArtifactInventoryRow> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Cross-kind read model for the <c>/admin/imported-documents</c> view.
/// UNIONs the four wizard artifact source tables and joins
/// <c>WizardArtifactDependency</c> for dependents counts.
/// </summary>
public interface IWizardArtifactInventoryService
{
    Task<WizardArtifactInventoryPage> ListAsync(
        Guid tenantId,
        ArtifactSourceKind? filter = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);
}
