using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.State.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public sealed class WorkspaceToolAuthorizerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _person = Guid.NewGuid();
    private readonly Guid _directory = Guid.NewGuid();
    private readonly Guid _subject = Guid.NewGuid();
    private readonly string _system = Guid.NewGuid().ToString();
    private readonly string _otherSystem = Guid.NewGuid().ToString();
    private readonly Mock<ISystemWorkspaceAccessService> _access = new();
    private readonly DbContextOptions<AtoCopilotContext> _options = new DbContextOptionsBuilder<AtoCopilotContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    [Theory]
    [InlineData("evidence_classify", "evidence_artifact_id")]
    [InlineData("compliance_update_poam", "poam_id")]
    public async Task IndirectTarget_UsesStoredOwner_NotForgedAllowedSystem(string tool, string parameter)
    {
        // Arrange
        await SeedAsync(_tenant, _otherSystem);
        var policy = Policy(Permissions(write: true));

        // Act
        var act = () => policy.AuthorizeAsync(tool, new()
        {
            [parameter] = "indirect", ["system_id"] = _system
        }, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_TOOL_TARGET_MISMATCH");
        _access.Verify(a => a.GetAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("evidence_classify", "evidence_artifact_id")]
    [InlineData("compliance_update_poam", "poam_id")]
    public async Task OtherOrganizationArtifact_IsNotResolved(string tool, string parameter)
    {
        // Arrange
        await SeedAsync(Guid.NewGuid(), _system);
        var policy = Policy(Permissions(write: true));

        // Act
        var act = () => policy.AuthorizeAsync(tool, new() { [parameter] = "indirect" }, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_RESOURCE_NOT_FOUND");
    }

    [Theory]
    [InlineData("evidence_classify", "evidence_artifact_id", true)]
    [InlineData("evidence_classify", "evidence_artifact_id", false)]
    [InlineData("compliance_update_poam", "poam_id", true)]
    [InlineData("compliance_update_poam", "poam_id", false)]
    public async Task IndirectMutation_NeedsWriteNotRead(string tool, string parameter, bool write)
    {
        // Arrange
        await SeedAsync(_tenant, _system);
        var policy = Policy(Permissions(write));

        // Act
        var act = () => policy.AuthorizeAsync(tool, new() { [parameter] = "indirect" }, CancellationToken.None);

        // Assert
        if (write) await act.Should().NotThrowAsync();
        else (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_OPERATION_NOT_AUTHORIZED");
        _access.Verify(a => a.GetAccessAsync(_tenant, _person, _system, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("narrative_set_policy")]
    [InlineData("narrative_set_technical")]
    [InlineData("compliance_write_narrative")]
    [InlineData("compliance_rollback_narrative")]
    [InlineData("compliance_submit_narrative")]
    [InlineData("compliance_review_narrative")]
    public async Task DirectOperations_UseActualPermission_NotHighestClaim(string tool)
    {
        // Arrange
        var policy = Policy(new(true, true, true, false, false, true, true, true, true));

        // Act
        var act = () => policy.AuthorizeAsync(tool, new()
        {
            ["system_id"] = _system, ["personId"] = Guid.NewGuid().ToString(), ["user_role"] = "Compliance.Administrator"
        }, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_OPERATION_NOT_AUTHORIZED");
    }

    [Theory]
    [InlineData("compliance_get_system")]
    [InlineData("compliance_narrative_history")]
    [InlineData("compliance_narrative_diff")]
    public async Task MappedSystemRead_DoesNotRequireAnUnrelatedWriteRole(string tool)
    {
        // Arrange
        var policy = Policy(Permissions(false));

        // Act
        var act = () => policy.AuthorizeAsync(tool, new() { ["system_id"] = _system }, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("kb_search_nist_controls")]
    [InlineData("kb_explain_nist_control")]
    [InlineData("kb_explain_stig")]
    [InlineData("kb_search_stigs")]
    [InlineData("kb_explain_rmf")]
    [InlineData("kb_explain_impact_level")]
    [InlineData("kb_get_fedramp_template_guidance")]
    public async Task MappedReferenceTools_DoNotReadProtectedSystemState(string tool)
    {
        // Arrange
        var policy = Policy(Permissions(false));

        // Act
        await policy.AuthorizeAsync(tool, new(), CancellationToken.None);

        // Assert
        _access.Verify(a => a.GetAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("configuration_manage")]
    [InlineData("compliance_remediate")]
    [InlineData("compliance_collect_evidence")]
    [InlineData("unmapped_write")]
    public async Task UnmappedOperations_NeverInferPermissionFromNamesOrGlobalClaims(string tool)
    {
        // Arrange
        var policy = Policy(Permissions(true));

        // Act
        var act = () => policy.AuthorizeAsync(tool, new() { ["system_id"] = _system }, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_TOOL_NOT_SUPPORTED");
    }

    [Fact]
    public async Task UnknownSubOperation_AndConflictingAliases_FailClosed()
    {
        // Arrange
        var policy = Policy(Permissions(true));
        var otherPolicy = Policy(Permissions(true));

        // Act
        var action = () => policy.AuthorizeAsync("narrative_set_policy",
            new() { ["system_id"] = _system, ["action"] = "approve" }, CancellationToken.None);
        var aliases = () => otherPolicy.AuthorizeAsync("narrative_set_policy",
            new() { ["system_id"] = _system, ["systemId"] = _otherSystem }, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_TOOL_NOT_SUPPORTED");
        (await aliases.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_TOOL_TARGET_MISMATCH");
    }

    [Fact]
    public async Task FailureIsSticky_AndCannotBeHiddenByLaterAllowedTools()
    {
        // Arrange
        var policy = Policy(Permissions(true));
        var denied = () => policy.AuthorizeAsync("unmapped", new(), CancellationToken.None);
        await denied.Should().ThrowAsync<WorkspaceException>();

        // Act
        var subsequent = () => policy.AuthorizeAsync("kb_search_nist_controls", new(), CancellationToken.None);
        Action finalCheck = policy.ThrowIfDenied;

        // Assert
        (await subsequent.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_TOOL_NOT_SUPPORTED");
        finalCheck.Should().Throw<WorkspaceException>();
    }

    [Fact]
    public async Task Cancellation_DoesNotBecomeAnAllowOrAuthorizationFailure()
    {
        // Arrange
        var policy = Policy(Permissions(true));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var act = () => policy.AuthorizeAsync("narrative_set_policy", new() { ["system_id"] = _system }, cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _access.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("narrative_set_policy", true, false)]
    [InlineData("compliance_review_narrative", false, true)]
    public async Task AuthorshipAndReview_AreIndependentPermissions(string tool, bool author, bool review)
    {
        // Arrange
        var policy = Policy(new(true, false, false, author, review, false, false, false, false));

        // Act
        var act = () => policy.AuthorizeAsync(tool, new() { ["system_id"] = _system, ["decision"] = "approve" }, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnboundEfContext_FailsClosedBeforePermissionLookup()
    {
        // Arrange
        var policy = Policy(Permissions(true), bound: false);

        // Act
        var act = () => policy.AuthorizeAsync("narrative_set_policy", new() { ["system_id"] = _system }, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_CONTEXT_INVALID");
        _access.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task PermissionServiceFailure_IsExplicitAndSticky()
    {
        // Arrange
        var policy = Policy(Permissions(true));
        _access.Setup(a => a.GetAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Synthetic failure"));

        // Act
        var act = () => policy.AuthorizeAsync("narrative_set_policy", new() { ["system_id"] = _system }, CancellationToken.None);

        // Assert
        var failure = (await act.Should().ThrowAsync<WorkspaceException>()).Which;
        failure.Code.Should().Be("WORKSPACE_AUTHORIZATION_UNAVAILABLE");
        failure.StatusCode.Should().Be(503);
        Action check = policy.ThrowIfDenied;
        check.Should().Throw<WorkspaceException>();
    }

    [Theory]
    [InlineData(null, "SYSTEM_TARGET_REQUIRED")]
    [InlineData("", "INVALID_TOOL_TARGET")]
    [InlineData(" ", "INVALID_TOOL_TARGET")]
    public async Task MissingOrEmptyTargets_AreNotTakenFromHints(string? system, string code)
    {
        // Arrange
        var policy = Policy(Permissions(true));

        // Act
        var act = () => policy.AuthorizeAsync("narrative_set_policy",
            new() { ["system_id"] = system, ["target_system_id"] = _system }, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be(code);
    }

    [Fact]
    public async Task ReadOnlyPoam_ResolvesItsActualOwner()
    {
        // Arrange
        await SeedAsync(_tenant, _system);
        var policy = Policy(Permissions(false));

        // Act
        await policy.AuthorizeAsync("compliance_get_poam", new() { ["poam_id"] = "indirect" }, CancellationToken.None);

        // Assert
        _access.Verify(a => a.GetAccessAsync(_tenant, _person, _system, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("inventory_get")]
    [InlineData("inventory_list")]
    public async Task InventoryRead_CannotIncludeSoftwareFromAnotherSystem(string tool)
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.InventoryItems.AddRange(
                new() { Id = "hardware", TenantId = _tenant, RegisteredSystemId = _system, ItemName = "Hardware", CreatedBy = "fixture" },
                new() { Id = "software", TenantId = _tenant, RegisteredSystemId = _otherSystem,
                    ParentHardwareId = "hardware", ItemName = "Foreign software", CreatedBy = "fixture" });
            await db.SaveChangesAsync();
        }
        var policy = Policy(Permissions(false));

        // Act
        var act = () => policy.AuthorizeAsync(tool,
            new() { ["system_id"] = _system, ["item_id"] = "hardware" }, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_TOOL_TARGET_MISMATCH");
    }

    [Fact]
    public async Task ComponentRead_CannotReturnLinkedPoamFromAnotherSystem()
    {
        // Arrange
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemComponents.Add(new() { Id = "component", TenantId = _tenant,
                RegisteredSystemId = _system, Name = "Component", CreatedBy = "fixture" });
            db.PoamItems.Add(new() { Id = "foreign-poam", TenantId = _tenant,
                RegisteredSystemId = _otherSystem, Weakness = "Foreign", CreatedBy = "fixture" });
            db.PoamComponentLinks.Add(new() { TenantId = _tenant, SystemComponentId = "component",
                PoamItemId = "foreign-poam", LinkedBy = "fixture" });
            await db.SaveChangesAsync();
        }
        var policy = Policy(Permissions(false));

        // Act
        var act = () => policy.AuthorizeAsync("compliance_poam_by_component",
            new() { ["component_id"] = "component" }, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<WorkspaceException>()).Which.Code.Should().Be("WORKSPACE_TOOL_TARGET_MISMATCH");
    }

    [Fact]
    public async Task RequestScopes_AreIndependentAndRestoreThePreviousPolicy()
    {
        // Arrange
        var seen = new List<string>();
        using var outer = ToolExecutionAuthorization.Push((_, _, _) => { seen.Add("outer"); return Task.CompletedTask; });

        // Act
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            using var inner = ToolExecutionAuthorization.Push((_, _, _) => { lock (seen) seen.Add($"inner-{index}"); return Task.CompletedTask; });
            await Task.Yield();
            await ToolExecutionAuthorization.DemandAsync("synthetic", new(), CancellationToken.None);
        }));
        await ToolExecutionAuthorization.DemandAsync("synthetic", new(), CancellationToken.None);

        // Assert
        seen.Should().BeEquivalentTo("inner-0", "inner-1", "outer");
    }

    private WorkspaceToolAuthorizer Policy(SystemWorkspacePermissions permissions, bool bound = true)
    {
        _access.Setup(a => a.GetAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? _, string system, bool _, CancellationToken _) =>
                new SystemWorkspaceAccessResponse(system, [], permissions));
        var workspace = Mock.Of<IWorkspaceService>(w => w.Current == new WorkspaceResponse(
            "organization", _tenant, "Synthetic", "ordinary", _person, new[] { "Assessor" },
            new WorkspacePermissionsResponse(false, false, false, false)));
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options));
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("tid", _directory.ToString()), new Claim("oid", _subject.ToString()),
                new Claim(ClaimTypes.Role, "CSP.Admin")
            ], "Synthetic")),
            RequestServices = new ServiceCollection().AddSingleton(workspace).AddSingleton(_access.Object)
                .AddSingleton(factory.Object).AddSingleton<ITenantContext>(
                    new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = bound }).BuildServiceProvider()
        };
        return new(http, conversation: false, NullLogger.Instance);
    }

    private async Task SeedAsync(Guid tenant, string system)
    {
        await using var db = new AtoCopilotContext(_options);
        db.EvidenceArtifacts.Add(new() { Id = "indirect", TenantId = tenant, RegisteredSystemId = system,
            FileName = "synthetic.txt", StoragePath = "synthetic", ContentType = "text/plain", ContentHash = "test", UploadedBy = "fixture" });
        db.PoamItems.Add(new() { Id = "indirect", TenantId = tenant, RegisteredSystemId = system,
            Weakness = "Synthetic weakness", CreatedBy = "fixture" });
        await db.SaveChangesAsync();
    }

    private static SystemWorkspacePermissions Permissions(bool write) =>
        new(true, false, false, write, write, write, false, write, false);
}
