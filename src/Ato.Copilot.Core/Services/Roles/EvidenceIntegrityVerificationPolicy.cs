using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services.Roles;

/// <summary>Integrity checking only; does not grant evidence authorship or assessment approval.</summary>
public static class EvidenceIntegrityVerificationPolicy
{
    /// <summary>Accepts only access resolved by the authoritative system workspace access service.</summary>
    public static bool IsAllowed(SystemWorkspaceAccessResponse access) =>
        access.Permissions.CanRead
        && (access.Permissions.CanManageEvidence || access.Roles.Contains(nameof(RmfRole.Sca), StringComparer.Ordinal));
}
