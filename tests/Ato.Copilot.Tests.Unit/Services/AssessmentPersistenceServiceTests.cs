using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Tests for <see cref="AssessmentPersistenceService"/>: upsert, get by ID,
/// history, finding status update with cache behavior.
/// Uses EF Core InMemory provider.
/// </summary>
public class AssessmentPersistenceServiceTests : IDisposable
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly AssessmentPersistenceService _service;
    private readonly string _dbName;

    public AssessmentPersistenceServiceTests()
    {
        _dbName = $"test-db-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(options));

        _dbFactory = factory.Object;
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Seed NistControls so ControlId normalization doesn't null them out
        using (var seedContext = new AtoCopilotContext(options))
        {
            seedContext.NistControls.AddRange(
                new NistControl { Id = "ac-1", Family = "AC", Title = "Access Control Policy" },
                new NistControl { Id = "ia-1", Family = "IA", Title = "Identification and Authentication Policy" },
                new NistControl { Id = "sc-1", Family = "SC", Title = "System and Communications Protection Policy" });
            seedContext.SaveChanges();
        }

        _service = new AssessmentPersistenceService(
            _dbFactory,
            _cache,
            Mock.Of<ILogger<AssessmentPersistenceService>>());
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ComplianceAssessment CreateAssessment(
        string id = "test-assessment-1",
        string subscriptionId = "sub-001",
        double score = 85.0)
    {
        return new ComplianceAssessment
        {
            Id = id,
            SubscriptionId = subscriptionId,
            ComplianceScore = score,
            Framework = "NIST80053",
            AssessedAt = DateTime.UtcNow,
            Status = AssessmentStatus.Completed,
            Findings = new List<ComplianceFinding>
            {
                new()
                {
                    Id = $"{id}-finding-1",
                    AssessmentId = id,
                    ControlId = "AC-1",
                    ControlTitle = "Access Control Policy",
                    Severity = FindingSeverity.High,
                    Status = FindingStatus.Open,
                    RemediationTrackingStatus = RemediationTrackingStatus.NotStarted
                }
            }
        };
    }

    // ─── SaveAssessment (Insert) ────────────────────────────────────────

    [Fact]
    public async Task SaveAssessmentAsync_Insert_SavesNewAssessment()
    {
        var assessment = CreateAssessment();

        await _service.SaveAssessmentAsync(assessment);

        var result = await _service.GetAssessmentAsync(assessment.Id);
        result.Should().NotBeNull();
        result!.ComplianceScore.Should().Be(85.0);
    }

    [Fact]
    public async Task SaveAssessmentAsync_Insert_SavesFindings()
    {
        var assessment = CreateAssessment();

        await _service.SaveAssessmentAsync(assessment);

        var result = await _service.GetAssessmentAsync(assessment.Id);
        result!.Findings.Should().HaveCount(1);
        result.Findings[0].ControlId.Should().Be("ac-1");
    }

    [Fact]
    public async Task SaveAssessmentAsync_Insert_CachesLatest()
    {
        var assessment = CreateAssessment();

        await _service.SaveAssessmentAsync(assessment);

        // Cache should contain the assessment
        var cacheKey = $"latest-assessment:{assessment.SubscriptionId}";
        _cache.TryGetValue<ComplianceAssessment>(cacheKey, out var cached).Should().BeTrue();
        cached!.Id.Should().Be(assessment.Id);
    }

    // ─── SaveAssessment (Upsert) ────────────────────────────────────────

    [Fact]
    public async Task SaveAssessmentAsync_Upsert_UpdatesExistingAssessment()
    {
        var assessment = CreateAssessment();
        await _service.SaveAssessmentAsync(assessment);

        // Update score
        assessment.ComplianceScore = 95.0;
        await _service.SaveAssessmentAsync(assessment);

        var result = await _service.GetAssessmentAsync(assessment.Id);
        result!.ComplianceScore.Should().Be(95.0);
    }

    [Fact]
    public async Task SaveAssessmentAsync_Upsert_RetainsExistingFindingsAndAddsNewObservations()
    {
        // Arrange
        var assessment = CreateAssessment();
        var retainedFinding = assessment.Findings.Single();
        retainedFinding.ImportRecordId = "retained-import";
        retainedFinding.DeviationId = "retained-deviation";
        retainedFinding.RemediationTrackingStatus = RemediationTrackingStatus.InProgress;
        await _service.SaveAssessmentAsync(assessment);

        assessment.Findings = new List<ComplianceFinding>
        {
            new()
            {
                Id = "new-finding-1",
                AssessmentId = assessment.Id,
                ControlId = "IA-1",
                Severity = FindingSeverity.Medium,
                Status = FindingStatus.Open
            },
            new()
            {
                Id = "new-finding-2",
                AssessmentId = assessment.Id,
                ControlId = "SC-1",
                Severity = FindingSeverity.Low,
                Status = FindingStatus.Open
            }
        };

        // Act
        await _service.SaveAssessmentAsync(assessment);

        // Assert
        var result = await _service.GetAssessmentAsync(assessment.Id);
        result.Should().NotBeNull();
        result!.Findings.Should().HaveCount(3);
        result.Findings.Select(f => f.Id).Should().BeEquivalentTo(
            retainedFinding.Id, "new-finding-1", "new-finding-2");
        result.Findings.Select(f => f.ControlId).Should().BeEquivalentTo("ac-1", "ia-1", "sc-1");
        result.Findings.Single(f => f.Id == retainedFinding.Id).Should().BeEquivalentTo(retainedFinding);
    }

    [Fact]
    public async Task SaveAssessmentAsync_Upsert_UpdatesMatchingFindingWithoutDuplicatingIdentity()
    {
        // Arrange
        var assessment = CreateAssessment();
        var finding = assessment.Findings.Single();
        finding.ImportRecordId = "retained-import";
        finding.DeviationId = "retained-deviation";
        await _service.SaveAssessmentAsync(assessment);
        assessment.Findings = new List<ComplianceFinding>
        {
            new()
            {
                Id = finding.Id,
                AssessmentId = assessment.Id,
                ControlId = "AC-1",
                Severity = FindingSeverity.Low,
                Status = FindingStatus.Remediated,
                ImportRecordId = finding.ImportRecordId,
                DeviationId = finding.DeviationId
            }
        };

        // Act
        await _service.SaveAssessmentAsync(assessment);

        // Assert
        var result = await _service.GetAssessmentAsync(assessment.Id);
        result.Should().NotBeNull();
        var updated = result!.Findings.Should().ContainSingle().Which;
        updated.Id.Should().Be(finding.Id);
        updated.ControlId.Should().Be("ac-1");
        updated.Severity.Should().Be(FindingSeverity.Low);
        updated.Status.Should().Be(FindingStatus.Remediated);
        updated.ImportRecordId.Should().Be("retained-import");
        updated.DeviationId.Should().Be("retained-deviation");
    }

    // ─── GetAssessmentAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetAssessmentAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetAssessmentAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAssessmentAsync_IncludesFindings()
    {
        var assessment = CreateAssessment();
        await _service.SaveAssessmentAsync(assessment);

        var result = await _service.GetAssessmentAsync(assessment.Id);
        result!.Findings.Should().NotBeEmpty();
    }

    // ─── GetLatestAssessmentAsync ───────────────────────────────────────

    [Fact]
    public async Task GetLatestAssessmentAsync_ReturnsNull_WhenNoAssessments()
    {
        var result = await _service.GetLatestAssessmentAsync("nonexistent-sub");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAssessmentAsync_ReturnsMostRecent()
    {
        var older = CreateAssessment("old-1", "sub-001", 70.0);
        older.AssessedAt = DateTime.UtcNow.AddDays(-2);
        await _service.SaveAssessmentAsync(older);

        // Clear cache to force DB read
        _cache.Remove("latest-assessment:sub-001");

        var newer = CreateAssessment("new-1", "sub-001", 90.0);
        newer.AssessedAt = DateTime.UtcNow;
        newer.Findings = new List<ComplianceFinding>
        {
            new()
            {
                Id = "new-1-finding-1",
                AssessmentId = "new-1",
                ControlId = "AC-2",
                Severity = FindingSeverity.Low,
                Status = FindingStatus.Open
            }
        };
        await _service.SaveAssessmentAsync(newer);

        // Clear cache to test fresh DB lookup
        _cache.Remove("latest-assessment:sub-001");

        var result = await _service.GetLatestAssessmentAsync("sub-001");
        result.Should().NotBeNull();
        result!.Id.Should().Be("new-1");
        result.ComplianceScore.Should().Be(90.0);
    }

    [Fact]
    public async Task GetLatestAssessmentAsync_UsesCache()
    {
        var assessment = CreateAssessment();
        await _service.SaveAssessmentAsync(assessment);

        // First call populates cache; second should hit cache
        var first = await _service.GetLatestAssessmentAsync("sub-001");
        var second = await _service.GetLatestAssessmentAsync("sub-001");

        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    // ─── GetAssessmentHistoryAsync ──────────────────────────────────────

    [Fact]
    public async Task GetAssessmentHistoryAsync_ReturnsEmpty_WhenNone()
    {
        var result = await _service.GetAssessmentHistoryAsync("nonexistent-sub", 30);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssessmentHistoryAsync_FiltersOlderThanDays()
    {
        var recent = CreateAssessment("recent", "sub-001", 85.0);
        recent.AssessedAt = DateTime.UtcNow.AddDays(-5);
        await _service.SaveAssessmentAsync(recent);

        var old = CreateAssessment("old", "sub-001", 70.0);
        old.AssessedAt = DateTime.UtcNow.AddDays(-60);
        old.Findings = new List<ComplianceFinding>
        {
            new()
            {
                Id = "old-finding",
                AssessmentId = "old",
                ControlId = "AU-1",
Severity = FindingSeverity.Low,
                Status = FindingStatus.Open
            }
        };
        await _service.SaveAssessmentAsync(old);

        var result = await _service.GetAssessmentHistoryAsync("sub-001", 30);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("recent");
    }

    [Fact]
    public async Task GetAssessmentHistoryAsync_OrdersByNewestFirst()
    {
        for (int i = 0; i < 3; i++)
        {
            var a = CreateAssessment($"a-{i}", "sub-001", 80 + i);
            a.AssessedAt = DateTime.UtcNow.AddDays(-i);
            a.Findings = new List<ComplianceFinding>
            {
                new()
                {
                    Id = $"a-{i}-f",
                    AssessmentId = $"a-{i}",
                    ControlId = "CM-1",
                Severity = FindingSeverity.Low,
                    Status = FindingStatus.Open
                }
            };
            await _service.SaveAssessmentAsync(a);
        }

        var result = await _service.GetAssessmentHistoryAsync("sub-001", 30);

        result.Should().HaveCount(3);
        result[0].Id.Should().Be("a-0"); // most recent
    }

    // ─── GetFindingAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetFindingAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetFindingAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFindingAsync_ReturnsFinding_WhenExists()
    {
        var assessment = CreateAssessment();
        await _service.SaveAssessmentAsync(assessment);

        var result = await _service.GetFindingAsync($"{assessment.Id}-finding-1");
        result.Should().NotBeNull();
        result!.ControlId.Should().Be("ac-1");
    }

    // ─── UpdateFindingStatusAsync ───────────────────────────────────────

    [Fact]
    public async Task UpdateFindingStatusAsync_ReturnsFalse_WhenNotFound()
    {
        var result = await _service.UpdateFindingStatusAsync("nonexistent", FindingStatus.Remediated);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateFindingStatusAsync_UpdatesStatus()
    {
        var assessment = CreateAssessment();
        await _service.SaveAssessmentAsync(assessment);

        var result = await _service.UpdateFindingStatusAsync(
            $"{assessment.Id}-finding-1", FindingStatus.InProgress);

        result.Should().BeTrue();

        var finding = await _service.GetFindingAsync($"{assessment.Id}-finding-1");
        finding!.Status.Should().Be(FindingStatus.InProgress);
    }

    [Fact]
    public async Task UpdateFindingStatusAsync_Remediated_SetsTrackingCompleted()
    {
        var assessment = CreateAssessment();
        await _service.SaveAssessmentAsync(assessment);

        await _service.UpdateFindingStatusAsync(
            $"{assessment.Id}-finding-1", FindingStatus.Remediated);

        var finding = await _service.GetFindingAsync($"{assessment.Id}-finding-1");
        finding!.RemediationTrackingStatus.Should().Be(RemediationTrackingStatus.Completed);
        finding.RemediatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateFindingStatusAsync_InProgress_SetsTrackingInProgress()
    {
        var assessment = CreateAssessment();
        await _service.SaveAssessmentAsync(assessment);

        await _service.UpdateFindingStatusAsync(
            $"{assessment.Id}-finding-1", FindingStatus.InProgress);

        var finding = await _service.GetFindingAsync($"{assessment.Id}-finding-1");
        finding!.RemediationTrackingStatus.Should().Be(RemediationTrackingStatus.InProgress);
    }

    // ─── Contract Compliance ────────────────────────────────────────────

    [Fact]
    public void AssessmentPersistenceService_ImplementsInterface()
    {
        typeof(AssessmentPersistenceService)
            .Should().Implement<IAssessmentPersistenceService>();
    }
}
