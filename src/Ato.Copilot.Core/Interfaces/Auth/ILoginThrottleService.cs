namespace Ato.Copilot.Core.Interfaces.Auth;

/// <summary>
/// Feature 051 (FR-034 / FR-035) — per-IP AND per-identity failed-login
/// counter backed by <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
/// Decision latency target: ≤ 5 ms p95 per <c>plan.md</c>.
/// </summary>
/// <remarks>
/// Per <c>contracts/internal-services.md § 2</c> and <c>research.md § R7</c>.
/// The HTTP middleware that consumes this service decides the response code
/// (<c>429</c>) and emits the audit row — the service itself is pure
/// decision logic.
/// </remarks>
public interface ILoginThrottleService
{
    /// <summary>
    /// Register a failed login attempt and return whether the next attempt
    /// should be allowed.
    /// </summary>
    /// <param name="sourceIp">
    /// The originating IP address (capped at 45 chars per RFC 4291 IPv6
    /// representation). Empty or whitespace is normalised to
    /// <c>"unknown"</c>.
    /// </param>
    /// <param name="identityKey">
    /// A stable identity key — typically the Entra <c>oid</c>, or
    /// <c>tid:{tid}</c> when only the tenant is known, or <c>null</c>
    /// (treated as <c>"anonymous"</c>) for pre-session events.
    /// </param>
    Task<LoginThrottleDecision> RegisterAttemptAsync(
        string sourceIp,
        string? identityKey,
        CancellationToken ct = default);

    /// <summary>
    /// Reset counters for an identity after a successful login. The per-IP
    /// counter is NOT reset (a successful sign-in from the same IP does
    /// not invalidate prior failures from that IP — that IP may be a
    /// shared NAT or proxy).
    /// </summary>
    Task ResetIdentityAsync(string identityKey, CancellationToken ct = default);

    /// <summary>
    /// Feature 051 T143 — peek the current per-IP and per-identity counters
    /// WITHOUT incrementing them, and return whether the next attempt
    /// would be allowed based on the existing counts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <c>LoginThrottleMiddleware</c> for the INBOUND short-circuit
    /// path: when the per-IP or per-identity counter has already reached
    /// the configured cap, the next request must be denied with a
    /// <c>429</c> BEFORE running the auth pipeline (otherwise the request
    /// would consume a slot reserved for legit sign-ins) — and per
    /// analysis C17 we MUST NOT increment the counter on a request that
    /// doesn't actually result in a failed-auth response.
    /// </para>
    /// <para>
    /// Returns <c>Allowed = true</c> when both counters are below their
    /// caps (i.e. there is still headroom for another attempt). Returns
    /// <c>Allowed = false</c> when EITHER counter has reached OR exceeded
    /// its cap. <c>CurrentIpCount</c> and <c>CurrentIdentityCount</c> on
    /// the returned decision are the CURRENT values (no increment).
    /// </para>
    /// </remarks>
    Task<LoginThrottleDecision> PeekAsync(
        string sourceIp,
        string? identityKey,
        CancellationToken ct = default);
}

/// <summary>
/// The result of a single <see cref="ILoginThrottleService.RegisterAttemptAsync"/>
/// call. Per <c>contracts/internal-services.md § 2.2</c>.
/// </summary>
/// <param name="Allowed">
/// <c>true</c> when the next attempt is below the per-IP AND per-identity
/// thresholds; <c>false</c> when either threshold is exceeded.
/// </param>
/// <param name="RetryAfter">
/// The wall-clock duration remaining in the current 60-second bucket. The
/// caller emits this as a <c>Retry-After</c> response header.
/// </param>
/// <param name="CurrentIpCount">
/// The per-IP counter AFTER this attempt was registered. Surfaced for
/// metrics / audit purposes.
/// </param>
/// <param name="CurrentIdentityCount">
/// The per-identity counter AFTER this attempt was registered.
/// </param>
public sealed record LoginThrottleDecision(
    bool Allowed,
    TimeSpan RetryAfter,
    int CurrentIpCount,
    int CurrentIdentityCount);
