using Azure;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.AzureSubscriptions;

/// <summary>
/// Enumerates Azure subscriptions visible to the signed-in user via a delegated
/// ARM token (research §R8 / FR-070..FR-074). No server-side cache.
/// </summary>
public sealed class AzureSubscriptionEnumerationService : IAzureSubscriptionEnumerationService
{
    private readonly IDelegatedArmTokenProvider _tokens;
    private readonly ILogger<AzureSubscriptionEnumerationService> _logger;

    public AzureSubscriptionEnumerationService(
        IDelegatedArmTokenProvider tokens,
        ILogger<AzureSubscriptionEnumerationService> logger)
    {
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<AzureSubscriptionEnumerationResult> EnumerateAsync(
        Guid tenantId, Guid actorUserId, CancellationToken ct = default)
    {
        var credential = await _tokens.GetCredentialAsync(actorUserId, ct);
        if (credential is null)
        {
            return AzureSubscriptionEnumerationResult.Failure(
                Ato.Copilot.Core.Onboarding.WizardErrorCodes.ArmConsentRequired,
                "Azure Resource Manager consent is required to enumerate subscriptions.",
                "Approve the ARM consent prompt for the user_impersonation scope and retry.");
        }

        try
        {
            var arm = new ArmClient(credential);
            var list = new List<AzureSubscriptionInfo>();
            await foreach (var sub in arm.GetSubscriptions().GetAllAsync(ct))
            {
                var data = sub.Data;
                if (!Guid.TryParse(data.SubscriptionId, out var subId)) continue;
                Guid.TryParse(data.TenantId?.ToString(), out var parentTid);
                list.Add(new AzureSubscriptionInfo(
                    subId,
                    data.DisplayName ?? string.Empty,
                    parentTid,
                    DetectEnvironment(arm)));
            }
            return AzureSubscriptionEnumerationResult.Success(list);
        }
        catch (RequestFailedException rex) when (rex.Status == 401 || rex.Status == 403)
        {
            _logger.LogInformation(rex, "ARM consent missing/expired for tenant {TenantId}", tenantId);
            return AzureSubscriptionEnumerationResult.Failure(
                rex.Status == 401
                    ? Ato.Copilot.Core.Onboarding.WizardErrorCodes.ArmTokenExpired
                    : Ato.Copilot.Core.Onboarding.WizardErrorCodes.ArmConsentRequired,
                rex.Status == 401
                    ? "ARM token has expired. Sign in again to refresh."
                    : "Insufficient claims for ARM scope.",
                rex.Status == 401
                    ? "Re-authenticate and retry the subscription enumeration."
                    : "Approve the ARM user_impersonation scope when prompted.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ARM unreachable while enumerating subscriptions for tenant {TenantId}", tenantId);
            return AzureSubscriptionEnumerationResult.Failure(
                Ato.Copilot.Core.Onboarding.WizardErrorCodes.ArmUnreachable,
                "Azure Resource Manager could not be reached.",
                "Verify outbound connectivity to management.azure.com or management.usgovcloudapi.net and retry.");
        }
    }

    private static AzureEnvironment DetectEnvironment(ArmClient client) =>
        // Best-effort: if a tenant is government-only, the ArmClient endpoint will
        // be the gov endpoint. Without explicit configuration we assume Public.
        AzureEnvironment.AzureCloud;
}
