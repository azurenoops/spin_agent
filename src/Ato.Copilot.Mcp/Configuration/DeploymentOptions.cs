namespace Ato.Copilot.Mcp.Configuration;

/// <summary>
/// Top-level deployment configuration. Bound from the <c>Deployment</c>
/// configuration section (or environment variables prefixed
/// <c>ATO_DEPLOYMENT__</c>).
/// See feature 048 spec FR-040 / FR-055.
/// </summary>
/// <example>
/// JSON shape:
/// <code>
/// "Deployment": {
///   "Mode": "SingleTenant",
///   "DefaultTenantId": "00000000-0000-0000-0000-000000000001",
///   "Tenants": { "AllowSelfOnboarding": false }
/// }
/// </code>
/// </example>
public sealed class DeploymentOptions
{
    public const string SectionName = "Deployment";

    /// <summary>
    /// Deployment mode. <see cref="DeploymentMode.SingleTenant"/> (default) is
    /// the on-prem / single-organization configuration.
    /// <see cref="DeploymentMode.MultiTenant"/> is the CSP-hosted configuration
    /// where many tenants share one ATO Copilot installation.
    /// </summary>
    public DeploymentMode Mode { get; set; } = DeploymentMode.SingleTenant;

    /// <summary>
    /// In <see cref="DeploymentMode.SingleTenant"/> mode, the singleton
    /// default tenant id used when no Entra <c>tid</c> claim is available
    /// (e.g., on-prem CAC-only deployments). Ignored in MultiTenant mode.
    /// </summary>
    public Guid? DefaultTenantId { get; set; }

    /// <summary>Tenant-management policy.</summary>
    public TenantPolicyOptions Tenants { get; set; } = new();
}

/// <summary>Tenant-management policy options.</summary>
public sealed class TenantPolicyOptions
{
    /// <summary>
    /// When <c>true</c>, an unknown Entra tenant signing into the dashboard is
    /// auto-pre-provisioned and routed into the onboarding wizard (FR-040).
    /// When <c>false</c>, unknown tenants are rejected with
    /// <c>404 TENANT_NOT_PROVISIONED</c>.
    /// </summary>
    public bool AllowSelfOnboarding { get; set; }
}

/// <summary>Deployment mode for the ATO Copilot MCP host.</summary>
public enum DeploymentMode
{
    /// <summary>Single tenant — on-prem / single-organization deployment.</summary>
    SingleTenant = 0,

    /// <summary>Multi-tenant — CSP-hosted deployment with many customer tenants.</summary>
    MultiTenant = 1
}
