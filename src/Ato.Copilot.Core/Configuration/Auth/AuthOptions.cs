namespace Ato.Copilot.Core.Configuration.Auth;

/// <summary>
/// Configuration options for Feature 051 (First-class login). Bound from the
/// <c>Auth</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// Per <c>contracts/internal-services.md § 5.1</c>. NOTE: the simulation-enable
/// gate is sourced from Feature 027's <c>CacAuth:SimulationMode</c> flag, NOT
/// from a parallel <c>Auth:Simulation:Enabled</c> flag (analysis C10).
/// </remarks>
public sealed class AuthOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c>.</summary>
    public const string SectionName = "Auth";

    /// <summary>Default authentication method offered on the login page.</summary>
    public LoginAuthMethod DefaultMethod { get; set; } = LoginAuthMethod.Cac;

    /// <summary>Idle-timeout in minutes (5–480, default 30; FR-007 / FR-007a).</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>Lifetime of the HMAC-signed remembered-tenant cookie in days (1–365, default 30; FR-012).</summary>
    public int RememberTenantCookieDays { get; set; } = 30;

    /// <summary>Target Azure cloud — Public or US Government.</summary>
    public AzureCloud Cloud { get; set; } = AzureCloud.AzureUSGovernment;

    public AuthCookieOptions Cookie { get; set; } = new();
    public AuthVsCodeOptions VSCode { get; set; } = new();
    public AuthThrottleOptions Throttle { get; set; } = new();
    public AuthTeamsSsoOptions TeamsSso { get; set; } = new();
    public AuthArchiveOptions Archive { get; set; } = new();
    public AuthMsalOptions Msal { get; set; } = new();
    public AuthBrandingOptions Branding { get; set; } = new();
}

/// <summary>
/// Branding shown on the dashboard's <c>/login</c> page (FR-002 / FR-003).
/// All three fields are optional; the SPA falls back to safe defaults
/// ("ATO Copilot", no logo, no support link) when this section is empty.
/// </summary>
public sealed class AuthBrandingOptions
{
    /// <summary>Deployment name shown as the &lt;h1&gt; on the login page. Empty → "ATO Copilot".</summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>Absolute or relative URL to the deployment logo. Empty → no &lt;img&gt; is rendered.</summary>
    public string LogoUrl { get; set; } = string.Empty;

    /// <summary>Support email shown in the login-page footer. Empty → no mailto link is rendered.</summary>
    public string SupportEmail { get; set; } = string.Empty;
}

/// <summary>Cookie-signing configuration (FR-012).</summary>
public sealed class AuthCookieOptions
{
    /// <summary>HMAC-SHA256 signing key (32 bytes, base64). Required outside Development.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Cookie <c>Domain</c> attribute; empty = host-only.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Whether <c>Secure</c> is set on the cookie (HTTPS only).</summary>
    public bool Secure { get; set; } = true;
}

/// <summary>VS Code extension flow configuration.</summary>
public sealed class AuthVsCodeOptions
{
    /// <summary>VS Code login flow — DeviceCode is the only supported value for now.</summary>
    public string Mode { get; set; } = "DeviceCode";
}

/// <summary>
/// Throttle thresholds per 60-second sliding bucket. Selection:
/// <c>ASPNETCORE_ENVIRONMENT == "Development"</c> → <see cref="Development"/>;
/// any other value uses <see cref="Production"/> (analysis C11).
/// Defaults match spec.md FR-034 — Production 20/min/IP + 10/min/identity;
/// Development 100/min/IP + 100/min/identity.
/// </summary>
public sealed class AuthThrottleOptions
{
    public ThrottleBucket Development { get; set; } = new() { PerIpPerMinute = 100, PerIdentityPerMinute = 100 };

    public ThrottleBucket Production { get; set; } = new() { PerIpPerMinute = 20, PerIdentityPerMinute = 10 };
}

/// <summary>One throttle bucket (per environment).</summary>
public sealed class ThrottleBucket
{
    public int PerIpPerMinute { get; set; }

    public int PerIdentityPerMinute { get; set; }
}

/// <summary>Teams SSO configuration (FR-021).</summary>
public sealed class AuthTeamsSsoOptions
{
    public TeamsSsoMode Mode { get; set; } = TeamsSsoMode.Optional;

    /// <summary>The OAuth connection name configured in the Bot resource. Required when <see cref="Mode"/> = <see cref="TeamsSsoMode.Required"/>.</summary>
    public string ConnectionName { get; set; } = string.Empty;
}

/// <summary>Teams SSO modes (FR-021).</summary>
public enum TeamsSsoMode
{
    /// <summary>Teams SSO required — bot startup fails without a configured connection.</summary>
    Required,

    /// <summary>Teams SSO offered if configured; deviceCode otherwise.</summary>
    Optional,

    /// <summary>Teams SSO disabled — deviceCode-only.</summary>
    Disabled,
}

/// <summary>Login-audit archive configuration (FR-036a / R3).</summary>
public sealed class AuthArchiveOptions
{
    public ArchiveSinkKind Sink { get; set; } = ArchiveSinkKind.FileSystem;

    /// <summary>Hour-of-day in UTC when the daily archive job runs (0–23, default 2).</summary>
    public int RunHourUtc { get; set; } = 2;

    public string AzureBlobAccountUrl { get; set; } = string.Empty;

    public string AzureBlobContainer { get; set; } = "audit-archive";

    public string FileSystemRoot { get; set; } = "./archive";
}

/// <summary>Archive-sink kinds.</summary>
public enum ArchiveSinkKind
{
    AzureBlobAppend,
    FileSystem,
}

/// <summary>MSAL.js bootstrap configuration emitted by <c>GET /api/auth/login-config</c>.</summary>
public sealed class AuthMsalOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string Authority { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    public string PostLogoutRedirectUri { get; set; } = string.Empty;
}

/// <summary>Available login methods (FR-001 / FR-002).</summary>
public enum LoginAuthMethod
{
    Cac,
    Entra,
}

/// <summary>Target Azure cloud.</summary>
public enum AzureCloud
{
    AzurePublic,
    AzureUSGovernment,
}
