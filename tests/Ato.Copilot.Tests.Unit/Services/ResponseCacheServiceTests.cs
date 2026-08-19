using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Interfaces.Tenancy;
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
/// WM-BUG-3 / #686: tenant isolation is now enforced inside the service.
/// </summary>
public class ResponseCacheServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (ResponseCacheService svc, Mock<ITenantContextAccessor> accessorMock) BuildService(
        Guid? tenantId = null)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var metrics = new HttpMetrics();
        var options = Options.Create(new CachingOptions
        {
            DefaultTtlSeconds = 60,
            ControlLookupTtlSeconds = 120,
            AssessmentTtlSeconds = 30,
            EnableStaleWhileRevalidate = false,
            SizeLimitMb = 256
        });

        var accessorMock = new Mock<ITenantContextAccessor>();

        if (tenantId.HasValue)
        {
            var ctxMock = new Mock<ITenantContext>();
            ctxMock.Setup(c => c.EffectiveTenantId).Returns(tenantId.Value);
            accessorMock.Setup(a => a.Current).Returns(ctxMock.Object);
        }
        else
        {
            accessorMock.Setup(a => a.Current).Returns((ITenantContext?)null);
        }

        var svc = new ResponseCacheService(
            cache, metrics, options, accessorMock.Object,
            Mock.Of<ILogger<ResponseCacheService>>());

        return (svc, accessorMock);
    }

    private static ResponseCacheService BuildServiceWithTenant(Guid tenantId)
        => BuildService(tenantId).svc;

    // ── Basic hit / miss ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_OnFirstRequest()
    {
        var svc = BuildServiceWithTenant(Guid.NewGuid());

        var called = false;
        var result = await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => { called = true; return Task.FromResult("response1"); });

        result.Should().Be("response1");
        called.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_OnSecondIdenticalRequest()
    {
        var tenant = Guid.NewGuid();
        var svc = BuildServiceWithTenant(tenant);

        await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response1"));

        var called = false;
        var result = await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => { called = true; return Task.FromResult("response2"); });

        result.Should().Be("response1");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_DifferentSubscriptions_HaveIndependentCache()
    {
        var svc = BuildServiceWithTenant(Guid.NewGuid());

        await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response-sub1"));

        var result = await svc.GetOrSetAsync("tool1", "{}", "sub2",
            () => Task.FromResult("response-sub2"));

        result.Should().Be("response-sub2");
    }

    [Fact]
    public void GetCacheStatus_ReturnsMiss_WhenNotCached()
    {
        var svc = BuildServiceWithTenant(Guid.NewGuid());
        svc.GetCacheStatus("tool1", "{}", "sub1").Should().Be("MISS");
    }

    [Fact]
    public async Task GetCacheStatus_ReturnsHit_WhenCached()
    {
        var svc = BuildServiceWithTenant(Guid.NewGuid());

        await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response1"));

        svc.GetCacheStatus("tool1", "{}", "sub1").Should().Be("HIT");
    }

    [Fact]
    public async Task ClearByScope_RemovesCachedEntries()
    {
        var svc = BuildServiceWithTenant(Guid.NewGuid());

        await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response1"));

        svc.ClearByScope("tool1", "sub1");

        var called = false;
        await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => { called = true; return Task.FromResult("response2"); });

        called.Should().BeTrue();
    }

    [Fact]
    public async Task IsMutation_BypassesCache()
    {
        var svc = BuildServiceWithTenant(Guid.NewGuid());

        await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("response1"));

        var result = await svc.GetOrSetAsync("tool1", "{}", "sub1",
            () => Task.FromResult("mutation-result"), isMutation: true);

        result.Should().Be("mutation-result");
    }

    // ── WM-BUG-3 / #686 regression: cross-tenant isolation ───────────────────

    /// <summary>
    /// Critical regression for WM-BUG-3 / #686: two different tenants issuing the
    /// exact same tool call with the same params and subscriptionId MUST receive
    /// independent cache entries — the service folds EffectiveTenantId into the
    /// key internally, so callers no longer need to prefix anything.
    /// </summary>
    [Fact]
    public async Task GetOrSetAsync_DifferentTenants_ProduceIndependentCacheEntries()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Two service instances — each wired to a different tenant (simulates per-request DI scope).
        var svcA = BuildServiceWithTenant(tenantA);
        var svcB = BuildServiceWithTenant(tenantB);

        // Tenant A populates the cache
        await svcA.GetOrSetAsync("compliance_scan", "{\"resource\":\"vm1\"}", "sub-shared",
            () => Task.FromResult("tenant-a-findings"));

        // Tenant B with identical params must NOT receive tenant A's data
        var callCount = 0;
        var result = await svcB.GetOrSetAsync("compliance_scan", "{\"resource\":\"vm1\"}", "sub-shared",
            () => { callCount++; return Task.FromResult("tenant-b-findings"); });

        result.Should().Be("tenant-b-findings",
            "tenant B must never receive tenant A's cached compliance data (WM-BUG-3 / #686)");
        callCount.Should().Be(1,
            "factory must have been invoked — no cross-tenant cache hit should occur");
    }

    [Fact]
    public async Task GetOrSetAsync_SameTenantSameParams_ReturnsCachedEntry()
    {
        var tenant = Guid.NewGuid();
        var svc = BuildServiceWithTenant(tenant);

        await svc.GetOrSetAsync("tool", "{}", "sub-1",
            () => Task.FromResult("first-response"));

        var callCount = 0;
        var result = await svc.GetOrSetAsync("tool", "{}", "sub-1",
            () => { callCount++; return Task.FromResult("second-response"); });

        result.Should().Be("first-response",
            "same tenant + same params must still hit the cache (regression guard)");
        callCount.Should().Be(0);
    }

    // ── Fail-closed: no tenant ────────────────────────────────────────────────

    /// <summary>
    /// When no tenant is resolved the service must fail closed: factory is always
    /// called and nothing is written or read from cache.
    /// </summary>
    [Fact]
    public async Task GetOrSetAsync_NoTenant_FailsClosed_AlwaysCallsFactory()
    {
        var (svc, _) = BuildService(tenantId: null); // no tenant

        var callCount = 0;
        // First call
        var r1 = await svc.GetOrSetAsync("tool", "{}", "sub",
            () => { callCount++; return Task.FromResult("r1"); });
        // Second call — should NOT hit cache
        var r2 = await svc.GetOrSetAsync("tool", "{}", "sub",
            () => { callCount++; return Task.FromResult("r2"); });

        r1.Should().Be("r1");
        r2.Should().Be("r2");
        callCount.Should().Be(2, "factory must be called every time when no tenant is present");
    }

    [Fact]
    public void GetCacheStatus_NoTenant_ReturnsMiss()
    {
        var (svc, _) = BuildService(tenantId: null);
        svc.GetCacheStatus("tool", "{}", "sub").Should().Be("MISS");
    }

    /// <summary>
    /// #640 regression: when subscriptionId is null (not resolved from request context),
    /// the service must fail closed — factory always called, result never cached.
    /// Prevents cross-subscription cache bleed within the same tenant.
    /// </summary>
    [Fact]
    public async Task GetOrSetAsync_NullSubscriptionId_FailsClosed_AlwaysCallsFactory()
    {
        var tenantId = Guid.NewGuid();
        var (svc, _) = BuildService(tenantId: tenantId);

        var callCount = 0;
        // First call with null subscriptionId
        var r1 = await svc.GetOrSetAsync("tool", "{}", null,
            () => { callCount++; return Task.FromResult("r1"); });
        // Second identical call — must NOT hit cache
        var r2 = await svc.GetOrSetAsync("tool", "{}", null,
            () => { callCount++; return Task.FromResult("r2"); });

        r1.Should().Be("r1");
        r2.Should().Be("r2");
        callCount.Should().Be(2, "factory must be called every time when subscriptionId is null (fail-closed)");
    }

    [Fact]
    public void GetCacheStatus_NullSubscriptionId_ReturnsMiss()
    {
        var tenantId = Guid.NewGuid();
        var (svc, _) = BuildService(tenantId: tenantId);
        svc.GetCacheStatus("tool", "{}", null).Should().Be("MISS",
            "null subscriptionId must fail closed — no cache read permitted");
    }
}
