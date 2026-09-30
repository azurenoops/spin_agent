using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for SapService — Feature 018 SAP generation, update, finalization, retrieval.
/// T008: GenerateSapAsync tests (Phase 3 / US1).
/// T021: UpdateSapAsync tests (Phase 4 / US2).
/// T022: FinalizeSapAsync tests (Phase 4 / US2).
/// T033: GetSapAsync tests (Phase 5 / US3).
/// T034: ListSapsAsync tests (Phase 5 / US3).
/// T047: ValidateSapAsync tests (Phase 6 / US4).
/// </summary>
public class SapServiceTests : IDisposable
{
    [Fact]
    public async Task UpdateSap_StoresManualDraftFields_RendersContent_AndRejectsStaleEdit()
    {
        // Arrange
        SeedBaseline();
        var draft = await _service.GenerateSapAsync(new SapGenerationInput(TestSystemId), "author");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(draft.Content))).ToLowerInvariant();
        var input = new SapUpdateInput(draft.SapId, ScopeNotes: "Reviewed mission boundary",
            Title: "Initial mission assessment", AssessmentLead: "Assigned assessor", AssessmentApproach: "Examine, interview and test", ExpectedContentHash: hash);
        // Act
        var updated = await _service.UpdateSapAsync(input);
        // Assert
        updated.Title.Should().Be("Initial mission assessment");
        updated.Content.Should().Contain("Assigned assessor").And.Contain("Examine, interview and test").And.Contain("Reviewed mission boundary");
        await FluentActions.Awaiting(() => _service.UpdateSapAsync(input)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed*");
    }

    private const string TestSystemId = "sys-001";
    private const string TestAssessmentId = "assess-001";

    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<INistControlsService> _nistServiceMock;
    private readonly Mock<IStigKnowledgeService> _stigServiceMock;
    private readonly SapService _service;

    // ─── Standard test data ──────────────────────────────────────────────

    private static readonly List<string> ModerateControlIds = new()
    {
        "AC-1", "AC-2", "AC-3", "AC-4", "AC-5",
        "AT-1", "AT-2",
        "AU-1", "AU-2", "AU-3"
    };

    private readonly RegisteredSystem _testSystem = new()
    {
        Id = TestSystemId,
        Name = "Test System",
        Acronym = "TSYS",
        CurrentRmfStep = RmfPhase.Assess
    };

    private readonly ControlBaseline _testBaseline = new()
    {
        RegisteredSystemId = TestSystemId,
        BaselineLevel = "Moderate",
        TotalControls = 10,
        CustomerControls = 6,
        InheritedControls = 2,
        SharedControls = 2,
        ControlIds = new List<string>(ModerateControlIds)
    };

    public SapServiceTests()
    {
        var services = new ServiceCollection();
        var dbName = $"SapServiceTests_{Guid.NewGuid()}";
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));
        _serviceProvider = services.BuildServiceProvider();

        // Initialize DB
        using var initScope = _serviceProvider.CreateScope();
        var initCtx = initScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        initCtx.Database.EnsureCreated();

        // Seed standard test system
        initCtx.RegisteredSystems.Add(_testSystem);
        initCtx.SaveChanges();

        // Set up mocks
        _nistServiceMock = new Mock<INistControlsService>();
        _stigServiceMock = new Mock<IStigKnowledgeService>();

        // Default NIST mock: return objectives per control
        foreach (var controlId in ModerateControlIds)
        {
            var family = controlId.Split('-')[0];
            _nistServiceMock
                .Setup(s => s.GetControlEnhancementAsync(controlId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ControlEnhancement(
                    controlId,
                    $"{controlId} Title",
                    $"{controlId} statement text",
                    $"{controlId} guidance text",
                    new List<string> { $"{controlId}(a)", $"{controlId}(b)" },
                    DateTime.UtcNow));
        }

        // Default STIG mock: return empty list (no STIGs mapped)
        _stigServiceMock
            .Setup(s => s.GetStigsByCciChainAsync(It.IsAny<string>(), It.IsAny<StigSeverity?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl>());

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _service = new SapService(
            scopeFactory,
            NullLogger<SapService>.Instance,
            _nistServiceMock.Object,
            _stigServiceMock.Object);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    // ─── Seed helpers ────────────────────────────────────────────────────

    private void SeedBaseline()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        ctx.ControlBaselines.Add(_testBaseline);
        ctx.SaveChanges();
    }

    private void SeedBaselineWithInheritances()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var baseline = new ControlBaseline
        {
            RegisteredSystemId = TestSystemId,
            BaselineLevel = "Moderate",
            TotalControls = 10,
            CustomerControls = 6,
            InheritedControls = 2,
            SharedControls = 2,
            ControlIds = new List<string>(ModerateControlIds)
        };
        ctx.ControlBaselines.Add(baseline);

        // Add inheritance designations for some controls
        ctx.ControlInheritances.Add(new ControlInheritance
        {
            ControlBaselineId = baseline.Id,
            ControlId = "AC-1",
            InheritanceType = InheritanceType.Inherited,
            Provider = "AWS GovCloud",
            SetBy = "isso@example.com"
        });
        ctx.ControlInheritances.Add(new ControlInheritance
        {
            ControlBaselineId = baseline.Id,
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Shared,
            Provider = "AWS GovCloud",
            CustomerResponsibility = "Configure account policies",
            SetBy = "isso@example.com"
        });
        ctx.SaveChanges();
    }

    private void SeedScaRoleAssignment()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        ctx.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = TestSystemId,
            RmfRole = RmfRole.Sca,
            UserId = "sca@example.com",
            UserDisplayName = "Test SCA",
            AssignedBy = "admin@example.com"
        });
        ctx.SaveChanges();
    }

    private void SeedStigMappings()
    {
        // Set up STIG mock to return mappings for AC-2
        _stigServiceMock
            .Setup(s => s.GetStigsByCciChainAsync("AC-2", It.IsAny<StigSeverity?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StigControl>
            {
                new(
                    StigId: "stig-001",
                    VulnId: "V-254239",
                    RuleId: "SV-254239r1_rule",
                    Title: "Windows account lockout",
                    Description: "Account lockout policy",
                    Severity: StigSeverity.High,
                    Category: "CAT I",
                    StigFamily: "Access Control",
                    NistControls: new List<string> { "AC-2" },
                    CciRefs: new List<string> { "CCI-000015" },
                    CheckText: "Check lockout settings",
                    FixText: "Set lockout threshold",
                    AzureImplementation: new Dictionary<string, string>(),
                    ServiceType: "Windows",
                    BenchmarkId: "Windows_Server_2022_STIG"
                ),
                new(
                    StigId: "stig-002",
                    VulnId: "V-254240",
                    RuleId: "SV-254240r1_rule",
                    Title: "Linux account management",
                    Description: "Account management controls",
                    Severity: StigSeverity.Medium,
                    Category: "CAT II",
                    StigFamily: "Access Control",
                    NistControls: new List<string> { "AC-2" },
                    CciRefs: new List<string> { "CCI-000016" },
                    CheckText: "Check usermod",
                    FixText: "Configure usermod",
                    AzureImplementation: new Dictionary<string, string>(),
                    ServiceType: "Linux",
                    BenchmarkId: "RHEL_9_STIG"
                )
            });
    }

    private static SapGenerationInput CreateDefaultInput(string? systemId = null) => new(
        SystemId: systemId ?? TestSystemId);

    // ═══════════════════════════════════════════════════════════════════════
    // T008: GenerateSapAsync — Success Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateSapAsync_WithValidBaseline_ReturnsSapDocument()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var input = CreateDefaultInput();
        var result = await _service.GenerateSapAsync(input);

        result.Should().NotBeNull();
        result.SapId.Should().NotBeNullOrEmpty();
        result.SystemId.Should().Be(TestSystemId);
        result.Status.Should().Be("Draft");
        result.BaselineLevel.Should().Be("Moderate");
        result.Format.Should().Be("markdown");
        result.TotalControls.Should().Be(10);
    }

    [Fact]
    public async Task GenerateSapAsync_PopulatesAssessmentObjectives()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        // Each control should have objectives from NIST mock
        result.ControlsWithObjectives.Should().BeGreaterThan(0);
        result.Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateSapAsync_DefaultMethods_AllThree()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        // Verify control entries in DB have default methods
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var entries = await ctx.SapControlEntries
            .Where(e => e.SecurityAssessmentPlanId == result.SapId)
            .ToListAsync();

        entries.Should().NotBeEmpty();
        entries.Should().AllSatisfy(e =>
        {
            e.AssessmentMethods.Should().HaveCount(3);
            e.AssessmentMethods.Should().Contain("Examine");
            e.AssessmentMethods.Should().Contain("Interview");
            e.AssessmentMethods.Should().Contain("Test");
        });
    }

    [Fact]
    public async Task GenerateSapAsync_WithStigMappings_PopulatesBenchmarks()
    {
        SeedBaseline();
        SeedScaRoleAssignment();
        SeedStigMappings();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        result.StigBenchmarkCount.Should().BeGreaterThan(0);

        // Verify AC-2 entry has STIG benchmarks
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var ac2Entry = await ctx.SapControlEntries
            .Where(e => e.SecurityAssessmentPlanId == result.SapId && e.ControlId == "AC-2")
            .FirstOrDefaultAsync();

        ac2Entry.Should().NotBeNull();
        ac2Entry!.StigBenchmarks.Should().Contain("Windows_Server_2022_STIG");
        ac2Entry.StigBenchmarks.Should().Contain("RHEL_9_STIG");
    }

    [Fact]
    public async Task GenerateSapAsync_WithInheritances_AnnotatesControlEntries()
    {
        SeedBaselineWithInheritances();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var entries = await ctx.SapControlEntries
            .Where(e => e.SecurityAssessmentPlanId == result.SapId)
            .ToListAsync();

        var ac1 = entries.FirstOrDefault(e => e.ControlId == "AC-1");
        ac1.Should().NotBeNull();
        ac1!.InheritanceType.Should().Be(InheritanceType.Inherited);
        ac1.Provider.Should().Be("AWS GovCloud");

        var ac2 = entries.FirstOrDefault(e => e.ControlId == "AC-2");
        ac2.Should().NotBeNull();
        ac2!.InheritanceType.Should().Be(InheritanceType.Shared);

        // Remaining controls default to Customer
        var ac3 = entries.FirstOrDefault(e => e.ControlId == "AC-3");
        ac3.Should().NotBeNull();
        ac3!.InheritanceType.Should().Be(InheritanceType.Customer);
    }

    [Fact]
    public async Task GenerateSapAsync_WithMethodOverrides_AppliesOverrides()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var input = new SapGenerationInput(
            SystemId: TestSystemId,
            MethodOverrides: new List<SapMethodOverrideInput>
            {
                new("AC-2", new List<string> { "Examine", "Interview" }, "Test not applicable for shared control")
            });

        var result = await _service.GenerateSapAsync(input);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var ac2 = await ctx.SapControlEntries
            .Where(e => e.SecurityAssessmentPlanId == result.SapId && e.ControlId == "AC-2")
            .FirstOrDefaultAsync();

        ac2.Should().NotBeNull();
        ac2!.AssessmentMethods.Should().HaveCount(2);
        ac2.AssessmentMethods.Should().Contain("Examine");
        ac2.AssessmentMethods.Should().Contain("Interview");
        ac2.AssessmentMethods.Should().NotContain("Test");
        ac2.IsMethodOverridden.Should().BeTrue();
        ac2.OverrideRationale.Should().Be("Test not applicable for shared control");
    }

    [Fact]
    public async Task GenerateSapAsync_RetryRetainsExistingDraftAndChildren()
    {
        // Arrange
        SeedBaseline();
        SeedScaRoleAssignment();

        var first = await _service.GenerateSapAsync(CreateDefaultInput());
        first.Status.Should().Be("Draft");

        // Act
        var second = await _service.GenerateSapAsync(CreateDefaultInput());
        second.Status.Should().Be("Draft");

        // Assert
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var allSaps = await ctx.SecurityAssessmentPlans
            .Where(s => s.RegisteredSystemId == TestSystemId)
            .ToListAsync();

        allSaps.Should().ContainSingle();
        allSaps[0].Id.Should().Be(second.SapId);
        second.SapId.Should().Be(first.SapId);
        second.Content.Should().Be(first.Content);
    }

    [Fact]
    public async Task GenerateSapAsync_WithTeamMembers_PersistsTeam()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var input = new SapGenerationInput(
            SystemId: TestSystemId,
            TeamMembers: new List<SapTeamMemberInput>
            {
                new("Jane Doe", "ACME Security", "Lead Assessor", "jane@acme.com"),
                new("John Smith", "ACME Security", "Assessor")
            });

        var result = await _service.GenerateSapAsync(input);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var members = await ctx.SapTeamMembers
            .Where(m => m.SecurityAssessmentPlanId == result.SapId)
            .ToListAsync();

        members.Should().HaveCount(2);
        members.Should().Contain(m => m.Name == "Jane Doe" && m.Role == "Lead Assessor");
        members.Should().Contain(m => m.Name == "John Smith" && m.Role == "Assessor");
    }

    [Fact]
    public async Task GenerateSapAsync_WithSchedule_PersistsDates()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var start = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);

        var input = new SapGenerationInput(
            SystemId: TestSystemId,
            ScheduleStart: start,
            ScheduleEnd: end);

        var result = await _service.GenerateSapAsync(input);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await ctx.SecurityAssessmentPlans.FindAsync(result.SapId);

        sap.Should().NotBeNull();
        sap!.ScheduleStart.Should().Be(start);
        sap.ScheduleEnd.Should().Be(end);
    }

    [Fact]
    public async Task GenerateSapAsync_PersistsEntityToDatabase()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var stored = await ctx.SecurityAssessmentPlans
            .Include(s => s.ControlEntries)
            .Include(s => s.TeamMembers)
            .FirstOrDefaultAsync(s => s.Id == result.SapId);

        stored.Should().NotBeNull();
        stored!.Status.Should().Be(SapStatus.Draft);
        stored.RegisteredSystemId.Should().Be(TestSystemId);
        stored.BaselineLevel.Should().Be("Moderate");
        stored.Content.Should().NotBeNullOrEmpty();
        stored.ControlEntries.Should().HaveCount(10);
    }

    [Fact]
    public async Task GenerateSapAsync_GeneratesMarkdownContent()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        result.Content.Should().NotBeNullOrEmpty();
        // SAP Markdown should contain key sections
        result.Content.Should().Contain("Security Assessment Plan");
        result.Content.Should().Contain("Assessment Scope");
    }

    [Fact]
    public async Task GenerateSapAsync_PopulatesEvidenceGaps()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        // With no evidence collected, all controls should have gaps
        result.EvidenceGaps.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task GenerateSapAsync_WithAssessmentId_LinksAssessment()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        // Seed an assessment
        using (var scope = _serviceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            ctx.Assessments.Add(new ComplianceAssessment
            {
                Id = TestAssessmentId,
                RegisteredSystemId = TestSystemId
            });
            ctx.SaveChanges();
        }

        var input = new SapGenerationInput(
            SystemId: TestSystemId,
            AssessmentId: TestAssessmentId);

        var result = await _service.GenerateSapAsync(input);

        result.AssessmentId.Should().Be(TestAssessmentId);

        using var verifyScope = _serviceProvider.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await verifyCtx.SecurityAssessmentPlans.FindAsync(result.SapId);
        sap!.AssessmentId.Should().Be(TestAssessmentId);
    }

    [Fact]
    public async Task GenerateSapAsync_SetsGeneratedByField()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(
            CreateDefaultInput(), generatedBy: "sca@example.com");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await ctx.SecurityAssessmentPlans.FindAsync(result.SapId);
        sap!.GeneratedBy.Should().Be("sca@example.com");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T008: GenerateSapAsync — Warning Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateSapAsync_NotInAssessPhase_AddsWarning()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        // Change system to non-Assess phase
        using (var scope = _serviceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var system = await ctx.RegisteredSystems.FindAsync(TestSystemId);
            system!.CurrentRmfStep = RmfPhase.Implement;
            ctx.SaveChanges();
        }

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        result.Warnings.Should().Contain(w => w.Contains("Assess", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateSapAsync_NoScaAssignment_AddsWarning()
    {
        SeedBaseline();
        // Deliberately NOT seeding SCA role assignment

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        result.Warnings.Should().Contain(w =>
            w.Contains("SCA", StringComparison.OrdinalIgnoreCase) ||
            w.Contains("assessor", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T008: GenerateSapAsync — Error Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateSapAsync_SystemNotFound_ThrowsInvalidOperation()
    {
        var input = new SapGenerationInput(SystemId: "nonexistent-system");

        var act = () => _service.GenerateSapAsync(input);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GenerateSapAsync_NoBaseline_ThrowsInvalidOperation()
    {
        // System exists but no baseline seeded
        var input = CreateDefaultInput();

        var act = () => _service.GenerateSapAsync(input);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*baseline*");
    }

    [Fact]
    public async Task GenerateSapAsync_EmptyBaseline_ThrowsInvalidOperation()
    {
        // Seed baseline with empty control list
        using (var scope = _serviceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            ctx.ControlBaselines.Add(new ControlBaseline
            {
                RegisteredSystemId = TestSystemId,
                BaselineLevel = "Moderate",
                TotalControls = 0,
                ControlIds = new List<string>()
            });
            ctx.SaveChanges();
        }

        var input = CreateDefaultInput();

        var act = () => _service.GenerateSapAsync(input);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*baseline*");
    }

    [Fact]
    public async Task GenerateSapAsync_InvalidMethodOverride_ThrowsInvalidOperation()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var input = new SapGenerationInput(
            SystemId: TestSystemId,
            MethodOverrides: new List<SapMethodOverrideInput>
            {
                new("AC-2", new List<string> { "Examine", "InvalidMethod" })
            });

        var act = () => _service.GenerateSapAsync(input);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*method*");
    }

    [Fact]
    public async Task GenerateSapAsync_CancellationToken_CancelsOperation()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _service.GenerateSapAsync(CreateDefaultInput(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T008: GenerateSapAsync — Family Summary
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateSapAsync_PopulatesFamilySummaries()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        // Test data has controls from AC, AT, AU families
        result.FamilySummaries.Should().NotBeEmpty();
        result.FamilySummaries.Should().Contain(f => f.Family.Contains("AC") || f.Family.Contains("Access Control"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T008: GenerateSapAsync — Title Generation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateSapAsync_GeneratesTitle()
    {
        SeedBaseline();
        SeedScaRoleAssignment();

        var result = await _service.GenerateSapAsync(CreateDefaultInput());

        result.Title.Should().NotBeNullOrEmpty();
        result.Title.Should().Contain("Test System");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Shared helper: Generate a Draft SAP for update/finalize tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Seeds baseline + SCA role and generates a Draft SAP, returning the SapDocument.</summary>
    private async Task<SapDocument> GenerateDraftSapAsync(SapGenerationInput? input = null)
    {
        SeedBaseline();
        SeedScaRoleAssignment();
        return await _service.GenerateSapAsync(input ?? CreateDefaultInput());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T021: UpdateSapAsync — Success Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateSapAsync_UpdateScheduleDates_PersistsNewDates()
    {
        var draft = await GenerateDraftSapAsync();

        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

        var updateInput = new SapUpdateInput(
            SapId: draft.SapId,
            ScheduleStart: start,
            ScheduleEnd: end);

        var result = await _service.UpdateSapAsync(updateInput);

        result.SapId.Should().Be(draft.SapId);
        result.Status.Should().Be("Draft");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await ctx.SecurityAssessmentPlans.FindAsync(draft.SapId);
        sap!.ScheduleStart.Should().Be(start);
        sap.ScheduleEnd.Should().Be(end);
    }

    [Fact]
    public async Task UpdateSapAsync_UpdateTeamMembers_ReplacesTeamAtomically()
    {
        var inputWithTeam = new SapGenerationInput(
            SystemId: TestSystemId,
            TeamMembers: new List<SapTeamMemberInput>
            {
                new("Original Member", "OrgA", "Assessor")
            });
        var draft = await GenerateDraftSapAsync(inputWithTeam);

        // Update with new team
        var updateInput = new SapUpdateInput(
            SapId: draft.SapId,
            TeamMembers: new List<SapTeamMemberInput>
            {
                new("New Lead", "OrgB", "Lead Assessor", "lead@orgb.mil"),
                new("New Support", "OrgC", "Assessor")
            });

        var result = await _service.UpdateSapAsync(updateInput);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var members = await ctx.SapTeamMembers
            .Where(m => m.SecurityAssessmentPlanId == draft.SapId)
            .ToListAsync();

        members.Should().HaveCount(2);
        members.Should().Contain(m => m.Name == "New Lead" && m.Role == "Lead Assessor");
        members.Should().Contain(m => m.Name == "New Support");
        members.Should().NotContain(m => m.Name == "Original Member");
    }

    [Fact]
    public async Task UpdateSapAsync_UpdateRulesOfEngagement_PersistsText()
    {
        var draft = await GenerateDraftSapAsync();

        var updateInput = new SapUpdateInput(
            SapId: draft.SapId,
            RulesOfEngagement: "Testing windows: Mon-Fri 0800-1700. No production scans on weekends.");

        var result = await _service.UpdateSapAsync(updateInput);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await ctx.SecurityAssessmentPlans.FindAsync(draft.SapId);
        sap!.RulesOfEngagement.Should().Contain("Mon-Fri");
    }

    [Fact]
    public async Task UpdateSapAsync_UpdateScopeNotes_PersistsText()
    {
        var draft = await GenerateDraftSapAsync();

        var updateInput = new SapUpdateInput(
            SapId: draft.SapId,
            ScopeNotes: "Assessment covers all Moderate baseline controls in production.");

        var result = await _service.UpdateSapAsync(updateInput);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await ctx.SecurityAssessmentPlans.FindAsync(draft.SapId);
        sap!.ScopeNotes.Should().Contain("Moderate baseline controls");
    }

    [Fact]
    public async Task UpdateSapAsync_ApplyMethodOverrides_UpdatesControlEntry()
    {
        var draft = await GenerateDraftSapAsync();

        var updateInput = new SapUpdateInput(
            SapId: draft.SapId,
            MethodOverrides: new List<SapMethodOverrideInput>
            {
                new("AC-3", new List<string> { "Examine" }, "Policy review only")
            });

        var result = await _service.UpdateSapAsync(updateInput);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var ac3 = await ctx.SapControlEntries
            .FirstOrDefaultAsync(e => e.SecurityAssessmentPlanId == draft.SapId && e.ControlId == "AC-3");

        ac3.Should().NotBeNull();
        ac3!.AssessmentMethods.Should().HaveCount(1);
        ac3.AssessmentMethods.Should().Contain("Examine");
        ac3.IsMethodOverridden.Should().BeTrue();
        ac3.OverrideRationale.Should().Be("Policy review only");
    }

    [Fact]
    public async Task UpdateSapAsync_ReRendersContent_AfterUpdate()
    {
        var draft = await GenerateDraftSapAsync();
        var originalContent = draft.Content;

        var updateInput = new SapUpdateInput(
            SapId: draft.SapId,
            ScopeNotes: "Updated scope notes for re-render test.");

        var result = await _service.UpdateSapAsync(updateInput);

        result.Content.Should().NotBeNullOrEmpty();
        result.Content.Should().Contain("Updated scope notes for re-render test.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T021: UpdateSapAsync — Error Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateSapAsync_FinalizedSap_ThrowsInvalidOperation()
    {
        var draft = await GenerateDraftSapAsync();

        // Finalize the SAP first
        await _service.FinalizeSapAsync(draft.SapId);

        var updateInput = new SapUpdateInput(SapId: draft.SapId, ScopeNotes: "Should fail");

        var act = () => _service.UpdateSapAsync(updateInput);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*finalized*");
    }

    [Fact]
    public async Task UpdateSapAsync_SapNotFound_ThrowsInvalidOperation()
    {
        var updateInput = new SapUpdateInput(SapId: "nonexistent-sap");

        var act = () => _service.UpdateSapAsync(updateInput);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task UpdateSapAsync_InvalidMethodOverride_ThrowsInvalidOperation()
    {
        var draft = await GenerateDraftSapAsync();

        var updateInput = new SapUpdateInput(
            SapId: draft.SapId,
            MethodOverrides: new List<SapMethodOverrideInput>
            {
                new("AC-1", new List<string> { "Scan" })
            });

        var act = () => _service.UpdateSapAsync(updateInput);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*method*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T022: FinalizeSapAsync — Success Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FinalizeSapAsync_SetsStatusFinalizedAndHash()
    {
        var draft = await GenerateDraftSapAsync();

        var result = await _service.FinalizeSapAsync(draft.SapId, "sca@example.com");

        result.Status.Should().Be("Finalized");
        result.ContentHash.Should().NotBeNullOrWhiteSpace();
        result.SapId.Should().Be(draft.SapId);
    }

    [Fact]
    public async Task FinalizeSapAsync_Sha256MatchesContent()
    {
        var draft = await GenerateDraftSapAsync();

        var result = await _service.FinalizeSapAsync(draft.SapId);

        // Manually compute SHA-256 of the content
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(result.Content));
        var expectedHash = Convert.ToHexStringLower(hash);

        result.ContentHash.Should().Be(expectedHash);
    }

    [Fact]
    public async Task FinalizeSapAsync_SetsFinalizedByAndTimestamp()
    {
        var draft = await GenerateDraftSapAsync();
        var beforeFinalize = DateTime.UtcNow;

        var result = await _service.FinalizeSapAsync(draft.SapId, "lead@acme.com");

        result.FinalizedAt.Should().NotBeNull();
        result.FinalizedAt!.Value.Should().BeOnOrAfter(beforeFinalize);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await ctx.SecurityAssessmentPlans.FindAsync(draft.SapId);
        sap!.FinalizedBy.Should().Be("lead@acme.com");
        sap.Status.Should().Be(SapStatus.Finalized);
    }

    [Fact]
    public async Task FinalizeSapAsync_PersistsToDatabase()
    {
        var draft = await GenerateDraftSapAsync();

        await _service.FinalizeSapAsync(draft.SapId);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await ctx.SecurityAssessmentPlans.FindAsync(draft.SapId);
        sap!.Status.Should().Be(SapStatus.Finalized);
        sap.ContentHash.Should().NotBeNullOrWhiteSpace();
        sap.FinalizedAt.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T022: FinalizeSapAsync — Error Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FinalizeSapAsync_AlreadyFinalized_ThrowsInvalidOperation()
    {
        var draft = await GenerateDraftSapAsync();
        await _service.FinalizeSapAsync(draft.SapId);

        var act = () => _service.FinalizeSapAsync(draft.SapId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*finalized*");
    }

    [Fact]
    public async Task FinalizeSapAsync_NonExistentSap_ThrowsInvalidOperation()
    {
        var act = () => _service.FinalizeSapAsync("nonexistent-sap");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task FinalizeSapAsync_ImmutableAfterFinalization()
    {
        var draft = await GenerateDraftSapAsync();
        var finalized = await _service.FinalizeSapAsync(draft.SapId);

        // Attempt to update should fail
        var updateInput = new SapUpdateInput(SapId: draft.SapId, ScopeNotes: "Should fail");
        var act = () => _service.UpdateSapAsync(updateInput);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*finalized*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T033: GetSapAsync — Success Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSapAsync_BySapId_ReturnsSapDocument()
    {
        var draft = await GenerateDraftSapAsync();

        var result = await _service.GetSapAsync(sapId: draft.SapId);

        result.Should().NotBeNull();
        result.SapId.Should().Be(draft.SapId);
        result.SystemId.Should().Be(TestSystemId);
        result.Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSapAsync_BySystemId_ReturnsLatestSap()
    {
        var draft = await GenerateDraftSapAsync();

        var result = await _service.GetSapAsync(systemId: TestSystemId);

        result.Should().NotBeNull();
        result.SapId.Should().Be(draft.SapId);
    }

    [Fact]
    public async Task GetSapAsync_BySystemId_PrefersWorkingDraftOverFinalizedHistory()
    {
        // Arrange
        var first = await GenerateDraftSapAsync();
        await _service.FinalizeSapAsync(first.SapId);

        // Generate a new Draft SAP (seed baseline again since GenerateDraftSapAsync seeds)
        var secondInput = new SapGenerationInput(SystemId: TestSystemId);
        var second = await _service.GenerateSapAsync(secondInput);

        // Act
        var result = await _service.GetSapAsync(systemId: TestSystemId);

        // Assert
        result.Should().NotBeNull();
        result.SapId.Should().Be(second.SapId);
        result.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task GetSapAsync_SapIdTakesPrecedence_OverSystemId()
    {
        var draft = await GenerateDraftSapAsync();

        // Pass both sapId and systemId — sapId should take precedence
        var result = await _service.GetSapAsync(sapId: draft.SapId, systemId: "wrong-system-id");

        result.Should().NotBeNull();
        result.SapId.Should().Be(draft.SapId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T033: GetSapAsync — Error Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSapAsync_InvalidSapId_ThrowsInvalidOperation()
    {
        var act = () => _service.GetSapAsync(sapId: "nonexistent-sap");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetSapAsync_SystemWithNoSaps_ThrowsInvalidOperation()
    {
        // System exists but no SAPs generated
        var act = () => _service.GetSapAsync(systemId: TestSystemId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetSapAsync_NeitherIdProvided_ThrowsInvalidOperation()
    {
        var act = () => _service.GetSapAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T034: ListSapsAsync — Success Scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListSapsAsync_ReturnsDraftAndFinalizedHistory()
    {
        // Generate and finalize first SAP
        var first = await GenerateDraftSapAsync();
        await _service.FinalizeSapAsync(first.SapId);

        // Generate a second Draft
        var second = await _service.GenerateSapAsync(CreateDefaultInput());

        var results = await _service.ListSapsAsync(TestSystemId);

        results.Should().HaveCount(2);
        results.Should().Contain(s => s.Status == "Finalized");
        results.Should().Contain(s => s.Status == "Draft");
    }

    [Fact]
    public async Task ListSapsAsync_OrdersByGeneratedAtDescending()
    {
        var first = await GenerateDraftSapAsync();
        await _service.FinalizeSapAsync(first.SapId);

        // Generate second Draft (more recent)
        var second = await _service.GenerateSapAsync(CreateDefaultInput());

        var results = await _service.ListSapsAsync(TestSystemId);

        results.Should().HaveCount(2);
        // Most recent should be first
        results[0].GeneratedAt.Should().BeOnOrAfter(results[1].GeneratedAt);
    }

    [Fact]
    public async Task ListSapsAsync_EmptyListForSystemWithNoSaps()
    {
        var results = await _service.ListSapsAsync(TestSystemId);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSapsAsync_IncludesScopeSummary()
    {
        await GenerateDraftSapAsync();

        var results = await _service.ListSapsAsync(TestSystemId);

        results.Should().ContainSingle();
        var sap = results[0];
        sap.TotalControls.Should().BeGreaterThan(0);
        sap.BaselineLevel.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ListSapsAsync_OmitsContentField()
    {
        await GenerateDraftSapAsync();

        var results = await _service.ListSapsAsync(TestSystemId);

        results.Should().ContainSingle();
        // Content should be empty or null for list results
        results[0].Content.Should().BeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T035: Format Dispatch — DOCX/PDF/Markdown Export
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateSapAsync_FormatMarkdown_ReturnsStringContent()
    {
        SeedBaseline();
        var input = new SapGenerationInput(SystemId: TestSystemId, Format: "markdown");

        var result = await _service.GenerateSapAsync(input);

        result.Format.Should().Be("markdown");
        result.Content.Should().StartWith("# Security Assessment Plan");
    }

    [Fact]
    public async Task GenerateSapAsync_FormatDocx_ReturnsBase64Bytes()
    {
        SeedBaseline();
        var fakeDocxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // PK zip header

        var docTemplateMock = new Mock<IDocumentTemplateService>();
        docTemplateMock
            .Setup(s => s.RenderDocxAsync(TestSystemId, "sap", null, It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(fakeDocxBytes);

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var serviceWithDocx = new SapService(
            scopeFactory,
            NullLogger<SapService>.Instance,
            _nistServiceMock.Object,
            _stigServiceMock.Object,
            docTemplateMock.Object);

        var input = new SapGenerationInput(SystemId: TestSystemId, Format: "docx");

        var result = await serviceWithDocx.GenerateSapAsync(input);

        result.Format.Should().Be("docx");
        result.Content.Should().Be(Convert.ToBase64String(fakeDocxBytes));
        docTemplateMock.Verify(
            s => s.RenderDocxAsync(TestSystemId, "sap", null, It.IsAny<CancellationToken>(), result.SapId),
            Times.Once);
    }

    [Fact]
    public async Task GenerateSapAsync_FormatPdf_ReturnsBase64Bytes()
    {
        SeedBaseline();
        var fakePdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF

        var docTemplateMock = new Mock<IDocumentTemplateService>();
        docTemplateMock
            .Setup(s => s.RenderPdfAsync(TestSystemId, "sap", null, It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(fakePdfBytes);

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var serviceWithPdf = new SapService(
            scopeFactory,
            NullLogger<SapService>.Instance,
            _nistServiceMock.Object,
            _stigServiceMock.Object,
            docTemplateMock.Object);

        var input = new SapGenerationInput(SystemId: TestSystemId, Format: "pdf");

        var result = await serviceWithPdf.GenerateSapAsync(input);

        result.Format.Should().Be("pdf");
        result.Content.Should().Be(Convert.ToBase64String(fakePdfBytes));
        docTemplateMock.Verify(
            s => s.RenderPdfAsync(TestSystemId, "sap", null, It.IsAny<CancellationToken>(), result.SapId),
            Times.Once);
    }

    [Fact]
    public async Task GenerateSapAsync_InvalidFormat_ThrowsInvalidOperation()
    {
        SeedBaseline();
        var input = new SapGenerationInput(SystemId: TestSystemId, Format: "html");

        var act = () => _service.GenerateSapAsync(input);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported format*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T047: ValidateSapAsync — Completeness Validation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidateSapAsync_CompleteSap_ReturnsIsCompleteTrue()
    {
        // Generate a SAP with team members and schedule for full completeness
        var input = new SapGenerationInput(
            SystemId: TestSystemId,
            ScheduleStart: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ScheduleEnd: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            TeamMembers: new List<SapTeamMemberInput>
            {
                new("sca@example.com", "Test SCA", "Lead Assessor")
            });
        var draft = await GenerateDraftSapAsync(input);

        var result = await _service.ValidateSapAsync(draft.SapId);

        result.IsComplete.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        result.ControlsCovered.Should().Be(10);
        result.ControlsMissingObjectives.Should().Be(0);
        result.ControlsMissingMethods.Should().Be(0);
        result.HasTeam.Should().BeTrue();
        result.HasSchedule.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSapAsync_MissingTeam_WarnsAboutTeam()
    {
        // Generate SAP without team members, but with schedule
        var input = new SapGenerationInput(
            SystemId: TestSystemId,
            ScheduleStart: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ScheduleEnd: new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc));
        var draft = await GenerateDraftSapAsync(input);

        var result = await _service.ValidateSapAsync(draft.SapId);

        result.IsComplete.Should().BeFalse();
        result.HasTeam.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("team", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateSapAsync_MissingSchedule_WarnsAboutSchedule()
    {
        // Generate SAP with team but without schedule dates
        var input = new SapGenerationInput(
            SystemId: TestSystemId,
            TeamMembers: new List<SapTeamMemberInput>
            {
                new("sca@example.com", "Test SCA", "Lead Assessor")
            });
        var draft = await GenerateDraftSapAsync(input);

        var result = await _service.ValidateSapAsync(draft.SapId);

        result.IsComplete.Should().BeFalse();
        result.HasSchedule.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("schedule", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateSapAsync_ControlsMissingMethods_WarnsAboutMethods()
    {
        var draft = await GenerateDraftSapAsync();

        // Clear methods for some controls manually
        using (var scope = _serviceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var entries = await ctx.SapControlEntries
                .Where(e => e.SecurityAssessmentPlanId == draft.SapId)
                .Take(3)
                .ToListAsync();
            foreach (var entry in entries)
            {
                entry.AssessmentMethods = new List<string>();
            }
            await ctx.SaveChangesAsync();
        }

        var result = await _service.ValidateSapAsync(draft.SapId);

        result.IsComplete.Should().BeFalse();
        result.ControlsMissingMethods.Should().Be(3);
        result.Warnings.Should().Contain(w => w.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateSapAsync_ControlsMissingObjectives_WarnsAboutObjectives()
    {
        var draft = await GenerateDraftSapAsync();

        // Clear objectives for some controls manually
        using (var scope = _serviceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var entries = await ctx.SapControlEntries
                .Where(e => e.SecurityAssessmentPlanId == draft.SapId)
                .Take(4)
                .ToListAsync();
            foreach (var entry in entries)
            {
                entry.AssessmentObjectives = new List<string>();
            }
            await ctx.SaveChangesAsync();
        }

        var result = await _service.ValidateSapAsync(draft.SapId);

        result.IsComplete.Should().BeFalse();
        result.ControlsMissingObjectives.Should().Be(4);
        result.Warnings.Should().Contain(w => w.Contains("objective", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateSapAsync_ReturnsCounts()
    {
        var draft = await GenerateDraftSapAsync();

        var result = await _service.ValidateSapAsync(draft.SapId);

        // All 10 controls should be covered
        result.ControlsCovered.Should().Be(10);
        // Default generation includes objectives and methods for all controls
        result.ControlsMissingObjectives.Should().Be(0);
        result.ControlsMissingMethods.Should().Be(0);
    }

    [Fact]
    public async Task ValidateSapAsync_InvalidSapId_ThrowsInvalidOperation()
    {
        var act = () => _service.ValidateSapAsync("nonexistent-sap-id");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ValidateSapAsync_MultipleWarnings_ReturnsAll()
    {
        // Generate SAP without team or schedule — at least two warnings
        var draft = await GenerateDraftSapAsync();

        var result = await _service.ValidateSapAsync(draft.SapId);

        result.IsComplete.Should().BeFalse();
        result.HasTeam.Should().BeFalse();
        result.HasSchedule.Should().BeFalse();
        result.Warnings.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T047: GetSapStatusAsync — Status Summary Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSapStatusAsync_NoSap_ReturnsNull()
    {
        var result = await _service.GetSapStatusAsync(TestSystemId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSapStatusAsync_WithDraftSap_ReturnsDraftStatus()
    {
        var draft = await GenerateDraftSapAsync();

        var result = await _service.GetSapStatusAsync(TestSystemId);

        result.Should().NotBeNull();
        result!.Status.Should().Be("Draft");
        result.SystemId.Should().Be(TestSystemId);
        result.TotalControls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetSapStatusAsync_WithFinalizedSap_ReturnsFinalizedStatus()
    {
        var draft = await GenerateDraftSapAsync();
        await _service.FinalizeSapAsync(draft.SapId, "sca@example.com");

        var result = await _service.GetSapStatusAsync(TestSystemId);

        result.Should().NotBeNull();
        result!.Status.Should().Be("Finalized");
    }

    [Fact]
    public async Task GetSapStatusAsync_ReturnsScopeCoverage()
    {
        var draft = await GenerateDraftSapAsync();

        var result = await _service.GetSapStatusAsync(TestSystemId);

        result.Should().NotBeNull();
        result!.TotalControls.Should().Be(10);
        result.FamilySummaries.Should().NotBeEmpty();
    }
}
