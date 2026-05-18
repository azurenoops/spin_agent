using System.Security.Claims;
using Ato.Copilot.Mcp.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// T064 [US2]: Verifies that the role-claim mapping driven by
/// <see cref="RoleClaimMappingsOptions"/> correctly translates a configured
/// Entra Security-Group object id (carried as a <c>groups</c> claim on the
/// JWT) into a <c>CSP.Admin</c> role claim on the resulting
/// <see cref="ClaimsPrincipal"/>. Per FR-050.
/// </summary>
/// <remarks>
/// We exercise the mapping logic directly rather than spinning up the full
/// CAC middleware stack so this remains a fast unit test. The middleware is
/// a thin wrapper around <see cref="RoleClaimMappingsOptions.GetGroupIdForRole"/>
/// + a list-mutation step; replicating that here is faithful and minimal.
/// </remarks>
public class RoleClaimMappingTests
{
    private const string CspAdminGroupId = "00000000-0000-0000-0000-000000001234";
    private const string OtherGroupId    = "ffffffff-ffff-ffff-ffff-ffffffffffff";

    private static RoleClaimMappingsOptions BuildOptions(string? cspAdmin = CspAdminGroupId)
        => new RoleClaimMappingsOptions { CspAdmin = cspAdmin ?? string.Empty };

    /// <summary>
    /// Helper that mirrors the <c>ApplyGroupToRoleMappings</c> logic in
    /// <c>CacAuthenticationMiddleware</c> so we can unit-test the policy
    /// without standing up the full HTTP pipeline.
    /// </summary>
    private static void ApplyMapping(List<Claim> claims, RoleClaimMappingsOptions options)
    {
        var cspAdminGroupId = options.GetGroupIdForRole("CSP.Admin");
        if (string.IsNullOrWhiteSpace(cspAdminGroupId)) return;

        var hasGroup = claims.Any(c =>
            string.Equals(c.Type, "groups", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Value, cspAdminGroupId, StringComparison.OrdinalIgnoreCase));
        if (!hasGroup) return;

        var alreadyHasRole = claims.Any(c =>
            string.Equals(c.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Value, "CSP.Admin", StringComparison.Ordinal));
        if (!alreadyHasRole)
        {
            claims.Add(new Claim(ClaimTypes.Role, "CSP.Admin"));
        }
    }

    [Fact]
    public void GivenTokenWithConfiguredGroupId_ThenPrincipalCarriesCspAdminRole()
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("groups", CspAdminGroupId),
        };

        ApplyMapping(claims, BuildOptions());

        claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "CSP.Admin",
            "the configured group id should map to the CSP.Admin role");
    }

    [Fact]
    public void GivenTokenWithDifferentGroupId_ThenPrincipalDoesNotCarryCspAdminRole()
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("groups", OtherGroupId),
        };

        ApplyMapping(claims, BuildOptions());

        claims.Should().NotContain(c => c.Type == ClaimTypes.Role && c.Value == "CSP.Admin",
            "an unrelated group id must not elevate the principal");
    }

    [Fact]
    public void GivenEmptyMappingConfiguration_ThenNoRoleIsApplied()
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("groups", CspAdminGroupId),
        };

        ApplyMapping(claims, BuildOptions(cspAdmin: string.Empty));

        claims.Should().NotContain(c => c.Type == ClaimTypes.Role && c.Value == "CSP.Admin",
            "with an empty CspAdmin mapping the elevation must be disabled");
    }

    [Fact]
    public void GivenIdempotentInvocation_ThenRoleIsNotDuplicated()
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("groups", CspAdminGroupId),
            new(ClaimTypes.Role, "CSP.Admin"),
        };

        ApplyMapping(claims, BuildOptions());

        claims.Count(c => c.Type == ClaimTypes.Role && c.Value == "CSP.Admin")
            .Should().Be(1, "the mapping must be idempotent");
    }

    [Fact]
    public void GetGroupIdForRole_ReturnsConfiguredId_ForCspAdmin()
    {
        var options = BuildOptions();
        options.GetGroupIdForRole("CSP.Admin").Should().Be(CspAdminGroupId);
    }

    [Fact]
    public void GetGroupIdForRole_ReturnsNull_ForUnknownRole()
    {
        var options = BuildOptions();
        options.GetGroupIdForRole("SomeUnknownRole").Should().BeNull();
    }

    [Fact]
    public void GetGroupIdForRole_ReturnsNull_ForCspAdmin_WhenEmptyOrWhitespace()
    {
        BuildOptions(cspAdmin: string.Empty).GetGroupIdForRole("CSP.Admin").Should().BeNull();
        BuildOptions(cspAdmin: "   ").GetGroupIdForRole("CSP.Admin").Should().BeNull();
    }
}
