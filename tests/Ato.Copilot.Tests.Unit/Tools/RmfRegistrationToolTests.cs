using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Tools;

/// <summary>
/// Unit tests for Feature 015 Phase 3 — RMF Registration Tools.
/// Covers T032–T035: RegisterSystem, AdvanceRmfStep, Boundary, Role Assignment tools.
/// </summary>
public class RmfRegistrationToolTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ─── T032: RegisterSystemTool ──────────────────────────────────────────────

    [Fact]
    public async Task RegisterSystem_ValidInput_ReturnsSuccess()
    {
        var mock = new Mock<IRmfLifecycleService>();
        var expected = MakeSystem("Test System", SystemType.MajorApplication, MissionCriticality.MissionCritical);
        mock.Setup(s => s.RegisterSystemAsync(
            "Test System", SystemType.MajorApplication, MissionCriticality.MissionCritical,
            "AzureGovernment", "mcp-user", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new RegisterSystemTool(mock.Object, Mock.Of<ILogger<RegisterSystemTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Test System",
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("name").GetString().Should().Be("Test System");
        json.RootElement.GetProperty("data").GetProperty("current_rmf_step").GetString().Should().Be("Prepare");
    }

    [Fact]
    public async Task RegisterSystem_MissingName_ReturnsError()
    {
        var tool = new RegisterSystemTool(
            Mock.Of<IRmfLifecycleService>(),
            Mock.Of<ILogger<RegisterSystemTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task RegisterSystem_InvalidSystemType_ReturnsError()
    {
        var tool = new RegisterSystemTool(
            Mock.Of<IRmfLifecycleService>(),
            Mock.Of<ILogger<RegisterSystemTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Test System",
            ["system_type"] = "InvalidType",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
        json.RootElement.GetProperty("message").GetString().Should().Contain("InvalidType");
    }

    [Fact]
    public async Task RegisterSystem_InvalidMissionCriticality_ReturnsError()
    {
        var tool = new RegisterSystemTool(
            Mock.Of<IRmfLifecycleService>(),
            Mock.Of<ILogger<RegisterSystemTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Test System",
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "BadValue",
            ["hosting_environment"] = "AzureGovernment"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task RegisterSystem_WithAzureProfile_IncludesProfile()
    {
        var mock = new Mock<IRmfLifecycleService>();
        var expected = MakeSystem("Profile System", SystemType.Enclave, MissionCriticality.MissionEssential);
        mock.Setup(s => s.RegisterSystemAsync(
            "Profile System", SystemType.Enclave, MissionCriticality.MissionEssential,
            "AzureGovernment", "mcp-user", "PS", "Test description",
            It.IsAny<AzureEnvironmentProfile?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new RegisterSystemTool(mock.Object, Mock.Of<ILogger<RegisterSystemTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Profile System",
            ["system_type"] = "Enclave",
            ["mission_criticality"] = "MissionEssential",
            ["hosting_environment"] = "AzureGovernment",
            ["acronym"] = "PS",
            ["description"] = "Test description",
            ["cloud_environment"] = "Government"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        mock.Verify(s => s.RegisterSystemAsync(
            "Profile System", SystemType.Enclave, MissionCriticality.MissionEssential,
            "AzureGovernment", "mcp-user", "PS", "Test description",
            It.Is<AzureEnvironmentProfile?>(p => p != null && p.CloudEnvironment == AzureCloudEnvironment.Government),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterSystem_MissingHostingEnvironment_ReturnsError()
    {
        var tool = new RegisterSystemTool(
            Mock.Of<IRmfLifecycleService>(),
            Mock.Of<ILogger<RegisterSystemTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Test System",
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "MissionCritical"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    // ─── T032: ListSystemsTool ─────────────────────────────────────────────────

    [Fact]
    public async Task ListSystems_DefaultParams_ReturnsPaginatedList()
    {
        var mock = new Mock<IRmfLifecycleService>();
        var systems = new List<RegisteredSystem>
        {
            MakeSystem("System A", SystemType.MajorApplication, MissionCriticality.MissionCritical),
            MakeSystem("System B", SystemType.Enclave, MissionCriticality.MissionEssential)
        };
        mock.Setup(s => s.ListSystemsAsync(true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((systems.AsReadOnly(), 2));

        var tool = new ListSystemsTool(mock.Object, Mock.Of<ILogger<ListSystemsTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("pagination").GetProperty("total_count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ListSystems_CustomPagination_PassesParams()
    {
        var mock = new Mock<IRmfLifecycleService>();
        mock.Setup(s => s.ListSystemsAsync(false, 2, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RegisteredSystem>().AsReadOnly(), 0));

        var tool = new ListSystemsTool(mock.Object, Mock.Of<ILogger<ListSystemsTool>>());
        await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["active_only"] = false,
            ["page"] = 2,
            ["page_size"] = 50
        });

        mock.Verify(s => s.ListSystemsAsync(false, 2, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── T032: GetSystemTool ───────────────────────────────────────────────────

    [Fact]
    public async Task GetSystem_ExistingSystem_ReturnsFullDetails()
    {
        var mock = new Mock<IRmfLifecycleService>();
        var system = MakeSystem("Full System", SystemType.PlatformIt, MissionCriticality.MissionSupport);
        system.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = system.Id,
            RmfRole = RmfRole.Issm,
            UserId = "user-1",
            UserDisplayName = "Test User",
            AssignedBy = "admin",
            IsActive = true
        });
        mock.Setup(s => s.GetSystemAsync(system.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(system);

        var tool = new GetSystemTool(mock.Object, Mock.Of<ILogger<GetSystemTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["system_id"] = system.Id });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("name").GetString().Should().Be("Full System");
        json.RootElement.GetProperty("data").GetProperty("role_assignments").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetSystem_NotFound_ReturnsError()
    {
        var mock = new Mock<IRmfLifecycleService>();
        mock.Setup(s => s.GetSystemAsync("non-existent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisteredSystem?)null);

        var tool = new GetSystemTool(mock.Object, Mock.Of<ILogger<GetSystemTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["system_id"] = "non-existent" });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task GetSystem_MissingSystemId_ReturnsError()
    {
        var tool = new GetSystemTool(
            Mock.Of<IRmfLifecycleService>(),
            Mock.Of<ILogger<GetSystemTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    // ─── T033: AdvanceRmfStepTool ──────────────────────────────────────────────

    [Fact]
    public async Task AdvanceRmfStep_SuccessfulAdvance_ReturnsSuccess()
    {
        var mock = new Mock<IRmfLifecycleService>();
        var system = MakeSystem("Step System", SystemType.MajorApplication, MissionCriticality.MissionCritical);
        mock.Setup(s => s.AdvanceRmfStepAsync("sys-1", RmfPhase.Categorize, false, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RmfStepAdvanceResult
            {
                Success = true,
                System = system,
                PreviousStep = RmfPhase.Prepare,
                NewStep = RmfPhase.Categorize,
                GateResults = new List<GateCheckResult>
                {
                    new() { GateName = "RMF Roles Assigned", Passed = true, Message = "1 role(s) assigned.", Severity = "Error" },
                    new() { GateName = "Authorization Boundary Defined", Passed = true, Message = "2 resource(s) in boundary.", Severity = "Error" }
                }
            });

        var tool = new AdvanceRmfStepTool(mock.Object, Mock.Of<ILogger<AdvanceRmfStepTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["target_step"] = "Categorize"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("previous_step").GetString().Should().Be("Prepare");
        json.RootElement.GetProperty("data").GetProperty("new_step").GetString().Should().Be("Categorize");
        json.RootElement.GetProperty("data").GetProperty("was_forced").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AdvanceRmfStep_GateFailure_ReturnsError()
    {
        var mock = new Mock<IRmfLifecycleService>();
        mock.Setup(s => s.AdvanceRmfStepAsync("sys-1", RmfPhase.Categorize, false, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RmfStepAdvanceResult
            {
                Success = false,
                PreviousStep = RmfPhase.Prepare,
                NewStep = RmfPhase.Prepare,
                ErrorMessage = "Gate conditions not met. Use force=true to override.",
                GateResults = new List<GateCheckResult>
                {
                    new() { GateName = "RMF Roles Assigned", Passed = false, Message = "At least 1 RMF role must be assigned.", Severity = "Error" }
                }
            });

        var tool = new AdvanceRmfStepTool(mock.Object, Mock.Of<ILogger<AdvanceRmfStepTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["target_step"] = "Categorize"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("data").GetProperty("gate_results").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AdvanceRmfStep_ForceOverride_SetsForceFlag()
    {
        var mock = new Mock<IRmfLifecycleService>();
        var system = MakeSystem("Force System", SystemType.MajorApplication, MissionCriticality.MissionCritical);
        mock.Setup(s => s.AdvanceRmfStepAsync("sys-1", RmfPhase.Categorize, true, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RmfStepAdvanceResult
            {
                Success = true,
                System = system,
                PreviousStep = RmfPhase.Prepare,
                NewStep = RmfPhase.Categorize,
                WasForced = true,
                GateResults = new List<GateCheckResult>()
            });

        var tool = new AdvanceRmfStepTool(mock.Object, Mock.Of<ILogger<AdvanceRmfStepTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["target_step"] = "Categorize",
            ["force"] = true
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("was_forced").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AdvanceRmfStep_InvalidStep_ReturnsError()
    {
        var tool = new AdvanceRmfStepTool(
            Mock.Of<IRmfLifecycleService>(),
            Mock.Of<ILogger<AdvanceRmfStepTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["target_step"] = "InvalidStep"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task AdvanceRmfStep_MissingSystemId_ReturnsError()
    {
        var tool = new AdvanceRmfStepTool(
            Mock.Of<IRmfLifecycleService>(),
            Mock.Of<ILogger<AdvanceRmfStepTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["target_step"] = "Categorize"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    // ─── T034: Boundary Tools ──────────────────────────────────────────────────

    [Fact]
    public async Task DefineBoundary_ValidResources_ReturnsSuccess()
    {
        var mock = new Mock<IBoundaryService>();
        var entries = new List<AuthorizationBoundary>
        {
            new()
            {
                RegisteredSystemId = "sys-1",
                ResourceId = "/subscriptions/abc/providers/Microsoft.Compute/virtualMachines/vm1",
                ResourceType = "Microsoft.Compute/virtualMachines",
                ResourceName = "vm1",
                IsInBoundary = true,
                AddedBy = "mcp-user"
            }
        };
        mock.Setup(s => s.DefineBoundaryAsync("sys-1", It.IsAny<IEnumerable<BoundaryResourceInput>>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.AsReadOnly());

        var tool = new DefineBoundaryTool(mock.Object, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<DefineBoundaryTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["resources"] = new List<BoundaryResourceInput>
            {
                new()
                {
                    ResourceId = "/subscriptions/abc/providers/Microsoft.Compute/virtualMachines/vm1",
                    ResourceType = "Microsoft.Compute/virtualMachines",
                    ResourceName = "vm1"
                }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("resources_added").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task DefineBoundary_MissingSystemId_ReturnsError()
    {
        var tool = new DefineBoundaryTool(
            Mock.Of<IBoundaryService>(), Mock.Of<IServiceScopeFactory>(),
            Mock.Of<ILogger<DefineBoundaryTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["resources"] = new List<BoundaryResourceInput>()
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task DefineBoundary_EmptyResources_ReturnsError()
    {
        var tool = new DefineBoundaryTool(
            Mock.Of<IBoundaryService>(), Mock.Of<IServiceScopeFactory>(),
            Mock.Of<ILogger<DefineBoundaryTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["resources"] = new List<BoundaryResourceInput>()
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task DefineBoundary_SystemNotFound_ReturnsError()
    {
        var mock = new Mock<IBoundaryService>();
        mock.Setup(s => s.DefineBoundaryAsync("non-existent", It.IsAny<IEnumerable<BoundaryResourceInput>>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System 'non-existent' not found."));

        var tool = new DefineBoundaryTool(mock.Object, Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<DefineBoundaryTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "non-existent",
            ["resources"] = new List<BoundaryResourceInput>
            {
                new() { ResourceId = "res-1", ResourceType = "Microsoft.Compute/virtualMachines" }
            }
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ExcludeFromBoundary_ValidExclusion_ReturnsSuccess()
    {
        var mock = new Mock<IBoundaryService>();
        mock.Setup(s => s.ExcludeResourceAsync("sys-1", "res-1", "Not in scope", "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthorizationBoundary
            {
                RegisteredSystemId = "sys-1",
                ResourceId = "res-1",
                ResourceType = "Microsoft.Storage/storageAccounts",
                IsInBoundary = false,
                ExclusionRationale = "Not in scope",
                AddedBy = "admin"
            });

        var tool = new ExcludeFromBoundaryTool(mock.Object, Mock.Of<ILogger<ExcludeFromBoundaryTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["resource_id"] = "res-1",
            ["rationale"] = "Not in scope"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("is_in_boundary").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("data").GetProperty("exclusion_rationale").GetString().Should().Be("Not in scope");
    }

    [Fact]
    public async Task ExcludeFromBoundary_ResourceNotFound_ReturnsError()
    {
        var mock = new Mock<IBoundaryService>();
        mock.Setup(s => s.ExcludeResourceAsync("sys-1", "res-999", "reason", "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationBoundary?)null);

        var tool = new ExcludeFromBoundaryTool(mock.Object, Mock.Of<ILogger<ExcludeFromBoundaryTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["resource_id"] = "res-999",
            ["rationale"] = "reason"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ExcludeFromBoundary_MissingRationale_ReturnsError()
    {
        var tool = new ExcludeFromBoundaryTool(
            Mock.Of<IBoundaryService>(),
            Mock.Of<ILogger<ExcludeFromBoundaryTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["resource_id"] = "res-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    // ─── T035: Role Assignment Tools ───────────────────────────────────────────

    [Fact]
    public async Task AssignRmfRole_ValidAssignment_ReturnsSuccess()
    {
        var mock = new Mock<IBoundaryService>();
        mock.Setup(s => s.AssignRmfRoleAsync("sys-1", RmfRole.Issm, "user-1", "Jane Doe", "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RmfRoleAssignment
            {
                RegisteredSystemId = "sys-1",
                RmfRole = RmfRole.Issm,
                UserId = "user-1",
                UserDisplayName = "Jane Doe",
                AssignedBy = "mcp-user",
                IsActive = true
            });

        var tool = new AssignRmfRoleTool(mock.Object, Mock.Of<IProfileNotificationService>(), Mock.Of<ILogger<AssignRmfRoleTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["role"] = "Issm",
            ["user_id"] = "user-1",
            ["user_display_name"] = "Jane Doe"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("role").GetString().Should().Be("Issm");
        json.RootElement.GetProperty("data").GetProperty("user_id").GetString().Should().Be("user-1");
    }

    [Fact]
    public async Task AssignRmfRole_InvalidRole_ReturnsError()
    {
        var tool = new AssignRmfRoleTool(
            Mock.Of<IBoundaryService>(),
            Mock.Of<IProfileNotificationService>(),
            Mock.Of<ILogger<AssignRmfRoleTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["role"] = "InvalidRole",
            ["user_id"] = "user-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task AssignRmfRole_MissingUserId_ReturnsError()
    {
        var tool = new AssignRmfRoleTool(
            Mock.Of<IBoundaryService>(),
            Mock.Of<IProfileNotificationService>(),
            Mock.Of<ILogger<AssignRmfRoleTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["role"] = "ISSM"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task AssignRmfRole_SystemNotFound_ReturnsError()
    {
        var mock = new Mock<IBoundaryService>();
        mock.Setup(s => s.AssignRmfRoleAsync("non-existent", RmfRole.AuthorizingOfficial, "user-1", null, "mcp-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System 'non-existent' not found."));

        var tool = new AssignRmfRoleTool(mock.Object, Mock.Of<IProfileNotificationService>(), Mock.Of<ILogger<AssignRmfRoleTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "non-existent",
            ["role"] = "AuthorizingOfficial",
            ["user_id"] = "user-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData("AuthorizingOfficial")]
    [InlineData("Issm")]
    [InlineData("Isso")]
    [InlineData("Sca")]
    [InlineData("SystemOwner")]
    public async Task AssignRmfRole_AllValidRoles_AcceptsRole(string roleName)
    {
        var mock = new Mock<IBoundaryService>();
        var role = Enum.Parse<RmfRole>(roleName, true);
        mock.Setup(s => s.AssignRmfRoleAsync("sys-1", role, "user-1", null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RmfRoleAssignment
            {
                RegisteredSystemId = "sys-1",
                RmfRole = role,
                UserId = "user-1",
                AssignedBy = "mcp-user",
                IsActive = true
            });

        var tool = new AssignRmfRoleTool(mock.Object, Mock.Of<IProfileNotificationService>(), Mock.Of<ILogger<AssignRmfRoleTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["role"] = roleName,
            ["user_id"] = "user-1"
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("role").GetString().Should().Be(roleName);
    }

    [Fact]
    public async Task ListRmfRoles_ReturnsRoleList()
    {
        var mock = new Mock<IBoundaryService>();
        var roles = new List<RmfRoleAssignment>
        {
            new() { RmfRole = RmfRole.AuthorizingOfficial, UserId = "ao-user", UserDisplayName = "AO User", AssignedBy = "admin", IsActive = true },
            new() { RmfRole = RmfRole.Issm, UserId = "issm-user", UserDisplayName = "ISSM User", AssignedBy = "admin", IsActive = true }
        };
        mock.Setup(s => s.ListRmfRolesAsync("sys-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles.AsReadOnly());

        var tool = new ListRmfRolesTool(mock.Object, Mock.Of<ILogger<ListRmfRolesTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["system_id"] = "sys-1" });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("total_roles").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("data").GetProperty("roles").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ListRmfRoles_MissingSystemId_ReturnsError()
    {
        var tool = new ListRmfRolesTool(
            Mock.Of<IBoundaryService>(),
            Mock.Of<ILogger<ListRmfRolesTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>());

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task ListRmfRoles_EmptyList_ReturnsZero()
    {
        var mock = new Mock<IBoundaryService>();
        mock.Setup(s => s.ListRmfRolesAsync("sys-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RmfRoleAssignment>().AsReadOnly());

        var tool = new ListRmfRolesTool(mock.Object, Mock.Of<ILogger<ListRmfRolesTool>>());
        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["system_id"] = "sys-1" });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("data").GetProperty("total_roles").GetInt32().Should().Be(0);
    }

    // ─── Tool Metadata Tests ───────────────────────────────────────────────────

    [Fact]
    public void RegisterSystemTool_HasCorrectName()
    {
        var tool = new RegisterSystemTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<RegisterSystemTool>>());
        tool.Name.Should().Be("compliance_register_system");
    }

    [Fact]
    public void ListSystemsTool_HasCorrectName()
    {
        var tool = new ListSystemsTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<ListSystemsTool>>());
        tool.Name.Should().Be("compliance_list_systems");
    }

    [Fact]
    public void GetSystemTool_HasCorrectName()
    {
        var tool = new GetSystemTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<GetSystemTool>>());
        tool.Name.Should().Be("compliance_get_system");
    }

    [Fact]
    public void AdvanceRmfStepTool_HasCorrectName()
    {
        var tool = new AdvanceRmfStepTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<AdvanceRmfStepTool>>());
        tool.Name.Should().Be("compliance_advance_rmf_step");
    }

    [Fact]
    public void DefineBoundaryTool_HasCorrectName()
    {
        var tool = new DefineBoundaryTool(Mock.Of<IBoundaryService>(), Mock.Of<IServiceScopeFactory>(), Mock.Of<ILogger<DefineBoundaryTool>>());
        tool.Name.Should().Be("compliance_define_boundary");
    }

    [Fact]
    public void ExcludeFromBoundaryTool_HasCorrectName()
    {
        var tool = new ExcludeFromBoundaryTool(Mock.Of<IBoundaryService>(), Mock.Of<ILogger<ExcludeFromBoundaryTool>>());
        tool.Name.Should().Be("compliance_exclude_from_boundary");
    }

    [Fact]
    public void AssignRmfRoleTool_HasCorrectName()
    {
        var tool = new AssignRmfRoleTool(Mock.Of<IBoundaryService>(), Mock.Of<IProfileNotificationService>(), Mock.Of<ILogger<AssignRmfRoleTool>>());
        tool.Name.Should().Be("compliance_assign_rmf_role");
    }

    [Fact]
    public void ListRmfRolesTool_HasCorrectName()
    {
        var tool = new ListRmfRolesTool(Mock.Of<IBoundaryService>(), Mock.Of<ILogger<ListRmfRolesTool>>());
        tool.Name.Should().Be("compliance_list_rmf_roles");
    }

    [Theory]
    [InlineData("name", true)]
    [InlineData("system_type", true)]
    [InlineData("mission_criticality", true)]
    [InlineData("hosting_environment", true)]
    [InlineData("acronym", false)]
    [InlineData("description", false)]
    public void RegisterSystemTool_ParameterRequiredness(string paramName, bool required)
    {
        var tool = new RegisterSystemTool(Mock.Of<IRmfLifecycleService>(), Mock.Of<ILogger<RegisterSystemTool>>());
        tool.Parameters.Should().ContainKey(paramName);
        tool.Parameters[paramName].Required.Should().Be(required);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static RegisteredSystem MakeSystem(string name, SystemType type, MissionCriticality mission)
    {
        return new RegisteredSystem
        {
            Name = name,
            SystemType = type,
            MissionCriticality = mission,
            HostingEnvironment = "AzureGovernment",
            CurrentRmfStep = RmfPhase.Prepare,
            CreatedBy = "test-user",
            IsActive = true
        };
    }
}
