using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Linear step machine for the CSP onboarding wizard. Mirrors the
/// <c>currentStep</c> enum in
/// <c>specs/048-tenant-isolation/contracts/csp-onboarding.openapi.yaml</c>.
/// </summary>
public enum CspOnboardingStep
{
    Identity = 0,
    SupportContact = 1,
    Classification = 2,
    Review = 3,
    Complete = 4,
}

/// <summary>
/// Service that owns the singleton <see cref="CspProfile"/> row and drives
/// the FR-006 / FR-090 / FR-092 onboarding wizard. See User Story 7 in
/// <c>specs/048-tenant-isolation/spec.md</c>.
/// </summary>
public interface ICspProfileService
{
    /// <summary>
    /// Returns the singleton <see cref="CspProfile"/> row, or <c>null</c> when
    /// no row has been created yet (the deployment is in <c>Pending</c>
    /// onboarding state). Result is cached for 30 s in <c>IMemoryCache</c>.
    /// </summary>
    Task<CspProfile?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Computes which step the wizard should resume on, given the current
    /// row (or absence thereof). Stateless helper used by the dashboard
    /// router and the contract endpoint.
    /// </summary>
    CspOnboardingStep ComputeCurrentStep(CspProfile? profile);

    /// <summary>
    /// Lazily upserts the singleton row in <see cref="OnboardingState.InWizard"/>.
    /// Idempotent: returns the existing row unchanged if already present.
    /// Throws <see cref="CspAlreadyOnboardedException"/> if the row is
    /// already <see cref="OnboardingState.Active"/>.
    /// </summary>
    Task<CspProfile> EnsureCreatedAsync(string actor, CancellationToken ct = default);

    /// <summary>Persists step 1 (identity). Lazy-creates the row if missing.</summary>
    Task<CspProfile> UpdateIdentityAsync(
        string legalEntityName,
        string displayName,
        string? logoUrl,
        string actor,
        CancellationToken ct = default);

    /// <summary>Persists step 2 (primary support contact).</summary>
    Task<CspProfile> UpdateSupportAsync(
        string primarySupportEmail,
        string? supportPhone,
        string actor,
        CancellationToken ct = default);

    /// <summary>Persists step 3 (default classification floor).</summary>
    Task<CspProfile> UpdateClassificationAsync(
        ClassificationLevel defaultClassificationFloor,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Finalizes the wizard. Sets <c>OnboardingState = Active</c>, persists
    /// <c>OnboardingCompletedAt</c>, emits an audit row
    /// <c>Action = CspOnboarding.Complete</c> per FR-092, and invalidates the
    /// 30 s cache. Throws <see cref="CspAlreadyOnboardedException"/> if called
    /// after the row is already <c>Active</c>. Throws
    /// <see cref="CspOnboardingIncompleteException"/> when one or more
    /// required steps have not been completed.
    /// </summary>
    Task<CspProfile> SubmitAsync(string actor, CancellationToken ct = default);
}

/// <summary>
/// Thrown by <see cref="ICspProfileService"/> when a wizard mutation arrives
/// after <c>OnboardingState = Active</c>. Mapped to HTTP
/// <c>409 CSP_ALREADY_ONBOARDED</c>. See FR-092.
/// </summary>
public sealed class CspAlreadyOnboardedException : Exception
{
    public CspAlreadyOnboardedException()
        : base("CSP onboarding is already complete; further submissions are rejected.")
    {
    }
}

/// <summary>
/// Thrown by <see cref="ICspProfileService.SubmitAsync"/> when one or more
/// required steps are missing. Mapped to HTTP <c>422 VALIDATION_FAILED</c>.
/// </summary>
public sealed class CspOnboardingIncompleteException : Exception
{
    public CspOnboardingIncompleteException(IReadOnlyList<string> missingSteps)
        : base($"CSP onboarding cannot be submitted; missing steps: {string.Join(", ", missingSteps)}.")
    {
        MissingSteps = missingSteps;
    }

    public IReadOnlyList<string> MissingSteps { get; }
}
