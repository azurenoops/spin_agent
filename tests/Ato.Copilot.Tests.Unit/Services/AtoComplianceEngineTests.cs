using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for AtoComplianceEngine: scan orchestration, finding correlation,
/// score computation, persistence, CancellationToken timeout.
/// </summary>
public class AtoComplianceEngineTests : IDisposable
{
    private readonly Mock<INistControlsService> _nistMock = new();
    private readonly Mock<IAzurePolicyComplianceService> _policyMock = new();
    private readonly Mock<IDefenderForCloudService> _defenderMock = new();
    private readonly Mock<ILogger<AtoComplianceEngine>> _loggerMock = new();
    private readonly Mock<IScannerRegistry> _scannerRegistryMock = new();
    private readonly Mock<IAssessmentPersistenceService> _persistenceMock = new();
    private readonly Mock<IAzureResourceService> _azureResourceMock = new();
    private readonly Mock<IStigValidationService> _stigValidationMock = new();
    private readonly Mock<IEvidenceCollectorRegistry> _evidenceCollectorRegistryMock = new();
    private readonly Mock<IComplianceWatchService> _complianceWatchMock = new();
    private readonly Mock<IAlertManager> _alertManagerMock = new();
    private readonly Mock<ISystemEnvironmentScopeResolver> _environmentResolver = new();
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly string _dbName;

    public AtoComplianceEngineTests()
    {
        _dbName = $"AtoComplianceTest_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        _dbFactory = new InMemoryDbContextFactory(options);

        // Default NIST mock: return some controls for AC family
        _nistMock.Setup(x => x.GetControlFamilyAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string family, bool _, CancellationToken _) =>
            {
                if (family.Equals("AC", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<NistControl>
                    {
                        new() { Id = "AC-1", Family = "AC", Title = "Policy" },
                        new() { Id = "AC-2", Family = "AC", Title = "Account Management" },
                        new() { Id = "AC-3", Family = "AC", Title = "Access Enforcement" }
                    };
                }
                return new List<NistControl>();
            });

        // Default STIG validation: return empty findings
        _stigValidationMock.Setup(x => x.ValidateAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<NistControl>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ComplianceFinding>());
    }

    public void Dispose()
    {
        // InMemory databases are automatically cleaned up
        GC.SuppressFinalize(this);
    }

    private AtoComplianceEngine CreateEngine()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_complianceWatchMock.Object);
        services.AddSingleton(_alertManagerMock.Object);
        services.AddSingleton(_environmentResolver.Object);
        var sp = services.BuildServiceProvider();

        return new(
            _nistMock.Object,
            _policyMock.Object,
            _defenderMock.Object,
            _dbFactory,
            _loggerMock.Object,
            _scannerRegistryMock.Object,
            _persistenceMock.Object,
            _azureResourceMock.Object,
            _stigValidationMock.Object,
            _evidenceCollectorRegistryMock.Object,
            sp, _environmentResolver.Setups.Any()
                ? new CanonicalEnvironmentCollectionGuard(_dbFactory, sp.GetRequiredService<IServiceScopeFactory>())
                : null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanonicalExecution_BlocksBeforeAnyScannerOrResourceRead(bool eligible)
    {
        // Arrange
        var subscription = Guid.NewGuid();
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.RegisteredSystems.Add(new() { Id = "canonical-system", Name = "Synthetic system" });
        await db.SaveChangesAsync();
        var source = new ResolvedSystemEnvironmentScope(Guid.NewGuid(), 1, Guid.NewGuid(), 1,
            new(Guid.NewGuid(), Guid.NewGuid(), subscription, Guid.NewGuid(), "Government", "Synthetic",
                "Selected", DateTimeOffset.UtcNow), "ProviderAllocation", Guid.NewGuid(), 1,
            eligible, eligible ? null : "Allocation withdrawn",
            [$"/subscriptions/{subscription}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/a"],
            [], [], new("Test", null, null, "Reconciled", null, DateTimeOffset.UtcNow));
        _environmentResolver.Setup(x => x.ResolveAsync("canonical-system", It.IsAny<EnvironmentScopePurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes("canonical-system", 1, [source], []));
        var engine = CreateEngine();

        // Act
        var act = () => engine.RunRetainedAssessmentAsync(new()
            { RegisteredSystemId = "canonical-system", SubscriptionId = subscription.ToString() });

        // Assert
        (await act.Should().ThrowAsync<AssessmentEnvironmentException>()).Which.ErrorCode
            .Should().Be(eligible ? "ASSESSMENT_SCOPE_UNSUPPORTED" : "ASSESSMENT_ENVIRONMENT_INELIGIBLE");
        _azureResourceMock.Invocations.Should().BeEmpty();
        _policyMock.Invocations.Should().BeEmpty();
        _defenderMock.Invocations.Should().BeEmpty();
        _scannerRegistryMock.Invocations.Should().BeEmpty();
    }

    private static string MakePolicyResponse(List<object>? states = null) =>
        JsonSerializer.Serialize(new
        {
            subscriptionId = "test-sub",
            totalStates = states?.Count ?? 0,
            states = states ?? new List<object>(),
            truncated = false,
            evaluatedAt = DateTime.UtcNow
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static string MakeDefenderResponse(List<object>? recommendations = null) =>
        JsonSerializer.Serialize(new
        {
            subscriptionId = "test-sub",
            totalRecommendations = recommendations?.Count ?? 0,
            recommendations = recommendations ?? new List<object>(),
            truncated = false,
            evaluatedAt = DateTime.UtcNow
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    // ─── Combined Scan ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAssessment_CombinedScan_RunsBothScanSources()
    {
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub-id", scanType: "combined");

        result.Should().NotBeNull();
        result.Status.Should().Be(AssessmentStatus.Completed);
        result.ScanType.Should().Be("combined");
        result.SubscriptionId.Should().Be("test-sub-id");

        _policyMock.Verify(x => x.GetPolicyStatesAsync("test-sub-id", null, It.IsAny<CancellationToken>()), Times.Once);
        _defenderMock.Verify(x => x.GetRecommendationsAsync("test-sub-id", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAssessment_PolicyOnly_SkipsDefender()
    {
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub", scanType: "policy");

        result.ScanType.Should().Be("policy");
        _policyMock.Verify(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _defenderMock.Verify(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAssessment_ResourceOnly_SkipsPolicy()
    {
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub", scanType: "resource");

        result.ScanType.Should().Be("resource");
        _policyMock.Verify(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Never);
        _defenderMock.Verify(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Finding Extraction ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunAssessment_WithPolicyNonCompliance_CreatesFindings()
    {
        var states = new List<object>
        {
            new
            {
                policyDefinitionId = "def-1",
                policyAssignmentId = "assign-1",
                complianceState = "NonCompliant",
                resourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/test",
                resourceType = "Microsoft.Storage/storageAccounts",
                policyDefinitionGroupNames = new[] { "NIST_SP_800-53_Rev._5_AC-2" },
                timestamp = DateTime.UtcNow
            }
        };

        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse(states));
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub");

        result.Findings.Should().NotBeEmpty();
        result.Findings.Should().Contain(f => f.ControlId == "AC-2");
        result.Findings.First(f => f.ControlId == "AC-2").ScanSource.Should().Be(ScanSourceType.Policy);
    }

    [Fact]
    public async Task RunAssessment_WithDefenderRecommendations_CreatesFindings()
    {
        var recs = new List<object>
        {
            new
            {
                id = "rec-1",
                name = "rec-name",
                displayName = "MFA should be enabled for all accounts",
                status = "Unhealthy"
            }
        };

        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse(recs));

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub");

        result.Findings.Should().NotBeEmpty();
        result.Findings.Should().Contain(f => f.ControlId == "IA-2"); // MFA → IA-2
        result.Findings.First(f => f.ControlId == "IA-2").ScanSource.Should().Be(ScanSourceType.Defender);
    }

    // ─── Compliance Score ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunAssessment_NoFindings_ScoreIs100()
    {
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        // Only AC family with 3 controls, no findings = 100%
        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub", controlFamilies: "AC");

        result.TotalControls.Should().Be(3);
        result.PassedControls.Should().Be(3);
        result.FailedControls.Should().Be(0);
        result.ComplianceScore.Should().Be(100.0);
    }

    [Fact]
    public async Task RunAssessment_AllFailed_ScoreIsLow()
    {
        // Create findings for all 3 AC controls
        var states = new List<object>
        {
            new { complianceState = "NonCompliant", policyDefinitionGroupNames = new[] { "NIST_SP_800-53_Rev._5_AC-1" }, resourceId = "r1", resourceType = "t1", policyDefinitionId = "d1", policyAssignmentId = "a1", timestamp = DateTime.UtcNow },
            new { complianceState = "NonCompliant", policyDefinitionGroupNames = new[] { "NIST_SP_800-53_Rev._5_AC-2" }, resourceId = "r2", resourceType = "t2", policyDefinitionId = "d2", policyAssignmentId = "a2", timestamp = DateTime.UtcNow },
            new { complianceState = "NonCompliant", policyDefinitionGroupNames = new[] { "NIST_SP_800-53_Rev._5_AC-3" }, resourceId = "r3", resourceType = "t3", policyDefinitionId = "d3", policyAssignmentId = "a3", timestamp = DateTime.UtcNow }
        };

        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse(states));
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub", controlFamilies: "AC");

        result.TotalControls.Should().Be(3);
        result.FailedControls.Should().Be(3);
        result.PassedControls.Should().Be(0);
        result.ComplianceScore.Should().Be(0.0);
    }

    // ─── Framework Normalization ─────────────────────────────────────────────

    [Fact]
    public async Task RunAssessment_DefaultFramework_IsNIST80053()
    {
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub");

        result.Framework.Should().Be("NIST80053");
    }

    [Fact]
    public async Task RunAssessment_ScanTypeNormalization()
    {
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();

        var quickResult = await engine.RunAssessmentAsync("test-sub", scanType: "quick");
        quickResult.ScanType.Should().Be("combined");

        var fullResult = await engine.RunAssessmentAsync("test-sub", scanType: "full");
        fullResult.ScanType.Should().Be("combined");
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAssessment_PersistsToDatabase()
    {
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub");

        // Verify persisted
        await using var db = await _dbFactory.CreateDbContextAsync();
        var persisted = await db.Assessments.FindAsync(result.Id);
        persisted.Should().NotBeNull();
        persisted!.SubscriptionId.Should().Be("test-sub");
        persisted.Status.Should().Be(AssessmentStatus.Completed);
    }

    [Fact]
    public async Task GetAssessmentHistoryAsync_ReturnsPersistedAssessments()
    {
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        await engine.RunAssessmentAsync("test-sub");
        await engine.RunAssessmentAsync("test-sub");

        var history = await engine.GetAssessmentHistoryAsync("test-sub");
        history.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFindingAsync_ExistingFinding_ReturnsFinding()
    {
        var states = new List<object>
        {
            new { complianceState = "NonCompliant", policyDefinitionGroupNames = new[] { "NIST_SP_800-53_Rev._5_AC-2" }, resourceId = "r1", resourceType = "t1", policyDefinitionId = "d1", policyAssignmentId = "a1", timestamp = DateTime.UtcNow }
        };
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse(states));
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var assessment = await engine.RunAssessmentAsync("test-sub", controlFamilies: "AC");

        if (assessment.Findings.Count > 0)
        {
            var findingId = assessment.Findings.First().Id;
            var finding = await engine.GetFindingAsync(findingId);
            finding.Should().NotBeNull();
            finding!.ControlId.Should().Be("AC-2");
        }
    }

    // ─── CancellationToken ──────────────────────────────────────────────────────

    [Fact]
    public async Task RunAssessment_WhenCancelled_ThrowsAndMarksCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var engine = CreateEngine();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.RunAssessmentAsync("test-sub", cancellationToken: cts.Token));
    }

    // ─── Error Handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task RunAssessment_PolicyScanError_StillCompletes()
    {
        // Policy returns error JSON, Defender returns empty
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { error = "Auth failed" }));
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub");

        // Assessment should still complete (partial results)
        result.Status.Should().Be(AssessmentStatus.Completed);
    }

    [Fact]
    public async Task RunAssessment_DefenderScanError_StillCompletes()
    {
        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse());
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { error = "Defender unavailable" }));

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub");

        result.Status.Should().Be(AssessmentStatus.Completed);
    }

    // ─── Control Family Filtering ──────────────────────────────────────────────

    [Fact]
    public async Task RunAssessment_WithControlFamilyFilter_FiltersFindings()
    {
        var states = new List<object>
        {
            new { complianceState = "NonCompliant", policyDefinitionGroupNames = new[] { "NIST_SP_800-53_Rev._5_AC-2" }, resourceId = "r1", resourceType = "t1", policyDefinitionId = "d1", policyAssignmentId = "a1", timestamp = DateTime.UtcNow },
            new { complianceState = "NonCompliant", policyDefinitionGroupNames = new[] { "NIST_SP_800-53_Rev._5_AU-3" }, resourceId = "r2", resourceType = "t2", policyDefinitionId = "d2", policyAssignmentId = "a2", timestamp = DateTime.UtcNow }
        };

        _policyMock.Setup(x => x.GetPolicyStatesAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePolicyResponse(states));
        _defenderMock.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeDefenderResponse());

        var engine = CreateEngine();
        var result = await engine.RunAssessmentAsync("test-sub", controlFamilies: "AC");

        // Should only have AC findings, not AU
        result.Findings.Should().OnlyContain(f => f.ControlFamily == "AC");
    }

    // ─── T038: RunComprehensiveAssessmentAsync Tests ──────────────────────────

    [Fact]
    public async Task RunComprehensiveAssessment_Scans20Families()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        result.Should().NotBeNull();
        result.Status.Should().Be(AssessmentStatus.Completed);
        result.ControlFamilyResults.Should().HaveCount(20);
        result.Framework.Should().Be("NIST80053");
        result.ScanType.Should().Be("comprehensive");
        result.SubscriptionId.Should().Be("test-sub");
        result.SubscriptionIds.Should().Contain("test-sub");
        result.CompletedAt.Should().NotBeNull();
        result.AssessmentDuration.Should().NotBeNull();
    }

    [Fact]
    public async Task RunComprehensiveAssessment_CalculatesScore()
    {
        SetupScannerRegistryForAllFamilies(passedControls: 8, totalControls: 10);

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        // 20 families * 10 controls = 200 total, 20 * 8 passed = 160 passed → 80%
        result.TotalControls.Should().Be(200);
        result.PassedControls.Should().Be(160);
        result.FailedControls.Should().Be(40);
        result.ComplianceScore.Should().Be(80.0);
    }

    [Fact]
    public async Task RunComprehensiveAssessment_GeneratesExecutiveSummary()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        result.ExecutiveSummary.Should().NotBeNullOrEmpty();
        result.ExecutiveSummary.Should().Contain("Executive Summary");
        result.ExecutiveSummary.Should().Contain("Compliance Score");
    }

    [Fact]
    public async Task RunComprehensiveAssessment_WhenCancelled_Throws()
    {
        SetupScannerRegistryForAllFamilies();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var engine = CreateEngine();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.RunComprehensiveAssessmentAsync("test-sub", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RunComprehensiveAssessment_PartialFailure_RetainsWorkWithoutClaimingCompletion()
    {
        // Setup most families to succeed, but one scanner throws
        var failingScanner = new Mock<IComplianceScanner>();
        failingScanner.Setup(x => x.FamilyCode).Returns("AC");
        failingScanner.Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<NistControl>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ControlFamilyAssessment.Failed("AC", "Scanner error"));

        var defaultScanner = CreateMockScanner("DEFAULT");

        _scannerRegistryMock.Setup(x => x.GetScanner("AC")).Returns(failingScanner.Object);
        _scannerRegistryMock.Setup(x => x.GetScanner(It.Is<string>(
            s => !s.Equals("AC", StringComparison.OrdinalIgnoreCase))))
            .Returns(defaultScanner.Object);

        SetupNistForAllFamilies();

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        result.Status.Should().Be(AssessmentStatus.Failed);
        result.ControlFamilyResults.Should().HaveCount(20);

        var acResult = result.ControlFamilyResults.First(f => f.FamilyCode == "AC");
        acResult.Status.Should().Be(FamilyAssessmentStatus.Failed);
    }

    [Fact]
    public async Task RunComprehensiveAssessment_WithResourceGroup_SetsFilter()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub", resourceGroup: "my-rg");

        result.ResourceGroupFilter.Should().Be("my-rg");
    }

    [Fact]
    public async Task RunComprehensiveAssessment_PersistsViaPersistenceService()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        await engine.RunComprehensiveAssessmentAsync("test-sub");

        _persistenceMock.Verify(x => x.SaveAssessmentAsync(
            It.Is<ComplianceAssessment>(a => a.Status == AssessmentStatus.Completed),
            It.IsAny<CancellationToken>()), Times.Exactly(21));
    }

    [Fact]
    public async Task RunComprehensiveAssessment_PreWarmsCache()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        await engine.RunComprehensiveAssessmentAsync("test-sub");

        _azureResourceMock.Verify(x => x.PreWarmCacheAsync("test-sub",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunComprehensiveAssessment_RecordsScanPillarResults()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        result.ScanPillarResults.Should().ContainKey("ARM");
        result.ScanPillarResults.Should().ContainKey("Policy");
        result.ScanPillarResults.Should().ContainKey("Defender");
        result.ScanPillarResults["Policy"].Should().BeFalse("a missing evaluator outcome is not proof of success");
        result.ScanPillarResults["Defender"].Should().BeFalse("a missing evaluator outcome is not proof of success");
    }

    [Fact]
    public async Task RunComprehensiveAssessment_ReportsProgress()
    {
        SetupScannerRegistryForAllFamilies();

        var progressReports = new List<AssessmentProgress>();
        var progress = new Progress<AssessmentProgress>(p =>
            progressReports.Add(new AssessmentProgress
            {
                TotalFamilies = p.TotalFamilies,
                CompletedFamilies = p.CompletedFamilies,
                CurrentFamily = p.CurrentFamily,
                PercentComplete = p.PercentComplete,
                FamilyResults = new List<string>(p.FamilyResults)
            }));

        var engine = CreateEngine();
        await engine.RunComprehensiveAssessmentAsync("test-sub", progress: progress);

        // Progress should have been reported (at least the final one with 100%)
        // Note: IProgress<T> may buffer reports, but we verify the engine calls it
        progressReports.Should().NotBeEmpty();
    }

    // ─── T039: RunEnvironmentAssessmentAsync Tests ──────────────────────────

    [Fact]
    public async Task RunEnvironmentAssessment_MultiSubscriptionAggregation()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        var subs = new[] { "sub-1", "sub-2" };
        var result = await engine.RunEnvironmentAssessmentAsync(subs, "Production");

        result.Should().NotBeNull();
        result.Status.Should().Be(AssessmentStatus.Completed);
        result.EnvironmentName.Should().Be("Production");
        result.SubscriptionIds.Should().HaveCount(2);
        result.SubscriptionIds.Should().Contain("sub-1");
        result.SubscriptionIds.Should().Contain("sub-2");
        result.ScanType.Should().Be("environment");
        result.ControlFamilyResults.Should().HaveCount(20);
    }

    [Fact]
    public async Task RunEnvironmentAssessment_PreWarmsAllCaches()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        await engine.RunEnvironmentAssessmentAsync(new[] { "sub-1", "sub-2", "sub-3" }, "Staging");

        _azureResourceMock.Verify(x => x.PreWarmCacheAsync("sub-1", It.IsAny<CancellationToken>()), Times.Once);
        _azureResourceMock.Verify(x => x.PreWarmCacheAsync("sub-2", It.IsAny<CancellationToken>()), Times.Once);
        _azureResourceMock.Verify(x => x.PreWarmCacheAsync("sub-3", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunEnvironmentAssessment_EmptySubscriptions_Throws()
    {
        var engine = CreateEngine();
        await Assert.ThrowsAsync<ArgumentException>(
            () => engine.RunEnvironmentAssessmentAsync(Array.Empty<string>(), "Production"));
    }

    [Fact]
    public async Task RunEnvironmentAssessment_SetsEnvironmentName()
    {
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        var result = await engine.RunEnvironmentAssessmentAsync(
            new[] { "sub-1" }, "Development");

        result.EnvironmentName.Should().Be("Development");
        result.SubscriptionId.Should().Be("sub-1");
    }

    // ─── T040: AssessControlFamilyAsync Tests ──────────────────────────────

    [Fact]
    public async Task AssessControlFamily_ValidFamily_DispatchesToScanner()
    {
        var mockScanner = CreateMockScanner("AC", 3, 1);
        _scannerRegistryMock.Setup(x => x.GetScanner("AC")).Returns(mockScanner.Object);
        SetupNistForFamily("AC", 3);

        var engine = CreateEngine();
        var result = await engine.AssessControlFamilyAsync("AC", "test-sub");

        result.Should().NotBeNull();
        result.FamilyCode.Should().Be("AC");
        mockScanner.Verify(x => x.ScanAsync("test-sub", null,
            It.IsAny<IEnumerable<NistControl>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssessControlFamily_InvalidFamily_ThrowsArgument()
    {
        var engine = CreateEngine();
        await Assert.ThrowsAsync<ArgumentException>(
            () => engine.AssessControlFamilyAsync("ZZ", "test-sub"));
    }

    [Fact]
    public async Task AssessControlFamily_ScannerFailure_ReturnsFailedResult()
    {
        _nistMock.Setup(x => x.GetControlFamilyAsync("AU", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("NIST service unavailable"));

        var engine = CreateEngine();
        var result = await engine.AssessControlFamilyAsync("AU", "test-sub");

        result.Status.Should().Be(FamilyAssessmentStatus.Failed);
        result.ErrorMessage.Should().Contain("Failed to get controls");
    }

    [Fact]
    public async Task AssessControlFamily_MergesStigFindings()
    {
        var mockScanner = CreateMockScanner("AC", 3, 0); // scanner returns 0 findings
        _scannerRegistryMock.Setup(x => x.GetScanner("AC")).Returns(mockScanner.Object);
        SetupNistForFamily("AC", 3);

        // STIG returns 1 finding
        _stigValidationMock.Setup(x => x.ValidateAsync("AC",
                It.IsAny<IEnumerable<NistControl>>(), "test-sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ComplianceFinding>
            {
                new() { ControlId = "AC-2", ControlFamily = "AC", Title = "STIG Finding", Severity = FindingSeverity.High }
            });

        var engine = CreateEngine();
        var result = await engine.AssessControlFamilyAsync("AC", "test-sub");

        result.Findings.Should().HaveCount(1);
        result.FailedControls.Should().Be(1);
        result.PassedControls.Should().Be(2);
    }

    [Fact]
    public async Task AssessControlFamily_StigFailure_MarksFailedAndRetainsScannerResults()
    {
        // Arrange
        var mockScanner = CreateMockScanner("AC", 3, 1);
        _scannerRegistryMock.Setup(x => x.GetScanner("AC")).Returns(mockScanner.Object);
        SetupNistForFamily("AC", 3);

        _stigValidationMock.Setup(x => x.ValidateAsync(It.IsAny<string>(),
                It.IsAny<IEnumerable<NistControl>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("STIG service down"));

        var engine = CreateEngine();

        // Act
        var result = await engine.AssessControlFamilyAsync("AC", "test-sub");

        // Assert
        result.Status.Should().Be(FamilyAssessmentStatus.Failed);
        result.ErrorMessage.Should().Be("STIG validation failed; scanner observations retained.");
        result.Findings.Should().ContainSingle()
            .Which.Should().Match<ComplianceFinding>(f =>
                f.ControlFamily == "AC" && f.Title == "Finding 1" && f.Severity == FindingSeverity.Medium);
        result.TotalControls.Should().Be(3);
        result.PassedControls.Should().Be(2);
        result.FailedControls.Should().Be(1);
        result.ComplianceScore.Should().BeApproximately(2.0 / 3 * 100, 0.001);
        result.ScannerName.Should().Be("MockScanner");
    }

    [Fact]
    public async Task AssessControlFamily_WithResourceGroup_PassesToScanner()
    {
        var mockScanner = CreateMockScanner("AC", 3, 0);
        _scannerRegistryMock.Setup(x => x.GetScanner("AC")).Returns(mockScanner.Object);
        SetupNistForFamily("AC", 3);

        var engine = CreateEngine();
        await engine.AssessControlFamilyAsync("AC", "test-sub", "my-rg");

        mockScanner.Verify(x => x.ScanAsync("test-sub", "my-rg",
            It.IsAny<IEnumerable<NistControl>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── T041: Finding Correlation Tests ────────────────────────────────────

    [Fact]
    public async Task RunComprehensiveAssessment_CorrelatesFindings_ByControlIdAndResourceId()
    {
        // Setup scanner that returns duplicate findings (same controlId+resourceId)
        var scannerWithDups = new Mock<IComplianceScanner>();
        scannerWithDups.Setup(x => x.FamilyCode).Returns("DEFAULT");
        scannerWithDups.Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<NistControl>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sub, string? rg, IEnumerable<NistControl> controls, CancellationToken ct) =>
            {
                var family = controls.First().Family;
                // Only AC returns duplicates
                if (family == "AC")
                {
                    return new ControlFamilyAssessment
                    {
                        FamilyCode = family,
                        FamilyName = "Access Control",
                        TotalControls = 2,
                        PassedControls = 0,
                        FailedControls = 2,
                        ComplianceScore = 0,
                        Status = FamilyAssessmentStatus.Completed,
                        Findings = new List<ComplianceFinding>
                        {
                            new() { ControlId = "AC-2", ResourceId = "res-1", Severity = FindingSeverity.Medium, Source = "Scanner" },
                            new() { ControlId = "AC-2", ResourceId = "res-1", Severity = FindingSeverity.High, Source = "STIG" }
                        }
                    };
                }

                return new ControlFamilyAssessment
                {
                    FamilyCode = family,
                    TotalControls = 1,
                    PassedControls = 1,
                    ComplianceScore = 100,
                    Status = FamilyAssessmentStatus.Completed
                };
            });

        _scannerRegistryMock.Setup(x => x.GetScanner(It.IsAny<string>()))
            .Returns(scannerWithDups.Object);
        SetupNistForAllFamilies(controlsPerFamily: 2);

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        // The 2 findings with same ControlId+ResourceId should be correlated to 1
        var acFindings = result.Findings.Where(f => f.ControlId == "AC-2" && f.ResourceId == "res-1").ToList();
        acFindings.Should().HaveCount(1);
        // Should keep the higher severity (Critical < High < Medium < Low — Critical=0 is highest)
        acFindings[0].Severity.Should().Be(FindingSeverity.High);
        acFindings[0].ScanSource.Should().Be(ScanSourceType.Combined);
    }

    // ─── T042: Compliance Score Computation Tests ───────────────────────────

    [Fact]
    public async Task RunComprehensiveAssessment_NoFindings_ScoreIs100()
    {
        SetupScannerRegistryForAllFamilies(passedControls: 5, totalControls: 5);

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        result.ComplianceScore.Should().Be(100.0);
        result.FailedControls.Should().Be(0);
    }

    [Fact]
    public async Task RunComprehensiveAssessment_AllFail_ScoreIs0()
    {
        SetupScannerRegistryForAllFamilies(passedControls: 0, totalControls: 5);

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        result.ComplianceScore.Should().Be(0.0);
        result.PassedControls.Should().Be(0);
    }

    [Fact]
    public async Task RunComprehensiveAssessment_ProportionalScore()
    {
        SetupScannerRegistryForAllFamilies(passedControls: 3, totalControls: 4);

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        // 20 families * 4 controls = 80 total, 20 * 3 passed = 60 → 75%
        result.ComplianceScore.Should().Be(75.0);
    }

    [Fact]
    public async Task RunComprehensiveAssessment_PerFamilyScores()
    {
        SetupScannerRegistryForAllFamilies(passedControls: 7, totalControls: 10);

        var engine = CreateEngine();
        var result = await engine.RunComprehensiveAssessmentAsync("test-sub");

        foreach (var family in result.ControlFamilyResults)
        {
            family.ComplianceScore.Should().Be(70.0);
            family.TotalControls.Should().Be(10);
            family.PassedControls.Should().Be(7);
            family.FailedControls.Should().Be(3);
        }
    }

    // ─── T107: GenerateExecutiveSummary Tests ──────────────────────────────

    [Fact]
    public void GenerateExecutiveSummary_NullAssessment_Throws()
    {
        var engine = CreateEngine();
        Assert.Throws<ArgumentNullException>(() => engine.GenerateExecutiveSummary(null!));
    }

    [Fact]
    public void GenerateExecutiveSummary_EmptyFindings_ContainsZeroCounts()
    {
        var engine = CreateEngine();
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "test-sub",
            ComplianceScore = 100.0,
            TotalControls = 20,
            PassedControls = 20
        };

        var summary = engine.GenerateExecutiveSummary(assessment);

        summary.Should().Contain("Executive Summary");
        summary.Should().Contain("100.0%");
        summary.Should().Contain("| Critical | 0 |");
        summary.Should().Contain("| High | 0 |");
        summary.Should().Contain("| Medium | 0 |");
        summary.Should().Contain("| Low | 0 |");
    }

    [Fact]
    public void GenerateExecutiveSummary_MixedSeverity_OutputFormat()
    {
        var engine = CreateEngine();
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "test-sub",
            ComplianceScore = 65.0,
            TotalControls = 20,
            PassedControls = 13,
            FailedControls = 7,
            Findings = new List<ComplianceFinding>
            {
                new() { Severity = FindingSeverity.Critical },
                new() { Severity = FindingSeverity.High },
                new() { Severity = FindingSeverity.High },
                new() { Severity = FindingSeverity.Medium },
                new() { Severity = FindingSeverity.Medium },
                new() { Severity = FindingSeverity.Medium },
                new() { Severity = FindingSeverity.Low }
            },
            ControlFamilyResults = new List<ControlFamilyAssessment>
            {
                new() { FamilyCode = "AC", FamilyName = "Access Control", ComplianceScore = 50, FailedControls = 3, Status = FamilyAssessmentStatus.Completed },
                new() { FamilyCode = "IA", FamilyName = "Identification and Authentication", ComplianceScore = 60, FailedControls = 2, Status = FamilyAssessmentStatus.Completed },
                new() { FamilyCode = "SC", FamilyName = "System and Communications Protection", ComplianceScore = 80, FailedControls = 1, Status = FamilyAssessmentStatus.Completed }
            }
        };

        var summary = engine.GenerateExecutiveSummary(assessment);

        summary.Should().Contain("| Critical | 1 |");
        summary.Should().Contain("| High | 2 |");
        summary.Should().Contain("| Medium | 3 |");
        summary.Should().Contain("| Low | 1 |");
        summary.Should().Contain("**Total** | **7**");
        summary.Should().Contain("Top Risk Families");
        summary.Should().Contain("AC");
        summary.Should().Contain("IA");
    }

    [Fact]
    public void GenerateExecutiveSummary_TopRiskFamilies_OrderedByScore()
    {
        var engine = CreateEngine();
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "test-sub",
            ComplianceScore = 70.0,
            ControlFamilyResults = new List<ControlFamilyAssessment>
            {
                new() { FamilyCode = "AC", FamilyName = "Access Control", ComplianceScore = 30, FailedControls = 7, Status = FamilyAssessmentStatus.Completed },
                new() { FamilyCode = "IA", FamilyName = "Identification and Authentication", ComplianceScore = 50, FailedControls = 5, Status = FamilyAssessmentStatus.Completed },
                new() { FamilyCode = "SC", FamilyName = "System and Communications Protection", ComplianceScore = 90, FailedControls = 1, Status = FamilyAssessmentStatus.Completed },
                new() { FamilyCode = "AU", FamilyName = "Audit and Accountability", ComplianceScore = 40, FailedControls = 6, Status = FamilyAssessmentStatus.Completed }
            }
        };

        var summary = engine.GenerateExecutiveSummary(assessment);

        // Should be ordered by score ascending: AC(30), AU(40), IA(50), SC(90)
        var acIndex = summary.IndexOf("AC —");
        var auIndex = summary.IndexOf("AU —");
        var iaIndex = summary.IndexOf("IA —");
        acIndex.Should().BeLessThan(auIndex);
        auIndex.Should().BeLessThan(iaIndex);
    }

    [Fact]
    public void GenerateExecutiveSummary_WithEnvironmentName_IncludesIt()
    {
        var engine = CreateEngine();
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "test-sub",
            EnvironmentName = "Production",
            ComplianceScore = 85.0
        };

        var summary = engine.GenerateExecutiveSummary(assessment);

        summary.Should().Contain("Production");
    }

    [Fact]
    public void GenerateExecutiveSummary_WithResourceGroup_IncludesIt()
    {
        var engine = CreateEngine();
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "test-sub",
            ResourceGroupFilter = "my-rg",
            ComplianceScore = 85.0
        };

        var summary = engine.GenerateExecutiveSummary(assessment);

        summary.Should().Contain("my-rg");
    }

    [Fact]
    public void GenerateExecutiveSummary_RiskLevelBasedOnScore()
    {
        var engine = CreateEngine();

        var highScore = new ComplianceAssessment { ComplianceScore = 95 };
        engine.GenerateExecutiveSummary(highScore).Should().Contain("Low");

        var medScore = new ComplianceAssessment { ComplianceScore = 75 };
        engine.GenerateExecutiveSummary(medScore).Should().Contain("Medium");

        var highRisk = new ComplianceAssessment { ComplianceScore = 55 };
        engine.GenerateExecutiveSummary(highRisk).Should().Contain("High");

        var criticalRisk = new ComplianceAssessment { ComplianceScore = 40 };
        engine.GenerateExecutiveSummary(criticalRisk).Should().Contain("Critical");
    }

    // ─── T074: CollectEvidenceAsync Tests ──────────────────────────────────

    [Fact]
    public async Task CollectEvidenceAsync_SingleFamily_DispatchesToCollector()
    {
        var expectedPackage = new EvidencePackage
        {
            FamilyCode = "AC",
            SubscriptionId = "sub-1",
            CompletenessScore = 100,
            EvidenceItems = new List<EvidenceItem>
            {
                new() { Type = EvidenceType.Configuration, Title = "RBAC" },
                new() { Type = EvidenceType.Log, Title = "Access Log" },
                new() { Type = EvidenceType.Policy, Title = "Policy" },
                new() { Type = EvidenceType.Metric, Title = "Metric" },
                new() { Type = EvidenceType.AccessControl, Title = "Role Defs" }
            }
        };

        var collectorMock = new Mock<IEvidenceCollector>();
        collectorMock.Setup(x => x.CollectAsync("sub-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPackage);
        _evidenceCollectorRegistryMock.Setup(x => x.GetCollector("AC"))
            .Returns(collectorMock.Object);

        var engine = CreateEngine();
        var result = await engine.CollectEvidenceAsync("AC", "sub-1");

        result.Should().NotBeNull();
        result.FamilyCode.Should().Be("AC");
        result.CompletenessScore.Should().Be(100);
        result.EvidenceItems.Should().HaveCount(5);
        _evidenceCollectorRegistryMock.Verify(x => x.GetCollector("AC"), Times.Once);
    }

    [Fact]
    public async Task CollectEvidenceAsync_WithResourceGroup_PassedToCollector()
    {
        var collectorMock = new Mock<IEvidenceCollector>();
        collectorMock.Setup(x => x.CollectAsync("sub-1", "my-rg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvidencePackage { FamilyCode = "AU", SubscriptionId = "sub-1" });
        _evidenceCollectorRegistryMock.Setup(x => x.GetCollector("AU"))
            .Returns(collectorMock.Object);

        var engine = CreateEngine();
        await engine.CollectEvidenceAsync("AU", "sub-1", "my-rg");

        collectorMock.Verify(x => x.CollectAsync("sub-1", "my-rg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CollectEvidenceAsync_Cancelled_ThrowsOperationCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var collectorMock = new Mock<IEvidenceCollector>();
        collectorMock.Setup(x => x.CollectAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        _evidenceCollectorRegistryMock.Setup(x => x.GetCollector(It.IsAny<string>()))
            .Returns(collectorMock.Object);

        var engine = CreateEngine();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.CollectEvidenceAsync("AC", "sub-1", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task CollectEvidenceAsync_CollectorFailure_Propagates()
    {
        var collectorMock = new Mock<IEvidenceCollector>();
        collectorMock.Setup(x => x.CollectAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Collector crashed"));
        _evidenceCollectorRegistryMock.Setup(x => x.GetCollector("SI"))
            .Returns(collectorMock.Object);

        var engine = CreateEngine();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.CollectEvidenceAsync("SI", "sub-1"));
    }

    // ─── T087: CalculateRiskProfile Tests ──────────────────────────────────

    [Fact]
    public void CalculateRiskProfile_EmptyFindings_ReturnsLowRisk()
    {
        var assessment = new ComplianceAssessment
        {
            ComplianceScore = 100,
            Findings = new List<ComplianceFinding>()
        };

        var engine = CreateEngine();
        var profile = engine.CalculateRiskProfile(assessment);

        profile.RiskLevel.Should().Be(ComplianceRiskLevel.Low);
        profile.RiskScore.Should().Be(0);
        profile.CriticalCount.Should().Be(0);
        profile.HighCount.Should().Be(0);
        profile.MediumCount.Should().Be(0);
        profile.LowCount.Should().Be(0);
        profile.TopRisks.Should().BeEmpty();
    }

    [Fact]
    public void CalculateRiskProfile_SeverityWeights_CorrectScore()
    {
        // 2 Critical (10*2=20) + 3 High (7.5*3=22.5) + 4 Medium (5*4=20) + 1 Low (2.5*1=2.5) = 65
        var findings = new List<ComplianceFinding>();
        for (int i = 0; i < 2; i++) findings.Add(new ComplianceFinding { Severity = FindingSeverity.Critical, ControlFamily = "AC" });
        for (int i = 0; i < 3; i++) findings.Add(new ComplianceFinding { Severity = FindingSeverity.High, ControlFamily = "AU" });
        for (int i = 0; i < 4; i++) findings.Add(new ComplianceFinding { Severity = FindingSeverity.Medium, ControlFamily = "SI" });
        findings.Add(new ComplianceFinding { Severity = FindingSeverity.Low, ControlFamily = "CM" });

        var assessment = new ComplianceAssessment { Findings = findings };

        var engine = CreateEngine();
        var profile = engine.CalculateRiskProfile(assessment);

        profile.RiskScore.Should().Be(65);
        profile.CriticalCount.Should().Be(2);
        profile.HighCount.Should().Be(3);
        profile.MediumCount.Should().Be(4);
        profile.LowCount.Should().Be(1);
    }

    [Theory]
    [InlineData(0, ComplianceRiskLevel.Low)]       // score < 20 => Low
    [InlineData(19.9, ComplianceRiskLevel.Low)]
    [InlineData(20, ComplianceRiskLevel.Medium)]    // score >= 20 => Medium
    [InlineData(49.9, ComplianceRiskLevel.Medium)]
    [InlineData(50, ComplianceRiskLevel.High)]      // score >= 50 => High
    [InlineData(99.9, ComplianceRiskLevel.High)]
    [InlineData(100, ComplianceRiskLevel.Critical)] // score >= 100 => Critical
    [InlineData(200, ComplianceRiskLevel.Critical)]
    public void CalculateRiskProfile_RiskLevelThresholds(double riskScore, ComplianceRiskLevel expectedLevel)
    {
        // Build findings to hit the target score using Critical (weight=10)
        var criticalCount = (int)Math.Floor(riskScore / 10.0);
        var remainingScore = riskScore - (criticalCount * 10.0);
        var findings = new List<ComplianceFinding>();

        for (int i = 0; i < criticalCount; i++)
            findings.Add(new ComplianceFinding { Severity = FindingSeverity.Critical, ControlFamily = "AC" });

        // Add medium findings (weight=5) for fractional part
        if (remainingScore >= 7.5)
            findings.Add(new ComplianceFinding { Severity = FindingSeverity.High, ControlFamily = "AU" });
        else if (remainingScore >= 5)
            findings.Add(new ComplianceFinding { Severity = FindingSeverity.Medium, ControlFamily = "SI" });
        else if (remainingScore >= 2.5)
            findings.Add(new ComplianceFinding { Severity = FindingSeverity.Low, ControlFamily = "CM" });

        var assessment = new ComplianceAssessment { Findings = findings };

        var engine = CreateEngine();
        var profile = engine.CalculateRiskProfile(assessment);

        profile.RiskLevel.Should().Be(expectedLevel);
    }

    [Fact]
    public void CalculateRiskProfile_TopRisks_OnlyFamiliesBelow70Percent()
    {
        var assessment = new ComplianceAssessment
        {
            Findings = new List<ComplianceFinding>
            {
                new() { Severity = FindingSeverity.High, ControlFamily = "AC" }
            },
            ControlFamilyResults = new List<ControlFamilyAssessment>
            {
                new() { FamilyCode = "AC", FamilyName = "Access Control", ComplianceScore = 40, Status = FamilyAssessmentStatus.Completed },
                new() { FamilyCode = "AU", FamilyName = "Audit", ComplianceScore = 60, Status = FamilyAssessmentStatus.Completed },
                new() { FamilyCode = "SI", FamilyName = "System Integrity", ComplianceScore = 85, Status = FamilyAssessmentStatus.Completed },
                new() { FamilyCode = "CM", FamilyName = "Config Mgmt", ComplianceScore = 55, Status = FamilyAssessmentStatus.Completed }
            }
        };

        var engine = CreateEngine();
        var profile = engine.CalculateRiskProfile(assessment);

        profile.TopRisks.Should().HaveCount(3); // AC(40), CM(55), AU(60) — SI at 85 excluded
        profile.TopRisks[0].FamilyCode.Should().Be("AC"); // lowest first
        profile.TopRisks[1].FamilyCode.Should().Be("CM");
        profile.TopRisks[2].FamilyCode.Should().Be("AU");
    }

    [Fact]
    public void CalculateRiskProfile_TopRisks_MaxFive()
    {
        var familyResults = new List<ControlFamilyAssessment>();
        var families = new[] { "AC", "AU", "SI", "CM", "IR", "IA", "CP" };
        for (int i = 0; i < families.Length; i++)
        {
            familyResults.Add(new ControlFamilyAssessment
            {
                FamilyCode = families[i],
                FamilyName = families[i],
                ComplianceScore = 10 + (i * 5), // All below 70%
                Status = FamilyAssessmentStatus.Completed
            });
        }

        var assessment = new ComplianceAssessment
        {
            Findings = new List<ComplianceFinding>
            {
                new() { Severity = FindingSeverity.Low, ControlFamily = "AC" }
            },
            ControlFamilyResults = familyResults
        };

        var engine = CreateEngine();
        var profile = engine.CalculateRiskProfile(assessment);

        profile.TopRisks.Should().HaveCount(5); // Max 5 even though 7 are below 70%
    }

    // ─── T088: PerformRiskAssessmentAsync Tests ────────────────────────────

    [Fact]
    public async Task PerformRiskAssessmentAsync_Returns8Categories()
    {
        SetupScannerRegistryForAllFamilies();
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCompletedAssessment("sub-1"));

        var engine = CreateEngine();
        var result = await engine.PerformRiskAssessmentAsync("sub-1");

        result.Categories.Should().HaveCount(8);
        result.SubscriptionId.Should().Be("sub-1");
        result.OverallScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PerformRiskAssessmentAsync_ScoresAre1To10()
    {
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCompletedAssessment("sub-1"));

        var engine = CreateEngine();
        var result = await engine.PerformRiskAssessmentAsync("sub-1");

        foreach (var cat in result.Categories)
        {
            cat.Score.Should().BeGreaterOrEqualTo(1);
            cat.Score.Should().BeLessOrEqualTo(10);
        }
    }

    [Fact]
    public async Task PerformRiskAssessmentAsync_OverallIsAverage()
    {
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCompletedAssessment("sub-1"));

        var engine = CreateEngine();
        var result = await engine.PerformRiskAssessmentAsync("sub-1");

        var expectedAvg = result.Categories.Average(c => c.Score);
        result.OverallScore.Should().BeApproximately(expectedAvg, 0.1);
    }

    [Fact]
    public async Task PerformRiskAssessmentAsync_NoAssessment_RunsNew()
    {
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAssessment?)null);
        SetupScannerRegistryForAllFamilies();

        var engine = CreateEngine();
        var result = await engine.PerformRiskAssessmentAsync("sub-1");

        result.Categories.Should().HaveCount(8);
        result.OverallScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PerformRiskAssessmentAsync_LowScoreCategories_HaveRecommendations()
    {
        var assessment = CreateCompletedAssessment("sub-1", complianceScore: 30);
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        var engine = CreateEngine();
        var result = await engine.PerformRiskAssessmentAsync("sub-1");

        // Categories with score < 5 should have mitigations
        var lowCategories = result.Categories.Where(c => c.Score < 5).ToList();
        foreach (var cat in lowCategories)
        {
            cat.Mitigations.Should().NotBeEmpty();
        }
        result.Recommendations.Should().NotBeEmpty();
    }

    // ─── T091: GenerateCertificateAsync Tests ──────────────────────────────

    [Fact]
    public async Task GenerateCertificateAsync_ScoreAbove80_IssuesCertificate()
    {
        var assessment = CreateCompletedAssessment("sub-1", complianceScore: 85);
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        var engine = CreateEngine();
        var cert = await engine.GenerateCertificateAsync("sub-1", "Admin");

        cert.Should().NotBeNull();
        cert.ComplianceScore.Should().Be(85);
        cert.IssuedBy.Should().Be("Admin");
        cert.Status.Should().Be(CertificateStatus.Active);
        cert.SubscriptionId.Should().Be("sub-1");
        cert.Framework.Should().Be("NIST80053");
    }

    [Fact]
    public async Task GenerateCertificateAsync_ScoreBelow80_ThrowsInvalidOperation()
    {
        var assessment = CreateCompletedAssessment("sub-1", complianceScore: 70);
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        var engine = CreateEngine();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.GenerateCertificateAsync("sub-1", "Admin"));
    }

    [Fact]
    public async Task GenerateCertificateAsync_SixMonthValidity()
    {
        var assessment = CreateCompletedAssessment("sub-1", complianceScore: 90);
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        var engine = CreateEngine();
        var cert = await engine.GenerateCertificateAsync("sub-1", "Admin");

        var expectedExpiry = cert.IssuedAt.AddDays(180);
        cert.ExpiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GenerateCertificateAsync_HasFamilyAttestations()
    {
        var assessment = CreateCompletedAssessment("sub-1", complianceScore: 85);
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        var engine = CreateEngine();
        var cert = await engine.GenerateCertificateAsync("sub-1", "Admin");

        cert.FamilyAttestations.Should().NotBeEmpty();
        cert.CoverageFamilies.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateCertificateAsync_HasVerificationHash()
    {
        var assessment = CreateCompletedAssessment("sub-1", complianceScore: 95);
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        var engine = CreateEngine();
        var cert = await engine.GenerateCertificateAsync("sub-1", "Admin");

        cert.VerificationHash.Should().NotBeNullOrEmpty();
        cert.VerificationHash.Length.Should().Be(64); // SHA-256 hex
    }

    [Fact]
    public async Task GenerateCertificateAsync_NoAssessment_ThrowsInvalidOperation()
    {
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ComplianceAssessment?)null);

        var engine = CreateEngine();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.GenerateCertificateAsync("sub-1", "Admin"));
    }

    // ─── T093: GetContinuousComplianceStatusAsync Tests ────────────────────

    [Fact]
    public async Task GetContinuousComplianceStatusAsync_WithMonitoring_ReturnsDriftAndAlerts()
    {
        var assessment = CreateCompletedAssessment("sub-1", complianceScore: 85);
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        _complianceWatchMock.Setup(x => x.GetMonitoringStatusAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoringConfiguration>
            {
                new() { IsEnabled = true, SubscriptionId = "sub-1" }
            });

        _complianceWatchMock.Setup(x => x.DetectDriftAsync("sub-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ComplianceAlert>
            {
                new() { ControlId = "AC-1", Severity = AlertSeverity.High }
            });

        _alertManagerMock.Setup(x => x.GetAlertsAsync("sub-1", null, null, null, null, 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ComplianceAlert>
            {
                new() { Status = AlertStatus.New },
                new() { Status = AlertStatus.Resolved }
            }, 2));

        _complianceWatchMock.Setup(x => x.GetAutoRemediationRulesAsync("sub-1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AutoRemediationRule>());

        var engine = CreateEngine();
        var status = await engine.GetContinuousComplianceStatusAsync("sub-1");

        status.SubscriptionId.Should().Be("sub-1");
        status.OverallScore.Should().Be(85);
        status.MonitoringEnabled.Should().BeTrue();
        status.DriftDetected.Should().BeTrue();
        status.ActiveAlerts.Should().Be(1); // Only non-resolved/dismissed
    }

    [Fact]
    public async Task GetContinuousComplianceStatusAsync_NoMonitoring_FallbackGracefully()
    {
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCompletedAssessment("sub-1"));

        _complianceWatchMock.Setup(x => x.GetMonitoringStatusAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoringConfiguration>());

        var engine = CreateEngine();
        var status = await engine.GetContinuousComplianceStatusAsync("sub-1");

        status.MonitoringEnabled.Should().BeFalse();
        status.DriftDetected.Should().BeFalse();
    }

    [Fact]
    public async Task GetContinuousComplianceStatusAsync_PerControlStatus()
    {
        var assessment = CreateCompletedAssessment("sub-1", complianceScore: 60);
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        _complianceWatchMock.Setup(x => x.GetMonitoringStatusAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoringConfiguration>());

        var engine = CreateEngine();
        var status = await engine.GetContinuousComplianceStatusAsync("sub-1");

        // Each finding should map to a control status entry
        status.ControlStatuses.Should().NotBeEmpty();
    }

    // ─── T094: GetComplianceTimelineAsync Tests ────────────────────────────

    [Fact]
    public async Task GetComplianceTimelineAsync_DailyDataPoints()
    {
        var now = DateTime.UtcNow;
        var assessments = new List<ComplianceAssessment>
        {
            new() { AssessedAt = now.AddDays(-2), ComplianceScore = 70, Findings = new List<ComplianceFinding>
            {
                new() { Severity = FindingSeverity.High },
                new() { Severity = FindingSeverity.Critical }
            }},
            new() { AssessedAt = now, ComplianceScore = 85, Findings = new List<ComplianceFinding>
            {
                new() { Severity = FindingSeverity.Low }
            }}
        };

        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);

        var engine = CreateEngine();
        var timeline = await engine.GetComplianceTimelineAsync("sub-1", now.AddDays(-3), now);

        timeline.SubscriptionId.Should().Be("sub-1");
        timeline.DataPoints.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetComplianceTimelineAsync_ScoreImprovement_EventDetected()
    {
        var now = DateTime.UtcNow;
        var assessments = new List<ComplianceAssessment>
        {
            new() { AssessedAt = now.AddDays(-1), ComplianceScore = 60, Findings = new() },
            new() { AssessedAt = now, ComplianceScore = 85, Findings = new() }
        };

        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);

        var engine = CreateEngine();
        var timeline = await engine.GetComplianceTimelineAsync("sub-1", now.AddDays(-2), now);

        timeline.SignificantEvents.Should().Contain(e => e.EventType == TimelineEventType.ScoreImprovement);
        timeline.Trend.Should().Be(TrendDirection.Improving);
    }

    [Fact]
    public async Task GetComplianceTimelineAsync_ScoreDegradation_EventDetected()
    {
        var now = DateTime.UtcNow;
        var assessments = new List<ComplianceAssessment>
        {
            new() { AssessedAt = now.AddDays(-1), ComplianceScore = 90, Findings = new() },
            new() { AssessedAt = now, ComplianceScore = 70, Findings = new() }
        };

        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);

        var engine = CreateEngine();
        var timeline = await engine.GetComplianceTimelineAsync("sub-1", now.AddDays(-2), now);

        timeline.SignificantEvents.Should().Contain(e => e.EventType == TimelineEventType.ScoreDegradation);
        timeline.Trend.Should().Be(TrendDirection.Degrading);
    }

    [Fact]
    public async Task GetComplianceTimelineAsync_FindingSpike_EventDetected()
    {
        var now = DateTime.UtcNow;
        var assessments = new List<ComplianceAssessment>
        {
            new() { AssessedAt = now.AddDays(-1), ComplianceScore = 80, Findings = new List<ComplianceFinding>
            {
                new() { Severity = FindingSeverity.Low }
            }},
            new() { AssessedAt = now, ComplianceScore = 75, Findings = Enumerable.Range(0, 8)
                .Select(_ => new ComplianceFinding { Severity = FindingSeverity.Medium }).ToList() }
        };

        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);

        var engine = CreateEngine();
        var timeline = await engine.GetComplianceTimelineAsync("sub-1", now.AddDays(-2), now);

        timeline.SignificantEvents.Should().Contain(e => e.EventType == TimelineEventType.FindingSpike);
    }

    [Fact]
    public async Task GetComplianceTimelineAsync_TrendCalculation_Stable()
    {
        var now = DateTime.UtcNow;
        var assessments = new List<ComplianceAssessment>
        {
            new() { AssessedAt = now.AddDays(-1), ComplianceScore = 80, Findings = new() },
            new() { AssessedAt = now, ComplianceScore = 82, Findings = new() }
        };

        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);

        var engine = CreateEngine();
        var timeline = await engine.GetComplianceTimelineAsync("sub-1", now.AddDays(-2), now);

        timeline.Trend.Should().Be(TrendDirection.Stable); // delta of 2 < 5
    }

    [Fact]
    public async Task GetComplianceTimelineAsync_GeneratesInsights()
    {
        var now = DateTime.UtcNow;
        var assessments = new List<ComplianceAssessment>
        {
            new() { AssessedAt = now.AddDays(-1), ComplianceScore = 60, Findings = new() },
            new() { AssessedAt = now, ComplianceScore = 80, Findings = new() }
        };

        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);

        var engine = CreateEngine();
        var timeline = await engine.GetComplianceTimelineAsync("sub-1", now.AddDays(-2), now);

        timeline.Insights.Should().NotBeEmpty();
    }

    // ─── T097: Data Access Method Tests ────────────────────────────────────

    [Fact]
    public async Task GetAssessmentHistoryAsync_ReturnsOrderedByDate()
    {
        // Seed DB directly
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Assessments.Add(new ComplianceAssessment { SubscriptionId = "sub-1", AssessedAt = DateTime.UtcNow.AddDays(-1) });
            db.Assessments.Add(new ComplianceAssessment { SubscriptionId = "sub-1", AssessedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var engine = CreateEngine();
        var results = await engine.GetAssessmentHistoryAsync("sub-1");

        results.Should().HaveCount(2);
        results[0].AssessedAt.Should().BeAfter(results[1].AssessedAt);
    }

    [Fact]
    public async Task GetFindingAsync_ReturnsFinding()
    {
        var findingId = Guid.NewGuid().ToString();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Findings.Add(new ComplianceFinding { Id = findingId, ControlId = "AC-1", ControlFamily = "AC" });
            await db.SaveChangesAsync();
        }

        var engine = CreateEngine();
        var result = await engine.GetFindingAsync(findingId);

        result.Should().NotBeNull();
        result!.ControlId.Should().Be("AC-1");
    }

    [Fact]
    public async Task UpdateFindingStatusAsync_DelegatesToPersistence()
    {
        _persistenceMock.Setup(x => x.UpdateFindingStatusAsync("f-1", FindingStatus.Remediated, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var engine = CreateEngine();
        var result = await engine.UpdateFindingStatusAsync("f-1", FindingStatus.Remediated);

        result.Should().BeTrue();
        _persistenceMock.Verify(x => x.UpdateFindingStatusAsync("f-1", FindingStatus.Remediated, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLatestAssessmentAsync_DelegatesToPersistence()
    {
        var assessment = CreateCompletedAssessment("sub-1");
        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessment);

        var engine = CreateEngine();
        var result = await engine.GetLatestAssessmentAsync("sub-1");

        result.Should().NotBeNull();
        result!.SubscriptionId.Should().Be("sub-1");
    }

    [Fact]
    public async Task SaveAssessmentAsync_PersistsToDb()
    {
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "sub-1",
            ComplianceScore = 90,
            Status = AssessmentStatus.Completed
        };

        var engine = CreateEngine();
        await engine.SaveAssessmentAsync(assessment);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var persisted = await db.Assessments.FindAsync(assessment.Id);
        persisted.Should().NotBeNull();
        persisted!.ComplianceScore.Should().Be(90);
    }

    [Fact]
    public async Task GetAuditLogAsync_ReturnsFormattedLog()
    {
        var assessments = new List<ComplianceAssessment>
        {
            new() { SubscriptionId = "sub-1", AssessedAt = DateTime.UtcNow, Status = AssessmentStatus.Completed, ComplianceScore = 85, TotalControls = 100 }
        };

        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assessments);

        var engine = CreateEngine();
        var log = await engine.GetAuditLogAsync("sub-1");

        log.Should().Contain("Audit Log");
        log.Should().Contain("85.0%");
        log.Should().Contain("Completed");
    }

    [Fact]
    public async Task GetAuditLogAsync_NoAssessments_ReturnsEmptyMessage()
    {
        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ComplianceAssessment>());

        var engine = CreateEngine();
        var log = await engine.GetAuditLogAsync("sub-1");

        log.Should().Contain("No assessments found");
    }

    // ─── T104: End-to-End Cancellation Tests ───────────────────────────────

    [Fact]
    public void CalculateRiskProfile_IsSync_NoCancellationNeeded()
    {
        var assessment = CreateCompletedAssessment("sub-1");
        var engine = CreateEngine();

        // CalculateRiskProfile is synchronous — just verifying it doesn't crash
        var profile = engine.CalculateRiskProfile(assessment);
        profile.Should().NotBeNull();
    }

    [Fact]
    public async Task PerformRiskAssessmentAsync_Cancelled_Throws()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var engine = CreateEngine();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.PerformRiskAssessmentAsync("sub-1", cts.Token));
    }

    [Fact]
    public async Task GenerateCertificateAsync_Cancelled_Throws()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var engine = CreateEngine();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.GenerateCertificateAsync("sub-1", "Admin", cts.Token));
    }

    [Fact]
    public async Task GetContinuousComplianceStatusAsync_Cancelled_Throws()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _persistenceMock.Setup(x => x.GetLatestAssessmentAsync("sub-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var engine = CreateEngine();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.GetContinuousComplianceStatusAsync("sub-1", cts.Token));
    }

    [Fact]
    public async Task GetComplianceTimelineAsync_Cancelled_Throws()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var engine = CreateEngine();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.GetComplianceTimelineAsync("sub-1", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, cts.Token));
    }

    [Fact]
    public async Task UpdateFindingStatusAsync_Cancelled_Throws()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _persistenceMock.Setup(x => x.UpdateFindingStatusAsync("f-1", FindingStatus.Remediated, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var engine = CreateEngine();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.UpdateFindingStatusAsync("f-1", FindingStatus.Remediated, cts.Token));
    }

    // ─── T105: Persistence Failure Behavior Tests ──────────────────────────

    [Fact]
    public async Task RunComprehensiveAssessmentAsync_PersistenceFails_PropagatesFailure()
    {
        // Arrange
        SetupScannerRegistryForAllFamilies();

        _persistenceMock.Setup(x => x.SaveAssessmentAsync(It.IsAny<ComplianceAssessment>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB is down"));

        var engine = CreateEngine();
        // Act
        var run = () => engine.RunComprehensiveAssessmentAsync("sub-1");
        // Assert
        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB is down");
    }

    [Fact]
    public async Task GetAuditLogAsync_PersistenceFails_ThrowsPropagated()
    {
        _persistenceMock.Setup(x => x.GetAssessmentHistoryAsync("sub-1", 7, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB is down"));

        var engine = CreateEngine();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.GetAuditLogAsync("sub-1"));
    }

    // ─── Test Helpers ──────────────────────────────────────────────────────────

    private void SetupNistForFamily(string family, int controlCount)
    {
        var controls = Enumerable.Range(1, controlCount)
            .Select(i => new NistControl { Id = $"{family}-{i}", Family = family, Title = $"Control {i}" })
            .ToList();

        _nistMock.Setup(x => x.GetControlFamilyAsync(family, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(controls);
    }

    private void SetupNistForAllFamilies(int controlsPerFamily = 5)
    {
        foreach (var family in Ato.Copilot.Core.Constants.ControlFamilies.AllFamilies)
        {
            SetupNistForFamily(family, controlsPerFamily);
        }
    }

    private static Mock<IComplianceScanner> CreateMockScanner(string familyCode, int totalControls = 5, int failedControls = 0)
    {
        var scanner = new Mock<IComplianceScanner>();
        scanner.Setup(x => x.FamilyCode).Returns(familyCode);
        scanner.Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<NistControl>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sub, string? rg, IEnumerable<NistControl> controls, CancellationToken ct) =>
            {
                var controlList = controls.ToList();
                var tc = controlList.Count > 0 ? controlList.Count : totalControls;
                var fc = Math.Min(failedControls, tc);
                var findings = Enumerable.Range(0, fc)
                    .Select(i => new ComplianceFinding
                    {
                        ControlId = controlList.Count > i ? controlList[i].Id : $"{familyCode}-{i + 1}",
                        ControlFamily = controlList.Count > i ? controlList[i].Family : familyCode,
                        Title = $"Finding {i + 1}",
                        Severity = FindingSeverity.Medium
                    }).ToList();

                return new ControlFamilyAssessment
                {
                    FamilyCode = controlList.FirstOrDefault()?.Family ?? familyCode,
                    FamilyName = controlList.FirstOrDefault()?.Family ?? familyCode,
                    TotalControls = tc,
                    PassedControls = tc - fc,
                    FailedControls = fc,
                    ComplianceScore = tc > 0 ? (double)(tc - fc) / tc * 100 : 100,
                    Status = FamilyAssessmentStatus.Completed,
                    Findings = findings,
                    ScannerName = "MockScanner"
                };
            });
        return scanner;
    }

    private void SetupScannerRegistryForAllFamilies(int passedControls = 5, int totalControls = 5)
    {
        var failedControls = totalControls - passedControls;
        var defaultScanner = CreateMockScanner("DEFAULT", totalControls, failedControls);

        _scannerRegistryMock.Setup(x => x.GetScanner(It.IsAny<string>()))
            .Returns(defaultScanner.Object);

        SetupNistForAllFamilies(totalControls);
    }

    private static ComplianceAssessment CreateCompletedAssessment(
        string subscriptionId, double complianceScore = 80)
    {
        var families = Ato.Copilot.Core.Constants.ControlFamilies.AllFamilies.ToList();
        var familyResults = families.Select(f => new ControlFamilyAssessment
        {
            FamilyCode = f,
            FamilyName = Ato.Copilot.Core.Constants.ControlFamilies.FamilyNames.TryGetValue(f, out var n) ? n : f,
            TotalControls = 10,
            PassedControls = (int)(10 * complianceScore / 100),
            FailedControls = 10 - (int)(10 * complianceScore / 100),
            ComplianceScore = complianceScore,
            Status = FamilyAssessmentStatus.Completed
        }).ToList();

        return new ComplianceAssessment
        {
            SubscriptionId = subscriptionId,
            ComplianceScore = complianceScore,
            Status = AssessmentStatus.Completed,
            TotalControls = familyResults.Sum(f => f.TotalControls),
            PassedControls = familyResults.Sum(f => f.PassedControls),
            FailedControls = familyResults.Sum(f => f.FailedControls),
            ControlFamilyResults = familyResults,
            Findings = familyResults
                .Where(f => f.FailedControls > 0)
                .SelectMany(f => Enumerable.Range(0, f.FailedControls).Select(i => new ComplianceFinding
                {
                    ControlId = $"{f.FamilyCode}-{i + 1}",
                    ControlFamily = f.FamilyCode,
                    Title = $"Finding {f.FamilyCode}-{i + 1}",
                    Severity = i == 0 ? FindingSeverity.High : FindingSeverity.Medium
                }))
                .ToList()
        };
    }

    // ─── Helper: InMemory DbContextFactory ──────────────────────────────────

    private class InMemoryDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;

        public InMemoryDbContextFactory(DbContextOptions<AtoCopilotContext> options)
        {
            _options = options;
        }

        public AtoCopilotContext CreateDbContext() => new(_options);

        public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
