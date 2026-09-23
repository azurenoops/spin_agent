using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Resilience;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.State.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Chat;

/// <summary>
/// Real MCP route/envelope tests with synthetic prevalidated workspaces and an in-memory
/// thread stand-in. Membership/middleware authorization is covered by WorkspaceMembershipTests.
/// No model, production tenant or Azure calls are made.
/// </summary>
public sealed class WorkspaceChatHttpTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid DirectoryA = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid DirectoryB = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid SubjectA = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SubjectB = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid Person = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private readonly MemoryAgent _agent = new();
    private readonly RecordingTool _tool = new();
    private bool _canAuthor;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddRateLimiter(options =>
        {
            foreach (var name in new[] { "chat", "stream", "jsonrpc" })
                options.AddPolicy(name, _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
        });
        var http = new HttpContextAccessor();
        builder.Services.AddSingleton<IHttpContextAccessor>(http);
        var workspace = new Mock<IWorkspaceService>();
        workspace.SetupGet(w => w.Current).Returns(() =>
        {
            var request = http.HttpContext!.Request;
            if (!Guid.TryParse(request.Headers["X-Workspace-Tenant-Id"], out var tenant)
                || tenant != TenantA && tenant != TenantB) return null;
            return new("organization", tenant, "Synthetic organization",
                request.Headers["X-Workspace-Mode"].FirstOrDefault() ?? "ordinary", Person,
                ["ISSO"], new(false, false, false));
        });
        builder.Services.AddSingleton(workspace.Object);
        builder.Services.AddScoped<ITenantContext>(_ =>
        {
            var current = workspace.Object.Current!;
            return new TenantContext(current.TenantId!.Value)
            {
                IsWorkspaceRequest = true, PersonId = current.PersonId, IsCspAdmin = current.Mode == "support"
            };
        });
        builder.Services.AddSingleton<IConversationIdentityAccessor, WorkspaceChatScope>();
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(a => a.CanReadAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenant, Guid? person, string system, bool _, CancellationToken _) =>
                person == Person && (tenant == TenantA && system == "system-a" || tenant == TenantB && system == "system-b"));
        access.Setup(a => a.GetAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenant, Guid? person, string system, bool _, CancellationToken _) =>
                new SystemWorkspaceAccessResponse(system, [],
                    new(person == Person && (tenant == TenantA && system == "system-a" || tenant == TenantB && system == "system-b"),
                        false, false, _canAuthor, false, false, false, false, false)));
        builder.Services.AddSingleton(access.Object);
        var cache = new ResponseCacheService(new MemoryCache(new MemoryCacheOptions()), new HttpMetrics(),
            Options.Create(new CachingOptions()), Mock.Of<ITenantContextAccessor>(), NullLogger<ResponseCacheService>.Instance);
        var offline = new OfflineModeService(new ConfigurationBuilder().Build(), NullLogger<OfflineModeService>.Instance);
        var server = new McpServer(null!, null!, null!, null!, null!,
            new AgentOrchestrator([_agent], NullLogger<AgentOrchestrator>.Instance), [_tool], http,
            Mock.Of<IPathSanitizationService>(), cache, Options.Create(new PaginationOptions()),
            offline, Mock.Of<IModelCallLedger>(), NullLogger<McpServer>.Instance);
        builder.Services.AddSingleton(new SseEventBuffer(Options.Create(new StreamingOptions())));
        _app = builder.Build();
        _app.UseRouting();
        _app.UseRateLimiter();
        _app.Use(async (context, next) =>
        {
            // The test fixture supplies an already authenticated directory principal.
            context.User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("tid", context.Request.Headers["X-Test-Directory"].ToString()),
                new Claim("oid", context.Request.Headers["X-Test-Subject"].ToString())
            ], "Synthetic"));
            await next();
        });
        new McpHttpBridge(server, [], new HttpMetrics(), offline,
            _app.Services.GetRequiredService<SseEventBuffer>(), NullLogger<McpHttpBridge>.Instance).MapEndpoints(_app);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("directory")]
    [InlineData("subject")]
    [InlineData("support")]
    public async Task AuthorizedFirstMessageAndContinuation_DoNotShareThreadHistoryAcrossContexts(string change)
    {
        // Arrange
        using var first = await SendAsync("/mcp/chat", "private-first", TenantA, DirectoryA, SubjectA);
        using var continuation = await SendAsync("/mcp/chat", "private-second", TenantA, DirectoryA, SubjectA);

        // Act
        using var other = await SendAsync("/mcp/chat",
            "independent", change == "organization" ? TenantB : TenantA,
            change == "directory" ? DirectoryB : DirectoryA,
            change == "subject" ? SubjectB : SubjectA, change == "support" ? "support" : "ordinary");

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var sameContext = await continuation.Content.ReadAsStringAsync();
        sameContext.Should().Contain("private-first").And.Contain("private-second");
        other.StatusCode.Should().Be(HttpStatusCode.OK);
        var differentContext = await other.Content.ReadAsStringAsync();
        differentContext.Should().Contain("independent").And.NotContain("private-first").And.NotContain("private-second");
        var response = await other.Content.ReadFromJsonAsync<JsonElement>();
        response.GetProperty("conversationId").GetString().Should().Be("client-generated");
    }

    [Theory]
    [InlineData("/mcp/chat")]
    [InlineData("/mcp/chat/stream")]
    public async Task ForgedSystemReference_FailsBeforeModelOrReplay(string route)
    {
        // Arrange
        var count = _agent.Calls;

        // Act
        using var response = await SendAsync(route, "forged", TenantA, DirectoryA, SubjectA, system: "system-b");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SYSTEM_ACCESS_DENIED");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        _agent.Calls.Should().Be(count);
    }

    [Fact]
    public async Task StreamingReplay_CrossDirectorySameSubject_DoesNotReadBufferedEvents()
    {
        // Arrange
        using var first = await SendAsync("/mcp/chat/stream", "private-first", TenantA, DirectoryA, SubjectA);
        using var second = await SendAsync("/mcp/chat/stream", "private-second", TenantA, DirectoryA, SubjectA);

        // Act
        using var other = await SendAsync("/mcp/chat/stream", "independent", TenantA, DirectoryB, SubjectA, lastEvent: "1");

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await other.Content.ReadAsStringAsync();
        text.Should().Contain("independent").And.NotContain("private-first").And.NotContain("private-second");
        text.Should().Contain("\"conversationId\":\"client-generated\"");
    }

    [Fact]
    public async Task JsonRpcComplianceChat_ValidatesExplicitSystemBeforeDispatch()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call",
                @params = new { name = "compliance_chat",
                    arguments = new { message = "forged", conversation_id = "client-generated", system_id = "system-b" } } })
        };
        request.Headers.Add("X-Workspace-Kind", "organization");
        request.Headers.Add("X-Workspace-Tenant-Id", TenantA.ToString());
        request.Headers.Add("X-Test-Directory", DirectoryA.ToString());
        request.Headers.Add("X-Test-Subject", SubjectA.ToString());

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SYSTEM_ACCESS_DENIED");
        _agent.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MultipartStream_ValidatesContextAndPreservesAuthorizedAttachments()
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Read reference attachment"), "message");
        form.Add(new StringContent("client-generated"), "conversationId");
        form.Add(new StringContent("{\"system_id\":\"system-a\"}"), "context");
        form.Add(new StringContent("system-a"), "systemId");
        var attachment = new ByteArrayContent(Encoding.UTF8.GetBytes(new string('a', 52000)));
        attachment.Headers.ContentType = new("text/plain");
        form.Add(attachment, "files", "evidence.txt");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/chat/stream") { Content = form };
        request.Headers.Add("X-Workspace-Kind", "organization");
        request.Headers.Add("X-Workspace-Tenant-Id", TenantA.ToString());
        request.Headers.Add("X-Test-Directory", DirectoryA.ToString());
        request.Headers.Add("X-Test-Subject", SubjectA.ToString());

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("evidence.txt").And.Contain("content truncated");
        _agent.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(true, "system-a", HttpStatusCode.OK)]
    [InlineData(false, "system-a", HttpStatusCode.Forbidden)]
    [InlineData(true, "system-b", HttpStatusCode.Forbidden)]
    public async Task ExplicitMutator_RequiresActualTargetPermission(bool author, string system, HttpStatusCode expected)
    {
        // Arrange
        _canAuthor = author;
        using var request = ToolRequest("narrative_set_policy", system);

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        _tool.Executions.Should().Be(expected == HttpStatusCode.OK ? 1 : 0);
        _agent.Calls.Should().Be(0, "explicit tool requests must not be rewritten into model instructions");
        if (expected == HttpStatusCode.OK)
            _tool.Actor.Should().Be($"{DirectoryA:D}/{SubjectA:D}");
    }

    [Fact]
    public async Task UnknownExplicitTool_FailsClosedWithoutRoutingThroughModel()
    {
        // Arrange
        using var request = ToolRequest("future_global_write", "system-a");

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKSPACE_TOOL_NOT_SUPPORTED");
        _tool.Executions.Should().Be(0);
        _agent.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitToolValidationFailure_IsNotWrappedAsSuccessfulToolExecution()
    {
        // Arrange
        _canAuthor = true;
        _tool.Fail = true;
        using var request = ToolRequest("narrative_set_policy", "system-a");

        // Act
        using var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        envelope.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().Contain("SYNTHETIC_VALIDATION_ERROR");
    }

    private static HttpRequestMessage ToolRequest(string tool, string system)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call",
                @params = new { name = tool,
                    arguments = new { system_id = system, user_id = "forged", user_role = "CSP.Admin" } } })
        };
        request.Headers.Add("X-Workspace-Kind", "organization");
        request.Headers.Add("X-Workspace-Tenant-Id", TenantA.ToString());
        request.Headers.Add("X-Test-Directory", DirectoryA.ToString());
        request.Headers.Add("X-Test-Subject", SubjectA.ToString());
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(string route, string message, Guid tenant,
        Guid directory, Guid subject, string mode = "ordinary", string? system = null, string? lastEvent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(new { message, conversationId = "client-generated", userId = "forged",
                context = new { systemId = system, user_id = "forged" } })
        };
        request.Headers.Add("X-Workspace-Kind", "organization");
        request.Headers.Add("X-Workspace-Tenant-Id", tenant.ToString());
        request.Headers.Add("X-Workspace-Mode", mode);
        request.Headers.Add("X-Test-Directory", directory.ToString());
        request.Headers.Add("X-Test-Subject", subject.ToString());
        if (lastEvent is not null) request.Headers.Add("Last-Event-ID", lastEvent);
        return await _client.SendAsync(request);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class MemoryAgent() : BaseAgent(NullLogger.Instance)
    {
        private readonly ConcurrentDictionary<string, string> _threads = new();
        public int Calls { get; private set; }
        public override string AgentId => "synthetic";
        public override string AgentName => "Synthetic";
        public override string Description => "In-memory thread stand-in";
        public override string GetSystemPrompt() => "";
        public override double CanHandle(string message) => 1;
        public override Task<AgentResponse> ProcessAsync(string message, AgentConversationContext context,
            CancellationToken cancellationToken = default, IProgress<string>? progress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            var history = _threads.AddOrUpdate(context.ConversationId, message, (_, previous) => $"{previous};{message}");
            return Task.FromResult(new AgentResponse { Success = true, Response = history, AgentName = AgentName });
        }
    }

    private sealed class RecordingTool() : BaseTool(NullLogger.Instance)
    {
        public int Executions { get; private set; }
        public string? Actor { get; private set; }
        public bool Fail { get; set; }
        public override string Name => "narrative_set_policy";
        public override string Description => "Synthetic mutation";
        public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>();
        public override Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
        {
            Executions++;
            Actor = arguments.GetValueOrDefault("user_id")?.ToString();
            return Task.FromResult(Fail
                ? "{\"status\":\"error\",\"errorCode\":\"SYNTHETIC_VALIDATION_ERROR\"}"
                : "Synthetic tool result");
        }
    }
}
