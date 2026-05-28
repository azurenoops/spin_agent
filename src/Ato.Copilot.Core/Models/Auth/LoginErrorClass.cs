namespace Ato.Copilot.Core.Models.Auth;

/// <summary>
/// Classification of a <see cref="LoginAuditEventType.LoginFailure"/> row
/// per Feature 051 FR-014 (CAC failures) and FR-015 (Entra failures).
/// Set only when <see cref="LoginAuditEventType"/> is
/// <see cref="LoginAuditEventType.LoginFailure"/>; null otherwise.
/// Persisted as the enum name via <c>HasConversion&lt;string&gt;()</c>.
/// </summary>
public enum LoginErrorClass
{
    // --- CAC failures (FR-014) ---

    /// <summary>No CAC/PIV card inserted or PIN not entered.</summary>
    NoCardInserted = 0,
    /// <summary>Certificate expired (past <c>notAfter</c>).</summary>
    CertExpired = 1,
    /// <summary>Certificate not yet valid (before <c>notBefore</c>).</summary>
    CertNotYetValid = 2,
    /// <summary>Certificate revoked per OCSP / CRL.</summary>
    CertRevoked = 3,
    /// <summary>Clock skew between client and validation service exceeds tolerance.</summary>
    ClockSkew = 4,

    // --- Entra failures (FR-015) ---

    /// <summary>Authenticated Entra identity has no matching <c>Tenants</c> row.</summary>
    NoTenantAssignment = 5,
    /// <summary>Entra account disabled.</summary>
    AccountDisabled = 6,
    /// <summary>MFA challenge failed or cancelled.</summary>
    MfaFailure = 7,
    /// <summary>Entra Conditional Access policy blocked the sign-in.</summary>
    ConditionalAccessBlock = 8,
    /// <summary>Network failure reaching the identity provider.</summary>
    NetworkFailure = 9,
}
