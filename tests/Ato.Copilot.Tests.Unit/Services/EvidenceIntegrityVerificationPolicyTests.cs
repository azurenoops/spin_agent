using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Roles;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class EvidenceIntegrityVerificationPolicyTests
{
    [Theory]
    [InlineData(true, false, "Sca", true)]
    [InlineData(true, false, "SCA", false)]
    [InlineData(true, false, "sca", false)]
    [InlineData(true, false, "Compliance.Auditor", false)]
    [InlineData(true, false, "CSP.Admin", false)]
    [InlineData(true, false, "Administrator", false)]
    [InlineData(true, false, "AuthorizingOfficial", false)]
    [InlineData(true, false, "Isso", false)]
    [InlineData(true, true, "", true)]
    [InlineData(false, true, "Sca", false)]
    [InlineData(false, false, "Sca", false)]
    [InlineData(true, false, "", false)]
    public void Allowed_RequiresReadAndExactEffectiveScaOrAuthoritativeEvidencePermission(
        bool canRead, bool canManageEvidence, string role, bool expected)
    {
        // Arrange
        var access = new SystemWorkspaceAccessResponse("system", [role],
            new(canRead, false, false, false, false, canManageEvidence, false, false, false));

        // Act
        var allowed = EvidenceIntegrityVerificationPolicy.IsAllowed(access);

        // Assert
        allowed.Should().Be(expected);
    }
}
