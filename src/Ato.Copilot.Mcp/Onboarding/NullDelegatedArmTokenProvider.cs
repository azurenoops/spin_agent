using Azure.Core;
using Ato.Copilot.Core.Interfaces.Onboarding;

namespace Ato.Copilot.Mcp.Onboarding;

/// <summary>
/// Default <see cref="IDelegatedArmTokenProvider"/> for environments that have
/// not yet wired Microsoft.Identity.Web's <c>ITokenAcquisition</c>. Always
/// returns <c>null</c> so the wizard surfaces <c>WIZARD_ARM_CONSENT_REQUIRED</c>
/// rather than silently failing — production deployments register a credential-
/// returning provider that calls
/// <c>tokenAcquisition.GetAccessTokenForUserAsync(["https://management.azure.com/user_impersonation"])</c>.
/// </summary>
public sealed class NullDelegatedArmTokenProvider : IDelegatedArmTokenProvider
{
    public Task<TokenCredential?> GetCredentialAsync(Guid actorUserId, CancellationToken ct = default)
        => Task.FromResult<TokenCredential?>(null);
}
