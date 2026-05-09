namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Component type for a <see cref="CspInheritedComponent"/> (Feature 048
/// FR-007). Matches the canonical CSP-component classifications used in the
/// dashboard's inherited-components picker.
/// </summary>
public enum CspComponentType
{
    /// <summary>Foundational facilities (datacenters, regions, AZs).</summary>
    Infrastructure = 0,

    /// <summary>Platform-level shared services (PaaS).</summary>
    Platform = 1,

    /// <summary>Discrete CSP-managed services (e.g. KMS, Secrets, Monitor).</summary>
    Service = 2,

    /// <summary>Identity provider / IAM components.</summary>
    Identity = 3,

    /// <summary>Network fabric (firewalls, virtual networks, peering).</summary>
    Network = 4,

    /// <summary>Storage components (object, block, file).</summary>
    Storage = 5,

    /// <summary>Compute components (VM, container, serverless).</summary>
    Compute = 6,
}
