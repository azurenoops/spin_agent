namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Result of a bootstrap-administrator grant attempt (research §R10, FR-001).
/// </summary>
public sealed record BootstrapAdministratorResult(
    bool Granted,
    Guid? AssignmentId,
    string? ErrorCode,
    string? Message);

/// <summary>
/// Atomically grants the in-app <c>Administrator</c> RMF role to the first
/// authenticated user under a tenant-level lock (FR-001 / FR-002). When the
/// lock is contended, returns a structured failure with
/// <see cref="Ato.Copilot.Core.Onboarding.WizardErrorCodes.BootstrapRace"/>.
/// </summary>
public interface IBootstrapAdministratorService
{
    /// <summary>Grant the bootstrap administrator role for the supplied subject.</summary>
    Task<BootstrapAdministratorResult> GrantAsync(
        Guid tenantId,
        Guid subjectUserId,
        string? subjectDisplayName,
        string? subjectEmail,
        Guid correlationId,
        CancellationToken ct = default);
}
