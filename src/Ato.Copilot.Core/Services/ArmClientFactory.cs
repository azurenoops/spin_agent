using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Provides <see cref="ArmClient"/> instances for Azure Commercial and Government clouds.
/// Clients are lazily created and cached as singletons (ArmClient is thread-safe).
/// </summary>
public sealed class ArmClientFactory
{
    private readonly ILogger<ArmClientFactory> _logger;
    private readonly Lazy<ArmClient> _commercial;
    private readonly Lazy<ArmClient> _government;

    /// <summary>The default cloud environment key (from configuration).</summary>
    public string DefaultCloud { get; }

    public ArmClientFactory(string defaultCloud, ILogger<ArmClientFactory> logger)
    {
        _logger = logger;
        DefaultCloud = defaultCloud;

        _commercial = new Lazy<ArmClient>(() => CreateClient("AzureCloud"));
        _government = new Lazy<ArmClient>(() => CreateClient("AzureGovernment"));
    }

    /// <summary>
    /// Returns the <see cref="ArmClient"/> for the configured default cloud.
    /// </summary>
    public ArmClient GetDefault() => GetClient(DefaultCloud);

    /// <summary>
    /// Returns the <see cref="ArmClient"/> for the specified cloud environment.
    /// </summary>
    /// <param name="cloudEnvironment">"AzureCloud" or "AzureGovernment".</param>
    public ArmClient GetClient(string cloudEnvironment)
    {
        if (cloudEnvironment.Equals("AzureCloud", StringComparison.OrdinalIgnoreCase) ||
            cloudEnvironment.Equals("AzurePublicCloud", StringComparison.OrdinalIgnoreCase) ||
            cloudEnvironment.Equals("AzureCommercial", StringComparison.OrdinalIgnoreCase))
        {
            return _commercial.Value;
        }

        // Default to Government for any other value
        return _government.Value;
    }

    /// <summary>
    /// Attempts discovery on the default cloud first. If credentials fail,
    /// automatically falls back to the other cloud.
    /// </summary>
    public async Task<(ArmClient Client, string Cloud)> GetClientWithFallbackAsync(CancellationToken ct = default)
    {
        var primaryCloud = DefaultCloud;
        var fallbackCloud = IsGovernment(primaryCloud) ? "AzureCloud" : "AzureGovernment";

        var primaryResult = await TryProbeClientAsync(primaryCloud, ct);
        if (primaryResult != null)
            return (primaryResult, primaryCloud);

        _logger.LogWarning("ARM probe failed for {PrimaryCloud}, falling back to {FallbackCloud}", primaryCloud, fallbackCloud);

        var fallbackResult = await TryProbeClientAsync(fallbackCloud, ct);
        if (fallbackResult != null)
            return (fallbackResult, fallbackCloud);

        _logger.LogError("ARM credentials failed for both {Primary} and {Fallback}", primaryCloud, fallbackCloud);
        throw new Azure.Identity.CredentialUnavailableException(
            $"Azure authentication failed for both {primaryCloud} and {fallbackCloud}. " +
            "Run 'az login' on the host (use 'az cloud set --name AzureUSGovernment' for GovCloud).");
    }

    /// <summary>
    /// Tries to create and probe an ArmClient for the given cloud.
    /// Returns the client if successful, null if credentials/auth failed.
    /// </summary>
    private async Task<ArmClient?> TryProbeClientAsync(string cloud, CancellationToken ct)
    {
        try
        {
            var client = GetClient(cloud);
            // Use GetTenants() as a lightweight probe — it requires valid creds but doesn't need a specific subscription
            var tenants = await Task.Run(() => client.GetTenants().FirstOrDefault(), ct);
            if (tenants != null)
            {
                _logger.LogInformation("ARM client authenticated against {Cloud} (tenant: {TenantId})", cloud, tenants.Data.TenantId);
                return client;
            }
            // No tenants found — credentials didn't resolve
            _logger.LogWarning("ARM probe for {Cloud}: no tenants returned", cloud);
            return null;
        }
        catch (Exception ex) when (IsCredentialOrAuthError(ex))
        {
            _logger.LogDebug("ARM probe for {Cloud} failed: {Error}", cloud, ex.Message);
            return null;
        }
    }

    private ArmClient CreateClient(string cloudEnvironment)
    {
        var isGov = IsGovernment(cloudEnvironment);
        var authorityHost = isGov ? AzureAuthorityHosts.AzureGovernment : AzureAuthorityHosts.AzurePublicCloud;
        var armEnvironment = isGov ? ArmEnvironment.AzureGovernment : ArmEnvironment.AzurePublicCloud;

        _logger.LogInformation(
            "Initializing ArmClient for {CloudEnvironment} ({ArmEndpoint})",
            cloudEnvironment, armEnvironment.Endpoint);

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            AuthorityHost = authorityHost
        });

        return new ArmClient(credential, default, new ArmClientOptions
        {
            Environment = armEnvironment
        });
    }

    private static bool IsGovernment(string cloud) =>
        !cloud.Equals("AzureCloud", StringComparison.OrdinalIgnoreCase) &&
        !cloud.Equals("AzurePublicCloud", StringComparison.OrdinalIgnoreCase) &&
        !cloud.Equals("AzureCommercial", StringComparison.OrdinalIgnoreCase);

    private static bool IsCredentialOrAuthError(Exception ex) =>
        ex is Azure.Identity.CredentialUnavailableException ||
        ex is Azure.Identity.AuthenticationFailedException ||
        ex is InvalidOperationException ||
        (ex is Azure.RequestFailedException rfe && rfe.Status is 401 or 403);
}
