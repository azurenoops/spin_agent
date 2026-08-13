using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for ResponseCacheService (US4 / FR-016 through FR-020).
/// </summary>
public class ResponseCacheServiceTests
{
    private readonly IMemoryCache _cache;
    private readonly HttpMetrics _metrics;
    private readonly ResponseCacheService _service;

    public ResponseCacheServiceTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        _metrics = new HttpMetrics();
        var options = Options.Create(new CachingOptions
        {
            DefaultTtlSeconds = 60,
            ControlLookupTtlSeconds = 120,
            AssessmentTtlSeconds = 30,
            EnableStaleWhileRevalidate = false,
            SizeLimitMb = 256
        });
        _service = new ResponseCacheService(
            _cache, _metrics, options, Mock.Of<ILogger<ResponseCacheService>>());
    }

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_OnFirstRequest()
    {
        var called = false;
        var result = await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => { called = true; return Task.FromResult("response1"); });

        result.Should().Be("response1");
        called.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_OnSecondIdenticalRequest()
    {
        await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response1"));

        var called = false;
        var result = await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => { called = true; return Task.FromResult("response2"); });

        result.Should().Be("response1");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_DifferentSubscriptions_HaveIndependentCache()
    {
        await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response-sub1"));

        var result = await _service.GetOrSetAsync("tool1", "{}", "sub2",
            () => Task.FromResult("response-sub2"));

        result.Should().Be("response-sub2");
    }

    [Fact]
    public void GetCacheStatus_ReturnsMiss_WhenNotCached()
    {
        var status = _service.GetCacheStatus("tool1", "{}", "sub1");
        status.Should().Be("MISS");
    }

    [Fact]
    public async Task GetCacheStatus_ReturnsHit_WhenCached()
    {
        await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response1"));

        var status = _service.GetCacheStatus("tool1", "{}", "sub1");
        status.Should().Be("HIT");
    }

    [Fact]
    public async Task ClearByScope_RemovesCachedEntries()
    {
        await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response1"));

        _service.ClearByScope("tool1", "sub1");

        var called = false;
        await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => { called = true; return Task.FromResult("response2"); });

        called.Should().BeTrue();
    }

    [Fact]
    public async Task IsMutation_BypassesCache()
    {
        await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response1"));

        var result = await _service.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("mutation-result"), isMutation: true);

        result.Should().Be("mutation-result");
    }

    // ─── WM-BUG-3 regression: cross-tenant isolation ──────────────────────

    /// <summary>
    /// Regression test for WM-BUG-3: two different tenants issuing the exact same
    /// tool call with the same params MUST receive independent cache entries.
    /// McpServer prefixes the subscriptionId with the tenant GUID after the fix:
    /// "{tenantId}:{subscriptionId}".
    /// </summary>
    [Fact]
    public async Task GetOrSetAsync_DifferentTenantPrefixes_ProduceIndependentCacheEntries()
    {
        var tenantA = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid().ToString();
        const string innerSub = "sub-shared";
        var subA = $"{tenantA}:{innerSub}";
        var subB = $"{tenantB}:{innerSub}";

        // Tenant A populates the cache
        await _service.GetOrSetAsync("compliance_scan", "{\"resource\":\"vm1\"}", subA,
            () => Task.FromResult("tenant-a-findings"));

        // Tenant B with identical params must NOT receive tenant A's data
        var callCount = 0;
        var result = await _service.GetOrSetAsync("compliance_scan", "{\"resource\":\"vm1\"}", subB,
            () => { callCount++; return Task.FromResult("tenant-b-findings"); });

        result.Should().Be("tenant-b-findings",
            "tenant B must never receive tenant A's cached compliance data (WM-BUG-3)");
        callCount.Should().Be(1,
            "factory must have been invoked — no cross-tenant cache hit should occur");
    }

    [Fact]
    public async Task GetOrSetAsync_SameTenantPrefixSameParams_ReturnsCachedEntry()
    {
        // Sanity: single-tenant cache-hit behaviour must still work after the fix.
        var tenant = Guid.NewGuid().ToString();
        var sub = $"{tenant}:sub-1";

        await _service.GetOrSetAsync("tool", "{}", sub,
            () => Task.FromResult("first-response"));

        var callCount = 0;
        var result = await _service.GetOrSetAsync("tool", "{}", sub,
            () => { callCount++; return Task.FromResult("second-response"); });

        result.Should().Be("first-response",
            "same tenant + same params must still hit the cache (regression guard)");
        callCount.Should().Be(0);
    }
}
