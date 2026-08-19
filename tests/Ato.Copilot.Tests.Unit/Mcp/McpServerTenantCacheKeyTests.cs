using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.Compliance.Agents;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Server;

namespace Ato.Copilot.Tests.Unit.Mcp;

/// <summary>
/// WM-BUG-3 / #686 regression tests: tenant isolation is now the sole
/// responsibility of ResponseCacheService (Cyborg architecture). McpServer no
/// longer carries per-call tenant prefixing — that logic was deleted.
///
/// These tests verify McpServer's end-to-end caching behaviour with the
/// new service signature (ITenantContextAccessor injected into the cache service).
/// </summary>
public class McpServerTenantCacheKeyTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Mock<ITenantContextAccessor> MakeAccessor(Guid tenantId)
    {
        var ctxMock = new Mock<ITenantContext>();
        ctxMock.Setup(c => c.EffectiveTenantId).Returns(tenantId);

        var accessorMock = new Mock<ITenantContextAccessor>();
        accessorMock.Setup(a => a.Current).Returns(ctxMock.Object);
        return accessorMock;
    }

    private static (McpServer server, ResponseCacheService cacheService) CreateServer(
        Mock<ITenantContextAccessor> accessorMock,
        IMemoryCache? sharedCache = null)
    {
        var complianceAgentMock = TestMockFactory.CreateComplianceAgentMock();
        complianceAgentMock
            .Setup(a => a.ProcessAsync(
                It.IsAny<string>(),
                It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<string>>()))
            .ReturnsAsync(new AgentResponse
            {
                Success = true,
                Response = "ok",
                AgentName = "Compliance Agent"
            });

        var orchestrator = TestMockFactory.CreateOrchestrator(complianceAgentMock.Object);

        var memCache = sharedCache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 200 });
        var cacheService = new ResponseCacheService(
            memCache,
            new HttpMetrics(),
            Options.Create(new CachingOptions()),
            accessorMock.Object,
            Mock.Of<ILogger<ResponseCacheService>>());

        var server = new McpServer(
            (Ato.Copilot.Mcp.Tools.ComplianceMcpTools)null!,
            (Ato.Copilot.Mcp.Tools.KnowledgeBaseMcpTools)null!,
            complianceAgentMock.Object,
            (Ato.Copilot.Agents.Configuration.Agents.ConfigurationAgent)null!,
            null!,
            orchestrator,
            Enumerable.Empty<BaseTool>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(),
            cacheService,
            Options.Create(new PaginationOptions()),
            new OfflineModeService(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                Mock.Of<ILogger<OfflineModeService>>()),
            Mock.Of<Ato.Copilot.Core.Interfaces.Provenance.IModelCallLedger>(),
            Mock.Of<ILogger<McpServer>>());

        return (server, cacheService);
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Two authenticated tenants sharing the same MemoryCache and the same
    /// subscriptionId + message must each get independent cache entries.
    /// The accessor switches tenant between calls to simulate two different
    /// per-request scopes writing into one shared in-process cache.
    /// </summary>
    [Fact]
    public async Task ProcessChatRequestAsync_DifferentAuthenticatedTenants_DoNotShareCacheEntry()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        const string sharedSub = "sub-shared";

        // Shared MemoryCache — isolation must be proven at the key level.
        var sharedMem = new MemoryCache(new MemoryCacheOptions { SizeLimit = 400 });

        // Tenant A populates the cache.
        var accessorA = MakeAccessor(tenantAId);
        var (serverA, _) = CreateServer(accessorA, sharedMem);
        var ctxA = new Dictionary<string, object> { ["subscriptionId"] = sharedSub };
        await serverA.ProcessChatRequestAsync("Run assessment", context: ctxA);

        // Tenant B — same shared memory, same params. Agent must be invoked (cache miss).
        var agentBMock = TestMockFactory.CreateComplianceAgentMock();
        var agentBInvoked = false;
        agentBMock
            .Setup(a => a.ProcessAsync(
                It.IsAny<string>(),
                It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<string>>()))
            .ReturnsAsync(() =>
            {
                agentBInvoked = true;
                return new AgentResponse { Success = true, Response = "tenant-b", AgentName = "Compliance Agent" };
            });

        var orchestratorB = TestMockFactory.CreateOrchestrator(agentBMock.Object);
        var accessorB = MakeAccessor(tenantBId);

        var cacheSvcB = new ResponseCacheService(
            sharedMem,
            new HttpMetrics(),
            Options.Create(new CachingOptions()),
            accessorB.Object,
            Mock.Of<ILogger<ResponseCacheService>>());

        var serverB = new McpServer(
            (Ato.Copilot.Mcp.Tools.ComplianceMcpTools)null!,
            (Ato.Copilot.Mcp.Tools.KnowledgeBaseMcpTools)null!,
            agentBMock.Object,
            (Ato.Copilot.Agents.Configuration.Agents.ConfigurationAgent)null!,
            null!,
            orchestratorB,
            Enumerable.Empty<BaseTool>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(),
            cacheSvcB,
            Options.Create(new PaginationOptions()),
            new OfflineModeService(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                Mock.Of<ILogger<OfflineModeService>>()),
            Mock.Of<Ato.Copilot.Core.Interfaces.Provenance.IModelCallLedger>(),
            Mock.Of<ILogger<McpServer>>());

        var result = await serverB.ProcessChatRequestAsync("Run assessment",
            context: new Dictionary<string, object> { ["subscriptionId"] = sharedSub });

        result.Should().NotBeNull();
        agentBInvoked.Should().BeTrue(
            "Tenant B's agent must be invoked — cross-tenant cache hit must not occur (WM-BUG-3 / #686)");
    }

    /// <summary>
    /// Same authenticated tenant making two identical requests must benefit from
    /// the cache on the second call (regression guard).
    /// </summary>
    [Fact]
    public async Task ProcessChatRequestAsync_SameAuthenticatedTenant_SecondCallHitsCache()
    {
        var tenantId = Guid.NewGuid();
        const string sub = "sub-1";

        var agentMock = TestMockFactory.CreateComplianceAgentMock();
        var invocationCount = 0;
        agentMock
            .Setup(a => a.ProcessAsync(
                It.IsAny<string>(),
                It.IsAny<AgentConversationContext>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<string>>()))
            .ReturnsAsync(() =>
            {
                invocationCount++;
                return new AgentResponse { Success = true, Response = "cached-response", AgentName = "Compliance Agent" };
            });

        var orchestrator = TestMockFactory.CreateOrchestrator(agentMock.Object);
        var accessor = MakeAccessor(tenantId);

        var cacheService = new ResponseCacheService(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 200 }),
            new HttpMetrics(),
            Options.Create(new CachingOptions()),
            accessor.Object,
            Mock.Of<ILogger<ResponseCacheService>>());

        var server = new McpServer(
            (Ato.Copilot.Mcp.Tools.ComplianceMcpTools)null!,
            (Ato.Copilot.Mcp.Tools.KnowledgeBaseMcpTools)null!,
            agentMock.Object,
            (Ato.Copilot.Agents.Configuration.Agents.ConfigurationAgent)null!,
            null!,
            orchestrator,
            Enumerable.Empty<BaseTool>(),
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(),
            cacheService,
            Options.Create(new PaginationOptions()),
            new OfflineModeService(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                Mock.Of<ILogger<OfflineModeService>>()),
            Mock.Of<Ato.Copilot.Core.Interfaces.Provenance.IModelCallLedger>(),
            Mock.Of<ILogger<McpServer>>());

        var ctx = new Dictionary<string, object> { ["subscriptionId"] = sub };

        await server.ProcessChatRequestAsync("Run assessment", context: ctx);
        await server.ProcessChatRequestAsync("Run assessment", context: ctx);

        invocationCount.Should().Be(1,
            "same tenant + same params must cache on first call and return cached on second (WM-BUG-3 regression guard)");
    }
}
