using Azure.Core;
using Azure.Identity;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Microsoft.Extensions.Configuration;

namespace Ato.Copilot.Mcp.Onboarding;

/// <summary>
/// Production/dev-friendly <see cref="IDelegatedArmTokenProvider"/>. When a
/// service-principal is configured via <c>AzureAd:TenantId</c> + <c>:ClientId</c>
/// + <c>:ClientSecret</c> (or the equivalent <c>AZURE_*</c> environment
/// variables), it returns a <see cref="ClientSecretCredential"/>. Otherwise it
/// falls back to <see cref="DefaultAzureCredential"/> — the same chain used by
/// the rest of the platform (managed identity → workload identity →
/// VS Code → Azure CLI). Returning <c>null</c> only when no credential is
/// configurable preserves the existing <c>WIZARD_ARM_CONSENT_REQUIRED</c>
/// surface for the wizard.
/// </summary>
public sealed class ConfiguredArmTokenProvider : IDelegatedArmTokenProvider
{
    private readonly IConfiguration _cfg;

    public ConfiguredArmTokenProvider(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    public Task<TokenCredential?> GetCredentialAsync(Guid actorUserId, CancellationToken ct = default)
    {
        var tenantId = _cfg["AzureAd:TenantId"]
            ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        var clientId = _cfg["AzureAd:ClientId"]
            ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        var clientSecret = _cfg["AzureAd:ClientSecret"]
            ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        var cloud = _cfg["AzureAd:CloudEnvironment"]
            ?? _cfg["Gateway:Azure:CloudEnvironment"]
            ?? "AzurePublicCloud";

        var authority = IsGovernment(cloud)
            ? AzureAuthorityHosts.AzureGovernment
            : AzureAuthorityHosts.AzurePublicCloud;

        if (!string.IsNullOrWhiteSpace(tenantId)
            && !string.IsNullOrWhiteSpace(clientId)
            && !string.IsNullOrWhiteSpace(clientSecret))
        {
            var cred = new ClientSecretCredential(
                tenantId,
                clientId,
                clientSecret,
                new ClientSecretCredentialOptions { AuthorityHost = authority });
            return Task.FromResult<TokenCredential?>(cred);
        }

        // Fall back to the standard credential chain (CLI / MI / workload).
        var fallback = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            AuthorityHost = authority,
        });
        return Task.FromResult<TokenCredential?>(fallback);
    }

    private static bool IsGovernment(string cloud) =>
        cloud.Equals("AzureUSGovernment", StringComparison.OrdinalIgnoreCase)
        || cloud.Equals("AzureGovernment", StringComparison.OrdinalIgnoreCase);
}
