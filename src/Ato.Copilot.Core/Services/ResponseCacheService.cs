using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Observability;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// In-memory response cache with per-subscription scoping, TTL registry,
/// and cache hit/miss metrics (FR-016, FR-019).
///
/// WM-BUG-3 / #686: Tenant isolation is enforced HERE as the SINGLE
/// enforcement point. <see cref="ITenantContextAccessor"/> (singleton,
/// AsyncLocal-backed) is resolved per-operation so a singleton cache service
/// never captures a stale scoped tenant. If no tenant is resolved the service
/// fails closed — no entry is served or written.
/// </summary>
public class ResponseCacheService
{
    private readonly IMemoryCache _cache;
    private readonly HttpMetrics _metrics;
    private readonly CachingOptions _options;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ILogger<ResponseCacheService> _logger;
    private readonly HashSet<string> _trackedKeys = [];
    private readonly object _keysLock = new();

    public ResponseCacheService(
        IMemoryCache cache,
        HttpMetrics metrics,
        IOptions<CachingOptions> options,
        ITenantContextAccessor tenantAccessor,
        ILogger<ResponseCacheService> logger)
    {
        _cache = cache;
        _metrics = metrics;
        _options = options.Value;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Gets a cached response or executes the factory and caches the result.
    /// Composite key: SHA256(tenantId:toolName:paramsJson:subscriptionId).
    /// Fails closed: if no tenant is resolved, the factory is always called
    /// and the result is never cached.
    /// </summary>
    public async Task<string> GetOrSetAsync(
        string toolName,
        string paramsJson,
        string subscriptionId,
        Func<Task<string>> factory,
        bool isMutation = false)
    {
        // Resolve tenant per-operation (AsyncLocal — safe for singleton service).
        var tenantId = ResolveTenantId();

        if (tenantId is null)
        {
            // Fail closed: no tenant → no cache read or write.
            _logger.LogWarning("ResponseCacheService: no tenant resolved for tool={Tool}; cache bypassed (fail-closed).", toolName);
            _metrics.RecordCacheMiss("response");
            return await factory();
        }

        var cacheKey = ComputeKey(tenantId.Value, toolName, paramsJson, subscriptionId);

        if (isMutation)
        {
            _cache.Remove(cacheKey);
            RemoveTrackedKey(cacheKey);
            var result = await factory();
            _logger.LogDebug("Cache bypassed for mutation: {Tool}", toolName);
            return result;
        }

        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
        {
            _metrics.RecordCacheHit("response");
            _logger.LogDebug("Cache HIT for {Tool} key={Key}", toolName, cacheKey[..8]);
            return cached;
        }

        _metrics.RecordCacheMiss("response");
        var response = await factory();

        var ttl = GetTtl(toolName);
        var entryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(ttl))
            .SetSize(1);

        _cache.Set(cacheKey, response, entryOptions);
        TrackKey(cacheKey, tenantId.Value, toolName, subscriptionId);

        _logger.LogDebug("Cache SET for {Tool} TTL={Ttl}s key={Key}", toolName, ttl, cacheKey[..8]);
        return response;
    }

    /// <summary>
    /// Returns the cache status for a given key: "HIT" or "MISS".
    /// Returns "MISS" when no tenant is resolved (fail-closed).
    /// </summary>
    public string GetCacheStatus(string toolName, string paramsJson, string subscriptionId)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null)
            return "MISS";

        var cacheKey = ComputeKey(tenantId.Value, toolName, paramsJson, subscriptionId);
        return _cache.TryGetValue(cacheKey, out _) ? "HIT" : "MISS";
    }

    /// <summary>
    /// Clears cache entries matching the given scope filter.
    /// </summary>
    public int ClearByScope(string? toolName = null, string? subscriptionId = null)
    {
        var keysToRemove = new List<string>();
        lock (_keysLock)
        {
            foreach (var key in _trackedKeys)
            {
                // Keys are tracked as "sha:tenantId:toolName:subscriptionId"
                var match = true;
                if (toolName != null && !key.Contains($":{toolName}:"))
                    match = false;
                if (subscriptionId != null && !key.EndsWith($":{subscriptionId}"))
                    match = false;
                if (match)
                    keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            var sha = key.Split(':')[0];
            _cache.Remove(sha);
            RemoveTrackedKey(key);
        }

        _logger.LogInformation("Cleared {Count} cache entries for tool={Tool} sub={Sub}",
            keysToRemove.Count, toolName ?? "*", subscriptionId ?? "*");
        return keysToRemove.Count;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective tenant ID from the ambient AsyncLocal context.
    /// Returns null when no tenant context is available.
    /// </summary>
    private Guid? ResolveTenantId()
    {
        var ctx = _tenantAccessor.Current;
        if (ctx is null)
            return null;

        var id = ctx.EffectiveTenantId;
        return id == Guid.Empty ? null : id;
    }

    private int GetTtl(string toolName)
    {
        var lower = toolName.ToLowerInvariant();
        if (lower.Contains("lookup") || lower.Contains("search") || lower.Contains("nist"))
            return _options.ControlLookupTtlSeconds;
        if (lower.Contains("assessment") || lower.Contains("scan"))
            return _options.AssessmentTtlSeconds;
        return _options.DefaultTtlSeconds;
    }

    private static string ComputeKey(Guid tenantId, string toolName, string paramsJson, string subscriptionId)
    {
        var input = $"{tenantId:N}:{toolName}:{paramsJson}:{subscriptionId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    private void TrackKey(string sha, Guid tenantId, string toolName, string subscriptionId)
    {
        lock (_keysLock)
        {
            _trackedKeys.Add($"{sha}:{tenantId:N}:{toolName}:{subscriptionId}");
        }
    }

    private void RemoveTrackedKey(string trackingKey)
    {
        lock (_keysLock)
        {
            _trackedKeys.Remove(trackingKey);
        }
    }
}
