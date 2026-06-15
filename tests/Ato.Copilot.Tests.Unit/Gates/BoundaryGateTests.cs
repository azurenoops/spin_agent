using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Gates;

/// <summary>
/// Unit tests for Gate 2 (Authorization Boundary Defined) in CheckPrepareToCategorize
/// via RmfLifecycleService.CheckGateConditionsAsync.
///
/// Regression tests for Issue #408:
///   - A component assigned without an explicit AuthorizationBoundaryDefinitionId (null)
///     must still satisfy the "Authorization Boundary Defined" gate.
///   - Previously, only CSAs with AuthorizationBoundaryDefinitionId != null were counted,
///     causing the gate to fail even when a component was visible in the boundary UI.
/// </summary>
public class BoundaryGateTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;
    private readonly RmfLifecycleService _service;

    public BoundaryGateTests()
    {
        var dbName = $"BoundaryGate_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();

        _service = new RmfLifecycleService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<RmfLifecycleService>>());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<RegisteredSystem> SeedSystemWithRole()
    {
        var system = new RegisteredSystem
        {
            Name = "Boundary Gate Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test-user",
            CurrentRmfStep = RmfPhase.Prepare
        };
        _db.RegisteredSystems.Add(system);

        system.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = system.Id,
            RmfRole = RmfRole.Isso,
            UserId = "isso-user",
            UserDisplayName = "Test ISSO",
            IsActive = true,
            AssignedBy = "test-admin"
        });

        await _db.SaveChangesAsync();
        return system;
    }

    // ─── Gate 2: Authorization Boundary Defined ───────────────────────────────

    /// <summary>
    /// Issue #408 — A CSA with AuthorizationBoundaryDefinitionId = NULL (assigned via wizard
    /// or boundary page without an explicit boundary) must satisfy the gate.
    /// </summary>
    [Fact]
    public async Task Gate2_NullBoundaryComponentSystemAssignment_Passes()
    {
        var system = await SeedSystemWithRole();

        // Assign a component with no explicit boundary (null) — the pattern produced by
        // AssignToSystemAsync when no boundaryDefinitionId is supplied by the wizard.
        _db.ComponentSystemAssignments.Add(new ComponentSystemAssignment
        {
            SystemComponentId = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            AuthorizationBoundaryDefinitionId = null,   // ← the failing case
            CreatedBy = "test-user"
        });
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate = results.Single(r => r.GateName == "Authorization Boundary Defined");
        gate.Passed.Should().BeTrue(
            "a CSA with null AuthorizationBoundaryDefinitionId represents a system-wide " +
            "(primary boundary) assignment and must satisfy the gate");
        gate.Message.Should().Contain("1 component(s) in boundary.");
    }

    /// <summary>
    /// A CSA with an explicit AuthorizationBoundaryDefinitionId (the existing path) still passes.
    /// </summary>
    [Fact]
    public async Task Gate2_ExplicitBoundaryComponentSystemAssignment_Passes()
    {
        var system = await SeedSystemWithRole();

        var boundaryId = Guid.NewGuid().ToString();
        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = boundaryId,
            RegisteredSystemId = system.Id,
            Name = "Production Boundary",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "test-user"
        });

        _db.ComponentSystemAssignments.Add(new ComponentSystemAssignment
        {
            SystemComponentId = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            AuthorizationBoundaryDefinitionId = boundaryId,  // ← explicit boundary
            CreatedBy = "test-user"
        });
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate = results.Single(r => r.GateName == "Authorization Boundary Defined");
        gate.Passed.Should().BeTrue(
            "a CSA with an explicit AuthorizationBoundaryDefinitionId must satisfy the gate");
    }

    /// <summary>
    /// A BoundaryComponentAssignment (Feature 040 path, IsInScope=true) still passes.
    /// </summary>
    [Fact]
    public async Task Gate2_BoundaryComponentAssignmentInScope_Passes()
    {
        var system = await SeedSystemWithRole();

        var boundaryId = Guid.NewGuid().ToString();
        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = boundaryId,
            RegisteredSystemId = system.Id,
            Name = "Production Boundary",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "test-user"
        });

        _db.BoundaryComponentAssignments.Add(new BoundaryComponentAssignment
        {
            SystemComponentId = Guid.NewGuid().ToString(),
            AuthorizationBoundaryDefinitionId = boundaryId,
            IsInScope = true,
            CreatedBy = "test-user"
        });
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate = results.Single(r => r.GateName == "Authorization Boundary Defined");
        gate.Passed.Should().BeTrue(
            "a BoundaryComponentAssignment with IsInScope=true must satisfy the gate");
    }

    /// <summary>
    /// A BoundaryComponentAssignment with IsInScope=false must NOT satisfy the gate
    /// when it is the only assignment.
    /// </summary>
    [Fact]
    public async Task Gate2_BoundaryComponentAssignmentOutOfScope_Fails()
    {
        var system = await SeedSystemWithRole();

        var boundaryId = Guid.NewGuid().ToString();
        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = boundaryId,
            RegisteredSystemId = system.Id,
            Name = "Production Boundary",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "test-user"
        });

        // Out-of-scope component — should NOT count toward the gate.
        _db.BoundaryComponentAssignments.Add(new BoundaryComponentAssignment
        {
            SystemComponentId = Guid.NewGuid().ToString(),
            AuthorizationBoundaryDefinitionId = boundaryId,
            IsInScope = false,
            ExclusionRationale = "Excluded for test",
            CreatedBy = "test-user"
        });
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate = results.Single(r => r.GateName == "Authorization Boundary Defined");
        gate.Passed.Should().BeFalse(
            "an out-of-scope BoundaryComponentAssignment must not satisfy the gate");
    }

    /// <summary>
    /// No component assignments at all — gate must fail.
    /// </summary>
    [Fact]
    public async Task Gate2_NoComponentAssignments_Fails()
    {
        var system = await SeedSystemWithRole();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate = results.Single(r => r.GateName == "Authorization Boundary Defined");
        gate.Passed.Should().BeFalse(
            "with no component assignments the gate must not pass");
        gate.Message.Should().Contain("At least 1 component must be assigned");
    }

    /// <summary>
    /// Mixed: one null-boundary CSA + one in-scope BCA → count = 2.
    /// </summary>
    [Fact]
    public async Task Gate2_MixedNullBoundaryAndBCA_CountsBoth()
    {
        var system = await SeedSystemWithRole();

        var boundaryId = Guid.NewGuid().ToString();
        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = boundaryId,
            RegisteredSystemId = system.Id,
            Name = "Production Boundary",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "test-user"
        });

        // Feature 040 path: one in-scope BCA
        _db.BoundaryComponentAssignments.Add(new BoundaryComponentAssignment
        {
            SystemComponentId = Guid.NewGuid().ToString(),
            AuthorizationBoundaryDefinitionId = boundaryId,
            IsInScope = true,
            CreatedBy = "test-user"
        });

        // Legacy null-boundary CSA (the issue #408 scenario)
        _db.ComponentSystemAssignments.Add(new ComponentSystemAssignment
        {
            SystemComponentId = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            AuthorizationBoundaryDefinitionId = null,
            CreatedBy = "test-user"
        });

        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate = results.Single(r => r.GateName == "Authorization Boundary Defined");
        gate.Passed.Should().BeTrue();
        gate.Message.Should().Contain("2 component(s) in boundary.");
    }
}
