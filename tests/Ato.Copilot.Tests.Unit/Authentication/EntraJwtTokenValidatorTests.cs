using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Mcp.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Authentication;

public sealed class EntraJwtTokenValidatorTests
{
    private const string Issuer = "https://login.microsoftonline.com/test-tenant/v2.0";
    private const string ClientId = "cfb5e2d3-ea0a-457a-8a1f-743a39eb6deb";

    [Fact]
    public async Task ValidateAsync_ValidSignedToken_ReturnsAuthenticatedPrincipal()
    {
        // Arrange
        using var signingKey = RSA.Create(2048);
        var validator = CreateValidator(signingKey);
        var token = CreateToken(signingKey, ClientId);

        // Act
        var principal = await validator.ValidateAsync(token, CancellationToken.None);

        // Assert
        principal.Identity!.IsAuthenticated.Should().BeTrue();
        principal.FindFirst("oid")?.Value.Should().Be("user-123");
    }

    [Fact]
    public async Task ValidateAsync_TokenSignedByUnknownKey_Throws()
    {
        // Arrange
        using var trustedKey = RSA.Create(2048);
        using var unknownKey = RSA.Create(2048);
        var validator = CreateValidator(trustedKey);
        var token = CreateToken(unknownKey, ClientId);

        // Act
        var act = () => validator.ValidateAsync(token, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<SecurityTokenInvalidSignatureException>();
    }

    [Fact]
    public async Task ValidateAsync_TokenForDifferentAudience_Throws()
    {
        // Arrange
        using var signingKey = RSA.Create(2048);
        var validator = CreateValidator(signingKey);
        var token = CreateToken(signingKey, "different-client-id");

        // Act
        var act = () => validator.ValidateAsync(token, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<SecurityTokenInvalidAudienceException>();
    }

    private static EntraJwtTokenValidator CreateValidator(RSA signingKey)
    {
        var securityKey = new RsaSecurityKey(signingKey.ExportParameters(false))
        {
            KeyId = "test-key",
        };
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = Issuer,
            SigningKeys = { securityKey },
        };
        var configurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        var options = Options.Create(new AzureAdOptions
        {
            Instance = "https://login.microsoftonline.com/",
            TenantId = "test-tenant",
            ClientId = ClientId,
            Audience = ClientId,
        });

        return new EntraJwtTokenValidator(options, configurationManager);
    }

    private static string CreateToken(RSA signingKey, string audience)
    {
        var securityKey = new RsaSecurityKey(signingKey) { KeyId = "test-key" };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: audience,
            claims: [new Claim("oid", "user-123")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}