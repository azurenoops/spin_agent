// =============================================================================
//  AtoPostureServiceTests.cs
//  Ato.Copilot.Tests.Unit — Services
//  Issue #422 — AO Posture API (W10 cATO Gap Closure)
//
//  xUnit + Moq + FluentAssertions
//  Tests: cache hit vs miss, forceRefresh 403, role-gated field nulling,
//         cATO 4-criterion evaluation, Pillar3 Unknown (Phase 1).
// =============================================================================

using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="AtoPostureService"/>.
/// Uses InMemoryDatabase for data setup; IMemoryCache backed by
/// <see cref="MemoryCache"/> with no eviction to control hit/miss deterministically.
/// </summary>
public sealed class AtoPostureServiceTests : IDisposable
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly MemoryCache _cache;
    private readonly AtoPostureService _sut;

    private static readonly Guid SystemId = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("ffffffff-0000-0000-0000-000000000001");

    private static readonly IReadOnlySet<string> ViewerRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ISSO" };

    private static readonly IReadOnlySet<string> IssmRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ISSM" };

    private static readonly IReadOnlySet<string> AoRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AuthorizingOfficial" };

    public AtoPostureServiceTests()
    {
        // InMemory DB — unique name per test class instance
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"AtoPostureTests-{Guid.NewGuid()}")
            .Options;

        _dbFactory = new SimpleDbContextFactory(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _sut = new AtoPostureService(_dbFactory, _cache, NullLogger<AtoPostureService>.Instance);

        SeedSystem();
    }

    // ─── GetPostureAsync — basic scenarios ──────────────────────────────────

    [Fact]
    public async Task GetPostureAsync_SystemExists_ReturnsPosture()
    {
        var posture = await _sut.GetPostureAsync(
            SystemId, ViewerRoles, forceRefresh: false);

        posture.Should().NotBeNull();
        posture!.SystemId.Should().Be(SystemId);
        posture.SystemName.Should().Be("Test System Alpha");
    }

    [Fact]
    public async Task GetPostureAsync_SystemNotFound_ReturnsNull()
    {
        var missingId = Guid.NewGuid();

        var posture = await _sut.GetPostureAsync(
            missingId, ViewerRoles, forceRefresh: false);

        posture.Should().BeNull();
    }

    [Fact]
    public async Task GetPostureAsync_CacheMiss_ServedFromCacheIsFalse()
    {
        var posture = await _sut.GetPostureAsync(
            SystemId, ViewerRoles, forceRefresh: false);

        posture!.ServedFromCache.Should().BeFalse(
            "first call is always a cache miss");
    }

    [Fact]
    public async Task GetPostureAsync_SecondCall_ServedFromCacheIsTrue()
    {
        // First call — populates cache
        _ = await _sut.GetPostureAsync(SystemId, ViewerRoles, forceRefresh: false);

        // Second call — cache hit
        var posture = await _sut.GetPostureAsync(SystemId, ViewerRoles, forceRefresh: false);

        posture!.ServedFromCache.Should().BeTrue(
            "second call must be served from the 5-minute IMemoryCache");
    }

    [Fact]
    public async Task GetPostureAsync_ForceRefresh_BypassesCache()
    {
        // Seed cache manually
        _ = await _sut.GetPostureAsync(SystemId, IssmRoles, forceRefresh: false);

        // Force refresh — must recompute
        var posture = await _sut.GetPostureAsync(
            SystemId, IssmRoles, forceRefresh: true);

        posture!.ServedFromCache.Should().BeFalse(
            "forceRefresh bypasses the cache and recomputes from DB");
    }

    // ─── Role-gated field tests ──────────────────────────────────────────────

    [Fact]
    public async Task GetPostureAsync_ViewerRole_AuthorizingOfficialIsNull()
    {
        SeedAuthorizationDecision("Rear Admiral J. Smith");

        var posture = await _sut.GetPostureAsync(
            SystemId, ViewerRoles, forceRefresh: false);

        posture!.AuthorizationStatus.AuthorizingOfficial.Should().BeNull(
            "AuthorizingOfficial is role-gated to AuthorizingOfficial role");
    }

    [Fact]
    public async Task GetPostureAsync_AoRole_AuthorizingOfficialIsPopulated()
    {
        SeedAuthorizationDecision("Rear Admiral J. Smith");

        var posture = await _sut.GetPostureAsync(
            SystemId, AoRoles, forceRefresh: false);

        posture!.AuthorizationStatus.AuthorizingOfficial.Should().Be(
            "Rear Admiral J. Smith",
            "AO role callers should see the authorizing official name");
    }

    [Fact]
    public async Task GetPostureAsync_ViewerRole_CsrmcPillarStatusIsNull()
    {
        var posture = await _sut.GetPostureAsync(
            SystemId, ViewerRoles, forceRefresh: false);

        posture!.CsrmcPillarStatus.Should().BeNull(
            "CsrmcPillarStatus is AO role-gated");
    }

    [Fact]
    public async Task GetPostureAsync_AoRole_CsrmcPillarStatusIsPopulated()
    {
        var posture = await _sut.GetPostureAsync(
            SystemId, AoRoles, forceRefresh: false);

        posture!.CsrmcPillarStatus.Should().NotBeNull(
            "AO role callers should receive CSRMC pillar status");
    }

    // ─── forceRefresh authorization ─────────────────────────────────────────

    [Fact]
    public async Task GetPostureAsync_ViewerRole_ForceRefresh_ThrowsForbiddenException()
    {
        var act = async () => await _sut.GetPostureAsync(
            SystemId, ViewerRoles, forceRefresh: true);

        await act.Should().ThrowAsync<ForbiddenException>(
            "Viewer/ISSO callers cannot force cache bypass");
    }

    [Fact]
    public async Task GetPostureAsync_IssmRole_ForceRefresh_Succeeds()
    {
        // Should not throw
        var act = async () => await _sut.GetPostureAsync(
            SystemId, IssmRoles, forceRefresh: true);

        await act.Should().NotThrowAsync();
    }

    // ─── EvaluateCatoEligibilityAsync ────────────────────────────────────────

    [Fact]
    public async Task EvaluateCatoEligibilityAsync_AllCriteriaMet_IsEligibleTrue()
    {
        // No open CatI, no overdue POAMs, ConMon enabled
        SeedConMonPlan();

        var result = await _sut.EvaluateCatoEligibilityAsync(SystemId);

        result.HasZeroCatIFindings.Should().BeTrue();
        result.HasZeroOverduePOAMs.Should().BeTrue();
        result.IsConMonEnabled.Should().BeTrue();
        // Pillar 3 = Unknown in Phase 1 → adds an ineligibility reason → IsEligible=false
        result.IsEligible.Should().BeFalse(
            "Pillar 3 is Unknown in Phase 1 — cATO eligibility requires all 4 criteria");
        result.IneligibilityReasons.Should().ContainMatch("*Pillar 3*",
            "Pillar 3 gap should be reported in Phase 1");
    }

    [Fact]
    public async Task EvaluateCatoEligibilityAsync_OpenCatIFinding_NotEligible()
    {
        SeedOpenCatIFinding();

        var result = await _sut.EvaluateCatoEligibilityAsync(SystemId);

        result.HasZeroCatIFindings.Should().BeFalse();
        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().ContainMatch("*CAT I*");
    }

    [Fact]
    public async Task EvaluateCatoEligibilityAsync_OverduePoam_NotEligible()
    {
        SeedOverduePoamItem();

        var result = await _sut.EvaluateCatoEligibilityAsync(SystemId);

        result.HasZeroOverduePOAMs.Should().BeFalse();
        result.IsEligible.Should().BeFalse();
        result.IneligibilityReasons.Should().ContainMatch("*overdue*");
    }

    [Fact]
    public async Task EvaluateCatoEligibilityAsync_NoConMonPlan_NotEligible()
    {
        var result = await _sut.EvaluateCatoEligibilityAsync(SystemId);

        result.IsConMonEnabled.Should().BeFalse();
        result.IneligibilityReasons.Should().ContainMatch("*Continuous Monitoring*");
    }

    [Fact]
    public async Task EvaluateCatoEligibilityAsync_MissingSystem_ThrowsSystemNotFoundException()
    {
        var act = async () => await _sut.EvaluateCatoEligibilityAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<SystemNotFoundException>();
    }

    // ─── GetPillar3StatusAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetPillar3StatusAsync_Phase1_ReturnsUnknown()
    {
        // Phase 1 returns Unknown — no pipeline entity yet
        var status = await _sut.GetPillar3StatusAsync(SystemId);

        status.Should().Be(PillarComplianceStatus.Unknown,
            "Phase 1 always returns Unknown — pipeline ingestion DB added in Phase 2");
    }

    [Fact]
    public async Task GetPillar3StatusAsync_MissingSystem_ThrowsSystemNotFoundException()
    {
        var act = async () => await _sut.GetPillar3StatusAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<SystemNotFoundException>();
    }

    // ─── Findings summary ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPostureAsync_WithOpenFindings_FindingsSummaryIsCorrect()
    {
        SeedFindings(catI: 2, catII: 5, catIII: 3);

        var posture = await _sut.GetPostureAsync(
            SystemId, ViewerRoles, forceRefresh: false);

        posture!.FindingsSummary.CatI.Should().Be(2);
        posture.FindingsSummary.CatII.Should().Be(5);
        posture.FindingsSummary.CatIII.Should().Be(3);
        posture.FindingsSummary.Total.Should().Be(10);
    }

    // ─── Seed helpers ─────────────────────────────────────────────────────────

    private void SeedSystem()
    {
        using var db = _dbFactory.CreateDbContext();
        db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id       = SystemId.ToString(),
            TenantId = TenantId,
            Name     = "Test System Alpha",
            Acronym  = "TSA",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            CurrentRmfStep = RmfPhase.Authorize,
            IsActive = true,
            CreatedBy = "unit-test",
        });
        db.SaveChanges();
    }

    private void SeedAuthorizationDecision(string issuedByName)
    {
        using var db = _dbFactory.CreateDbContext();
        db.AuthorizationDecisions.Add(new AuthorizationDecision
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = TenantId,
            RegisteredSystemId = SystemId.ToString(),
            DecisionType = AuthorizationDecisionType.Ato,
            DecisionDate = DateTime.UtcNow.AddDays(-30),
            ExpirationDate = DateTime.UtcNow.AddYears(3),
            IsActive = true,
            IssuedBy = "test-ao-user",
            IssuedByName = issuedByName,
            ResidualRiskLevel = ComplianceRiskLevel.Low,
            ComplianceScoreAtDecision = 94.5,
        });
        db.SaveChanges();
    }

    private void SeedConMonPlan()
    {
        using var db = _dbFactory.CreateDbContext();
        db.ConMonPlans.Add(new ConMonPlan
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = TenantId,
            RegisteredSystemId = SystemId.ToString(),
            AssessmentFrequency = "Monthly",
            AnnualReviewDate = DateTime.UtcNow.AddMonths(6),
            CreatedBy = "unit-test",
        });
        db.SaveChanges();
    }


    private void SeedCompletedAssessment(string assessmentId = "test-assessment-findings")
    {
        using var db = _dbFactory.CreateDbContext();
        db.Assessments.Add(new ComplianceAssessment
        {
            Id = assessmentId,
            TenantId = TenantId,
            RegisteredSystemId = SystemId.ToString(),
            AssessmentType = "Internal",
            Status = AssessmentStatus.Completed,
            CompletedAt = DateTime.UtcNow.AddDays(-1),
        });
        db.SaveChanges();
    }

    private void SeedOpenCatIFinding()
    {
        SeedCompletedAssessment("test-assessment-1");
        using var db = _dbFactory.CreateDbContext();
        db.Findings.Add(new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = TenantId,
            ControlId = "AC-2",
            Title = "Critical Finding",
            Severity = FindingSeverity.Critical,
            Status = FindingStatus.Open,
            CatSeverity = CatSeverity.CatI,
            AssessmentId = "test-assessment-1",
        });
        db.SaveChanges();
    }

    private void SeedOverduePoamItem()
    {
        using var db = _dbFactory.CreateDbContext();
        db.PoamItems.Add(new PoamItem
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = TenantId,
            RegisteredSystemId = SystemId.ToString(),
            Weakness = "Test weakness",
            WeaknessSource = "SARIF-CI",
            SecurityControlNumber = "SI-2",
            CatSeverity = CatSeverity.CatII,
            PointOfContact = "test-poc@example.com",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(-10), // overdue
            Status = PoamStatus.Delayed,
        });
        db.SaveChanges();
    }

    private void SeedFindings(int catI, int catII, int catIII)
    {
        var assessmentId = "test-assessment-findings";
        SeedCompletedAssessment(assessmentId);
        using var db = _dbFactory.CreateDbContext();
        var findings = new List<ComplianceFinding>();

        for (var i = 0; i < catI; i++)
            findings.Add(MakeFinding($"AC-{i}", CatSeverity.CatI, FindingStatus.Open, assessmentId));
        for (var i = 0; i < catII; i++)
            findings.Add(MakeFinding($"SC-{i}", CatSeverity.CatII, FindingStatus.Open, assessmentId));
        for (var i = 0; i < catIII; i++)
            findings.Add(MakeFinding($"CM-{i}", CatSeverity.CatIII, FindingStatus.Open, assessmentId));

        db.Findings.AddRange(findings);
        db.SaveChanges();
    }

    private ComplianceFinding MakeFinding(
        string controlId, CatSeverity cat, FindingStatus status, string assessmentId) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = TenantId,
            ControlId = controlId,
            Title = $"Finding {controlId}",
            Severity = FindingSeverity.High,
            Status = status,
            CatSeverity = cat,
            AssessmentId = assessmentId,
        };

    public void Dispose()
    {
        _cache.Dispose();
        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    // ─── Mini IDbContextFactory ──────────────────────────────────────────────

    private sealed class SimpleDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public SimpleDbContextFactory(DbContextOptions<AtoCopilotContext> options)
            => _options = options;

        public AtoCopilotContext CreateDbContext() => new(_options);
        public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());
    }
}
