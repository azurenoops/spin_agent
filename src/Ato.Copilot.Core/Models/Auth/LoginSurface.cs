namespace Ato.Copilot.Core.Models.Auth;

/// <summary>
/// Which client surface produced a <see cref="LoginAuditEvent"/> row.
/// Persisted as the enum name via <c>HasConversion&lt;string&gt;()</c>
/// per Feature 051 data-model.md § 1.2.
/// </summary>
public enum LoginSurface
{
    /// <summary>The React dashboard SPA.</summary>
    Dashboard = 0,
    /// <summary>The VS Code extension (device-code flow).</summary>
    VSCode = 1,
    /// <summary>The M365 Teams bot.</summary>
    M365 = 2,
    /// <summary>The web chat app.</summary>
    Chat = 3,
}
