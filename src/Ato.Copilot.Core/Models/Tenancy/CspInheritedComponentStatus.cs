namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Lifecycle for a <see cref="CspInheritedComponent"/> during and after the
/// CSP-onboarding wizard (Feature 048 FR-007 / FR-100).
/// </summary>
public enum CspInheritedComponentStatus
{
    /// <summary>
    /// Created during ATO upload but not yet visible to hosted tenants.
    /// CSP-Admins can edit / archive / publish from here.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Visible read-only to every hosted tenant. Set by the wizard's
    /// "submit" step (T209) or by an explicit
    /// <c>POST /csp/inherited-components/{id}/publish</c>.
    /// </summary>
    Published = 1,

    /// <summary>
    /// Soft-deleted by a CSP-Admin. Hidden from hosted-tenant pickers
    /// but retained for audit (FR-105).
    /// </summary>
    Archived = 2,
}
