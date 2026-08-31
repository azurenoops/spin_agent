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
/// Unit tests for Gate 3 (Privacy Readiness) and Gate 4 (Interconnection Documentation)
/// in CheckPrepareToCategorize via RmfLifecycleService.CheckGateConditionsAsync.
/// Feature 021 Tasks: T044, T045.
/// </summary>
public class PrivacyGateTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;
    private readonly RmfLifecycleService _service;

    public PrivacyGateTests()
    {
        var dbName = $"PrivacyGate_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();

        _service = new RmfLifecycleService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(),
            Mock.Of<ILogger<RmfLifecycleService>>());
    }

    private async Task<RegisteredSystem> SeedSystemWithRolesAndBoundary(string? name = null)
    {
        var system = new RegisteredSystem
        {
            Name = name ?? "Gate Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test-user",
            CurrentRmfStep = RmfPhase.Prepare
        };
        _db.RegisteredSystems.Add(system);

        // Satisfy existing gates (role + boundary)
        system.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = system.Id,
            RmfRole = RmfRole.Isso,
            UserId = "isso-user",
            UserDisplayName = "Test ISSO",
            IsActive = true,
            AssignedBy = "test-admin"
        });

        system.AuthorizationBoundaries.Add(new AuthorizationBoundary
        {
            RegisteredSystemId = system.Id,
            ResourceId = "vm-001",
            ResourceType = "VirtualMachine",
            ResourceName = "App Server",
            IsInBoundary = true,
            AddedBy = "test-user"
        });

        await _db.SaveChangesAsync();
        return system;
    }

    // ─── Gate 3: Privacy Readiness ──────────────────────────────────────

    [Fact]
    public async Task Gate3_PtaPiaNotRequired_Passes()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PiaNotRequired,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate3 = results.FirstOrDefault(r => r.GateName == "Privacy Readiness");
        gate3.Should().NotBeNull();
        gate3!.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Gate3_PtaPiaRequired_PiaApproved_Passes()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PiaRequired,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };
        system.PrivacyImpactAssessment = new PrivacyImpactAssessment
        {
            RegisteredSystemId = system.Id,
            Status = PiaStatus.Approved,
            Version = 1,
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow
        };
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate3 = results.FirstOrDefault(r => r.GateName == "Privacy Readiness");
        gate3.Should().NotBeNull();
        gate3!.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Gate3_PtaPiaRequired_NoPia_Fails()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PiaRequired,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate3 = results.FirstOrDefault(r => r.GateName == "Privacy Readiness");
        gate3.Should().NotBeNull();
        gate3!.Passed.Should().BeFalse();
        gate3.Severity.Should().Be("Error");
    }

    [Fact]
    public async Task Gate3_PtaPendingConfirmation_Fails()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PendingConfirmation,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate3 = results.FirstOrDefault(r => r.GateName == "Privacy Readiness");
        gate3.Should().NotBeNull();
        gate3!.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task Gate3_NoPta_Fails()
    {
        var system = await SeedSystemWithRolesAndBoundary();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate3 = results.FirstOrDefault(r => r.GateName == "Privacy Readiness");
        gate3.Should().NotBeNull();
        gate3!.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task Gate3_SystemPastPrepare_AdvisoryOnly()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.CurrentRmfStep = RmfPhase.Select; // already past Prepare→Categorize

        // No PTA — gate would normally fail, but should be advisory/warning for systems past Prepare
        await _db.SaveChangesAsync();

        // Checking gates for Categorize→Select should not include privacy gate
        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Implement);

        // Privacy gate is only on Prepare→Categorize, so it shouldn't appear for later transitions
        var gate3 = results.FirstOrDefault(r => r.GateName == "Privacy Readiness");
        gate3.Should().BeNull("privacy gate only applies to Prepare→Categorize transition");
    }

    // ─── Gate 4: Interconnection Documentation ──────────────────────────

    [Fact]
    public async Task Gate4_AllInterconnectionsWithSignedIsa_Passes()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        // Add PTA so gate 3 passes too
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PiaNotRequired,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };

        var interconnection = new SystemInterconnection
        {
            RegisteredSystemId = system.Id,
            TargetSystemName = "DISA Gateway",
            InterconnectionType = InterconnectionType.Vpn,
            DataFlowDirection = DataFlowDirection.Bidirectional,
            DataClassification = "CUI",
            Status = InterconnectionStatus.Active,
            CreatedBy = "test-user"
        };
        interconnection.Agreements.Add(new InterconnectionAgreement
        {
            SystemInterconnectionId = interconnection.Id,
            Title = "DISA ISA",
            AgreementType = AgreementType.Isa,
            Status = AgreementStatus.Signed,
            EffectiveDate = DateTime.UtcNow.AddMonths(-6),
            ExpirationDate = DateTime.UtcNow.AddMonths(6),
            CreatedBy = "test-user"
        });
        system.SystemInterconnections.Add(interconnection);
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate4 = results.FirstOrDefault(r => r.GateName == "Interconnection Documentation");
        gate4.Should().NotBeNull();
        gate4!.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Gate4_MissingAgreement_Fails()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PiaNotRequired,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };

        // Active interconnection with no agreement
        system.SystemInterconnections.Add(new SystemInterconnection
        {
            RegisteredSystemId = system.Id,
            TargetSystemName = "External API",
            InterconnectionType = InterconnectionType.Api,
            DataFlowDirection = DataFlowDirection.Outbound,
            DataClassification = "Unclassified",
            Status = InterconnectionStatus.Active,
            CreatedBy = "test-user"
        });
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate4 = results.FirstOrDefault(r => r.GateName == "Interconnection Documentation");
        gate4.Should().NotBeNull();
        gate4!.Passed.Should().BeFalse();
        gate4.Severity.Should().Be("Error");
    }

    [Fact]
    public async Task Gate4_ExpiredAgreement_Fails()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PiaNotRequired,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };

        var interconnection = new SystemInterconnection
        {
            RegisteredSystemId = system.Id,
            TargetSystemName = "Legacy System",
            InterconnectionType = InterconnectionType.Direct,
            DataFlowDirection = DataFlowDirection.Inbound,
            DataClassification = "CUI",
            Status = InterconnectionStatus.Active,
            CreatedBy = "test-user"
        };
        interconnection.Agreements.Add(new InterconnectionAgreement
        {
            SystemInterconnectionId = interconnection.Id,
            Title = "Legacy ISA",
            AgreementType = AgreementType.Isa,
            Status = AgreementStatus.Signed,
            ExpirationDate = DateTime.UtcNow.AddDays(-30), // expired
            CreatedBy = "test-user"
        });
        system.SystemInterconnections.Add(interconnection);
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate4 = results.FirstOrDefault(r => r.GateName == "Interconnection Documentation");
        gate4.Should().NotBeNull();
        gate4!.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task Gate4_HasNoExternalInterconnections_Passes()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PiaNotRequired,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };
        system.HasNoExternalInterconnections = true;
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate4 = results.FirstOrDefault(r => r.GateName == "Interconnection Documentation");
        gate4.Should().NotBeNull();
        gate4!.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Gate4_NoInterconnectionsAndNotCertified_Fails()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.PrivacyThresholdAnalysis = new PrivacyThresholdAnalysis
        {
            RegisteredSystemId = system.Id,
            Determination = PtaDetermination.PiaNotRequired,
            AnalyzedBy = "test-user",
            AnalyzedAt = DateTime.UtcNow
        };
        // No interconnections AND not certified
        system.HasNoExternalInterconnections = false;
        await _db.SaveChangesAsync();

        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Categorize);

        var gate4 = results.FirstOrDefault(r => r.GateName == "Interconnection Documentation");
        gate4.Should().NotBeNull();
        gate4!.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task Gate4_SystemPastPrepare_NotApplied()
    {
        var system = await SeedSystemWithRolesAndBoundary();
        system.CurrentRmfStep = RmfPhase.Select;
        await _db.SaveChangesAsync();

        // Checking Categorize→Select — should not include interconnection gate
        var results = await _service.CheckGateConditionsAsync(system.Id, RmfPhase.Implement);

        var gate4 = results.FirstOrDefault(r => r.GateName == "Interconnection Documentation");
        gate4.Should().BeNull("interconnection gate only applies to Prepare→Categorize transition");
    }
}
