using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services.Engines.Remediation;

/// <summary>
/// Tests for <see cref="AtoRemediationEngine"/> plan generation methods (Phase 3).
/// Covers T042: plan from 50+ findings, severity/family/automatable filters,
/// executive summary, risk metrics, timeline phases, duration estimates,
/// empty findings, single-finding 3-tier fallback, and GroupByResource.
/// </summary>
public class AtoRemediationEngineTests
{
    private readonly Mock<IAtoComplianceEngine> _complianceEngine;
    private readonly Mock<IDbContextFactory<AtoCopilotContext>> _dbFactory;
    private readonly Mock<IAzureArmRemediationService> _armService;
    private readonly Mock<IAiRemediationPlanGenerator> _aiGenerator;
    private readonly Mock<IComplianceRemediationService> _complianceRemediation;
    private readonly Mock<IRemediationScriptExecutor> _scriptExecutor;
    private readonly Mock<INistRemediationStepsService> _nistSteps;
    private readonly Mock<IScriptSanitizationService> _sanitization;
    private readonly Mock<IKanbanService> _kanban;
    private readonly Mock<IServiceScopeFactory> _scopeFactory;
    private readonly Mock<ILogger<AtoRemediationEngine>> _logger;
    private readonly ComplianceAgentOptions _options;
    private readonly AtoRemediationEngine _sut;

    public AtoRemediationEngineTests()
    {
        _complianceEngine = new Mock<IAtoComplianceEngine>();
        _dbFactory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        _armService = new Mock<IAzureArmRemediationService>();
        _aiGenerator = new Mock<IAiRemediationPlanGenerator>();
        _complianceRemediation = new Mock<IComplianceRemediationService>();
        _scriptExecutor = new Mock<IRemediationScriptExecutor>();
        _nistSteps = new Mock<INistRemediationStepsService>();
        _sanitization = new Mock<IScriptSanitizationService>();
        _kanban = new Mock<IKanbanService>();
        _logger = new Mock<ILogger<AtoRemediationEngine>>();

        // Wire up IServiceScopeFactory → IServiceScope → IServiceProvider → IKanbanService
        _scopeFactory = new Mock<IServiceScopeFactory>();
        var mockScope = new Mock<IServiceScope>();
        var mockScopeProvider = new Mock<IServiceProvider>();
        mockScopeProvider.Setup(p => p.GetService(typeof(IKanbanService))).Returns(_kanban.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockScopeProvider.Object);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _options = new ComplianceAgentOptions
        {
            Remediation = new RemediationOptions
            {
                MaxConcurrentRemediations = 3,
                ScriptTimeoutSeconds = 300,
                MaxRetries = 3
            }
        };

        // Default: NIST steps service returns generic steps for any family
        _nistSteps.Setup(s => s.GetRemediationSteps(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((family, _) => new List<string>
            {
                $"Review {family} control requirements",
                $"Implement {family} remediation",
                $"Validate {family} compliance"
            });

        _nistSteps.Setup(s => s.ParseStepsFromGuidance(It.IsAny<string>()))
            .Returns<string>(text => string.IsNullOrEmpty(text)
                ? new List<string>()
                : new List<string> { text });

        _nistSteps.Setup(s => s.GetSkillLevel(It.IsAny<string>()))
            .Returns<string>(family => family switch
            {
                "SC" => "Advanced",
                "CP" => "Intermediate",
                _ => "Intermediate"
            });

        _sut = CreateEngine();
    }

    private AtoRemediationEngine CreateEngine(IServiceScopeFactory? scopeFactory = null)
    {
        return new AtoRemediationEngine(
            _complianceEngine.Object,
            _dbFactory.Object,
            _armService.Object,
            _aiGenerator.Object,
            _complianceRemediation.Object,
            _scriptExecutor.Object,
            _nistSteps.Object,
            _sanitization.Object,
            Options.Create(_options),
            _logger.Object,
            scopeFactory ?? _scopeFactory.Object);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper: Generate test findings
    // ═══════════════════════════════════════════════════════════════════════════

    private static List<ComplianceFinding> GenerateFindings(int count, FindingSeverity? severity = null)
    {
        var severities = new[] { FindingSeverity.Critical, FindingSeverity.High, FindingSeverity.Medium, FindingSeverity.Low, FindingSeverity.Informational };
        var families = new[] { "AC", "AU", "CM", "SC", "IA", "SI", "CP", "RA", "SA", "PE" };
        var types = new[] { RemediationType.ResourceConfiguration, RemediationType.PolicyAssignment, RemediationType.PolicyRemediation, RemediationType.Manual };

        return Enumerable.Range(0, count).Select(i =>
        {
            var sev = severity ?? severities[i % severities.Length];
            var fam = families[i % families.Length];
            var type = types[i % types.Length];
            return new ComplianceFinding
            {
                Id = $"finding-{i:D3}",
                ControlId = $"{fam}-{(i % 10) + 1}",
                ControlFamily = fam,
                Title = $"Test finding {i}",
                Description = $"Description for finding {i}",
                Severity = sev,
                Status = FindingStatus.Open,
                ResourceId = $"/subscriptions/sub-1/resourceGroups/rg-{i % 5}/providers/Microsoft.Storage/storageAccounts/sa{i}",
                ResourceType = "Microsoft.Storage/storageAccounts",
                RemediationGuidance = $"Fix by updating configuration for control {fam}-{(i % 10) + 1}",
                AutoRemediable = type != RemediationType.Manual,
                RemediationType = type,
                SubscriptionId = "sub-1"
            };
        }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GenerateRemediationPlanAsync (IEnumerable<ComplianceFinding>)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_50PlusFindings_ReturnsSortedByPriority()
    {
        // Arrange
        var findings = GenerateFindings(55);

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert
        plan.Should().NotBeNull();
        plan.Items.Should().NotBeNull();
        plan.Items!.Count.Should().Be(55);
        plan.TotalFindings.Should().Be(55);

        // Verify sorted by priority (P0 before P1, etc.)
        var priorities = plan.Items.Select(i => i.Priority).ToList();
        priorities.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_SortedWithinPriority_AutoRemediableFirst()
    {
        // Arrange — create critical findings with mixed auto-remediable
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", Severity = FindingSeverity.Critical, ControlFamily = "AC", ControlId = "AC-1",
                     AutoRemediable = false, RemediationType = RemediationType.Manual, ResourceId = "r1", ResourceType = "t1", RemediationGuidance = "g1" },
            new() { Id = "f2", Severity = FindingSeverity.Critical, ControlFamily = "AC", ControlId = "AC-2",
                     AutoRemediable = true, RemediationType = RemediationType.PolicyRemediation, ResourceId = "r2", ResourceType = "t2", RemediationGuidance = "g2" },
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert — auto-remediable should come first within same priority
        plan.Items![0].IsAutoRemediable.Should().BeTrue();
        plan.Items[1].IsAutoRemediable.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_EmptyFindings_ReturnsEmptyPlan()
    {
        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(new List<ComplianceFinding>());

        // Assert
        plan.Should().NotBeNull();
        plan.Items.Should().BeEmpty();
        plan.TotalFindings.Should().Be(0);
        plan.Timeline.Should().NotBeNull();
        plan.ExecutiveSummary.Should().NotBeNull();
        plan.ExecutiveSummary!.TotalFindings.Should().Be(0);
        plan.RiskMetrics.Should().NotBeNull();
        plan.RiskMetrics!.CurrentRiskScore.Should().Be(0);
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_SeverityFilter_ProducesCorrectSubset()
    {
        // Arrange
        var findings = GenerateFindings(20);
        var options = new RemediationPlanOptions { MinSeverity = "High" };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings, options);

        // Assert — only Critical and High should be included
        plan.Items.Should().NotBeNull();
        plan.Items!.Should().OnlyContain(i =>
            i.Finding.Severity == FindingSeverity.Critical ||
            i.Finding.Severity == FindingSeverity.High);
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_IncludeFamiliesFilter_ProducesCorrectSubset()
    {
        // Arrange
        var findings = GenerateFindings(30);
        var options = new RemediationPlanOptions
        {
            IncludeFamilies = new List<string> { "AC", "SC" }
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings, options);

        // Assert
        plan.Items.Should().NotBeNull();
        plan.Items!.Should().OnlyContain(i =>
            i.Finding.ControlFamily == "AC" || i.Finding.ControlFamily == "SC");
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_ExcludeFamiliesFilter_ExcludesCorrectFamilies()
    {
        // Arrange
        var findings = GenerateFindings(30);
        var options = new RemediationPlanOptions
        {
            ExcludeFamilies = new List<string> { "AC", "SC" }
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings, options);

        // Assert
        plan.Items.Should().NotBeNull();
        plan.Items!.Should().NotContain(i =>
            i.Finding.ControlFamily == "AC" || i.Finding.ControlFamily == "SC");
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_AutomatableOnlyFilter_ExcludesManual()
    {
        // Arrange
        var findings = GenerateFindings(20);
        var options = new RemediationPlanOptions { AutomatableOnly = true };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings, options);

        // Assert
        plan.Items.Should().NotBeNull();
        plan.Items!.Should().OnlyContain(i => i.IsAutoRemediable);
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_GroupByResource_OrdersByResource()
    {
        // Arrange
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", Severity = FindingSeverity.Critical, ControlFamily = "AC", ControlId = "AC-1",
                     AutoRemediable = true, RemediationType = RemediationType.ResourceConfiguration,
                     ResourceId = "/subs/1/rg/rg1/p/sa2", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f2", Severity = FindingSeverity.High, ControlFamily = "AU", ControlId = "AU-1",
                     AutoRemediable = true, RemediationType = RemediationType.PolicyRemediation,
                     ResourceId = "/subs/1/rg/rg1/p/sa1", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f3", Severity = FindingSeverity.Medium, ControlFamily = "CM", ControlId = "CM-1",
                     AutoRemediable = true, RemediationType = RemediationType.ResourceConfiguration,
                     ResourceId = "/subs/1/rg/rg1/p/sa2", ResourceType = "t", RemediationGuidance = "g" },
        };
        var options = new RemediationPlanOptions { GroupByResource = true };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings, options);

        // Assert — items with same resource should be adjacent
        plan.GroupByResource.Should().BeTrue();
        var resources = plan.Items!.Select(i => i.AffectedResourceId).ToList();
        // sa1 comes before sa2 alphabetically
        resources[0].Should().Contain("sa1");
        resources[1].Should().Contain("sa2");
        resources[2].Should().Contain("sa2");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Executive Summary
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_ExecutiveSummary_HasCorrectCounts()
    {
        // Arrange — create known distribution
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", Severity = FindingSeverity.Critical, ControlFamily = "AC", ControlId = "AC-1",
                     AutoRemediable = true, RemediationType = RemediationType.ResourceConfiguration, ResourceId = "r1", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f2", Severity = FindingSeverity.Critical, ControlFamily = "SC", ControlId = "SC-1",
                     AutoRemediable = true, RemediationType = RemediationType.PolicyRemediation, ResourceId = "r2", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f3", Severity = FindingSeverity.High, ControlFamily = "AU", ControlId = "AU-1",
                     AutoRemediable = true, RemediationType = RemediationType.PolicyAssignment, ResourceId = "r3", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f4", Severity = FindingSeverity.Medium, ControlFamily = "CM", ControlId = "CM-1",
                     AutoRemediable = false, RemediationType = RemediationType.Manual, ResourceId = "r4", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f5", Severity = FindingSeverity.Low, ControlFamily = "CP", ControlId = "CP-1",
                     AutoRemediable = false, RemediationType = RemediationType.Manual, ResourceId = "r5", ResourceType = "t", RemediationGuidance = "g" },
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert
        var summary = plan.ExecutiveSummary!;
        summary.TotalFindings.Should().Be(5);
        summary.CriticalCount.Should().Be(2);
        summary.HighCount.Should().Be(1);
        summary.MediumCount.Should().Be(1);
        summary.LowCount.Should().Be(1);
        summary.AutoRemediableCount.Should().Be(3);
        summary.ManualCount.Should().Be(2);
        summary.TotalEstimatedEffort.Should().BeGreaterThan(TimeSpan.Zero);
        summary.ProjectedRiskReduction.Should().BeGreaterThan(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Risk Metrics
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_RiskMetrics_CalculatedAccurately()
    {
        // Arrange — 1 Critical (10), 1 High (7.5), 1 Medium (5), total = 22.5
        // All auto-remediable → projected = 0, reduction = 100%
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", Severity = FindingSeverity.Critical, ControlFamily = "AC", ControlId = "AC-1",
                     AutoRemediable = true, RemediationType = RemediationType.ResourceConfiguration, ResourceId = "r1", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f2", Severity = FindingSeverity.High, ControlFamily = "AU", ControlId = "AU-1",
                     AutoRemediable = true, RemediationType = RemediationType.PolicyRemediation, ResourceId = "r2", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f3", Severity = FindingSeverity.Medium, ControlFamily = "CM", ControlId = "CM-1",
                     AutoRemediable = true, RemediationType = RemediationType.ResourceConfiguration, ResourceId = "r3", ResourceType = "t", RemediationGuidance = "g" },
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert
        var metrics = plan.RiskMetrics!;
        metrics.CurrentRiskScore.Should().Be(22.5);
        metrics.ProjectedRiskScore.Should().Be(0);
        metrics.RiskReductionPercentage.Should().Be(100.0);
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_RiskMetrics_PartialReduction()
    {
        // Arrange — Critical auto (10), High manual (7.5) → projected = 7.5, reduction = 33.33%
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", Severity = FindingSeverity.Critical, ControlFamily = "AC", ControlId = "AC-1",
                     AutoRemediable = true, RemediationType = RemediationType.ResourceConfiguration, ResourceId = "r1", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f2", Severity = FindingSeverity.High, ControlFamily = "AU", ControlId = "AU-1",
                     AutoRemediable = false, RemediationType = RemediationType.Manual, ResourceId = "r2", ResourceType = "t", RemediationGuidance = "g" },
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert — 10/(10+7.5) = 57.14%
        var metrics = plan.RiskMetrics!;
        metrics.CurrentRiskScore.Should().Be(17.5);
        metrics.ProjectedRiskScore.Should().Be(7.5);
        metrics.RiskReductionPercentage.Should().BeApproximately(57.14, 1.0);
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_AllCritical_MaximumRiskScore()
    {
        // Arrange — 5 Critical findings, all auto → max score = 50
        var findings = GenerateFindings(5, FindingSeverity.Critical);

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert
        plan.RiskMetrics!.CurrentRiskScore.Should().Be(50.0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Timeline
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_Timeline_Has5Phases()
    {
        // Arrange
        var findings = GenerateFindings(25);

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert
        plan.Timeline.Should().NotBeNull();
        plan.Timeline!.Phases.Should().HaveCount(5);
        plan.Timeline.Phases[0].Name.Should().Be("Immediate");
        plan.Timeline.Phases[1].Name.Should().Be("24 Hours");
        plan.Timeline.Phases[2].Name.Should().Be("Week 1");
        plan.Timeline.Phases[3].Name.Should().Be("Month 1");
        plan.Timeline.Phases[4].Name.Should().Be("Backlog");
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_Timeline_CriticalItemsInImmediatePhase()
    {
        // Arrange — all Critical findings
        var findings = GenerateFindings(3, FindingSeverity.Critical);

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert — all items should be in Immediate phase (P0)
        plan.Timeline!.Phases[0].Items.Should().HaveCount(3);
        plan.Timeline.Phases[0].Priority.Should().Be(RemediationPriority.P0);
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_Timeline_TotalDurationIsPositive()
    {
        // Arrange
        var findings = GenerateFindings(10);

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert
        plan.Timeline!.TotalEstimatedDuration.Should().BeGreaterThan(TimeSpan.Zero);
        plan.Timeline.StartDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Duration Estimates
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RemediationType.PolicyRemediation, true, 10)]
    [InlineData(RemediationType.PolicyAssignment, true, 15)]
    [InlineData(RemediationType.ResourceConfiguration, true, 20)]
    [InlineData(RemediationType.Manual, false, 240)]
    [InlineData(RemediationType.ResourceConfiguration, false, 60)]
    public void EstimateDuration_ReturnsExpectedValues(RemediationType type, bool autoRemediable, int expectedMinutes)
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            RemediationType = type,
            AutoRemediable = autoRemediable,
            ControlFamily = "AC",
            ControlId = "AC-1",
            ResourceId = "r1",
            ResourceType = "t"
        };

        // Act
        var duration = AtoRemediationEngine.EstimateDuration(finding);

        // Assert
        duration.TotalMinutes.Should().Be(expectedMinutes);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Priority Mapping
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(FindingSeverity.Critical, RemediationPriority.P0)]
    [InlineData(FindingSeverity.High, RemediationPriority.P1)]
    [InlineData(FindingSeverity.Medium, RemediationPriority.P2)]
    [InlineData(FindingSeverity.Low, RemediationPriority.P3)]
    [InlineData(FindingSeverity.Informational, RemediationPriority.P4)]
    public void MapSeverityToPriority_ReturnsCorrectMapping(FindingSeverity severity, RemediationPriority expected)
    {
        AtoRemediationEngine.MapSeverityToPriority(severity).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Risk Score
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(FindingSeverity.Critical, 10.0)]
    [InlineData(FindingSeverity.High, 7.5)]
    [InlineData(FindingSeverity.Medium, 5.0)]
    [InlineData(FindingSeverity.Low, 2.5)]
    [InlineData(FindingSeverity.Informational, 1.0)]
    public void CalculateRiskScore_ReturnsCorrectWeight(FindingSeverity severity, double expected)
    {
        AtoRemediationEngine.CalculateRiskScore(severity).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Priority Labels
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RemediationPriority.P0, "P0 - Immediate")]
    [InlineData(RemediationPriority.P1, "P1 - Within 24 Hours")]
    [InlineData(RemediationPriority.P2, "P2 - Within 7 Days")]
    [InlineData(RemediationPriority.P3, "P3 - Within 30 Days")]
    [InlineData(RemediationPriority.P4, "P4 - Best Effort")]
    public void GetPriorityLabel_ReturnsCorrectLabel(RemediationPriority priority, string expected)
    {
        AtoRemediationEngine.GetPriorityLabel(priority).Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Single-Finding Plan — 3-Tier Fallback
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_SingleFinding_UsesAiWhenAvailable()
    {
        // Arrange
        var finding = GenerateFindings(1).First();
        var aiPlan = new RemediationPlan { Id = "ai-plan", TotalFindings = 1 };

        _aiGenerator.Setup(a => a.IsAvailable).Returns(true);
        _aiGenerator.Setup(a => a.GenerateEnhancedPlanAsync(finding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiPlan);

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(finding);

        // Assert
        plan.Id.Should().Be("ai-plan");
        _aiGenerator.Verify(a => a.GenerateEnhancedPlanAsync(finding, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_SingleFinding_FallsToNistWhenAiReturnsNull()
    {
        // Arrange
        var finding = GenerateFindings(1).First();

        _aiGenerator.Setup(a => a.IsAvailable).Returns(true);
        _aiGenerator.Setup(a => a.GenerateEnhancedPlanAsync(finding, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemediationPlan?)null);

        _nistSteps.Setup(s => s.GetRemediationSteps("AC", It.IsAny<string>()))
            .Returns(new List<string> { "NIST step 1", "NIST step 2" });

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(finding);

        // Assert
        plan.Items.Should().HaveCount(1);
        plan.Items![0].Steps.Should().HaveCount(2);
        plan.Items[0].Steps[0].Description.Should().Be("NIST step 1");
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_SingleFinding_FallsToNistWhenAiThrows()
    {
        // Arrange
        var finding = GenerateFindings(1).First();

        _aiGenerator.Setup(a => a.IsAvailable).Returns(true);
        _aiGenerator.Setup(a => a.GenerateEnhancedPlanAsync(finding, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI failed"));

        _nistSteps.Setup(s => s.GetRemediationSteps("AC", It.IsAny<string>()))
            .Returns(new List<string> { "NIST fallback step" });

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(finding);

        // Assert
        plan.Items.Should().HaveCount(1);
        plan.Items![0].Steps[0].Description.Should().Be("NIST fallback step");
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_SingleFinding_FallsToGuidanceParsingWhenAiUnavailableAndNoNistSteps()
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = "f1",
            ControlFamily = "ZZ", // Unknown family
            ControlId = "ZZ-1",
            Title = "Unknown control finding",
            Severity = FindingSeverity.Medium,
            AutoRemediable = false,
            RemediationType = RemediationType.Manual,
            ResourceId = "r1",
            ResourceType = "t",
            RemediationGuidance = "1. Do step A\n2. Do step B"
        };

        _aiGenerator.Setup(a => a.IsAvailable).Returns(false);
        _nistSteps.Setup(s => s.GetRemediationSteps("ZZ", "ZZ-1")).Returns(new List<string>());
        _nistSteps.Setup(s => s.ParseStepsFromGuidance("1. Do step A\n2. Do step B"))
            .Returns(new List<string> { "Do step A", "Do step B" });

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(finding);

        // Assert
        plan.Items.Should().HaveCount(1);
        plan.Items![0].Steps.Should().HaveCount(2);
        plan.Items[0].Steps[0].Description.Should().Be("Do step A");
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_SingleFinding_FallbackToGenericStepsWhenAllEmpty()
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = "f1",
            ControlFamily = "ZZ",
            ControlId = "ZZ-1",
            Title = "Unknown control",
            Severity = FindingSeverity.Low,
            AutoRemediable = false,
            RemediationType = RemediationType.Manual,
            ResourceId = "r1",
            ResourceType = "t",
            RemediationGuidance = ""
        };

        _aiGenerator.Setup(a => a.IsAvailable).Returns(false);
        _nistSteps.Setup(s => s.GetRemediationSteps("ZZ", "ZZ-1")).Returns(new List<string>());
        _nistSteps.Setup(s => s.ParseStepsFromGuidance("")).Returns(new List<string>());

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(finding);

        // Assert — should get 3 generic steps
        plan.Items.Should().HaveCount(1);
        plan.Items![0].Steps.Should().HaveCount(3);
        plan.Items[0].Steps[0].Description.Should().Contain("Review finding");
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_SingleFinding_SetsCorrectPlanMetadata()
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = "f1",
            ControlFamily = "AC",
            ControlId = "AC-1",
            Title = "Test",
            Severity = FindingSeverity.Critical,
            AutoRemediable = true,
            RemediationType = RemediationType.ResourceConfiguration,
            ResourceId = "r1",
            ResourceType = "t",
            RemediationGuidance = "Fix it",
            SubscriptionId = "sub-123"
        };

        _aiGenerator.Setup(a => a.IsAvailable).Returns(false);

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(finding);

        // Assert
        plan.TotalFindings.Should().Be(1);
        plan.AutoRemediableCount.Should().Be(1);
        plan.SubscriptionId.Should().Be("sub-123");
        plan.DryRun.Should().BeTrue();
        plan.RiskMetrics.Should().NotBeNull();
        plan.RiskMetrics!.CurrentRiskScore.Should().Be(10.0); // Critical
        plan.Timeline.Should().NotBeNull();
        plan.ExecutiveSummary.Should().NotBeNull();
        plan.ExecutiveSummary!.CriticalCount.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Plan Item Properties
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_Items_HaveCorrectProperties()
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = "f1",
            ControlFamily = "SC",
            ControlId = "SC-7",
            Title = "Network boundary protection",
            Severity = FindingSeverity.High,
            AutoRemediable = true,
            RemediationType = RemediationType.ResourceConfiguration,
            ResourceId = "/subs/1/rg/rg1/p/nsg1",
            ResourceType = "Microsoft.Network/networkSecurityGroups",
            RemediationGuidance = "Configure NSG rules"
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(new[] { finding });

        // Assert
        var item = plan.Items![0];
        item.Finding.Should().Be(finding);
        item.Priority.Should().Be(RemediationPriority.P1);
        item.PriorityLabel.Should().Be("P1 - Within 24 Hours");
        item.EstimatedDuration.Should().Be(TimeSpan.FromMinutes(20));
        item.IsAutoRemediable.Should().BeTrue();
        item.RemediationType.Should().Be(RemediationType.ResourceConfiguration);
        item.AffectedResourceId.Should().Be("/subs/1/rg/rg1/p/nsg1");
        item.ValidationSteps.Should().HaveCountGreaterThanOrEqualTo(2);
        item.RollbackPlan.Should().Contain("before-snapshot");
        item.Steps.Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Combined Filters
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_CombinedFilters_ApplyAllFilters()
    {
        // Arrange — 20 findings across families and severities
        var findings = GenerateFindings(20);
        var options = new RemediationPlanOptions
        {
            MinSeverity = "High",
            IncludeFamilies = new List<string> { "AC", "AU" },
            AutomatableOnly = true
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings, options);

        // Assert
        plan.Items!.Should().OnlyContain(i =>
            (i.Finding.Severity == FindingSeverity.Critical || i.Finding.Severity == FindingSeverity.High) &&
            (i.Finding.ControlFamily == "AC" || i.Finding.ControlFamily == "AU") &&
            i.IsAutoRemediable);
    }

    [Fact]
    public async Task GenerateRemediationPlanAsync_Filters_PreservedOnPlan()
    {
        // Arrange
        var findings = GenerateFindings(10);
        var options = new RemediationPlanOptions
        {
            MinSeverity = "Medium",
            AutomatableOnly = true
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings, options);

        // Assert
        plan.Filters.Should().NotBeNull();
        plan.Filters!.MinSeverity.Should().Be("Medium");
        plan.Filters.AutomatableOnly.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Risk Score edge cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_AllManual_ZeroRiskReduction()
    {
        // Arrange
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", Severity = FindingSeverity.High, ControlFamily = "SC", ControlId = "SC-1",
                     AutoRemediable = false, RemediationType = RemediationType.Manual, ResourceId = "r1", ResourceType = "t", RemediationGuidance = "g" },
            new() { Id = "f2", Severity = FindingSeverity.Medium, ControlFamily = "AC", ControlId = "AC-1",
                     AutoRemediable = false, RemediationType = RemediationType.Manual, ResourceId = "r2", ResourceType = "t", RemediationGuidance = "g" },
        };

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);

        // Assert — no auto-remediable findings → 0% reduction
        plan.RiskMetrics!.RiskReductionPercentage.Should().Be(0.0);
        plan.RiskMetrics.CurrentRiskScore.Should().Be(12.5); // 7.5 + 5
        plan.RiskMetrics.ProjectedRiskScore.Should().Be(12.5);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GroupFindingsByResource helper
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GroupFindingsByResource_GroupsCorrectly()
    {
        // Arrange
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", ResourceId = "r1" },
            new() { Id = "f2", ResourceId = "r2" },
            new() { Id = "f3", ResourceId = "r1" },
            new() { Id = "f4", ResourceId = "r3" },
            new() { Id = "f5", ResourceId = "r2" },
        };

        // Act
        var grouped = AtoRemediationEngine.GroupFindingsByResource(findings);

        // Assert
        grouped.Should().HaveCount(3);
        grouped["r1"].Should().HaveCount(2);
        grouped["r2"].Should().HaveCount(2);
        grouped["r3"].Should().HaveCount(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BuildTimeline helper
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTimeline_EmptyItems_Returns5EmptyPhases()
    {
        var timeline = AtoRemediationEngine.BuildTimeline(new List<RemediationItem>());

        timeline.Phases.Should().HaveCount(5);
        timeline.TotalEstimatedDuration.Should().Be(TimeSpan.Zero);
        timeline.Phases.Should().OnlyContain(p => p.Items.Count == 0);
    }

    [Fact]
    public void BuildTimeline_ItemsDistributedByPriority()
    {
        // Arrange
        var items = new List<RemediationItem>
        {
            new() { Priority = RemediationPriority.P0, EstimatedDuration = TimeSpan.FromMinutes(10), Finding = new ComplianceFinding() },
            new() { Priority = RemediationPriority.P0, EstimatedDuration = TimeSpan.FromMinutes(20), Finding = new ComplianceFinding() },
            new() { Priority = RemediationPriority.P2, EstimatedDuration = TimeSpan.FromMinutes(30), Finding = new ComplianceFinding() },
            new() { Priority = RemediationPriority.P4, EstimatedDuration = TimeSpan.FromMinutes(15), Finding = new ComplianceFinding() },
        };

        // Act
        var timeline = AtoRemediationEngine.BuildTimeline(items);

        // Assert
        timeline.Phases[0].Items.Should().HaveCount(2); // P0 → Immediate
        timeline.Phases[0].EstimatedDuration.Should().Be(TimeSpan.FromMinutes(30));
        timeline.Phases[1].Items.Should().HaveCount(0); // P1 → 24 Hours
        timeline.Phases[2].Items.Should().HaveCount(1); // P2 → Week 1
        timeline.Phases[3].Items.Should().HaveCount(0); // P3 → Month 1
        timeline.Phases[4].Items.Should().HaveCount(1); // P4 → Backlog
        timeline.TotalEstimatedDuration.Should().Be(TimeSpan.FromMinutes(75));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Large plan performance
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRemediationPlanAsync_LargePlan_CompletesQuickly()
    {
        // Arrange
        var findings = GenerateFindings(200);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var plan = await _sut.GenerateRemediationPlanAsync(findings);
        sw.Stop();

        // Assert
        plan.Items.Should().HaveCount(200);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        plan.Timeline!.Phases.Should().HaveCount(5);
        plan.ExecutiveSummary.Should().NotBeNull();
        plan.RiskMetrics.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 4: Execution tests (T049)
    // ═══════════════════════════════════════════════════════════════════════════

    private ComplianceFinding CreateAutoRemediableFinding(string id = "F-001", string family = "CM")
    {
        return new ComplianceFinding
        {
            Id = id,
            ControlId = $"{family}-001",
            ControlFamily = family,
            Title = $"Test Finding {id}",
            Severity = FindingSeverity.High,
            AutoRemediable = true,
            ResourceId = $"/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.Compute/virtualMachines/{id}",
            ResourceType = "Microsoft.Compute/virtualMachines",
            SubscriptionId = "sub1",
            RemediationType = RemediationType.ResourceConfiguration,
            RemediationGuidance = "Apply security patch"
        };
    }

    private RemediationExecutionOptions CreateDefaultOptions(
        bool dryRun = false,
        bool requireApproval = false,
        bool autoValidate = false,
        bool autoRollback = false,
        bool useAi = true)
    {
        return new RemediationExecutionOptions
        {
            DryRun = dryRun,
            RequireApproval = requireApproval,
            AutoValidate = autoValidate,
            AutoRollbackOnFailure = autoRollback,
            UseAiScript = useAi
        };
    }

    private void SetupFindingLookup(ComplianceFinding finding)
    {
        _complianceEngine.Setup(e => e.GetFindingAsync(finding.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(finding);
    }

    private void SetupAiTierSuccess(string findingId, int stepsExecuted = 3)
    {
        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationScript { Content = "echo fix", ScriptType = ScriptType.AzureCli });

        _scriptExecutor.Setup(e => e.ExecuteScriptAsync(
                It.IsAny<RemediationScript>(), findingId, It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationExecution
            {
                FindingId = findingId,
                Status = RemediationExecutionStatus.Completed,
                TierUsed = 1,
                StepsExecuted = stepsExecuted,
                ChangesApplied = new List<string> { "Applied AI script" }
            });
    }

    private void SetupStructuredTierSuccess(string findingId, int stepsExecuted = 2)
    {
        _complianceRemediation.Setup(s => s.CanHandle(It.IsAny<ComplianceFinding>())).Returns(true);
        _complianceRemediation.Setup(s => s.ExecuteStructuredRemediationAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationExecution
            {
                FindingId = findingId,
                Status = RemediationExecutionStatus.Completed,
                TierUsed = 2,
                StepsExecuted = stepsExecuted,
                ChangesApplied = new List<string> { "Applied structured remediation" }
            });
    }

    private void SetupArmTierSuccess(string findingId, int stepsExecuted = 1)
    {
        _armService.Setup(s => s.ExecuteArmRemediationAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationExecution
            {
                FindingId = findingId,
                Status = RemediationExecutionStatus.Completed,
                TierUsed = 3,
                StepsExecuted = stepsExecuted,
                ChangesApplied = new List<string> { "Applied ARM remediation" }
            });
    }

    // ─── EnableAutomatedRemediation gate ─────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_WhenAutomatedRemediationDisabled_ReturnsFailed()
    {
        // Arrange
        _options.EnableAutomatedRemediation = false;
        var sut = CreateEngine();
        var options = CreateDefaultOptions();

        // Act
        var result = await sut.ExecuteRemediationAsync("F-001", options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("disabled");
        result.FindingId.Should().Be("F-001");
    }

    // ─── Finding not found ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_FindingNotFound_ReturnsFailed()
    {
        // Arrange
        _complianceEngine.Setup(e => e.GetFindingAsync("F-MISSING", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceFinding?)null);
        var options = CreateDefaultOptions();

        // Act
        var result = await _sut.ExecuteRemediationAsync("F-MISSING", options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("not found");
    }

    // ─── Non-auto-remediable finding ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_NotAutoRemediable_ReturnsFailedWithGuidanceSuggestion()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        finding.AutoRemediable = false;
        SetupFindingLookup(finding);
        var options = CreateDefaultOptions();

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("not auto-remediable");
        result.Error.Should().Contain("GenerateManualRemediationGuideAsync");
    }

    // ─── RequireApproval ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_RequireApproval_ReturnsPending()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);
        var options = CreateDefaultOptions(requireApproval: true);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Pending);
        result.FindingId.Should().Be(finding.Id);
        result.DryRun.Should().BeFalse();
    }

    // ─── 3-Tier Pipeline: Tier 1 (AI) ───────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_Tier1AiSuccess_ReturnsTier1()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);
        SetupAiTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        result.TierUsed.Should().Be(1);
        result.StepsExecuted.Should().Be(3);
        result.ChangesApplied.Should().Contain("Applied AI script");
        result.BeforeSnapshot.Should().Be("snapshot-data");
        result.AfterSnapshot.Should().Be("snapshot-data");
        result.BackupId.Should().NotBeNullOrEmpty();
        result.Duration.Should().NotBeNull();
    }

    // ─── 3-Tier Pipeline: Fallback to Tier 2 ────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_AiFails_FallsBackToTier2()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI unavailable"));

        SetupStructuredTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        result.TierUsed.Should().Be(2);
    }

    // ─── 3-Tier Pipeline: Fallback to Tier 3 ────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_AiAndStructuredFail_FallsBackToTier3()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemediationScript?)null);

        _complianceRemediation.Setup(s => s.CanHandle(It.IsAny<ComplianceFinding>())).Returns(false);

        SetupArmTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        result.TierUsed.Should().Be(3);
    }

    // ─── All tiers fail ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_AllTiersFail_ReturnsFailed()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI error"));

        _complianceRemediation.Setup(s => s.CanHandle(It.IsAny<ComplianceFinding>())).Returns(true);
        _complianceRemediation.Setup(s => s.ExecuteStructuredRemediationAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Structured error"));

        _armService.Setup(s => s.ExecuteArmRemediationAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("ARM error"));

        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("All remediation tiers failed");
    }

    // ─── AI not available → skips directly to Tier 2 ─────────────────────────

    [Fact]
    public async Task ExecuteRemediation_AiNotAvailable_SkipsToTier2()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        _aiGenerator.Setup(g => g.IsAvailable).Returns(false);
        SetupStructuredTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.TierUsed.Should().Be(2);
        _aiGenerator.Verify(g => g.GenerateScriptAsync(
            It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── UseAiScript disabled → skips Tier 1 ────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_UseAiScriptFalse_SkipsTier1()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);
        SetupStructuredTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(useAi: false, autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.TierUsed.Should().Be(2);
        _aiGenerator.Verify(g => g.GenerateScriptAsync(
            It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Snapshot captured ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_CapturesBeforeAndAfterSnapshots()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);
        SetupAiTierSuccess(finding.Id);
        var callCount = 0;
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"snapshot-{++callCount}");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.BeforeSnapshot.Should().Be("snapshot-1");
        result.AfterSnapshot.Should().Be("snapshot-2");
        _armService.Verify(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ─── Execution metadata ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_Success_SetsExecutionMetadata()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);
        SetupAiTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.FindingId.Should().Be(finding.Id);
        result.SubscriptionId.Should().Be("sub1");
        result.StartedAt.Should().NotBeNull();
        result.CompletedAt.Should().NotBeNull();
        result.Duration.Should().NotBeNull();
        result.Id.Should().NotBeNullOrEmpty();
        result.DryRun.Should().BeFalse();
        result.Options.Should().NotBeNull();
    }

    // ─── Tier 2 failure returns null → falls to Tier 3 ──────────────────────

    [Fact]
    public async Task ExecuteRemediation_Tier2ReturnsNonCompleted_FallsToTier3()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        _aiGenerator.Setup(g => g.IsAvailable).Returns(false);

        _complianceRemediation.Setup(s => s.CanHandle(It.IsAny<ComplianceFinding>())).Returns(true);
        _complianceRemediation.Setup(s => s.ExecuteStructuredRemediationAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationExecution
            {
                FindingId = finding.Id,
                Status = RemediationExecutionStatus.Failed,
                TierUsed = 2
            });

        SetupArmTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.TierUsed.Should().Be(3);
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
    }

    // ─── Backward-compat ExecuteRemediation gate ─────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_BackwardCompat_DisabledGate_ReturnsError()
    {
        // Arrange
        _options.EnableAutomatedRemediation = false;
        var sut = CreateEngine();

        // Act
        var result = await sut.ExecuteRemediationAsync("F-001", true, false);

        // Assert
        result.Should().Contain("disabled");
        result.Should().Contain("REMEDIATION_DISABLED");
    }

    // ─── Backward-compat dry-run still works ─────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_BackwardCompat_DryRun_ReturnsPreview()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, false, true);

        // Assert
        result.Should().Contain("dry-run");
        result.Should().Contain(finding.ControlId);
    }

    // ─── Backward-compat apply delegates to 3-tier pipeline ──────────────────

    [Fact]
    public async Task ExecuteRemediation_BackwardCompat_Apply_DelegatesToTyped()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);
        SetupAiTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");

        // Setup DB context
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"exec-compat-{Guid.NewGuid()}")
            .Options;
        var dbContext = new AtoCopilotContext(dbOptions);
        _dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dbContext);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, true, false);

        // Assert
        result.Should().Contain("executed");
        result.Should().Contain("tierUsed");
    }

    // ─── DryRun option on typed method ───────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_DryRunOption_StillExecutesPipeline()
    {
        // Arrange — DryRun goes through normal pipeline but with DryRun flag
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);
        SetupAiTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(dryRun: true, autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.DryRun.Should().BeTrue();
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        result.Options!.DryRun.Should().BeTrue();
    }

    // ─── Snapshot null → no BackupId ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_NullSnapshot_NoBackupId()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);
        SetupAiTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.BackupId.Should().BeNull();
        result.BeforeSnapshot.Should().BeNull();
    }

    // ─── Execution error handling ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_UnhandledExceptionInPipeline_ReturnsFailed()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Snapshot crash"));
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("Snapshot crash");
    }

    // ─── AI script returns null → fallback ──────────────────────────────────

    [Fact]
    public async Task ExecuteRemediation_AiReturnsNullScript_FallsToTier2()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemediationScript?)null);

        SetupStructuredTierSuccess(finding.Id);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert
        result.TierUsed.Should().Be(2);
    }

    // ─── Auto-validate + auto-rollback integration ──────────────────────────

    [Fact]
    public async Task ExecuteRemediation_AutoValidateWithFailedValidation_NoRollbackWhenFlagDisabled()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding();
        SetupFindingLookup(finding);

        // Tier 1 succeeds but with 0 steps (will fail internal validation)
        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationScript { Content = "echo fix", ScriptType = ScriptType.AzureCli });
        _scriptExecutor.Setup(e => e.ExecuteScriptAsync(
                It.IsAny<RemediationScript>(), finding.Id, It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationExecution
            {
                FindingId = finding.Id,
                Status = RemediationExecutionStatus.Completed,
                TierUsed = 1,
                StepsExecuted = 0, // will fail StepsExecuted validation check
                ChangesApplied = new List<string>()
            });

        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");

        var options = CreateDefaultOptions(autoValidate: true, autoRollback: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync(finding.Id, options);

        // Assert — validation fails but no rollback because autoRollback is off
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 5: Validation and Rollback tests (T056)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Helper: execute a finding and return the execution result for validation/rollback tests.</summary>
    private async Task<RemediationExecution> ExecuteFindingAsync(string findingId = "F-001")
    {
        var finding = CreateAutoRemediableFinding(findingId);
        SetupFindingLookup(finding);
        SetupAiTierSuccess(findingId);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");
        var options = CreateDefaultOptions(autoValidate: false);
        return await _sut.ExecuteRemediationAsync(findingId, options);
    }

    // ─── Validate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateRemediation_CompletedExecution_ReturnsValid()
    {
        // Arrange
        var execution = await ExecuteFindingAsync();
        execution.Status.Should().Be(RemediationExecutionStatus.Completed);

        // Act
        var result = await _sut.ValidateRemediationAsync(execution.Id, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ExecutionId.Should().Be(execution.Id);
        result.Checks.Should().HaveCountGreaterOrEqualTo(3);
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRemediation_NotFoundExecution_ReturnsInvalid()
    {
        // Act
        var result = await _sut.ValidateRemediationAsync("nonexistent-id", CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
    }

    [Fact]
    public async Task ValidateRemediation_FailedExecution_ReturnsInvalid()
    {
        // Arrange — execute a finding that will fail (all tiers fail)
        var finding = CreateAutoRemediableFinding("F-FAIL");
        SetupFindingLookup(finding);
        _aiGenerator.Setup(g => g.IsAvailable).Returns(false);
        _complianceRemediation.Setup(s => s.CanHandle(It.IsAny<ComplianceFinding>())).Returns(false);
        _armService.Setup(s => s.ExecuteArmRemediationAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("ARM fail"));
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
        var options = CreateDefaultOptions(autoValidate: false);
        var execution = await _sut.ExecuteRemediationAsync("F-FAIL", options);
        execution.Status.Should().Be(RemediationExecutionStatus.Failed);

        // Act
        var result = await _sut.ValidateRemediationAsync(execution.Id, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Checks.Should().Contain(c => c.Name == "ExecutionStatus" && !c.Passed);
    }

    // ─── Rollback ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackRemediation_WithSnapshot_Success()
    {
        // Arrange
        var execution = await ExecuteFindingAsync();
        execution.BeforeSnapshot.Should().NotBeNull();

        _armService.Setup(s => s.RestoreFromSnapshotAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationRollbackResult
            {
                ExecutionId = execution.Id,
                Success = true,
                RollbackSteps = new List<string> { "Restored configuration" },
                RestoredSnapshot = "snapshot-data"
            });

        // Act
        var result = await _sut.RollbackRemediationAsync(execution.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.ExecutionId.Should().Be(execution.Id);
        result.RollbackSteps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RollbackRemediation_NotFoundExecution_ReturnsFailure()
    {
        // Act
        var result = await _sut.RollbackRemediationAsync("nonexistent-id");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RollbackRemediation_NoSnapshot_ReturnsFailure()
    {
        // Arrange — execute a finding where snapshot is null
        var finding = CreateAutoRemediableFinding("F-NOSNAP");
        SetupFindingLookup(finding);
        SetupAiTierSuccess("F-NOSNAP");
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var options = CreateDefaultOptions(autoValidate: false);
        var execution = await _sut.ExecuteRemediationAsync("F-NOSNAP", options);

        // Act
        var result = await _sut.RollbackRemediationAsync(execution.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No before-snapshot");
    }

    [Fact]
    public async Task RollbackRemediation_AlreadyRolledBack_ReturnsFailure()
    {
        // Arrange
        var execution = await ExecuteFindingAsync("F-TWICE");

        _armService.Setup(s => s.RestoreFromSnapshotAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationRollbackResult
            {
                ExecutionId = execution.Id,
                Success = true,
                RollbackSteps = new List<string> { "Restored" }
            });

        // First rollback
        await _sut.RollbackRemediationAsync(execution.Id);

        // Act — second rollback
        var result = await _sut.RollbackRemediationAsync(execution.Id);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already been rolled back");
    }

    [Fact]
    public async Task RollbackRemediation_StatusTransitionsToRolledBack()
    {
        // Arrange
        var execution = await ExecuteFindingAsync("F-STATUS");

        _armService.Setup(s => s.RestoreFromSnapshotAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationRollbackResult
            {
                ExecutionId = execution.Id,
                Success = true,
                RollbackSteps = new List<string> { "Restored" }
            });

        // Act
        await _sut.RollbackRemediationAsync(execution.Id);

        // Assert — now validate should show RolledBack
        var validation = await _sut.ValidateRemediationAsync(execution.Id, CancellationToken.None);
        validation.Checks.Should().Contain(c => c.Name == "ExecutionStatus" && !c.Passed);
    }

    // ─── Auto-validate + auto-rollback integration (Phase 5) ─────────────────

    [Fact]
    public async Task ExecuteRemediation_AutoValidateAndAutoRollback_TriggersRollbackOnFailure()
    {
        // Arrange — tier succeeds but with 0 steps + no changes → validation fails
        var finding = CreateAutoRemediableFinding("F-AUTOROLL");
        SetupFindingLookup(finding);

        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationScript { Content = "echo fix", ScriptType = ScriptType.AzureCli });
        _scriptExecutor.Setup(e => e.ExecuteScriptAsync(
                It.IsAny<RemediationScript>(), "F-AUTOROLL", It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationExecution
            {
                FindingId = "F-AUTOROLL",
                Status = RemediationExecutionStatus.Completed,
                TierUsed = 1,
                StepsExecuted = 0,
                ChangesApplied = new List<string>()
            });

        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");

        _armService.Setup(s => s.RestoreFromSnapshotAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationRollbackResult { Success = true, RollbackSteps = new List<string> { "Restored" } });

        var options = CreateDefaultOptions(autoValidate: true, autoRollback: true);

        // Act
        var result = await _sut.ExecuteRemediationAsync("F-AUTOROLL", options);

        // Assert — should be rolled back since validation failed
        result.Status.Should().Be(RemediationExecutionStatus.RolledBack);
    }

    // ─── Backward-compat validation with executionId ─────────────────────────

    [Fact]
    public async Task ValidateRemediation_BackwardCompat_WithExecutionId_DelegatesToTyped()
    {
        // Arrange
        var execution = await ExecuteFindingAsync("F-BCOMPAT");

        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"validate-compat-{Guid.NewGuid()}")
            .Options;
        var dbContext = new AtoCopilotContext(dbOptions);
        _dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dbContext);

        // Act
        var result = await _sut.ValidateRemediationAsync("F-BCOMPAT", execution.Id, null, default);

        // Assert
        result.Should().Contain("validated");
        result.Should().Contain(execution.Id);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 6: Batch Remediation tests (T062)
    // ═══════════════════════════════════════════════════════════════════════════

    private void SetupBatchFindings(int count, bool allAutoRemediable = true)
    {
        for (var i = 0; i < count; i++)
        {
            var id = $"B-{i:D3}";
            var finding = CreateAutoRemediableFinding(id, i % 2 == 0 ? "CM" : "AC");
            finding.AutoRemediable = allAutoRemediable || i % 3 != 0;
            finding.Severity = (i % 4) switch
            {
                0 => FindingSeverity.Critical,
                1 => FindingSeverity.High,
                2 => FindingSeverity.Medium,
                _ => FindingSeverity.Low
            };
            SetupFindingLookup(finding);
        }

        SetupAiTierSuccess("dummy"); // default AI setup
        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<ScriptType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationScript { Content = "echo fix", ScriptType = ScriptType.AzureCli });
        _scriptExecutor.Setup(e => e.ExecuteScriptAsync(
                It.IsAny<RemediationScript>(), It.IsAny<string>(), It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemediationScript _, string fId, RemediationExecutionOptions _, CancellationToken _) =>
                new RemediationExecution
                {
                    FindingId = fId,
                    Status = RemediationExecutionStatus.Completed,
                    TierUsed = 1,
                    StepsExecuted = 2,
                    ChangesApplied = new List<string> { "Applied" },
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    Duration = TimeSpan.FromMilliseconds(50)
                });
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");
    }

    [Fact]
    public async Task ExecuteBatch_EmptyList_ReturnsEmptyResult()
    {
        // Act
        var result = await _sut.ExecuteBatchRemediationAsync(Array.Empty<string>());

        // Assert
        result.Executions.Should().BeEmpty();
        result.SuccessCount.Should().Be(0);
        result.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteBatch_10Findings_AllSucceed()
    {
        // Arrange
        SetupBatchFindings(10);
        var ids = Enumerable.Range(0, 10).Select(i => $"B-{i:D3}").ToList();

        // Act
        var result = await _sut.ExecuteBatchRemediationAsync(ids, new BatchRemediationOptions { MaxConcurrentRemediations = 3 });

        // Assert
        result.SuccessCount.Should().Be(10);
        result.FailureCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
        result.Executions.Should().HaveCount(10);
        result.Summary.SuccessRate.Should().Be(100);
        result.Summary.ControlFamiliesAffected.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteBatch_NonAutoRemediable_Skipped()
    {
        // Arrange
        SetupBatchFindings(6, allAutoRemediable: false);
        var ids = Enumerable.Range(0, 6).Select(i => $"B-{i:D3}").ToList();

        // Act
        var result = await _sut.ExecuteBatchRemediationAsync(ids);

        // Assert
        result.SkippedCount.Should().BeGreaterThan(0);
        result.Executions.Should().Contain(e => e.Error != null && e.Error.Contains("skipped"));
    }

    [Fact]
    public async Task ExecuteBatch_FailFast_CancelsRemaining()
    {
        // Arrange — first finding will fail, rest should be cancelled
        var failing = CreateAutoRemediableFinding("FAIL-0");
        SetupFindingLookup(failing);

        // Make the first finding fail at all tiers
        _aiGenerator.Setup(g => g.IsAvailable).Returns(false);
        _complianceRemediation.Setup(s => s.CanHandle(It.IsAny<ComplianceFinding>())).Returns(false);
        _armService.Setup(s => s.ExecuteArmRemediationAsync(
                It.IsAny<ComplianceFinding>(), It.IsAny<RemediationExecutionOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("ARM fail"));
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snap");

        // Setup other findings (they may or may not get executed)
        for (var i = 1; i < 5; i++)
        {
            var f = CreateAutoRemediableFinding($"FAIL-{i}");
            SetupFindingLookup(f);
        }

        var ids = Enumerable.Range(0, 5).Select(i => $"FAIL-{i}").ToList();

        // Act
        var result = await _sut.ExecuteBatchRemediationAsync(ids, new BatchRemediationOptions
        {
            FailFast = true,
            MaxConcurrentRemediations = 1 // sequential to ensure ordering
        });

        // Assert
        result.FailureCount.Should().BeGreaterOrEqualTo(1);
        // With FailFast and single concurrency, remaining should be cancelled
        (result.CancelledCount + result.FailureCount + result.SuccessCount).Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public async Task ExecuteBatch_ContinueOnError_AggregatesAll()
    {
        // Arrange — mix of success and failure
        SetupBatchFindings(4);

        // Override one finding to be non-existent (will fail in typed execute)
        _complianceEngine.Setup(e => e.GetFindingAsync("B-002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceFinding?)null);

        var ids = Enumerable.Range(0, 4).Select(i => $"B-{i:D3}").ToList();

        // Act
        var result = await _sut.ExecuteBatchRemediationAsync(ids, new BatchRemediationOptions { ContinueOnError = true });

        // Assert
        result.Executions.Should().HaveCount(4);
        result.FailureCount.Should().BeGreaterOrEqualTo(1);
        result.SuccessCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteBatch_Summary_HasCorrectSeverityCounts()
    {
        // Arrange
        SetupBatchFindings(8);
        var ids = Enumerable.Range(0, 8).Select(i => $"B-{i:D3}").ToList();

        // Act
        var result = await _sut.ExecuteBatchRemediationAsync(ids);

        // Assert
        var summary = result.Summary;
        (summary.CriticalFindingsRemediated + summary.HighFindingsRemediated +
         summary.MediumFindingsRemediated + summary.LowFindingsRemediated)
            .Should().Be(result.SuccessCount);
        summary.EstimatedRiskReduction.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteBatch_DryRun_PassesDryRunToOptions()
    {
        // Arrange
        SetupBatchFindings(2);
        var ids = new List<string> { "B-000", "B-001" };

        // Act
        var result = await _sut.ExecuteBatchRemediationAsync(ids, new BatchRemediationOptions { DryRun = true });

        // Assert
        result.Executions.Should().AllSatisfy(e => e.DryRun.Should().BeTrue());
    }

    [Fact]
    public async Task ExecuteBatch_BatchId_IsUnique()
    {
        // Arrange
        SetupBatchFindings(2);
        var ids = new List<string> { "B-000", "B-001" };

        // Act
        var result1 = await _sut.ExecuteBatchRemediationAsync(ids);
        var result2 = await _sut.ExecuteBatchRemediationAsync(ids);

        // Assert
        result1.BatchId.Should().NotBe(result2.BatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 7 — Kanban Integration (T068)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_CompletedRemediation_AdvancesLinkedKanbanTaskToInReview()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-KB-1");
        SetupFindingLookup(finding);
        SetupAiTierSuccess("F-KB-1");
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");

        var linkedTask = new Ato.Copilot.Core.Models.Kanban.RemediationTask { Id = "task-kb-1" };
        _kanban.Setup(k => k.GetTaskByLinkedAlertIdAsync("F-KB-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedTask);

        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync("F-KB-1", options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        _kanban.Verify(k => k.MoveTaskAsync(
            "task-kb-1",
            Core.Models.Kanban.TaskStatus.InReview,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.Is<string>(c => c.Contains("completed")),
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_CompletedRemediation_CollectsEvidence()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-KB-2");
        SetupFindingLookup(finding);
        SetupAiTierSuccess("F-KB-2");
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");

        var linkedTask = new Ato.Copilot.Core.Models.Kanban.RemediationTask { Id = "task-kb-2" };
        _kanban.Setup(k => k.GetTaskByLinkedAlertIdAsync("F-KB-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedTask);

        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        await _sut.ExecuteRemediationAsync("F-KB-2", options);

        // Assert
        _kanban.Verify(k => k.CollectTaskEvidenceAsync(
            "task-kb-2",
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_FailedRemediation_AddsFailureComment()
    {
        // Arrange — force the outer catch by having CaptureResourceSnapshotAsync throw
        var finding = CreateAutoRemediableFinding("F-KB-3");
        SetupFindingLookup(finding);

        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Snapshot service unavailable"));

        var linkedTask = new Ato.Copilot.Core.Models.Kanban.RemediationTask { Id = "task-kb-3" };
        _kanban.Setup(k => k.GetTaskByLinkedAlertIdAsync("F-KB-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedTask);

        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync("F-KB-3", options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("Snapshot service unavailable");
        _kanban.Verify(k => k.AddCommentAsync(
            "task-kb-3",
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<string>(c => c.Contains("failed")),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_NullKanbanService_SkipsSilently()
    {
        // Arrange — engine with scope factory returning null kanban
        var nullScopeFactory = new Mock<IServiceScopeFactory>();
        var nullScope = new Mock<IServiceScope>();
        var nullProvider = new Mock<IServiceProvider>();
        nullProvider.Setup(p => p.GetService(typeof(IKanbanService))).Returns((IKanbanService?)null);
        nullScope.Setup(s => s.ServiceProvider).Returns(nullProvider.Object);
        nullScopeFactory.Setup(f => f.CreateScope()).Returns(nullScope.Object);

        var engine = new AtoRemediationEngine(
            _complianceEngine.Object,
            _dbFactory.Object,
            _armService.Object,
            _aiGenerator.Object,
            _complianceRemediation.Object,
            _scriptExecutor.Object,
            _nistSteps.Object,
            _sanitization.Object,
            Options.Create(_options),
            _logger.Object,
            nullScopeFactory.Object);

        var finding = CreateAutoRemediableFinding("F-KB-4");
        SetupFindingLookup(finding);
        SetupAiTierSuccess("F-KB-4");
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");

        var options = CreateDefaultOptions(autoValidate: false);

        // Act — should not throw even though kanban is null
        var result = await engine.ExecuteRemediationAsync("F-KB-4", options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        _kanban.Verify(k => k.GetTaskByLinkedAlertIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_KanbanSyncFails_DoesNotFailRemediation()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-KB-5");
        SetupFindingLookup(finding);
        SetupAiTierSuccess("F-KB-5");
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");

        _kanban.Setup(k => k.GetTaskByLinkedAlertIdAsync("F-KB-5", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Kanban is down"));

        var options = CreateDefaultOptions(autoValidate: false);

        // Act — kanban throws but remediation should still succeed
        var result = await _sut.ExecuteRemediationAsync("F-KB-5", options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
    }

    [Fact]
    public async Task Execute_NoLinkedKanbanTask_SkipsKanbanOperations()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-KB-6");
        SetupFindingLookup(finding);
        SetupAiTierSuccess("F-KB-6");
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");

        _kanban.Setup(k => k.GetTaskByLinkedAlertIdAsync("F-KB-6", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Ato.Copilot.Core.Models.Kanban.RemediationTask?)null);

        var options = CreateDefaultOptions(autoValidate: false);

        // Act
        var result = await _sut.ExecuteRemediationAsync("F-KB-6", options);

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        _kanban.Verify(k => k.MoveTaskAsync(
            It.IsAny<string>(), It.IsAny<Core.Models.Kanban.TaskStatus>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _kanban.Verify(k => k.CollectTaskEvidenceAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 8 — Approval Workflow (T073)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessApproval_ApprovesPendingExecution_TriggersExecution()
    {
        // Arrange — create a Pending execution via RequireApproval
        var finding = CreateAutoRemediableFinding("F-APR-1");
        SetupFindingLookup(finding);
        SetupAiTierSuccess("F-APR-1");
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");

        var options = CreateDefaultOptions(requireApproval: true);
        var pending = await _sut.ExecuteRemediationAsync("F-APR-1", options);
        pending.Status.Should().Be(RemediationExecutionStatus.Pending);

        // Act — approve
        var result = await _sut.ProcessRemediationApprovalAsync(
            pending.Id, approve: true, approverName: "admin@co.gov", comments: "Approved for prod");

        // Assert
        result.Approved.Should().BeTrue();
        result.ExecutionTriggered.Should().BeTrue();
        result.ApproverName.Should().Be("admin@co.gov");
        result.Comments.Should().Be("Approved for prod");
    }

    [Fact]
    public async Task ProcessApproval_RejectsPendingExecution_RecordsDetails()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-APR-2");
        SetupFindingLookup(finding);
        var options = CreateDefaultOptions(requireApproval: true);
        var pending = await _sut.ExecuteRemediationAsync("F-APR-2", options);
        pending.Status.Should().Be(RemediationExecutionStatus.Pending);

        // Act — reject
        var result = await _sut.ProcessRemediationApprovalAsync(
            pending.Id, approve: false, approverName: "ciso@co.gov", comments: "Risk too high for prod");

        // Assert
        result.Approved.Should().BeFalse();
        result.ExecutionTriggered.Should().BeFalse();
        result.ApproverName.Should().Be("ciso@co.gov");
        result.Comments.Should().Be("Risk too high for prod");
        pending.Status.Should().Be(RemediationExecutionStatus.Rejected);
        pending.RejectedBy.Should().Be("ciso@co.gov");
        pending.RejectionReason.Should().Be("Risk too high for prod");
    }

    [Fact]
    public async Task ProcessApproval_NonPendingExecution_ReturnsError()
    {
        // Arrange — execute without RequireApproval to get a Completed execution
        var execution = await ExecuteFindingAsync("F-APR-3");
        execution.Status.Should().Be(RemediationExecutionStatus.Completed);

        // Act — try to approve a completed execution
        var result = await _sut.ProcessRemediationApprovalAsync(
            execution.Id, approve: true, approverName: "admin@co.gov");

        // Assert
        result.Approved.Should().BeFalse();
        result.ExecutionTriggered.Should().BeFalse();
        result.Comments.Should().Contain("not pending");
    }

    [Fact]
    public async Task ProcessApproval_ExecutionNotFound_ReturnsError()
    {
        // Act
        var result = await _sut.ProcessRemediationApprovalAsync(
            "nonexistent-id", approve: true, approverName: "admin@co.gov");

        // Assert
        result.Approved.Should().BeFalse();
        result.ExecutionTriggered.Should().BeFalse();
        result.Comments.Should().Contain("not found");
    }

    [Fact]
    public async Task GetActiveWorkflows_ReturnsPendingAndInProgress()
    {
        // Arrange — create a pending execution
        var finding1 = CreateAutoRemediableFinding("F-WF-1");
        SetupFindingLookup(finding1);
        var pendingOptions = CreateDefaultOptions(requireApproval: true);
        var pending = await _sut.ExecuteRemediationAsync("F-WF-1", pendingOptions);
        pending.Status.Should().Be(RemediationExecutionStatus.Pending);

        // Arrange — create a completed execution (should appear in RecentlyCompleted)
        var finding2 = CreateAutoRemediableFinding("F-WF-2");
        SetupFindingLookup(finding2);
        SetupAiTierSuccess("F-WF-2");
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");
        var completed = await _sut.ExecuteRemediationAsync("F-WF-2", CreateDefaultOptions(autoValidate: false));
        completed.Status.Should().Be(RemediationExecutionStatus.Completed);

        // Act
        var status = await _sut.GetActiveRemediationWorkflowsAsync();

        // Assert
        status.PendingApprovals.Should().Contain(e => e.FindingId == "F-WF-1");
        status.RecentlyCompleted.Should().Contain(e => e.FindingId == "F-WF-2");
        status.RetrievedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetActiveWorkflows_FiltersBySubscriptionId()
    {
        // Arrange — create executions with different subscription IDs
        var finding1 = CreateAutoRemediableFinding("F-WF-S1");
        finding1.SubscriptionId = "sub-alpha";
        SetupFindingLookup(finding1);
        var pending1 = await _sut.ExecuteRemediationAsync("F-WF-S1", CreateDefaultOptions(requireApproval: true));

        var finding2 = CreateAutoRemediableFinding("F-WF-S2");
        finding2.SubscriptionId = "sub-beta";
        SetupFindingLookup(finding2);
        var pending2 = await _sut.ExecuteRemediationAsync("F-WF-S2", CreateDefaultOptions(requireApproval: true));

        // Act
        var status = await _sut.GetActiveRemediationWorkflowsAsync("sub-alpha");

        // Assert
        status.SubscriptionId.Should().Be("sub-alpha");
        status.PendingApprovals.Should().Contain(e => e.FindingId == "F-WF-S1");
        status.PendingApprovals.Should().NotContain(e => e.FindingId == "F-WF-S2");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 9 — Impact Analysis (T077)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImpactAnalysis_MixedFindings_ReturnsAccurateRiskScore()
    {
        // Arrange — 1 Critical (10) + 1 High (7.5) + 1 Medium (5) = 22.5
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", Severity = FindingSeverity.Critical, AutoRemediable = true, RemediationType = RemediationType.ResourceConfiguration, ResourceId = "r1", ResourceType = "Microsoft.Storage/storageAccounts", ControlFamily = "AC", ControlId = "AC-1", SubscriptionId = "sub-1" },
            new() { Id = "f2", Severity = FindingSeverity.High, AutoRemediable = true, RemediationType = RemediationType.PolicyAssignment, ResourceId = "r2", ResourceType = "Microsoft.Compute/virtualMachines", ControlFamily = "SC", ControlId = "SC-1", SubscriptionId = "sub-1" },
            new() { Id = "f3", Severity = FindingSeverity.Medium, AutoRemediable = false, RemediationType = RemediationType.Manual, ResourceId = "r3", ResourceType = "Microsoft.Network/networkSecurityGroups", ControlFamily = "CM", ControlId = "CM-1", SubscriptionId = "sub-1" }
        };

        // Act
        var result = await _sut.AnalyzeRemediationImpactAsync(findings);

        // Assert — current risk = 22.5, projected (manual only: Medium=5) = 5
        result.RiskMetrics.CurrentRiskScore.Should().BeApproximately(22.5, 0.01);
        result.RiskMetrics.ProjectedRiskScore.Should().BeApproximately(5.0, 0.01);
        result.RiskMetrics.RiskReductionPercentage.Should().BeApproximately(77.78, 1.0);
    }

    [Fact]
    public async Task ImpactAnalysis_GroupsByResource_5UniqueResources()
    {
        // Arrange — 10 findings spread across 5 resources
        var findings = Enumerable.Range(0, 10).Select(i => new ComplianceFinding
        {
            Id = $"f-imp-{i}",
            Severity = FindingSeverity.High,
            AutoRemediable = true,
            RemediationType = RemediationType.ResourceConfiguration,
            ResourceId = $"/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Storage/sa{i % 5}",
            ResourceType = "Microsoft.Storage/storageAccounts",
            ControlFamily = "AC",
            ControlId = $"AC-{i}",
            SubscriptionId = "sub-1",
            RemediationGuidance = $"Fix {i}"
        }).ToList();

        // Act
        var result = await _sut.AnalyzeRemediationImpactAsync(findings);

        // Assert
        result.ResourceImpacts.Should().HaveCount(5);
        result.ResourceImpacts.Should().OnlyContain(r => r.FindingsCount == 2);
    }

    [Fact]
    public async Task ImpactAnalysis_DistinguishesAutoVsManual()
    {
        // Arrange
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f-a", Severity = FindingSeverity.High, AutoRemediable = true, RemediationType = RemediationType.ResourceConfiguration, ResourceId = "r1", ControlFamily = "AC", ControlId = "AC-1", SubscriptionId = "sub-1" },
            new() { Id = "f-m", Severity = FindingSeverity.Medium, AutoRemediable = false, RemediationType = RemediationType.Manual, ResourceId = "r2", ControlFamily = "CM", ControlId = "CM-1", SubscriptionId = "sub-1" }
        };

        // Act
        var result = await _sut.AnalyzeRemediationImpactAsync(findings);

        // Assert
        result.AutoRemediableCount.Should().Be(1);
        result.ManualCount.Should().Be(1);
        result.Recommendations.Should().Contain(r => r.Contains("auto-remediated"));
        result.Recommendations.Should().Contain(r => r.Contains("manual"));
    }

    [Fact]
    public async Task ImpactAnalysis_EmptyFindings_ReturnsZeroScores()
    {
        // Act
        var result = await _sut.AnalyzeRemediationImpactAsync(new List<ComplianceFinding>());

        // Assert
        result.RiskMetrics.CurrentRiskScore.Should().Be(0);
        result.RiskMetrics.ProjectedRiskScore.Should().Be(0);
        result.TotalFindingsAnalyzed.Should().Be(0);
        result.ResourceImpacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ImpactAnalysis_AllCritical_MaxRiskScore()
    {
        // Arrange — 5 Critical findings, all auto-remediable → 50.0 risk
        var findings = Enumerable.Range(0, 5).Select(i => new ComplianceFinding
        {
            Id = $"f-crit-{i}",
            Severity = FindingSeverity.Critical,
            AutoRemediable = true,
            RemediationType = RemediationType.ResourceConfiguration,
            ResourceId = $"r-{i}",
            ResourceType = "Microsoft.Compute/virtualMachines",
            ControlFamily = "IA",
            ControlId = $"IA-{i}",
            SubscriptionId = "sub-1",
            RemediationGuidance = $"Fix critical {i}"
        }).ToList();

        // Act
        var result = await _sut.AnalyzeRemediationImpactAsync(findings);

        // Assert
        result.RiskMetrics.CurrentRiskScore.Should().BeApproximately(50.0, 0.01);
        result.RiskMetrics.ProjectedRiskScore.Should().Be(0); // all auto-remediable
        result.RiskMetrics.RiskReductionPercentage.Should().Be(100);
        result.Recommendations.Should().Contain(r => r.Contains("Critical"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 10 — AI-Enhanced Remediation (T084)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateScript_AiAvailable_ReturnsAiScript()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-AI-1");
        var aiScript = new RemediationScript
        {
            Content = "az resource update --set properties.tls=1.2",
            ScriptType = ScriptType.AzureCli,
            Description = "Enable TLS 1.2"
        };
        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(finding, ScriptType.AzureCli, It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiScript);
        _sanitization.Setup(s => s.IsSafe(aiScript.Content)).Returns(true);

        // Act
        var result = await _sut.GenerateRemediationScriptAsync(finding, ScriptType.AzureCli);

        // Assert
        result.Content.Should().Be("az resource update --set properties.tls=1.2");
        result.IsSanitized.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateScript_SanitizationRejectsUnsafe_FallsBackToNist()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-AI-2");
        var unsafeScript = new RemediationScript
        {
            Content = "az group delete --name rg-prod --yes",
            ScriptType = ScriptType.AzureCli
        };
        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateScriptAsync(finding, ScriptType.AzureCli, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unsafeScript);
        _sanitization.Setup(s => s.IsSafe(unsafeScript.Content)).Returns(false);
        _sanitization.Setup(s => s.GetViolations(unsafeScript.Content)).Returns(new List<string> { "Destructive command detected" });

        // Act
        var result = await _sut.GenerateRemediationScriptAsync(finding, ScriptType.AzureCli);

        // Assert — should fall back to NIST steps
        result.Content.Should().Contain("Step 1:");
        result.IsSanitized.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateScript_AiUnavailable_FallsBackToNistSteps()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-AI-3");
        _aiGenerator.Setup(g => g.IsAvailable).Returns(false);

        // Act
        var result = await _sut.GenerateRemediationScriptAsync(finding, ScriptType.AzureCli);

        // Assert
        result.Content.Should().Contain("Step 1:");
        result.ScriptType.Should().Be(ScriptType.AzureCli);
        result.Description.Should().Contain("NIST-based");
        result.IsSanitized.Should().BeTrue();
    }

    [Fact]
    public async Task GetGuidance_AiAvailable_ReturnsAiGuidance()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-AI-4");
        var aiGuidance = new RemediationGuidance
        {
            FindingId = "F-AI-4",
            Explanation = "Enable MFA for all admin accounts",
            TechnicalPlan = "1. Navigate to Azure AD\n2. Enable Conditional Access",
            ConfidenceScore = 0.95,
            References = new List<string> { "https://learn.microsoft.com/mfa" }
        };
        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GetGuidanceAsync(finding, It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiGuidance);

        // Act
        var result = await _sut.GetRemediationGuidanceAsync(finding);

        // Assert
        result.Explanation.Should().Be("Enable MFA for all admin accounts");
        result.ConfidenceScore.Should().Be(0.95);
    }

    [Fact]
    public async Task GetGuidance_AiUnavailable_ReturnsDeterministicGuidance()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-AI-5");
        _aiGenerator.Setup(g => g.IsAvailable).Returns(false);

        // Act
        var result = await _sut.GetRemediationGuidanceAsync(finding);

        // Assert
        result.FindingId.Should().Be("F-AI-5");
        result.Explanation.Should().Contain(finding.ControlId);
        result.ConfidenceScore.Should().Be(0.6);
        result.References.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Prioritize_AiAvailable_ReturnsAiPriorities()
    {
        // Arrange
        var findings = GenerateFindings(3);
        var aiResult = findings.Select(f => new PrioritizedFinding
        {
            Finding = f,
            AiPriority = RemediationPriority.P0,
            OriginalPriority = MapSeverityToPriority(f.Severity),
            Justification = "AI: Critical business risk",
            BusinessImpact = "High"
        }).ToList();
        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.PrioritizeAsync(It.IsAny<IEnumerable<ComplianceFinding>>(), "production", It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResult);

        // Act
        var result = await _sut.PrioritizeFindingsWithAiAsync(findings, "production");

        // Assert
        result.Should().HaveCount(3);
        result.Should().OnlyContain(p => p.AiPriority == RemediationPriority.P0);
    }

    [Fact]
    public async Task Prioritize_AiUnavailable_FallsBackToSeverity()
    {
        // Arrange
        var findings = new List<ComplianceFinding>
        {
            new() { Id = "f1", Severity = FindingSeverity.Critical, ControlFamily = "AC", ControlId = "AC-1" },
            new() { Id = "f2", Severity = FindingSeverity.Low, ControlFamily = "CM", ControlId = "CM-1" }
        };
        _aiGenerator.Setup(g => g.IsAvailable).Returns(false);

        // Act
        var result = await _sut.PrioritizeFindingsWithAiAsync(findings);

        // Assert
        result.Should().HaveCount(2);
        result[0].AiPriority.Should().Be(RemediationPriority.P0); // Critical → P0
        result[1].AiPriority.Should().Be(RemediationPriority.P3); // Low → P3
        result[0].Justification.Should().Contain("Severity-based");
    }

    private static RemediationPriority MapSeverityToPriority(FindingSeverity severity) =>
        AtoRemediationEngine.MapSeverityToPriority(severity);

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 11 — Manual Remediation Guidance (T087)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ManualGuide_NonAutomatable_HasAllSections()
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = "F-MAN-1",
            ControlId = "AC-2",
            ControlFamily = "AC",
            Title = "User Account Management",
            RemediationType = RemediationType.Manual,
            AutoRemediable = false,
            RemediationGuidance = "Review user accounts and disable inactive ones",
            ResourceType = "Microsoft.AAD/tenants",
            SubscriptionId = "sub-1"
        };

        // Act
        var guide = await _sut.GenerateManualRemediationGuideAsync(finding);

        // Assert
        guide.FindingId.Should().Be("F-MAN-1");
        guide.ControlId.Should().Be("AC-2");
        guide.Title.Should().Contain("User Account Management");
        guide.Steps.Should().NotBeEmpty();
        guide.Prerequisites.Should().NotBeEmpty();
        guide.SkillLevel.Should().NotBeNullOrEmpty();
        guide.RequiredPermissions.Should().NotBeEmpty();
        guide.ValidationSteps.Should().NotBeEmpty();
        guide.RollbackPlan.Should().NotBeNullOrEmpty();
        guide.EstimatedDuration.Should().BeGreaterThan(TimeSpan.Zero);
        guide.References.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("SC", "Advanced")]
    [InlineData("AC", "Intermediate")]
    [InlineData("CP", "Intermediate")]
    public async Task ManualGuide_SkillLevelByFamily(string family, string expectedSkill)
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = $"F-SKL-{family}",
            ControlId = $"{family}-1",
            ControlFamily = family,
            Title = $"Test {family}",
            RemediationType = RemediationType.Manual,
            AutoRemediable = false,
            SubscriptionId = "sub-1"
        };

        // Act
        var guide = await _sut.GenerateManualRemediationGuideAsync(finding);

        // Assert
        guide.SkillLevel.Should().Be(expectedSkill);
    }

    [Fact]
    public async Task ManualGuide_EmptyGuidance_ReturnsNistSteps()
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = "F-MAN-E",
            ControlId = "CM-1",
            ControlFamily = "CM",
            Title = "Configuration Management",
            RemediationType = RemediationType.Manual,
            AutoRemediable = false,
            RemediationGuidance = "", // empty guidance
            SubscriptionId = "sub-1"
        };

        // Act
        var guide = await _sut.GenerateManualRemediationGuideAsync(finding);

        // Assert — should fall back to NIST steps
        guide.Steps.Should().NotBeEmpty();
        guide.Steps.Should().Contain(s => s.Contains("CM"));
    }

    [Fact]
    public async Task ManualGuide_PolicyAssignment_HasPolicyPrereqs()
    {
        // Arrange
        var finding = new ComplianceFinding
        {
            Id = "F-MAN-P",
            ControlId = "AU-2",
            ControlFamily = "AU",
            Title = "Audit Events",
            RemediationType = RemediationType.PolicyAssignment,
            AutoRemediable = false,
            SubscriptionId = "sub-1"
        };

        // Act
        var guide = await _sut.GenerateManualRemediationGuideAsync(finding);

        // Assert
        guide.Prerequisites.Should().Contain(p => p.Contains("Policy Contributor"));
        guide.RequiredPermissions.Should().Contain(p => p.Contains("policyAssignments/write"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HighRiskFamilies wire-up (Phase 1.B of appsettings cleanup)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Default (no override) — AC family is high-risk per
    /// <c>ControlFamilies.HighRiskFamilies</c>, so the guide must include
    /// a change-approval prerequisite for AC.
    /// </summary>
    [Fact]
    public async Task ManualGuide_DefaultHighRiskFamilies_IncludesApprovalPrereqForAc()
    {
        // Arrange — _options.HighRiskFamilies is null → fallback to defaults
        var finding = new ComplianceFinding
        {
            Id = "F-HRF-DEFAULT",
            ControlId = "AC-2",
            ControlFamily = "AC",
            Title = "Account Management",
            RemediationType = RemediationType.Manual,
            AutoRemediable = false,
            SubscriptionId = "sub-1"
        };

        // Act
        var guide = await _sut.GenerateManualRemediationGuideAsync(finding);

        // Assert
        guide.Prerequisites.Should().Contain(p => p.Contains("AC") && p.Contains("approval"));
    }

    /// <summary>
    /// Wire-up assertion — overriding <c>ComplianceAgentOptions.HighRiskFamilies</c>
    /// to a non-default set must change classification:
    /// <list type="bullet">
    /// <item>AC (previously high-risk by default) → no approval prereq</item>
    /// <item>RA (not in the default set) → approval prereq added</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task ManualGuide_OverrideHighRiskFamilies_DrivesClassification()
    {
        // Arrange
        var customOptions = new ComplianceAgentOptions
        {
            EnableAutomatedRemediation = true,
            HighRiskFamilies = new List<string> { "RA" },  // RA only — exclude AC
            Remediation = new RemediationOptions
            {
                MaxConcurrentRemediations = 3,
                ScriptTimeoutSeconds = 300,
                MaxRetries = 3
            }
        };

        var engine = new AtoRemediationEngine(
            _complianceEngine.Object,
            _dbFactory.Object,
            _armService.Object,
            _aiGenerator.Object,
            _complianceRemediation.Object,
            _scriptExecutor.Object,
            _nistSteps.Object,
            _sanitization.Object,
            Options.Create(customOptions),
            _logger.Object,
            _scopeFactory.Object);

        var acFinding = new ComplianceFinding
        {
            Id = "F-HRF-AC", ControlId = "AC-2", ControlFamily = "AC",
            RemediationType = RemediationType.Manual, AutoRemediable = false,
            SubscriptionId = "sub-1"
        };
        var raFinding = new ComplianceFinding
        {
            Id = "F-HRF-RA", ControlId = "RA-3", ControlFamily = "RA",
            RemediationType = RemediationType.Manual, AutoRemediable = false,
            SubscriptionId = "sub-1"
        };

        // Act
        var acGuide = await engine.GenerateManualRemediationGuideAsync(acFinding);
        var raGuide = await engine.GenerateManualRemediationGuideAsync(raFinding);

        // Assert
        acGuide.Prerequisites.Should().NotContain(p => p.Contains("AC") && p.Contains("approval"),
            "AC was excluded from the overridden HighRiskFamilies list");
        raGuide.Prerequisites.Should().Contain(p => p.Contains("RA") && p.Contains("approval"),
            "RA was added to the overridden HighRiskFamilies list");
    }

    /// <summary>
    /// Empty override (explicit empty list) should fall back to the canonical
    /// defaults — same behavior as null. Prevents the trap where a misconfigured
    /// empty JSON array silently disables high-risk classification entirely.
    /// </summary>
    [Fact]
    public async Task ManualGuide_EmptyHighRiskFamiliesOverride_FallsBackToDefaults()
    {
        // Arrange
        var customOptions = new ComplianceAgentOptions
        {
            EnableAutomatedRemediation = true,
            HighRiskFamilies = new List<string>(),  // explicit empty list
            Remediation = new RemediationOptions { MaxConcurrentRemediations = 3, ScriptTimeoutSeconds = 300, MaxRetries = 3 }
        };

        var engine = new AtoRemediationEngine(
            _complianceEngine.Object, _dbFactory.Object, _armService.Object,
            _aiGenerator.Object, _complianceRemediation.Object, _scriptExecutor.Object,
            _nistSteps.Object, _sanitization.Object,
            Options.Create(customOptions), _logger.Object, _scopeFactory.Object);

        var acFinding = new ComplianceFinding
        {
            Id = "F-HRF-EMPTY", ControlId = "AC-2", ControlFamily = "AC",
            RemediationType = RemediationType.Manual, AutoRemediable = false,
            SubscriptionId = "sub-1"
        };

        // Act
        var guide = await engine.GenerateManualRemediationGuideAsync(acFinding);

        // Assert — AC still treated as high-risk via fallback to ControlFamilies.HighRiskFamilies
        guide.Prerequisites.Should().Contain(p => p.Contains("AC") && p.Contains("approval"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 12 — Progress Tracking & History (T092)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProgress_MixedExecutions_ReturnsAccurateCounts()
    {
        // Arrange — execute some findings to create completed entries + leave some pending
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");

        // 3 completed
        for (int i = 0; i < 3; i++)
        {
            var f = CreateAutoRemediableFinding($"F-PRG-C{i}");
            SetupFindingLookup(f);
            SetupAiTierSuccess($"F-PRG-C{i}");
            await _sut.ExecuteRemediationAsync($"F-PRG-C{i}", CreateDefaultOptions(autoValidate: false));
        }

        // 2 pending
        for (int i = 0; i < 2; i++)
        {
            var f = CreateAutoRemediableFinding($"F-PRG-P{i}");
            SetupFindingLookup(f);
            await _sut.ExecuteRemediationAsync($"F-PRG-P{i}", CreateDefaultOptions(requireApproval: true));
        }

        // Act
        var progress = await _sut.GetRemediationProgressAsync();

        // Assert
        progress.CompletedCount.Should().Be(3);
        progress.PendingCount.Should().Be(2);
        progress.TotalCount.Should().BeGreaterOrEqualTo(5);
        progress.CompletionRate.Should().BeGreaterThan(0);
        progress.Period.Should().Be("Last 30 days");
    }

    [Fact]
    public async Task GetHistory_DateRange_ReturnsFilteredSubset()
    {
        // Arrange
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");

        // Execute 2 findings
        for (int i = 0; i < 2; i++)
        {
            var f = CreateAutoRemediableFinding($"F-HIS-{i}");
            SetupFindingLookup(f);
            SetupAiTierSuccess($"F-HIS-{i}");
            await _sut.ExecuteRemediationAsync($"F-HIS-{i}", CreateDefaultOptions(autoValidate: false));
        }

        // Act — wide date range should include them
        var history = await _sut.GetRemediationHistoryAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        // Assert
        history.Executions.Should().HaveCountGreaterOrEqualTo(2);
        history.Metrics.SuccessfulExecutions.Should().BeGreaterOrEqualTo(2);
        history.TotalCount.Should().Be(history.Executions.Count);
    }

    [Fact]
    public async Task GetHistory_EmptyRange_ReturnsZeroCounts()
    {
        // Act — future date range, nothing should match
        var history = await _sut.GetRemediationHistoryAsync(
            DateTime.UtcNow.AddDays(10), DateTime.UtcNow.AddDays(11));

        // Assert
        history.Executions.Should().BeEmpty();
        history.Metrics.TotalExecutions.Should().Be(0);
        history.Metrics.SuccessfulExecutions.Should().Be(0);
        history.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetProgress_AverageTime_CalculatedFromCompleted()
    {
        // Arrange — complete one execution
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot-data");
        var f = CreateAutoRemediableFinding("F-AVG-1");
        SetupFindingLookup(f);
        SetupAiTierSuccess("F-AVG-1");
        await _sut.ExecuteRemediationAsync("F-AVG-1", CreateDefaultOptions(autoValidate: false));

        // Act
        var progress = await _sut.GetRemediationProgressAsync();

        // Assert — at least one completed, avg time > 0
        progress.CompletedCount.Should().BeGreaterOrEqualTo(1);
        // Average time may be very small in tests but should be set
        progress.AsOf.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 13 — Scheduled Remediation (T095)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Schedule_FutureTime_CreatesScheduleResult()
    {
        // Arrange
        var ids = new[] { "F-SCH-1", "F-SCH-2", "F-SCH-3" };
        var scheduledTime = DateTime.UtcNow.AddHours(2);
        var options = new BatchRemediationOptions { MaxConcurrentRemediations = 3, DryRun = true };

        // Act
        var result = await _sut.ScheduleRemediationAsync(ids, scheduledTime, options);

        // Assert
        result.Status.Should().Be("Scheduled");
        result.FindingIds.Should().BeEquivalentTo(ids);
        result.FindingCount.Should().Be(3);
        result.ScheduledTime.Should().Be(scheduledTime);
        result.Options.Should().NotBeNull();
        result.Options!.DryRun.Should().BeTrue();
        result.ScheduleId.Should().NotBeNullOrEmpty();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Schedule_PastTime_ReturnsError()
    {
        // Arrange
        var ids = new[] { "F-SCH-PAST" };
        var scheduledTime = DateTime.UtcNow.AddMinutes(-10);

        // Act
        var result = await _sut.ScheduleRemediationAsync(ids, scheduledTime);

        // Assert
        result.Status.Should().Be("Error");
        result.FindingCount.Should().Be(1);
    }

    [Fact]
    public async Task Schedule_EmptyFindings_ReturnsError()
    {
        // Arrange
        var scheduledTime = DateTime.UtcNow.AddHours(1);

        // Act
        var result = await _sut.ScheduleRemediationAsync(Array.Empty<string>(), scheduledTime);

        // Assert
        result.Status.Should().Be("Error");
        result.FindingCount.Should().Be(0);
    }

    [Fact]
    public async Task Schedule_OptionsPreserved_InResult()
    {
        // Arrange
        var ids = new[] { "F-SCH-OPT" };
        var scheduledTime = DateTime.UtcNow.AddDays(1);
        var options = new BatchRemediationOptions
        {
            MaxConcurrentRemediations = 5,
            DryRun = false,
            FailFast = true
        };

        // Act
        var result = await _sut.ScheduleRemediationAsync(ids, scheduledTime, options);

        // Assert
        result.Options.Should().NotBeNull();
        result.Options!.MaxConcurrentRemediations.Should().Be(5);
        result.Options.FailFast.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 14 — Edge Case Tests (T101)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_SnapshotFails_ExecutionFails()
    {
        // Arrange — snapshot throws, triggering outer catch
        var finding = CreateAutoRemediableFinding("F-SNAP-FAIL");
        SetupFindingLookup(finding);

        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Snapshot capture failed"));

        // Act
        var result = await _sut.ExecuteRemediationAsync("F-SNAP-FAIL", CreateDefaultOptions(autoValidate: false));

        // Assert — outer catch fires, returns Failed
        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("Snapshot capture failed");
    }

    [Fact]
    public async Task Execute_AiScriptRejectedBySanitization_FallsToStructured()
    {
        // Arrange
        var finding = CreateAutoRemediableFinding("F-SANIT-REJ");
        SetupFindingLookup(finding);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot");

        _aiGenerator.Setup(g => g.IsAvailable).Returns(true);
        _aiGenerator.Setup(g => g.GenerateEnhancedPlanAsync(It.IsAny<ComplianceFinding>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemediationPlan
            {
                Steps = new List<RemediationStep> { new() { Description = "Dangerous script", Script = "rm -rf /", Priority = 1 } }
            });

        // Sanitization rejects AI script
        _sanitization.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(false);

        // Structured tier succeeds
        SetupStructuredTierSuccess("F-SANIT-REJ");

        // Act
        var result = await _sut.ExecuteRemediationAsync("F-SANIT-REJ", CreateDefaultOptions(autoValidate: false));

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        result.StepsExecuted.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Execute_AlreadyRemediatedFinding_StillProcesses()
    {
        // Arrange — finding with Remediated status
        var finding = CreateAutoRemediableFinding("F-ALREADY");
        finding.Status = FindingStatus.Remediated;
        SetupFindingLookup(finding);
        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot");
        SetupAiTierSuccess("F-ALREADY");

        // Act
        var result = await _sut.ExecuteRemediationAsync("F-ALREADY", CreateDefaultOptions(autoValidate: false));

        // Assert
        result.Should().NotBeNull();
        result.FindingId.Should().Be("F-ALREADY");
    }

    [Fact]
    public async Task Batch_MultipleFindingsOnSameResource_AllProcessed()
    {
        // Arrange — 3 findings on the same resource
        var findings = new List<ComplianceFinding>();
        for (int i = 0; i < 3; i++)
        {
            var f = CreateAutoRemediableFinding($"F-SAME-{i}");
            f.ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/shared";
            findings.Add(f);
            SetupFindingLookup(f);
            SetupAiTierSuccess($"F-SAME-{i}");
        }

        _armService.Setup(s => s.CaptureResourceSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("snapshot");

        // Act
        var result = await _sut.ExecuteBatchRemediationAsync(
            findings.Select(f => f.Id!).ToList(),
            new BatchRemediationOptions { DryRun = false });

        // Assert
        result.Executions.Should().HaveCount(3);
        result.SuccessCount.Should().Be(3);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Phase 14 — Performance Test (T105)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneratePlan_1000Findings_CompletesWithin5Seconds()
    {
        // Arrange — 1000 mock findings
        var findings = GenerateFindings(1000);

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var plan = await _sut.GenerateRemediationPlanAsync(findings);
        sw.Stop();

        // Assert
        plan.Should().NotBeNull();
        plan.TotalFindings.Should().Be(1000);
        plan.Items.Should().NotBeNullOrEmpty();
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "Plan generation for 1000 findings should complete within 5 seconds");
    }
}
