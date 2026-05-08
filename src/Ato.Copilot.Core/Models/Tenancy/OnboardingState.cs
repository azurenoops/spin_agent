namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Onboarding wizard state machine for a <see cref="Tenant"/>.
/// See feature 048 spec FR-040..FR-044 and data-model.md §1.1.
/// </summary>
public enum OnboardingState
{
    /// <summary>Tenant has been pre-provisioned but no user has signed in yet.</summary>
    Pending = 0,

    /// <summary>The first user has been routed into the onboarding wizard.</summary>
    InWizard = 1,

    /// <summary>Wizard submitted and required fields populated. Tenant is operational.</summary>
    Active = 2
}
