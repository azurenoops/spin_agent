using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Interfaces.Compliance;

public enum EnvironmentScopePurpose { Assessment, Monitoring, Documentation }

/// <summary>
/// Eligibility authorizes only the exact resource IDs, not the subscription. Access probes remain
/// purpose-specific. Denied, expired, withdrawn and unreviewed sources are returned, never omitted.
/// </summary>
public sealed record ResolvedSystemEnvironmentScope(Guid AttachmentId, long AttachmentVersion,
    Guid ScopeRevisionId, long ScopeVersion, EnvironmentRegistration Registration, string Source,
    Guid? AllocationId, long? AllocationVersion, bool Eligible, string? IneligibleReason,
    IReadOnlyList<string> ResourceIds, IReadOnlyList<EnvironmentExcludedResource> Exclusions,
    IReadOnlyList<string> SharedDependencyResourceIds, EnvironmentProvenance Provenance);
/// <summary>Sources are attachment scopes, not telemetry-service access or collection checks.</summary>
public sealed record ResolvedSystemEnvironmentScopes(string SystemId, long Version,
    IReadOnlyList<ResolvedSystemEnvironmentScope> Sources, IReadOnlyList<LegacyEnvironmentReference> LegacyReferences);

/// <summary>
/// Re-read authority at each collection/evaluation, including background execution. Implementations
/// require exact tenant/system visibility. Never cache grants across collection operations or infer
/// legacy scope. Documentation may retain historical identity without granting collection access.
/// </summary>
public interface ISystemEnvironmentScopeResolver
{
    Task<ResolvedSystemEnvironmentScopes> ResolveAsync(string systemId, EnvironmentScopePurpose purpose,
        CancellationToken cancellationToken = default);
}
