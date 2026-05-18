using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Tenant-and-Organization onboarding wizard surface (Feature 048 US4 / FR-054).
/// Drives the Tenants-row population + first Organization creation flow that
/// runs before any other tenant-scoped API call may succeed for tenants whose
/// <see cref="Tenant.OnboardingState"/> is not yet
/// <see cref="OnboardingState.Active"/>.
/// </summary>
/// <remarks>
/// All step submissions are idempotent — a re-submitted step overwrites the
/// matching <see cref="Tenant"/> fields and re-emits the audit row (FR-056).
/// Re-entrancy: callers can resume mid-wizard; <see cref="GetStateAsync"/>
/// reports both the current <see cref="OnboardingState"/> and the next
/// outstanding step.
/// </remarks>
public interface ITenantOnboardingService
{
    /// <summary>Read the current onboarding state + next step for the tenant.</summary>
    Task<TenantOnboardingProgress> GetStateAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Submit Step 1 — legal entity / DoD component / time zone.</summary>
    Task<TenantOnboardingProgress> SubmitLegalEntityAsync(
        Guid tenantId,
        Guid actorUserId,
        LegalEntityStepRequest request,
        CancellationToken ct = default);

    /// <summary>Submit Step 2 — HQ address.</summary>
    Task<TenantOnboardingProgress> SubmitHqAddressAsync(
        Guid tenantId,
        Guid actorUserId,
        HqAddressStepRequest request,
        CancellationToken ct = default);

    /// <summary>Submit Step 3 — default classification level.</summary>
    Task<TenantOnboardingProgress> SubmitClassificationAsync(
        Guid tenantId,
        Guid actorUserId,
        ClassificationStepRequest request,
        CancellationToken ct = default);

    /// <summary>Submit Step 4 — Authorizing Official.</summary>
    Task<TenantOnboardingProgress> SubmitAoAsync(
        Guid tenantId,
        Guid actorUserId,
        AoStepRequest request,
        CancellationToken ct = default);

    /// <summary>Submit Step 5 — primary POC.</summary>
    Task<TenantOnboardingProgress> SubmitPrimaryPocAsync(
        Guid tenantId,
        Guid actorUserId,
        PrimaryPocStepRequest request,
        CancellationToken ct = default);

    /// <summary>Submit Step 6 — first organization profile (creates an Organization row).</summary>
    Task<TenantOnboardingProgress> SubmitOrgProfileAsync(
        Guid tenantId,
        Guid actorUserId,
        OrgProfileStepRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Final submission — verifies all required fields are populated and the
    /// org has been created, then transitions <see cref="Tenant.OnboardingState"/>
    /// to <see cref="OnboardingState.Active"/>.
    /// </summary>
    Task<TenantOnboardingProgress> SubmitFinalAsync(
        Guid tenantId,
        Guid actorUserId,
        CancellationToken ct = default);
}

/// <summary>Read-model returned to the dashboard after each step.</summary>
public sealed record TenantOnboardingProgress(
    Guid TenantId,
    string CurrentStep,
    IReadOnlyList<string> CompletedSteps,
    OnboardingState OnboardingState,
    Guid? FirstOrganizationId);

/// <summary>Step 1 — legal-entity payload.</summary>
public sealed record LegalEntityStepRequest(
    string LegalEntityName,
    string? DoDComponent,
    string? TimeZone);

/// <summary>Step 2 — HQ address payload.</summary>
public sealed record HqAddressStepRequest(
    string HqAddressLine1,
    string? HqAddressLine2,
    string HqCity,
    string HqStateOrProvince,
    string HqPostalCode,
    string HqCountry);

/// <summary>Step 3 — default classification level.</summary>
/// <remarks>
/// The level is accepted as a string to keep the contract resilient when no
/// global <c>JsonStringEnumConverter</c> is registered. Valid values mirror
/// <see cref="ClassificationLevel"/> names: <c>Unclassified</c>, <c>CUI</c>,
/// <c>Secret</c>.
/// </remarks>
public sealed record ClassificationStepRequest(
    string DefaultClassificationLevel);

/// <summary>Step 4 — Authorizing Official.</summary>
public sealed record AoStepRequest(
    string AuthorizingOfficialName,
    string AuthorizingOfficialEmail);

/// <summary>Step 5 — primary POC.</summary>
public sealed record PrimaryPocStepRequest(
    string PrimaryPocName,
    string PrimaryPocEmail,
    string? PrimaryPocPhone);

/// <summary>Step 6 — first organization profile.</summary>
public sealed record OrgProfileStepRequest(
    string Name,
    string? Description);
