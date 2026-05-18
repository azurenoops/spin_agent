namespace Ato.Copilot.Mcp.Configuration;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Translates Entra Security-Group object IDs (carried as <c>groups</c> claims
/// on the JWT) into named roles on the resulting <c>ClaimsPrincipal</c>.
/// Bound from the <c>Auth:RoleClaimMappings</c> configuration section.
/// See feature 048 spec FR-050.
/// </summary>
/// <example>
/// JSON shape:
/// <code>
/// "Auth": {
///   "RoleClaimMappings": {
///     "CSP.Admin": "00000000-0000-0000-0000-000000001234"
///   }
/// }
/// </code>
/// </example>
public sealed class RoleClaimMappingsOptions
{
    public const string SectionName = "Auth:RoleClaimMappings";

    /// <summary>
    /// Entra group object ID granting the <c>CSP.Admin</c> role. Members of
    /// this group are permitted to impersonate other tenants for support
    /// purposes (FR-051). Empty string disables CSP-Admin elevation.
    /// The binder maps the JSON key <c>"CSP.Admin"</c> to this property via
    /// <see cref="ConfigurationKeyNameAttribute"/>.
    /// </summary>
    [ConfigurationKeyName("CSP.Admin")]
    public string CspAdmin { get; set; } = string.Empty;

    /// <summary>
    /// Convenience accessor that returns the configured group id for a given
    /// role name, or null if no mapping exists.
    /// </summary>
    public string? GetGroupIdForRole(string roleName) =>
        roleName switch
        {
            "CSP.Admin" => string.IsNullOrWhiteSpace(CspAdmin) ? null : CspAdmin,
            _ => null,
        };
}

