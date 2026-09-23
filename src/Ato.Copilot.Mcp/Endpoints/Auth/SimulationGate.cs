using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Configuration.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Endpoints.Auth;

/// <summary>
/// Feature 051 T125 [US7] — pure-function gate that decides whether the
/// dashboard <c>SimulationPanel</c> is allowed to render. The decision is
/// the FIRST of three layers of the simulation-panel security invariant
/// (see <c>specs/051-login/research.md</c> § R-Summary item 4):
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><c>/api/auth/login-config</c> omits the <c>simulation</c>
///   descriptor unless ALL three gate conditions hold (this class).</item>
///   <item>The SPA's <c>SimulationPanel</c> route guard refuses to mount
///   even if a malicious payload is injected client-side.</item>
///   <item><c>POST /api/auth/simulate</c> returns a BARE 404 in any
///   non-Development environment.</item>
/// </list>
/// <para>The gate uses Feature 027's <c>CacAuth:SimulationMode</c> flag
/// ONLY — per analysis C10 there is NO parallel
/// <c>Auth:Simulation:Enabled</c> flag.</para>
/// </remarks>
public static class SimulationGate
{
    /// <summary>
    /// Returns true iff every condition required to surface the simulation
    /// descriptor is satisfied:
    /// <list type="bullet">
    ///   <item><paramref name="env"/> is <c>Development</c>.</item>
    ///   <item><c>CacAuth:SimulationMode</c> is <c>true</c>.</item>
    ///   <item><c>CacAuth:SimulatedIdentities</c> is non-empty.</item>
    /// </list>
    /// </summary>
    public static bool ShouldSurfaceDescriptor(IHostEnvironment env, CacAuthOptions cacAuth)
    {
        if (env is null) throw new ArgumentNullException(nameof(env));
        if (cacAuth is null) return false;

        if (!env.IsDevelopment()) return false;
        if (!cacAuth.SimulationMode) return false;
        if (cacAuth.SimulatedIdentities is null || cacAuth.SimulatedIdentities.Count == 0)
        {
            return false;
        }
        // Filter out identities that fail the descriptor's own minimum-data
        // contract — an entry with an empty IdentityId can never be selected
        // by POST /api/auth/simulate, so treating "all entries invalid" as
        // "no identities configured" keeps the gate closed.
        foreach (var i in cacAuth.SimulatedIdentities)
        {
            if (!string.IsNullOrWhiteSpace(i.IdentityId))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Builds the wire-shaped descriptor returned by <c>/api/auth/login-config</c>
    /// when the gate is open. Returns <c>null</c> when
    /// <see cref="ShouldSurfaceDescriptor"/> returns <c>false</c>.
    /// </summary>
    public static object? BuildDescriptor(IHostEnvironment env, CacAuthOptions cacAuth)
    {
        if (!ShouldSurfaceDescriptor(env, cacAuth))
        {
            return null;
        }

        // The descriptor wire shape mirrors `specs/051-login/contracts/frontend-types.md § 1`.
        var identities = new List<object>(cacAuth.SimulatedIdentities.Count);
        foreach (var i in cacAuth.SimulatedIdentities)
        {
            if (string.IsNullOrWhiteSpace(i.IdentityId))
            {
                continue;
            }
            identities.Add(new
            {
                id = i.IdentityId,
                displayName = string.IsNullOrWhiteSpace(i.DisplayName)
                    ? i.IdentityId
                    : i.DisplayName,
                persona = string.IsNullOrWhiteSpace(i.Persona) ? "Developer" : i.Persona,
                tenantId = i.TenantId == Guid.Empty ? string.Empty : i.TenantId.ToString(),
                roles = i.Roles?.ToArray() ?? Array.Empty<string>(),
            });
        }
        return new { identities = identities.ToArray() };
    }

    /// <summary>
    /// Lookup an identity by its key. Returns <c>null</c> when the key is
    /// missing or no matching descriptor exists.
    /// </summary>
    public static SimulatedIdentityDescriptor? FindIdentity(
        CacAuthOptions cacAuth,
        string? identityId)
    {
        if (cacAuth is null) return null;
        if (string.IsNullOrWhiteSpace(identityId)) return null;
        if (cacAuth.SimulatedIdentities is null) return null;
        foreach (var i in cacAuth.SimulatedIdentities)
        {
            if (string.Equals(i.IdentityId, identityId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>
    /// Feature 051 T124 [US7] — emits the "simulation blocked" log line with
    /// a <c>severity=Security</c> scope tag attached so downstream SIEM
    /// configured against the Serilog pipeline can elevate the event.
    /// Extracted from <c>POST /api/auth/simulate</c> so the scope-tagging
    /// behavior is unit-testable against a mock <see cref="ILogger"/>.
    /// </summary>
    public static void LogSimulationBlocked(
        ILogger logger,
        string environmentName,
        string? attemptedIdentityId)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));

        var attempt = attemptedIdentityId ?? string.Empty;
        using (logger.BeginScope(new Dictionary<string, object?>
               {
                   ["severity"] = "Security",
                   ["attemptedIdentityId"] = attempt,
                   ["environment"] = environmentName,
               }))
        {
            logger.LogWarning(
                "Simulation blocked by environment or configuration in {Environment} (attemptedIdentityId={AttemptedIdentityId})",
                environmentName, attempt);
        }
    }
}
