using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Ato.Copilot.Mcp.Middleware;

/// <summary>
/// Feature 051 T039 — extracts the three forensic fields every login
/// endpoint needs to populate when calling
/// <see cref="Core.Interfaces.Auth.ILoginAuditService.AppendAsync"/>:
/// the source IP (forwarded-aware, capped at 45 chars per IPv6),
/// the user agent (truncated at 512), and a correlation id
/// (W3C Activity / TraceIdentifier / synthesised, in that order).
/// </summary>
/// <remarks>
/// <para>
/// Per <c>data-model.md § 1.6</c> the underlying entity caps
/// <c>SourceIp</c> at 45, <c>UserAgent</c> at 512, and
/// <c>CorrelationId</c> at 64. This accessor enforces the caps so the
/// caller does not have to.
/// </para>
/// <para>
/// Registered as <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped"/>
/// alongside an <see cref="IHttpContextAccessor"/>. It's intentionally a
/// concrete class (not an interface) — there's nothing to mock, and an
/// interface would just add a redundant DI seam.
/// </para>
/// </remarks>
public sealed class LoginAuditContextAccessor
{
    private const int IpMaxLength = 45;
    private const int UserAgentMaxLength = 512;
    private const int CorrelationIdMaxLength = 64;
    private const string Unknown = "unknown";

    /// <summary>
    /// Extract the audit context from the supplied <see cref="HttpContext"/>.
    /// Never throws — every failure path returns the <c>"unknown"</c>
    /// sentinel so an audit row can still be written.
    /// </summary>
    public LoginAuditContext FromHttpContext(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sourceIp = ResolveSourceIp(context);
        var userAgent = ResolveUserAgent(context);
        var correlationId = ResolveCorrelationId(context);

        return new LoginAuditContext(sourceIp, userAgent, correlationId);
    }

    private static string ResolveSourceIp(HttpContext context)
    {
        // X-Forwarded-For takes precedence (we're typically behind App
        // Gateway / Container Apps ingress). Take the FIRST IP from a
        // comma-separated chain — that's the originating client.
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?
                .Trim();
            if (!string.IsNullOrWhiteSpace(first))
            {
                return Cap(first, IpMaxLength);
            }
        }

        var remote = context.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(remote)
            ? Unknown
            : Cap(remote, IpMaxLength);
    }

    private static string ResolveUserAgent(HttpContext context)
    {
        var ua = context.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua)
            ? Unknown
            : Cap(ua, UserAgentMaxLength);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        // Honour the existing CorrelationIdMiddleware convention used by
        // AuditLoggingMiddleware: HttpContext.Items["CorrelationId"]
        // first, then W3C Activity.Current.Id, then TraceIdentifier, then
        // synthesise a 32-char id.
        if (context.Items.TryGetValue("CorrelationId", out var cid) &&
            cid is string s &&
            !string.IsNullOrWhiteSpace(s))
        {
            return Cap(s, CorrelationIdMaxLength);
        }

        var activityId = Activity.Current?.Id;
        if (!string.IsNullOrWhiteSpace(activityId))
        {
            return Cap(activityId, CorrelationIdMaxLength);
        }

        var trace = context.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(trace))
        {
            return Cap(trace, CorrelationIdMaxLength);
        }

        return Guid.NewGuid().ToString("N")[..32];
    }

    private static string Cap(string value, int max)
        => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Forensic context for a single login audit row. All three fields are
/// already truncated to the column caps documented in
/// <c>data-model.md § 1.6</c>; downstream callers can pass them directly
/// to <see cref="Core.Interfaces.Auth.LoginAuditEventDraft"/>.
/// </summary>
public sealed record LoginAuditContext(
    string SourceIp,
    string UserAgent,
    string CorrelationId);
