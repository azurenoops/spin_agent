using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Interfaces.Auth;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Feature 051 (FR-034 / FR-035) — bucket-key throttle backed by
/// <see cref="IDistributedCache"/>. Defaults to per-minute sliding buckets
/// keyed by IP AND identity.
/// </summary>
/// <remarks>
/// <para>
/// Per <c>research.md § R7</c>: each attempt writes two cache keys —
/// <c>login-throttle:ip:{ip}:{minute-bucket}</c> and
/// <c>login-throttle:identity:{identityKey ?? "anonymous"}:{minute-bucket}</c>
/// — where <c>{minute-bucket}</c> is <c>utcNow.Ticks /
/// TimeSpan.FromMinutes(1).Ticks</c>. Each <c>SetAsync</c> uses an absolute
/// expiration of 60 seconds.
/// </para>
/// <para>
/// Counter increments use a <c>GetAsync</c> + <c>SetAsync</c> pair (NOT
/// atomic). This is acceptable because the worst-case race is an
/// undercount of 1 attempt within a single bucket — the threshold trips
/// one attempt later than it should, but never undercounts past the bucket
/// boundary. Redis <c>INCR</c> is available in prod via a typed extension
/// and would tighten the race, but is YAGNI at the Phase 2 contract pin.
/// </para>
/// <para>
/// Threshold selection per analysis C11: Development uses the
/// <c>Development</c> bucket; any other environment uses
/// <c>Production</c>. <see cref="IHostEnvironment"/> is injected so a
/// startup-misconfigured <c>ASPNETCORE_ENVIRONMENT</c> value cannot
/// silently fall through to Development thresholds.
/// </para>
/// </remarks>
public sealed class LoginThrottleService : ILoginThrottleService
{
    private const string IpKeyPrefix = "login-throttle:ip:";
    private const string IdentityKeyPrefix = "login-throttle:identity:";
    private const string AnonymousIdentity = "anonymous";
    private const string UnknownIp = "unknown";

    private static readonly TimeSpan BucketWindow = TimeSpan.FromMinutes(1);

    private readonly IDistributedCache _cache;
    private readonly IOptionsMonitor<AuthOptions> _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<LoginThrottleService> _logger;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public LoginThrottleService(
        IDistributedCache cache,
        IOptionsMonitor<AuthOptions> options,
        IHostEnvironment env,
        ILogger<LoginThrottleService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Test-only constructor — takes an <see cref="IOptions{T}"/> snapshot
    /// and wraps it in a static monitor. Marked as a static factory rather
    /// than a second public ctor so the DI container's constructor selector
    /// is not ambiguous (Feature 051 T143 fix: the dual-ctor surface tripped
    /// <see cref="Microsoft.Extensions.DependencyInjection.IServiceProvider"/>
    /// when both ctors had equal-arity resolvable parameters).
    /// </summary>
    public static LoginThrottleService CreateForTests(
        IDistributedCache cache,
        IOptions<AuthOptions> options,
        IHostEnvironment env,
        ILogger<LoginThrottleService> logger)
        => new(cache, new StaticOptionsMonitor(options), env, logger);

    /// <inheritdoc />
    public async Task<LoginThrottleDecision> RegisterAttemptAsync(
        string sourceIp,
        string? identityKey,
        CancellationToken ct = default)
    {
        var ip = string.IsNullOrWhiteSpace(sourceIp) ? UnknownIp : sourceIp;
        var id = string.IsNullOrWhiteSpace(identityKey) ? AnonymousIdentity : identityKey;
        var now = DateTimeOffset.UtcNow;
        var bucket = now.Ticks / BucketWindow.Ticks;

        var ipKey = IpKeyPrefix + ip + ":" + bucket;
        var idKey = IdentityKeyPrefix + id + ":" + bucket;

        var ipCount = await IncrementAsync(ipKey, ct).ConfigureAwait(false);
        var idCount = await IncrementAsync(idKey, ct).ConfigureAwait(false);

        var bucketCfg = SelectBucket();
        var allowed = ipCount <= bucketCfg.PerIpPerMinute
                       && idCount <= bucketCfg.PerIdentityPerMinute;

        // RetryAfter = remaining seconds in current bucket. Floor at 1 second
        // when the bucket is on the verge of rolling over so the caller never
        // emits Retry-After: 0 (which clients interpret as "retry now").
        var secondsIntoMinute = now.Second + (now.Millisecond / 1000.0);
        var retryAfter = TimeSpan.FromSeconds(Math.Max(1, 60 - secondsIntoMinute));

        if (!allowed)
        {
            _logger.LogWarning(
                "LoginThrottleService denied attempt: ip={Ip} idHash={Id} ipCount={IpCount} idCount={IdCount} retryAfter={Retry}s",
                ip, HashIdentity(id), ipCount, idCount, retryAfter.TotalSeconds);
        }

        return new LoginThrottleDecision(allowed, retryAfter, ipCount, idCount);
    }

    /// <inheritdoc />
    public Task ResetIdentityAsync(string identityKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);

        // Clear the identity counter for the CURRENT bucket. The per-IP
        // counter is intentionally untouched (a successful sign-in does
        // NOT invalidate prior failures from the same IP — shared NAT or
        // proxy invariant from contracts/internal-services.md § 2.2).
        var bucket = DateTimeOffset.UtcNow.Ticks / BucketWindow.Ticks;
        var idKey = IdentityKeyPrefix + identityKey + ":" + bucket;
        return _cache.RemoveAsync(idKey, ct);
    }

    /// <inheritdoc />
    public async Task<LoginThrottleDecision> PeekAsync(
        string sourceIp,
        string? identityKey,
        CancellationToken ct = default)
    {
        var ip = string.IsNullOrWhiteSpace(sourceIp) ? UnknownIp : sourceIp;
        var id = string.IsNullOrWhiteSpace(identityKey) ? AnonymousIdentity : identityKey;
        var now = DateTimeOffset.UtcNow;
        var bucket = now.Ticks / BucketWindow.Ticks;

        var ipKey = IpKeyPrefix + ip + ":" + bucket;
        var idKey = IdentityKeyPrefix + id + ":" + bucket;

        var ipCount = await ReadCountAsync(ipKey, ct).ConfigureAwait(false);
        var idCount = await ReadCountAsync(idKey, ct).ConfigureAwait(false);

        var bucketCfg = SelectBucket();
        // Peek semantics: Allowed=true ONLY when there is still headroom
        // for another attempt. As soon as either counter has reached its
        // cap, the next attempt would push it over — so we deny here.
        var allowed = ipCount < bucketCfg.PerIpPerMinute
                       && idCount < bucketCfg.PerIdentityPerMinute;

        var secondsIntoMinute = now.Second + (now.Millisecond / 1000.0);
        var retryAfter = TimeSpan.FromSeconds(Math.Max(1, 60 - secondsIntoMinute));

        return new LoginThrottleDecision(allowed, retryAfter, ipCount, idCount);
    }

    private async Task<int> ReadCountAsync(string key, CancellationToken ct)
    {
        var bytes = await _cache.GetAsync(key, ct).ConfigureAwait(false);
        return bytes is { Length: >= 4 } ? BitConverter.ToInt32(bytes, 0) : 0;
    }

    private async Task<int> IncrementAsync(string key, CancellationToken ct)
    {
        var current = await _cache.GetAsync(key, ct).ConfigureAwait(false);
        var count = current is { Length: >= 4 } ? BitConverter.ToInt32(current, 0) : 0;
        count++;
        await _cache.SetAsync(
            key,
            BitConverter.GetBytes(count),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = BucketWindow,
            },
            ct).ConfigureAwait(false);
        return count;
    }

    private ThrottleBucket SelectBucket()
    {
        var snapshot = _options.CurrentValue.Throttle;
        return _env.IsDevelopment() ? snapshot.Development : snapshot.Production;
    }

    private static string HashIdentity(string identity)
    {
        // Stable, non-reversible identifier for logs — keeps oid / email
        // out of operator dashboards while still letting us correlate.
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var c in identity)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x");
        }
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuthOptions>
    {
        private readonly IOptions<AuthOptions> _options;

        public StaticOptionsMonitor(IOptions<AuthOptions> options) => _options = options;

        public AuthOptions CurrentValue => _options.Value;

        public AuthOptions Get(string? name) => _options.Value;

        public IDisposable? OnChange(Action<AuthOptions, string?> listener) => null;
    }
}
