using System.Net;
using System.Net.Http;
using System.Text.Json;
using Ato.Copilot.Core.Models.Auth;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Feature 051 T079/T080 [US4] — pure classifier that maps CAC/PIV and
/// Entra authentication-failure inputs to a single
/// <see cref="LoginErrorClass"/> per FR-014 / FR-015. Static so it can
/// be invoked from any middleware or endpoint without DI.
/// </summary>
/// <remarks>
/// <para>
/// The classifier is deliberately input-only (no I/O, no logger, no
/// <see cref="HttpContext"/>) so the full 10-class taxonomy is
/// reachable from a unit test. Wiring into the live pipeline is the
/// responsibility of the calling middleware — see
/// <c>CacAuthenticationMiddleware</c> for the CAC wire and
/// <c>AuthEndpoints.GetMeAsync</c> for the
/// <see cref="LoginErrorClass.NoTenantAssignment"/> wire.
/// </para>
/// <para>
/// <strong>FR-033 (NON-NEGOTIABLE)</strong>: callers that persist a
/// failure-row <c>MetadataJson</c> MUST use
/// <see cref="BuildSafeMetadata(LoginErrorClass)"/> to construct it —
/// never embed cert thumbprints, serials, subject/issuer DNs, or any
/// PII beyond the <c>oid</c> column.
/// </para>
/// </remarks>
public static class LoginErrorClassifier
{
    /// <summary>
    /// Threshold beyond which a <c>notBefore</c> in the future is
    /// attributed to the cert itself rather than to client/server
    /// clock skew. Anything below this within the skew band is
    /// <see cref="LoginErrorClass.ClockSkew"/>; anything above is
    /// <see cref="LoginErrorClass.CertNotYetValid"/>.
    /// </summary>
    private static readonly TimeSpan CertVsClockBoundary = TimeSpan.FromHours(1);

    /// <summary>
    /// Map a CAC/PIV validation outcome to a <see cref="LoginErrorClass"/>.
    /// Returns <c>null</c> when the inputs describe a healthy cert
    /// (in which case the caller should not write a failure row).
    /// </summary>
    /// <param name="hasCertificate">Did the browser/CSP present any client cert?</param>
    /// <param name="notBefore">Certificate's <c>NotBefore</c>, or null.</param>
    /// <param name="notAfter">Certificate's <c>NotAfter</c>, or null.</param>
    /// <param name="isRevoked">OCSP/CRL revocation outcome — null means "not checked".</param>
    /// <param name="now">Current UTC time (caller-supplied so tests can pin it).</param>
    /// <param name="clockSkewTolerance">Allowed ± skew window. 5 min is the spec default.</param>
    /// <param name="exception">Validation exception (e.g., HttpRequestException), or null.</param>
    public static LoginErrorClass? ClassifyCacFailure(
        bool hasCertificate,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter,
        bool? isRevoked,
        DateTimeOffset now,
        TimeSpan clockSkewTolerance,
        Exception? exception)
    {
        // Network failures dominate — if we couldn't reach OCSP / the
        // validation endpoint, the cert state is unknowable.
        if (IsNetworkException(exception))
        {
            return LoginErrorClass.NetworkFailure;
        }

        if (!hasCertificate)
        {
            return LoginErrorClass.NoCardInserted;
        }

        if (isRevoked == true)
        {
            return LoginErrorClass.CertRevoked;
        }

        if (notAfter is { } na && na < now - clockSkewTolerance)
        {
            return LoginErrorClass.CertExpired;
        }

        if (notBefore is { } nb && nb > now + clockSkewTolerance)
        {
            // Distinguish a cert that's genuinely too new from a clock-
            // skew issue: anything within an hour of "now + tolerance"
            // is almost certainly skew (workstation clock drift).
            var delta = nb - now;
            return delta > CertVsClockBoundary
                ? LoginErrorClass.CertNotYetValid
                : LoginErrorClass.ClockSkew;
        }

        return null;
    }

    /// <summary>
    /// Map an Entra/JWT validation outcome to a <see cref="LoginErrorClass"/>.
    /// Returns <c>null</c> when no failure condition is present (caller
    /// should not write a failure row in that case).
    /// </summary>
    /// <remarks>
    /// Precedence (most actionable first):
    /// <see cref="LoginErrorClass.NetworkFailure"/> →
    /// <see cref="LoginErrorClass.ConditionalAccessBlock"/> →
    /// <see cref="LoginErrorClass.AccountDisabled"/> →
    /// <see cref="LoginErrorClass.MfaFailure"/> →
    /// <see cref="LoginErrorClass.NoTenantAssignment"/>.
    /// </remarks>
    public static LoginErrorClass? ClassifyEntraFailure(
        bool tenantResolved,
        bool accountDisabled,
        bool mfaSatisfied,
        bool conditionalAccessBlocked,
        Exception? exception)
    {
        if (IsNetworkException(exception))
        {
            return LoginErrorClass.NetworkFailure;
        }

        if (conditionalAccessBlocked)
        {
            return LoginErrorClass.ConditionalAccessBlock;
        }

        if (accountDisabled)
        {
            return LoginErrorClass.AccountDisabled;
        }

        if (!mfaSatisfied)
        {
            return LoginErrorClass.MfaFailure;
        }

        if (!tenantResolved)
        {
            return LoginErrorClass.NoTenantAssignment;
        }

        return null;
    }

    /// <summary>
    /// FR-033 — build a privacy-safe <c>MetadataJson</c> payload for a
    /// <see cref="LoginAuditEventType.LoginFailure"/> row. Contains
    /// ONLY the error class. Callers MUST NOT append cert thumbprints,
    /// serial numbers, subject/issuer DNs, or any PII.
    /// </summary>
    public static string BuildSafeMetadata(LoginErrorClass errorClass)
        => JsonSerializer.Serialize(new { errorClass = errorClass.ToString() });

    private static bool IsNetworkException(Exception? ex)
        => ex is HttpRequestException
           || ex is WebException
           || ex is TimeoutException
           || (ex?.InnerException is not null && IsNetworkException(ex.InnerException));
}
