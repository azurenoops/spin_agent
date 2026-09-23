using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Services.Tenancy;
using WorkspaceMembershipFactory = Ato.Copilot.Tests.Integration.Tenancy.WorkspaceMembershipFactory;
using TenancySeedHostedService = Ato.Copilot.Tests.Integration.Tenancy.TenancySeedHostedService;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Chat;

/// <summary>
/// Real Program/middleware/SQLite workspace policy with synthetic authenticated principals
/// from the canonical membership fixture. Only agents/tools and background jobs are replaced.
/// </summary>
[Collection("IntegrationTests")]
public sealed class WorkspaceChatProductionPipelineTests : IClassFixture<WorkspaceMembershipFactory>, IAsyncLifetime
{
    private readonly WebApplicationFactory<McpProgram> _host;
    private readonly WorkspaceMembershipFactory _factory;
    private readonly RecordingTool _author = new("narrative_set_policy");
    private readonly RecordingTool _reference = new("kb_explain_nist_control");
    private readonly RecordingTool _unknown = new("unmapped_mutator");
    private readonly RecordingTool _evidence = new("evidence_classify");
    private readonly RecordingTool _reader = new("compliance_get_system");
    private static readonly Guid Directory = Guid.Parse("abad0000-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private Guid _actor;
    private string _system = "";
    private string _otherSystem = "";
    private string _artifact = "";

    public WorkspaceChatProductionPipelineTests(WorkspaceMembershipFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<BaseTool>();
            services.AddSingleton<BaseTool>(_author);
            services.AddSingleton<BaseTool>(_reference);
            services.AddSingleton<BaseTool>(_unknown);
            services.AddSingleton<BaseTool>(_evidence);
            services.AddSingleton<BaseTool>(_reader);
            services.RemoveAll<AgentOrchestrator>();
            services.AddSingleton(new AgentOrchestrator(
                [new DispatchAgent(_author, _reference, _unknown, _reader, () => _otherSystem)],
                NullLogger<AgentOrchestrator>.Instance));
            RemoveBackgroundJobs(services);
        }));
    }

    public async Task InitializeAsync()
    {
        _actor = Guid.NewGuid();
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var person = new Person { TenantId = Tenant, DisplayName = "Synthetic author", Email = $"{_actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = Tenant, Name = $"Tool system {_actor:N}", CreatedBy = "fixture", IsActive = true };
        var other = new RegisteredSystem { TenantId = Tenant, Name = $"Other tool system {_actor:N}", CreatedBy = "fixture", IsActive = true };
        db.Persons.Add(person);
        db.RegisteredSystems.AddRange(system, other);
        await db.SaveChangesAsync();
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = Directory,
            ObjectId = _actor, PersonId = person.Id, GrantedBy = "fixture" });
        db.SystemRoleAssignments.AddRange(
            new() { TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = system.Id, Role = OrganizationRole.Isso },
            new() { TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = other.Id, Role = OrganizationRole.Assessor });
        var artifact = new EvidenceArtifact { TenantId = Tenant, RegisteredSystemId = other.Id,
            FileName = "synthetic.txt", StoragePath = "synthetic", ContentHash = "synthetic", ContentType = "text/plain", UploadedBy = "fixture" };
        db.EvidenceArtifacts.Add(artifact);
        await db.SaveChangesAsync();
        _system = system.Id;
        _otherSystem = other.Id;
        _artifact = artifact.Id;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/chat")]
    [InlineData("/mcp/chat/stream")]
    public async Task ApplicableAssignment_WithoutGlobalComplianceRole_AllowsPolicyCheckedMutation(string route)
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await Send(client, route, _system);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _author.Executions.Should().Be(1);
        _author.Actor.Should().Be($"{Directory:D}/{_actor:D}");
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/chat")]
    [InlineData("/mcp/chat/stream")]
    public async Task AnonymousRequest_CannotReachAgentOrTool(string route)
    {
        // Arrange
        using var client = Client(authenticated: false);

        // Act
        using var response = await Send(client, route, _system);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _author.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/chat")]
    public async Task ReadOnlyAssignment_AndGlobalCspClaim_DoNotAuthorizeMutation(string route)
    {
        // Arrange
        using var client = Client();
        client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");

        // Act
        using var response = await Send(client, route, _otherSystem);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKSPACE_OPERATION_NOT_AUTHORIZED");
        _author.Executions.Should().Be(0);
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/chat")]
    public async Task UnknownRegisteredTool_IsDeniedByPolicyNotByMissingRegistration(string route)
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await Send(client, route, _system, "unknown");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKSPACE_TOOL_NOT_SUPPORTED");
        _unknown.Executions.Should().Be(0);
    }

    [Fact]
    public async Task ToolInferredOtherTarget_IsDeniedEvenWhenBothSystemsAreReadable()
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await Send(client, "/mcp/chat", _system, "other-target");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKSPACE_TOOL_TARGET_MISMATCH");
        _author.Executions.Should().Be(0);
    }

    [Fact]
    public async Task IndirectArtifact_CannotBorrowTheSelectedSystemsMutationPermission()
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "evidence_classify", arguments = new { system_id = _system, evidence_artifact_id = _artifact } }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WORKSPACE_TOOL_TARGET_MISMATCH");
        _evidence.Executions.Should().Be(0);
    }

    [Fact]
    public async Task SameObjectInAnotherDirectory_CannotReuseMembership()
    {
        // Arrange
        using var client = Client();
        client.DefaultRequestHeaders.Remove("X-Test-Tid");
        client.DefaultRequestHeaders.Add("X-Test-Tid", Guid.NewGuid().ToString());

        // Act
        using var response = await Send(client, "/mcp/chat", _system);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _author.Executions.Should().Be(0);
    }

    [Fact]
    public async Task UnmappedPost_IsNotGrantedBlanketWorkspaceWriteAccess()
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await client.PostAsJsonAsync("/api/unmapped-workspace-operation", new { user_role = "CSP.Admin" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _author.Executions.Should().Be(0);
    }

    [Fact]
    public async Task AuthorizedFirstMessage_WithoutSystem_CanUsePublicReferenceTool()
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await Send(client, "/mcp/chat", null, "reference");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("client-generated-first");
        _reference.Executions.Should().Be(1);
    }

    [Theory]
    [InlineData("/mcp", "read", HttpStatusCode.OK)]
    [InlineData("/mcp/chat", "read", HttpStatusCode.OK)]
    [InlineData("/mcp", "author", HttpStatusCode.Forbidden)]
    [InlineData("/mcp/chat", "author", HttpStatusCode.Forbidden)]
    public async Task ProviderOversight_WithoutMembership_IsReadOnly(string route, string message, HttpStatusCode expected)
    {
        // Arrange
        using var client = Client();
        client.DefaultRequestHeaders.Remove("X-Test-Oid");
        client.DefaultRequestHeaders.Add("X-Test-Oid", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        client.DefaultRequestHeaders.Remove("X-Workspace-Kind");
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
        client.DefaultRequestHeaders.Remove("X-Workspace-Tenant-Id");

        // Act
        using var response = await Send(client, route, _system, message);

        // Assert
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        _author.Executions.Should().Be(0);
        _reader.Executions.Should().Be(expected == HttpStatusCode.OK ? 1 : 0);
    }

    [Fact]
    public async Task StreamingMutationDenial_IsAnErrorEvent_NotModelSuccess()
    {
        // Arrange
        using var client = Client();

        // Act
        using var response = await Send(client, "/mcp/chat/stream", _otherSystem);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("event: error").And.Contain("WORKSPACE_OPERATION_NOT_AUTHORIZED")
            .And.NotContain("\"type\":\"result\"").And.NotContain("Model hid");
        _author.Executions.Should().Be(0);
    }

    [Fact]
    public async Task RegisteredToolInventory_ReportsSupportedAndExplicitlyUnsupportedOperations()
    {
        // Arrange
        using var inventoryHost = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(RemoveBackgroundJobs));
        var tools = inventoryHost.Services.GetServices<BaseTool>().ToArray();
        var names = tools.Select(t => t.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        // Act
        var supported = names.Intersect(WorkspaceToolAuthorizer.SupportedToolNames, StringComparer.Ordinal).ToArray();
        var unsupported = names.Except(WorkspaceToolAuthorizer.SupportedToolNames, StringComparer.Ordinal).ToArray();
        var output = System.IO.Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        await File.WriteAllTextAsync(Path.Combine(output.FullName, "workspace-tool-coverage.json"),
            JsonSerializer.Serialize(new { registeredCount = names.Length, supportedCount = supported.Length,
                unsupportedCount = unsupported.Length, supported, unsupported,
                reviewedPolicies = WorkspaceToolAuthorizer.ReviewedPolicies }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(output.FullName, "workspace-tool-contracts.json"),
            JsonSerializer.Serialize(tools.Select(t => new
            {
                t.Name, implementation = t.GetType().FullName, parameters = t.Parameters.Values
            }), new JsonSerializerOptions { WriteIndented = true }));

        // Assert
        supported.Should().BeEquivalentTo(WorkspaceToolAuthorizer.SupportedToolNames);
        unsupported.Should().NotBeEmpty();
        unsupported.Should().Contain("compliance_remediate");
    }

    [Fact]
    public async Task NativeOscalRender_ReadPermissionReachesNativeSchemaValidation()
    {
        // Arrange: this system deliberately has no export-ready SSP roles or implementation data.
        using var nativeHost = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(RemoveBackgroundJobs));
        await using (var scope = nativeHost.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.SecurityCategorizations.Add(new() { TenantId = Tenant, RegisteredSystemId = _otherSystem, CategorizedBy = "fixture" });
            db.ControlBaselines.Add(new() { TenantId = Tenant, RegisteredSystemId = _otherSystem,
                BaselineLevel = "Low", ControlIds = ["AC-1"], TotalControls = 1, CustomerControls = 1, CreatedBy = "fixture" });
            await db.SaveChangesAsync();
        }
        using var client = Client(host: nativeHost);

        // Act
        using var response = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "compliance_export_oscal_ssp",
                arguments = new { system_id = _otherSystem, include_back_matter = false, pretty_print = false } }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var native = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
        native.GetProperty("errorCode").GetString().Should().Be("OSCAL_SCHEMA_VALIDATION_FAILED",
            "read authority must reach the actual renderer without bypassing its artifact validation");
        native.GetProperty("violations").GetArrayLength().Should().BeGreaterThan(0);
    }

    private static void RemoveBackgroundJobs(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var registration = services[index];
            if (registration.ServiceType == typeof(IHostedService)
                && registration.ImplementationType != typeof(TenancySeedHostedService)
                && registration.ImplementationType?.Namespace?.StartsWith("Microsoft.", StringComparison.Ordinal) != true)
                services.RemoveAt(index);
        }
    }

    private HttpClient Client(bool authenticated = true, WebApplicationFactory<McpProgram>? host = null)
    {
        var client = (host ?? _host).CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        if (authenticated)
        {
            client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
            client.DefaultRequestHeaders.Add("X-Test-Oid", _actor.ToString());
        }
        return client;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string route, string? system, string message = "author") =>
        route == "/mcp"
            ? client.PostAsJsonAsync(route, new { jsonrpc = "2.0", id = 1, method = "tools/call",
                @params = new { name = message == "unknown" ? "unmapped_mutator" : message == "read" ? "compliance_get_system" : "narrative_set_policy",
                    arguments = new { system_id = system, user_id = "forged", user_role = "CSP.Admin" } } })
            : client.PostAsJsonAsync(route, new { message, conversationId = "client-generated-first",
                context = new { systemId = system, user_id = "forged", user_role = "CSP.Admin" } });

    private sealed class RecordingTool(string name) : BaseTool(NullLogger.Instance)
    {
        public int Executions { get; private set; }
        public string? Actor { get; private set; }
        public override string Name => name;
        public override string Description => "Synthetic fixture operation";
        public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
        {
            ["system_id"] = new() { Name = "system_id", Type = "string" }
        };
        public override Task<string> ExecuteCoreAsync(Dictionary<string, object?> arguments, CancellationToken cancellationToken = default)
        {
            Executions++;
            Actor = arguments.GetValueOrDefault("user_id")?.ToString();
            return Task.FromResult("Synthetic operation completed");
        }
    }

    private sealed class DispatchAgent(BaseTool author, BaseTool reference, BaseTool unknown, BaseTool reader, Func<string> other)
        : BaseAgent(NullLogger.Instance)
    {
        public override string AgentId => "synthetic";
        public override string AgentName => "Synthetic";
        public override string Description => "No model calls";
        public override string GetSystemPrompt() => "";
        public override double CanHandle(string message) => 1;
        public override async Task<AgentResponse> ProcessAsync(string message, AgentConversationContext context,
            CancellationToken cancellationToken = default, IProgress<string>? progress = null)
        {
            var tool = message == "reference" ? reference : message == "unknown" ? unknown : message == "read" ? reader : author;
            var arguments = new Dictionary<string, object?> { ["user_id"] = "forged", ["user_role"] = "CSP.Admin" };
            if (message != "reference")
                arguments["system_id"] = message == "other-target" ? other() : context.WorkflowState.GetValueOrDefault("system_id");
            var response = "Synthetic response";
            try { response = await tool.ExecuteAsync(arguments, cancellationToken); }
            catch (WorkspaceException error) { response = $"Model hid {error.Code}"; }
            return new() { Success = true, Response = response };
        }
    }
}
