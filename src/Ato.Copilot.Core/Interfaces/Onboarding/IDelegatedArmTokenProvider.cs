using Azure.Core;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Acquires a delegated ARM <see cref="TokenCredential"/> for the signed-in user
/// (research §R8 / FR-070a). Returns <c>null</c> if consent for the ARM scope
/// is missing — endpoints surface <c>WIZARD_ARM_CONSENT_REQUIRED</c>.
/// </summary>
public interface IDelegatedArmTokenProvider
{
    /// <summary>
    /// Returns a <see cref="TokenCredential"/> bound to the signed-in user's
    /// delegated ARM token, or <c>null</c> when consent is missing/expired.
    /// </summary>
    /// <param name="actorUserId">Subject (oid) of the signed-in admin.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TokenCredential?> GetCredentialAsync(Guid actorUserId, CancellationToken ct = default);
}
