using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Per-tenant organization-context (Step 1) read / upsert service (FR-010..FR-014).
/// Singleton row per tenant — replaced via <see cref="UpsertAsync"/>.
/// </summary>
public interface IOrganizationContextService
{
    /// <summary>Return the current organization context for a tenant, or <c>null</c> if Step 1 has not been saved.</summary>
    Task<OrganizationContext?> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Insert or update the per-tenant organization context. Validates the supplied input (FR-010 / FR-011 /
    /// FR-013) and writes an <c>OrganizationContextSaved</c> wizard-audit entry on every change.
    /// Throws <see cref="ArgumentException"/> when validation fails.
    /// </summary>
    Task<OrganizationContext> UpsertAsync(
        Guid tenantId,
        OrganizationContextInput input,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);
}

/// <summary>Input contract for <see cref="IOrganizationContextService.UpsertAsync"/> (FR-010..FR-013).</summary>
public sealed record OrganizationContextInput(
    string OrganizationName,
    BranchAffiliation Branch,
    string? BranchQualifier = null,
    string? SubOrganization = null,
    ClassificationPosture? ClassificationPosture = null,
    string? AuthoritativeRepositoryUrl = null,
    string? PrimaryPocEmail = null);
