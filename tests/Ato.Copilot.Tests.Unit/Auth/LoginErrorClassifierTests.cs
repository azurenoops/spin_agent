using System.Net.Http;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Services.Auth;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T078 [US4] — exhaustive unit coverage for
/// <see cref="LoginErrorClassifier"/>. One test per
/// <see cref="LoginErrorClass"/> value (10 total) so we can guarantee
/// every class in <c>FR-014</c>/<c>FR-015</c> is reachable via the
/// classifier function regardless of whether the surrounding
/// middleware pipeline can induce it today (some classes — e.g.,
/// <see cref="LoginErrorClass.CertRevoked"/> — require live OCSP/CRL
/// infrastructure and are wired-only until Phase 13).
/// </summary>
public sealed class LoginErrorClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan SkewTolerance = TimeSpan.FromMinutes(5);

    // ─── CAC classifier (FR-014) ─────────────────────────────────────

    [Fact]
    public void ClassifyCacFailure_NoCertificate_ReturnsNoCardInserted()
    {
        // Arrange — browser presented no client cert (no card / no PIN).

        // Act
        var result = LoginErrorClassifier.ClassifyCacFailure(
            hasCertificate: false,
            notBefore: null,
            notAfter: null,
            isRevoked: null,
            now: Now,
            clockSkewTolerance: SkewTolerance,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.NoCardInserted);
    }

    [Fact]
    public void ClassifyCacFailure_CertPastNotAfter_ReturnsCertExpired()
    {
        // Arrange — cert.NotAfter is yesterday.
        var notAfter = Now.AddDays(-1);

        // Act
        var result = LoginErrorClassifier.ClassifyCacFailure(
            hasCertificate: true,
            notBefore: Now.AddYears(-3),
            notAfter: notAfter,
            isRevoked: false,
            now: Now,
            clockSkewTolerance: SkewTolerance,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.CertExpired);
    }

    [Fact]
    public void ClassifyCacFailure_CertFarInFuture_ReturnsCertNotYetValid()
    {
        // Arrange — cert.NotBefore is one week from now (way beyond the
        // 5-min clock-skew tolerance band, so it's a true "not yet valid"
        // cert, not a clock mismatch).
        var notBefore = Now.AddDays(7);

        // Act
        var result = LoginErrorClassifier.ClassifyCacFailure(
            hasCertificate: true,
            notBefore: notBefore,
            notAfter: Now.AddYears(3),
            isRevoked: false,
            now: Now,
            clockSkewTolerance: SkewTolerance,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.CertNotYetValid);
    }

    [Fact]
    public void ClassifyCacFailure_RevokedCert_ReturnsCertRevoked()
    {
        // Arrange — OCSP/CRL flagged the cert as revoked.

        // Act
        var result = LoginErrorClassifier.ClassifyCacFailure(
            hasCertificate: true,
            notBefore: Now.AddYears(-1),
            notAfter: Now.AddYears(2),
            isRevoked: true,
            now: Now,
            clockSkewTolerance: SkewTolerance,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.CertRevoked);
    }

    [Fact]
    public void ClassifyCacFailure_CertNotBefore_SlightlyInFuture_ReturnsClockSkew()
    {
        // Arrange — cert.NotBefore is 10 min from now: outside the
        // 5-min tolerance but well under the "really not yet valid"
        // 1-hour threshold, so we attribute it to clock skew.
        var notBefore = Now.AddMinutes(10);

        // Act
        var result = LoginErrorClassifier.ClassifyCacFailure(
            hasCertificate: true,
            notBefore: notBefore,
            notAfter: Now.AddYears(3),
            isRevoked: false,
            now: Now,
            clockSkewTolerance: SkewTolerance,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.ClockSkew);
    }

    [Fact]
    public void ClassifyCacFailure_NetworkException_ReturnsNetworkFailure()
    {
        // Arrange — OCSP responder unreachable.

        // Act
        var result = LoginErrorClassifier.ClassifyCacFailure(
            hasCertificate: true,
            notBefore: Now.AddYears(-1),
            notAfter: Now.AddYears(2),
            isRevoked: null,
            now: Now,
            clockSkewTolerance: SkewTolerance,
            exception: new HttpRequestException("ocsp.example.gov unreachable"));

        // Assert
        result.Should().Be(LoginErrorClass.NetworkFailure);
    }

    [Fact]
    public void ClassifyCacFailure_HealthyCert_ReturnsNull()
    {
        // Arrange — cert is valid, in window, not revoked → no
        // classification (caller should not write a failure row).

        // Act
        var result = LoginErrorClassifier.ClassifyCacFailure(
            hasCertificate: true,
            notBefore: Now.AddYears(-1),
            notAfter: Now.AddYears(2),
            isRevoked: false,
            now: Now,
            clockSkewTolerance: SkewTolerance,
            exception: null);

        // Assert
        result.Should().BeNull();
    }

    // ─── Entra classifier (FR-015) ───────────────────────────────────

    [Fact]
    public void ClassifyEntraFailure_TenantUnresolved_ReturnsNoTenantAssignment()
    {
        // Arrange — Entra issued a valid token but no Tenants row maps
        // to the `tid` claim.

        // Act
        var result = LoginErrorClassifier.ClassifyEntraFailure(
            tenantResolved: false,
            accountDisabled: false,
            mfaSatisfied: true,
            conditionalAccessBlocked: false,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.NoTenantAssignment);
    }

    [Fact]
    public void ClassifyEntraFailure_AccountDisabled_ReturnsAccountDisabled()
    {
        // Arrange — Entra signaled account disabled (e.g.,
        // AADSTS50057 or AADSTS50034).

        // Act
        var result = LoginErrorClassifier.ClassifyEntraFailure(
            tenantResolved: true,
            accountDisabled: true,
            mfaSatisfied: true,
            conditionalAccessBlocked: false,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.AccountDisabled);
    }

    [Fact]
    public void ClassifyEntraFailure_MfaMissing_ReturnsMfaFailure()
    {
        // Arrange — token reached us without an `amr` claim of
        // `mfa`+`rsa`.

        // Act
        var result = LoginErrorClassifier.ClassifyEntraFailure(
            tenantResolved: true,
            accountDisabled: false,
            mfaSatisfied: false,
            conditionalAccessBlocked: false,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.MfaFailure);
    }

    [Fact]
    public void ClassifyEntraFailure_ConditionalAccessBlock_TakesPrecedence()
    {
        // Arrange — Entra returned a Conditional Access challenge.
        // CA block is the most actionable error class and should win
        // over any other simultaneous condition.

        // Act
        var result = LoginErrorClassifier.ClassifyEntraFailure(
            tenantResolved: false,
            accountDisabled: true,
            mfaSatisfied: false,
            conditionalAccessBlocked: true,
            exception: null);

        // Assert
        result.Should().Be(LoginErrorClass.ConditionalAccessBlock);
    }

    [Fact]
    public void ClassifyEntraFailure_NetworkException_ReturnsNetworkFailure()
    {
        // Arrange — could not reach login.microsoftonline.{us,com}.

        // Act
        var result = LoginErrorClassifier.ClassifyEntraFailure(
            tenantResolved: false,
            accountDisabled: false,
            mfaSatisfied: false,
            conditionalAccessBlocked: false,
            exception: new HttpRequestException("login.microsoftonline.us unreachable"));

        // Assert
        result.Should().Be(LoginErrorClass.NetworkFailure);
    }

    [Fact]
    public void ClassifyEntraFailure_HealthyAuth_ReturnsNull()
    {
        // Arrange — everything is fine.

        // Act
        var result = LoginErrorClassifier.ClassifyEntraFailure(
            tenantResolved: true,
            accountDisabled: false,
            mfaSatisfied: true,
            conditionalAccessBlocked: false,
            exception: null);

        // Assert
        result.Should().BeNull();
    }

    // ─── Privacy invariant (FR-033) ───────────────────────────────────

    [Fact]
    public void BuildSafeMetadata_OnlyEmitsErrorClass_NoCertOrPii()
    {
        // Arrange — even when the caller wants extra context, the
        // classifier helper must produce metadata that contains ONLY the
        // error class (FR-033: no cert thumbprints, no PII beyond oid
        // which is already on the audit row itself).

        // Act
        var metadata = LoginErrorClassifier.BuildSafeMetadata(LoginErrorClass.CertExpired);

        // Assert
        metadata.Should().Contain("\"errorClass\"");
        metadata.Should().Contain("\"CertExpired\"");
        metadata.Should().NotContain("thumbprint", "FR-033 forbids cert thumbprints");
        metadata.Should().NotContain("serial", "FR-033 forbids cert serial numbers");
        metadata.Should().NotContain("subject", "FR-033 forbids cert subject DNs");
        metadata.Should().NotContain("issuer", "FR-033 forbids cert issuer DNs");
    }
}
