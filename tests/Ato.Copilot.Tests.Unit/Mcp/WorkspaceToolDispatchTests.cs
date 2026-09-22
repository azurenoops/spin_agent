using System.Security.Claims;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces;
using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Observability;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

/// <summary>Executes BaseTool through the real server/agent boundary, never a real model or Azure tool.</summary>
public class WorkspaceToolDispatchTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly Guid Directory = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();
    private const string System = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string OtherSystem = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    [Theory]
    [InlineData("unmapped_mutator", false, "WORKSPACE_TOOL_NOT_SUPPORTED")]
    [InlineData("narrative_set_policy", false, "WORKSPACE_OPERATION_NOT_AUTHORIZED")]
    public async Task DeniedTool_CannotExecuteOrBeHiddenByAgentSuccess(string toolName, bool author, string code)
    {
        // Arrange
        var tool = new RecordingTool(toolName);
        var args = new Dictionary<string, object?> { ["system_id"] = System, ["user_role"] = "CSP.Admin" };
        var server = Create(tool, args, author);

        // Act
        var result = await server.ProcessChatRequestAsync("untrusted request", "first-client-id",
            new() { ["systemId"] = System });

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == code);
        tool.Executions.Should().Be(0);
    }

    [Fact]
    public async Task AuthorizedMutator_ExecutesWithQualifiedActorAndNoBodyRoleAuthority()
    {
        // Arrange
        var tool = new RecordingTool("narrative_set_policy");
        var args = new Dictionary<string, object?>
        {
            ["system_id"] = System, ["user_id"] = "forged", ["user_role"] = "CSP.Admin"
        };
        var server = Create(tool, args, author: true);

        // Act
        var result = await server.ProcessChatRequestAsync("authorized", "client-id",
            new() { ["systemId"] = System });

        // Assert
        result.Success.Should().BeTrue();
        tool.Executions.Should().Be(1);
        args["user_id"].Should().Be($"{Directory:D}/{Subject:D}");
        args.Should().NotContainKey("user_role");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ToolInferredOtherSystem_DoesNotEscapeSelectedOrUnboundConversation(bool selected)
    {
        // Arrange
        var tool = new RecordingTool("narrative_set_policy");
        var server = Create(tool, new() { ["system_id"] = OtherSystem }, author: true);

        // Act
        var result = await server.ProcessChatRequestAsync("reference another system", "client-id",
            selected ? new() { ["systemId"] = System } : null);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode ==
            (selected ? "WORKSPACE_TOOL_TARGET_MISMATCH" : "SYSTEM_CONTEXT_REQUIRED"));
        tool.Executions.Should().Be(0);
    }

    [Fact]
    public async Task ReadOnlyTool_StillRequiresReadPermission()
    {
        // Arrange
        var tool = new RecordingTool("compliance_narrative_history");
        var server = Create(tool, new() { ["system_id"] = System }, author: false, read: false);

        // Act
        var result = await server.ProcessChatRequestAsync("read", "client-id", new() { ["systemId"] = System });

        // Assert
        result.Success.Should().BeFalse();
        tool.Executions.Should().Be(0);
    }

    private static McpServer Create(RecordingTool tool, Dictionary<string, object?> args, bool author, bool read = true)
    {
        var workspace = Mock.Of<IWorkspaceService>(w => w.Current == new WorkspaceResponse(
            "organization", Tenant, "Synthetic", "ordinary", Person, new[] { "Assessor" },
            new WorkspacePermissionsResponse(false, false, false, false)));
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(a => a.CanReadAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(read);
        access.Setup(a => a.GetAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? _, string system, bool _, CancellationToken _) =>
                new SystemWorkspaceAccessResponse(system, ["Assessor"],
                    new(read, false, false, author, false, false, false, false, false)));
        var tenant = new TenantContext(Tenant) { IsWorkspaceRequest = true, PersonId = Person };
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("tid", Directory.ToString()), new Claim("oid", Subject.ToString()),
                    new Claim(ClaimTypes.Role, "CSP.Admin")
                ], "Synthetic")),
                RequestServices = new ServiceCollection().AddSingleton(workspace)
                    .AddSingleton(access.Object).AddSingleton<ITenantContext>(tenant).BuildServiceProvider()
            }
        };
        var agent = new CallingAgent(tool, args);
        return new(null!, null!, null!, null!, null!,
            new AgentOrchestrator([agent], NullLogger<AgentOrchestrator>.Instance), [tool], http,
            Mock.Of<IPathSanitizationService>(),
            new ResponseCacheService(new MemoryCache(new MemoryCacheOptions()), new HttpMetrics(),
                Options.Create(new CachingOptions()), Mock.Of<ITenantContextAccessor>(), NullLogger<ResponseCacheService>.Instance),
            Options.Create(new PaginationOptions()),
            new OfflineModeService(new ConfigurationBuilder().Build(), NullLogger<OfflineModeService>.Instance),
            Mock.Of<IModelCallLedger>(), NullLogger<McpServer>.Instance);
    }

    private sealed class RecordingTool(string name) : BaseTool(NullLogger.Instance)
    {
        public int Executions { get; private set; }
        public override string Name => name;
        public override string Description => "Synthetic operation";
        public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>();
        public override Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
        {
            Executions++;
            return Task.FromResult("done");
        }
    }

    private sealed class CallingAgent(BaseTool tool, Dictionary<string, object?> args) : BaseAgent(NullLogger.Instance)
    {
        public override string AgentId => "synthetic";
        public override string AgentName => "Synthetic";
        public override string Description => "Synthetic";
        public override string GetSystemPrompt() => "";
        public override double CanHandle(string message) => 1;
        public override async Task<AgentResponse> ProcessAsync(string message, AgentConversationContext context,
            CancellationToken cancellationToken = default, IProgress<string>? progress = null)
        {
            var response = "Synthetic answer";
            try { await tool.ExecuteAsync(args, cancellationToken); }
            catch (WorkspaceException error) { response = $"Agent masked {error.Code}"; }
            return new() { Success = true, Response = response };
        }
    }
}
