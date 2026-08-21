using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Mcp.Authorization;

/// <summary>
/// Canonical ASP.NET Core authorization policy names used throughout the MCP server.
///
/// fix(#733 / #636): replaces scattered <c>IsInRole("CSP.Admin")</c> / <c>IsInRole("Auth.SocAnalyst")</c>
/// literals with a single auditable, testable policy abstraction.  All policy names are registered
/// via <see cref="RegisterPolicies"/> during startup; call sites should reference the constants
/// here rather than embedding raw role strings.
/// </summary>
public static class Policies
{
    // ──────────────────────────────────────────────────────────────────────────
    // Cross-tenant / platform roles (mapped from AAD groups via RoleClaimMappings)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Policy that requires the caller to be in the <c>CSP.Admin</c> role.
    /// CSP-Admins can manage multi-tenant portfolios and impersonate tenants.
    /// </summary>
    public const string CspAdmin = "Policy:CspAdmin";

    /// <summary>
    /// Policy that requires the caller to be in <c>Auth.SocAnalyst</c> OR
    /// the legacy <c>SOC.Analyst</c> role.
    /// </summary>
    public const string SocAnalyst = "Policy:SocAnalyst";

    // ──────────────────────────────────────────────────────────────────────────
    // Compliance roles (from ComplianceRoles constants)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Policy that requires at least one of the read-capable compliance roles:
    /// Viewer, Analyst, Auditor, Administrator, PlatformEngineer, SecurityLead,
    /// or AuthorizingOfficial.
    /// </summary>
    public const string ComplianceReader = "Policy:ComplianceReader";

    /// <summary>
    /// Policy that requires a write-capable compliance role: Analyst, Administrator,
    /// SecurityLead, or PlatformEngineer.
    /// </summary>
    public const string ComplianceWriter = "Policy:ComplianceWriter";

    /// <summary>
    /// Policy that requires the Administrator compliance role.
    /// </summary>
    public const string ComplianceAdministrator = "Policy:ComplianceAdministrator";

    // ──────────────────────────────────────────────────────────────────────────
    // Registration helper
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all named policies defined in this class with the ASP.NET Core
    /// authorization options.  Call this from the <c>AddAuthorization</c> lambda in
    /// <c>Program.cs</c> so that every policy is registered in a single, auditable
    /// location.
    /// </summary>
    public static void RegisterPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(CspAdmin, policy =>
            policy.RequireRole(SystemRoles.CspAdmin));

        options.AddPolicy(SocAnalyst, policy =>
            policy.RequireAssertion(ctx =>
                ctx.User.IsInRole(SystemRoles.SocAnalyst) ||
                ctx.User.IsInRole(SystemRoles.SocAnalystLegacy)));

        options.AddPolicy(ComplianceReader, policy =>
            policy.RequireRole(
                Core.Constants.ComplianceRoles.Viewer,
                Core.Constants.ComplianceRoles.Analyst,
                Core.Constants.ComplianceRoles.Auditor,
                Core.Constants.ComplianceRoles.Administrator,
                Core.Constants.ComplianceRoles.PlatformEngineer,
                Core.Constants.ComplianceRoles.SecurityLead,
                Core.Constants.ComplianceRoles.AuthorizingOfficial));

        options.AddPolicy(ComplianceWriter, policy =>
            policy.RequireRole(
                Core.Constants.ComplianceRoles.Analyst,
                Core.Constants.ComplianceRoles.Administrator,
                Core.Constants.ComplianceRoles.SecurityLead,
                Core.Constants.ComplianceRoles.PlatformEngineer));

        options.AddPolicy(ComplianceAdministrator, policy =>
            policy.RequireRole(Core.Constants.ComplianceRoles.Administrator));
    }
}

/// <summary>
/// Role name constants for platform / cross-tenant system roles (i.e., roles sourced
/// from AAD group mappings, not from the in-app RBAC table).
///
/// These complement <see cref="Ato.Copilot.Core.Constants.ComplianceRoles"/> which
/// covers in-app compliance RBAC.
/// </summary>
public static class SystemRoles
{
    /// <summary>CSP (Cloud Service Provider) administrator role — multi-tenant portfolio management.</summary>
    public const string CspAdmin = "CSP.Admin";

    /// <summary>SOC Analyst role (canonical name).</summary>
    public const string SocAnalyst = "Auth.SocAnalyst";

    /// <summary>Legacy SOC Analyst role name — kept for backward compatibility.</summary>
    public const string SocAnalystLegacy = "SOC.Analyst";
}
