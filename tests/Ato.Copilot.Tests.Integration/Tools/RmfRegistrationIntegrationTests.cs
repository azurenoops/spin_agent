using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Integration.Tools;

/// <summary>
/// Integration tests for Feature 015 Phase 3 — RMF Registration Tools.
/// Uses real RmfLifecycleService & BoundaryService with in-memory EF Core database.
/// Validates register → boundary → roles → advance step end-to-end flow.
/// </summary>
public class RmfRegistrationIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly RegisterSystemTool _registerTool;
    private readonly GetSystemTool _getTool;
    private readonly ListSystemsTool _listTool;
    private readonly AdvanceRmfStepTool _advanceTool;
    private readonly DefineBoundaryTool _boundaryTool;
    private readonly ExcludeFromBoundaryTool _excludeTool;
    private readonly AssignRmfRoleTool _assignTool;
    private readonly ListRmfRolesTool _listRolesTool;

    public RmfRegistrationIntegrationTests()
    {
        var dbName = $"RmfIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var lifecycleSvc = new RmfLifecycleService(scopeFactory, Mock.Of<ILogger<RmfLifecycleService>>());
        var boundarySvc = new BoundaryService(scopeFactory, Mock.Of<ILogger<BoundaryService>>());

        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _getTool = new GetSystemTool(lifecycleSvc, Mock.Of<ILogger<GetSystemTool>>());
        _listTool = new ListSystemsTool(lifecycleSvc, Mock.Of<ILogger<ListSystemsTool>>());
        _advanceTool = new AdvanceRmfStepTool(lifecycleSvc, Mock.Of<ILogger<AdvanceRmfStepTool>>());
        _boundaryTool = new DefineBoundaryTool(boundarySvc, scopeFactory, Mock.Of<ILogger<DefineBoundaryTool>>());
        _excludeTool = new ExcludeFromBoundaryTool(boundarySvc, Mock.Of<ILogger<ExcludeFromBoundaryTool>>());
        _assignTool = new AssignRmfRoleTool(boundarySvc, Mock.Of<IProfileNotificationService>(), Mock.Of<ILogger<AssignRmfRoleTool>>());
        _listRolesTool = new ListRmfRolesTool(boundarySvc, Mock.Of<ILogger<ListRmfRolesTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    /// <summary>
    /// End-to-end: Register system → define boundary → assign roles → advance step.
    /// Validates full RMF lifecycle flow including gate conditions.
    /// </summary>
    [Fact]
    public async Task FullRmfLifecycle_RegisterBoundaryRolesAdvance()
    {
        // Step 1: Register a new system
        var registerResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Integration Test System",
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment",
            ["acronym"] = "ITS",
            ["description"] = "Integration test system for RMF lifecycle"
        });

        var regJson = JsonDocument.Parse(registerResult);
        regJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var systemId = regJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        regJson.RootElement.GetProperty("data").GetProperty("current_rmf_step").GetString().Should().Be("Prepare");

        // Step 2: Try to advance without prerequisites — should fail
        var advanceNoPrereq = await _advanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["target_step"] = "Categorize"
        });

        var failJson = JsonDocument.Parse(advanceNoPrereq);
        failJson.RootElement.GetProperty("status").GetString().Should().Be("error");
        failJson.RootElement.GetProperty("data").GetProperty("gate_results").GetArrayLength().Should().BeGreaterThan(0);

        // Step 3: Define authorization boundary
        var boundaryResult = await _boundaryTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["resources"] = new List<BoundaryResourceInput>
            {
                new()
                {
                    ResourceId = "/subscriptions/abc/providers/Microsoft.Compute/virtualMachines/vm1",
                    ResourceType = "Microsoft.Compute/virtualMachines",
                    ResourceName = "Production VM 1"
                },
                new()
                {
                    ResourceId = "/subscriptions/abc/providers/Microsoft.Storage/storageAccounts/sa1",
                    ResourceType = "Microsoft.Storage/storageAccounts",
                    ResourceName = "Production Storage"
                }
            }
        });

        var bndJson = JsonDocument.Parse(boundaryResult);
        bndJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        bndJson.RootElement.GetProperty("data").GetProperty("resources_added").GetInt32().Should().Be(2);

        // Step 4: Assign RMF roles
        var roleResult = await _assignTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["role"] = "Issm",
            ["user_id"] = "issm@contoso.com",
            ["user_display_name"] = "Jane Smith"
        });

        var roleJson = JsonDocument.Parse(roleResult);
        roleJson.RootElement.GetProperty("status").GetString().Should().Be("success");

        // Step 4b: Satisfy Gate 3 (Privacy) and Gate 4 (Interconnections)
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var system = await db.RegisteredSystems.FindAsync(systemId);
            system!.HasNoExternalInterconnections = true;
            system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
            {
                RegisteredSystemId = systemId,
                Determination = PtaDetermination.PiaNotRequired,
                AnalyzedBy = "integration-test"
            };
            await db.SaveChangesAsync();
        }

        // Step 5: Now advance should succeed (has boundary + role + privacy + interconnections)
        var advanceResult = await _advanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["target_step"] = "Categorize"
        });

        var advJson = JsonDocument.Parse(advanceResult);
        advJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        advJson.RootElement.GetProperty("data").GetProperty("previous_step").GetString().Should().Be("Prepare");
        advJson.RootElement.GetProperty("data").GetProperty("new_step").GetString().Should().Be("Categorize");

        // Step 6: Get system to verify everything persisted
        var getResult = await _getTool.ExecuteAsync(new Dictionary<string, object?> { ["system_id"] = systemId });
        var getJson = JsonDocument.Parse(getResult);
        getJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        getJson.RootElement.GetProperty("data").GetProperty("current_rmf_step").GetString().Should().Be("Categorize");
        getJson.RootElement.GetProperty("data").GetProperty("boundary_resource_count").GetInt32().Should().Be(2);

        // Step 7: List systems
        var listResult = await _listTool.ExecuteAsync(new Dictionary<string, object?>());
        var listJson = JsonDocument.Parse(listResult);
        listJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        listJson.RootElement.GetProperty("data").GetProperty("pagination").GetProperty("total_count").GetInt32().Should().BeGreaterOrEqualTo(1);
    }

    /// <summary>
    /// Boundary resource exclusion with rationale.
    /// </summary>
    [Fact]
    public async Task ExcludeResource_WithRationale_MarksResourceOutOfScope()
    {
        // Register system
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Exclusion Test System",
            ["system_type"] = "Enclave",
            ["mission_criticality"] = "MissionEssential",
            ["hosting_environment"] = "AzureGovernment"
        });
        var systemId = JsonDocument.Parse(regResult).RootElement.GetProperty("data").GetProperty("id").GetString()!;

        // Add resources
        await _boundaryTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["resources"] = new List<BoundaryResourceInput>
            {
                new() { ResourceId = "res-1", ResourceType = "Microsoft.Compute/virtualMachines", ResourceName = "VM 1" },
                new() { ResourceId = "res-2", ResourceType = "Microsoft.Storage/storageAccounts", ResourceName = "SA 1" }
            }
        });

        // Exclude a resource
        var excludeResult = await _excludeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["resource_id"] = "res-2",
            ["rationale"] = "Managed by separate team, outside system boundary"
        });

        var exJson = JsonDocument.Parse(excludeResult);
        exJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        exJson.RootElement.GetProperty("data").GetProperty("is_in_boundary").GetBoolean().Should().BeFalse();
        exJson.RootElement.GetProperty("data").GetProperty("exclusion_rationale").GetString()
            .Should().Be("Managed by separate team, outside system boundary");
    }

    /// <summary>
    /// Role listing returns all assigned roles.
    /// </summary>
    [Fact]
    public async Task ListRoles_AfterAssignment_ReturnsAllRoles()
    {
        // Register system
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Role Test System",
            ["system_type"] = "PlatformIt",
            ["mission_criticality"] = "MissionSupport",
            ["hosting_environment"] = "AzureGovernment"
        });
        var systemId = JsonDocument.Parse(regResult).RootElement.GetProperty("data").GetProperty("id").GetString()!;

        // Assign multiple roles
        await _assignTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["role"] = "AuthorizingOfficial",
            ["user_id"] = "ao@contoso.com", ["user_display_name"] = "AO User"
        });
        await _assignTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["role"] = "Issm",
            ["user_id"] = "issm@contoso.com", ["user_display_name"] = "ISSM User"
        });
        await _assignTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["role"] = "Isso",
            ["user_id"] = "isso@contoso.com", ["user_display_name"] = "ISSO User"
        });

        // List roles
        var listResult = await _listRolesTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var json = JsonDocument.Parse(listResult);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("total_roles").GetInt32().Should().Be(3);
    }

    /// <summary>
    /// Force override on gate failure advances the step.
    /// </summary>
    [Fact]
    public async Task AdvanceRmfStep_ForceOverride_BypassesGates()
    {
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Force Override System",
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment"
        });
        var systemId = JsonDocument.Parse(regResult).RootElement.GetProperty("data").GetProperty("id").GetString()!;

        // Force advance without prerequisites
        var advanceResult = await _advanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["target_step"] = "Categorize",
            ["force"] = true
        });

        var json = JsonDocument.Parse(advanceResult);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("new_step").GetString().Should().Be("Categorize");
        json.RootElement.GetProperty("data").GetProperty("was_forced").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Backward step movement requires force=true.
    /// </summary>
    [Fact]
    public async Task AdvanceRmfStep_BackwardWithoutForce_Fails()
    {
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Backward Test System",
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment"
        });
        var systemId = JsonDocument.Parse(regResult).RootElement.GetProperty("data").GetProperty("id").GetString()!;

        // Force to Categorize
        await _advanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["target_step"] = "Categorize", ["force"] = true
        });

        // Try backward without force — should fail
        var backResult = await _advanceTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["target_step"] = "Prepare"
        });

        var json = JsonDocument.Parse(backResult);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("message").GetString().Should().Contain("force");
    }
}
