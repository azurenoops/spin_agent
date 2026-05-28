using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;

namespace Ato.Copilot.Mcp.Middleware;

/// <summary>
/// Middleware for compliance-level authorization checks.
/// Validates user claims against required compliance roles and enforces
/// tool-level RBAC: Auditor is read-only, Analyst cannot approve, Administrator has full access.
/// Also enforces the Tier 2 CAC gate: Tier 2 tools require an active CAC session.
/// </summary>
public class ComplianceAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ComplianceAuthorizationMiddleware> _logger;

    /// <summary>Tools that modify compliance state (write operations).</summary>
    private static readonly HashSet<string> WriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "compliance_remediate",
        "compliance_validate_remediation",
        "compliance_remediation_plan",
        "compliance_collect_evidence"
    };

    /// <summary>Tools that require administrator/officer approval authority.</summary>
    private static readonly HashSet<string> ApprovalTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "compliance_validate_remediation",
        "pim_approve_request",
        "pim_deny_request"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceAuthorizationMiddleware"/> class.
    /// </summary>
    public ComplianceAuthorizationMiddleware(RequestDelegate next, ILogger<ComplianceAuthorizationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Processes the HTTP request, enforcing authentication and role-based tool access.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for health checks
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // Feature 051: skip the dashboard's first-class login surface. These
        // endpoints carry their own auth/authorization contract per
        // specs/051-login/contracts/http-api.md:
        //   * GET /api/auth/login-config is intentionally public.
        //   * GET /api/auth/me / POST /api/auth/signout / select-tenant are
        //     authenticated but ROLE-AGNOSTIC — a user with no compliance
        //     role must still be able to read their own identity, log out,
        //     and pick a tenant.
        if (context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await _next(context);
            return;
        }

        // In development or stdio mode, skip auth
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (environment == "Development")
        {
            await _next(context);
            return;
        }

        // Tool-level gating
        var toolName = context.Items["ToolName"] as string;

        // ── Tier 2 CAC gate ─────────────────────────────────────────────
        // Before any role checks, verify that Tier 2 tools have an active CAC session.
        // Tier 1 tools pass through without authentication.
        if (!string.IsNullOrEmpty(toolName) && AuthTierClassification.IsTier2(toolName))
        {
            var userId = context.User?.FindFirst("oid")?.Value
                      ?? context.User?.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Tier 2 tool {Tool} blocked — no authenticated user", toolName);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "error",
                    data = new
                    {
                        errorCode = "AUTH_REQUIRED",
                        message = $"'{toolName}' requires CAC/PIV authentication. Authenticate with your smart card to access Azure operations.",
                        suggestion = GetClientSpecificSuggestion(context)
                    },
                    metadata = new { toolName }
                });
                return;
            }

            // Resolve ICacSessionService from the request scope
            var cacService = context.RequestServices.GetService<ICacSessionService>();
            if (cacService != null)
            {
                var hasActiveSession = await cacService.IsSessionActiveAsync(userId, context.RequestAborted);
                if (!hasActiveSession)
                {
                    // ── Mid-operation session expiration (FR-026 / T059) ─────
                    // If we previously had a session that just expired, return
                    // OPERATION_PAUSED so the client can re-auth and resume.
                    var previousSessionId = context.Items["SessionId"] as string;
                    if (!string.IsNullOrEmpty(previousSessionId))
                    {
                        _logger.LogWarning(
                            "Tier 2 tool {Tool} paused for user {UserId} — session expired mid-operation",
                            toolName, userId);
                        context.Response.StatusCode = 202;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            status = "error",
                            data = new
                            {
                                errorCode = "OPERATION_PAUSED",
                                message = $"Your CAC session expired while '{toolName}' was in progress. Re-authenticate to resume.",
                                suggestion = "Re-authenticate with your CAC/PIV smart card. The operation will resume automatically.",
                                context = new
                                {
                                    toolName,
                                    previousSessionId,
                                    pausedAt = DateTimeOffset.UtcNow.ToString("o")
                                }
                            },
                            metadata = new { toolName }
                        });
                        return;
                    }

                    _logger.LogWarning("Tier 2 tool {Tool} blocked for user {UserId} — no active CAC session",
                        toolName, userId);
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        status = "error",
                        data = new
                        {
                            errorCode = "AUTH_REQUIRED",
                            message = $"'{toolName}' requires an active CAC session. Your session may have expired.",
                            suggestion = GetClientSpecificSuggestion(context)
                        },
                        metadata = new { toolName }
                    });
                    return;
                }
            }
        }

        // ── PIM Tier Enforcement (FR-001, FR-013, FR-034) ───────────────
        // After verifying CAC session, check if the tool requires PIM elevation.
        // Tier 2a (Read) requires Reader-level PIM role.
        // Tier 2b (Write) requires Contributor-level (or higher) PIM role.
        // Skip PIM checks in Development mode (RequirePim=false per FR-036).
        if (!string.IsNullOrEmpty(toolName))
        {
            var requiredPimTier = AuthTierClassification.GetRequiredPimTier(toolName);
            if (requiredPimTier != PimTier.None)
            {
                var pimUserId = context.User?.FindFirst("oid")?.Value
                             ?? context.User?.FindFirst("sub")?.Value;

                if (!string.IsNullOrEmpty(pimUserId))
                {
                    var pimService = context.RequestServices.GetService<IPimService>();
                    if (pimService != null)
                    {
                        var activeRoles = await pimService.ListActiveRolesAsync(pimUserId, context.RequestAborted);

                        if (activeRoles == null || activeRoles.Count == 0)
                        {
                            _logger.LogWarning(
                                "PIM elevation required for tool {Tool} — user {UserId} has no active PIM roles",
                                toolName, pimUserId);
                            context.Response.StatusCode = 403;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                status = "error",
                                data = new
                                {
                                    errorCode = "PIM_ELEVATION_REQUIRED",
                                    message = $"'{toolName}' requires PIM elevation. You have no active PIM roles.",
                                    requiredPimTier = requiredPimTier.ToString(),
                                    currentElevation = "None",
                                    suggestion = "Use 'pim_activate_role' to activate an eligible PIM role before running this operation."
                                },
                                metadata = new { toolName }
                            });
                            return;
                        }

                        if (requiredPimTier == PimTier.Write)
                        {
                            // Tier 2b requires Contributor or higher — check if any active role is above Reader
                            var hasWriteRole = activeRoles.Any(r =>
                                !r.RoleName.Equals("Reader", StringComparison.OrdinalIgnoreCase));

                            if (!hasWriteRole)
                            {
                                var currentRoles = string.Join(", ", activeRoles.Select(r => r.RoleName));
                                _logger.LogWarning(
                                    "Insufficient PIM tier for tool {Tool} — user {UserId} has Reader PIM but tool requires Write",
                                    toolName, pimUserId);
                                context.Response.StatusCode = 403;
                                await context.Response.WriteAsJsonAsync(new
                                {
                                    status = "error",
                                    data = new
                                    {
                                        errorCode = "INSUFFICIENT_PIM_TIER",
                                        message = $"'{toolName}' requires a write-eligible PIM role (Contributor or higher). Your current elevation: {currentRoles}.",
                                        requiredPimTier = requiredPimTier.ToString(),
                                        currentElevation = currentRoles,
                                        suggestion = "Use 'pim_activate_role' with a Contributor-eligible role to proceed."
                                    },
                                    metadata = new { toolName }
                                });
                                return;
                            }
                        }
                        // Tier 2a (Read): any active PIM role suffices — already validated above (count > 0)
                    }
                }
            }
        }

        // Check for authenticated user
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            // For non-tool endpoints (tools listing, root), or Tier 1 tools, allow through
            if (string.IsNullOrEmpty(toolName) || !AuthTierClassification.IsTier2(toolName))
            {
                await _next(context);
                return;
            }

            _logger.LogWarning("Unauthorized access attempt from {IP}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required" });
            return;
        }

        // Verify compliance reader role minimum
        var hasRole = context.User.IsInRole(ComplianceRoles.Viewer) ||
                      context.User.IsInRole(ComplianceRoles.Analyst) ||
                      context.User.IsInRole(ComplianceRoles.Auditor) ||
                      context.User.IsInRole(ComplianceRoles.Administrator) ||
                      context.User.IsInRole(ComplianceRoles.PlatformEngineer);

        if (!hasRole)
        {
            _logger.LogWarning("Access denied for user {User} — missing compliance role",
                context.User.Identity?.Name);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Insufficient compliance permissions" });
            return;
        }
        if (!string.IsNullOrEmpty(toolName))
        {
            // Auditor: read-only — deny write tools
            if (context.User.IsInRole(ComplianceRoles.Auditor) && !context.User.IsInRole(ComplianceRoles.Administrator))
            {
                if (WriteTools.Contains(toolName))
                {
                    _logger.LogWarning("Auditor {User} denied access to write tool {Tool}",
                        context.User.Identity?.Name, toolName);
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Auditors have read-only access. Write operations require Analyst or Administrator role."
                    });
                    return;
                }
            }

            // Analyst/Viewer: deny approval tools
            if ((context.User.IsInRole(ComplianceRoles.Analyst) || context.User.IsInRole(ComplianceRoles.Viewer))
                && !context.User.IsInRole(ComplianceRoles.Administrator))
            {
                if (ApprovalTools.Contains(toolName))
                {
                    _logger.LogWarning("User {User} denied access to approval tool {Tool}",
                        context.User.Identity?.Name, toolName);
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Approval operations require Administrator role."
                    });
                    return;
                }
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Checks whether a given tool name requires write access.
    /// </summary>
    public static bool IsWriteTool(string toolName) => WriteTools.Contains(toolName);

    /// <summary>
    /// Checks whether a given tool name requires approval authority.
    /// </summary>
    public static bool IsApprovalTool(string toolName) => ApprovalTools.Contains(toolName);

    /// <summary>
    /// Returns a client-appropriate authentication suggestion based on detected client type (T073).
    /// </summary>
    internal static string GetClientSpecificSuggestion(HttpContext context)
    {
        var clientType = context.Items.TryGetValue("ClientType", out var ct) && ct is ClientType type
            ? type
            : CacAuthenticationMiddleware.DetectClientType(context);

        return clientType switch
        {
            ClientType.VSCode => "Click the notification in VS Code to authenticate with your CAC/PIV card.",
            ClientType.Teams => "Tap the authentication button to re-authenticate with your CAC/PIV smart card.",
            ClientType.CLI => "Set the PLATFORM_COPILOT_TOKEN environment variable with a valid JWT to authenticate.",
            ClientType.Web => "You will be redirected to the login page to authenticate with your CAC/PIV card.",
            _ => "Re-authenticate with your CAC/PIV smart card to continue."
        };
    }
}
