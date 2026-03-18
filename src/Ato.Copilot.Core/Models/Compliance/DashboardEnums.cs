namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Implementation lifecycle status for a Security Capability.
/// </summary>
public enum CapabilityStatus
{
    /// <summary>Capability is planned but not yet being implemented.</summary>
    Planned,
    /// <summary>Capability implementation is underway.</summary>
    InProgress,
    /// <summary>Capability is fully implemented and operational.</summary>
    Implemented,
    /// <summary>Capability is deprecated and being phased out.</summary>
    Deprecated
}

/// <summary>
/// Role of a capability-to-control mapping describing responsibility.
/// </summary>
public enum CapabilityMappingRole
{
    /// <summary>This capability is the primary implementation for the control.</summary>
    Primary,
    /// <summary>This capability supports the primary implementation.</summary>
    Supporting,
    /// <summary>This capability is shared across multiple controls.</summary>
    Shared
}

/// <summary>
/// Person/Place/Thing classification for system components (SSP Appendix A).
/// </summary>
public enum ComponentType
{
    /// <summary>A person responsible for system security (e.g., ISSM, ISSO).</summary>
    Person,
    /// <summary>A location where system components reside (e.g., data center, cloud region).</summary>
    Place,
    /// <summary>A technical asset (e.g., Entra ID, Defender, Key Vault).</summary>
    Thing,
    /// <summary>A law, regulation, or policy applicable to the system (e.g., FISMA, Privacy Act).</summary>
    Policy
}

/// <summary>
/// Operational status of a system component.
/// </summary>
public enum ComponentStatus
{
    /// <summary>Component is actively in use.</summary>
    Active,
    /// <summary>Component is planned but not yet deployed.</summary>
    Planned,
    /// <summary>Component has been retired from service.</summary>
    Decommissioned
}
