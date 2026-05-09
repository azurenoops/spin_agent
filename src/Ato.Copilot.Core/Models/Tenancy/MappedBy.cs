namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Records who produced a <see cref="CspInheritedCapability.MappedNistControlIds"/>
/// list (Feature 048 FR-101 / FR-105).
/// </summary>
public enum MappedBy
{
    /// <summary>AI capability-mapping pipeline (`ICapabilityMappingService`).</summary>
    AI = 0,

    /// <summary>Human CSP-Admin via the review endpoint.</summary>
    User = 1,
}
