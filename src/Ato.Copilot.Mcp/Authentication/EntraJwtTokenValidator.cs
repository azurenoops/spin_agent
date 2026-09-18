using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ato.Copilot.Core.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Ato.Copilot.Mcp.Authentication;

public interface IEntraJwtTokenValidator
{
    Task<ClaimsPrincipal> ValidateAsync(string token, CancellationToken cancellationToken);
}

/// <summary>
/// Validates user access tokens against the tenant's OpenID metadata and signing keys.
/// </summary>
public sealed class EntraJwtTokenValidator : IEntraJwtTokenValidator
{
    private readonly AzureAdOptions _options;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

    public EntraJwtTokenValidator(IOptions<AzureAdOptions> options)
        : this(options, CreateConfigurationManager(options.Value))
    {
    }

    public EntraJwtTokenValidator(
        IOptions<AzureAdOptions> options,
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        _options = options.Value;
        _configurationManager = configurationManager;
    }

    public async Task<ClaimsPrincipal> ValidateAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var configuration = await _configurationManager
            .GetConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);
        var validIssuers = new[] { configuration.Issuer }
            .Concat(_options.ValidIssuers)
            .Where(issuer => !string.IsNullOrWhiteSpace(issuer))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var audience = string.IsNullOrWhiteSpace(_options.Audience)
            ? _options.ClientId
            : _options.Audience;
        var validationParameters = new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ValidateAudience = true,
            ValidAudience = audience,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            NameClaimType = "name",
            RoleClaimType = "roles",
        };
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false,
        };

        return handler.ValidateToken(token, validationParameters, out _);
    }

    private static IConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager(
        AzureAdOptions options)
    {
        var metadataAddress = $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration";
        return new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }
}