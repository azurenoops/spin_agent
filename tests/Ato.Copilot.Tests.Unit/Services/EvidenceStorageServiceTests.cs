using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

public class EvidenceStorageServiceTests : IDisposable
{
    private readonly Mock<IAzurePolicyComplianceService> _policyService;
    private readonly Mock<IDefenderForCloudService> _defenderService;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly EvidenceStorageService _sut;
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;

    public EvidenceStorageServiceTests()
    {
        _policyService = new Mock<IAzurePolicyComplianceService>();
        _defenderService = new Mock<IDefenderForCloudService>();
        var dbName = $"EvidenceTests_{Guid.NewGuid()}";
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _dbFactory = new InMemoryDbContextFactory(_dbOptions);
        var logger = Mock.Of<ILogger<EvidenceStorageService>>();

        _sut = new EvidenceStorageService(
            _policyService.Object,
            _defenderService.Object,
            _dbFactory,
            logger);
    }

    public void Dispose()
    {
        using var db = new AtoCopilotContext(_dbOptions);
        db.Database.EnsureDeleted();
    }

    // ─── CollectEvidenceAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CollectEvidence_AC_Family_ReturnsPolicyComplianceSnapshot()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"compliant\": 10, \"nonCompliant\": 2}");

        var evidence = await _sut.CollectEvidenceAsync("AC-2", "sub-1");

        evidence.ControlId.Should().Be("AC-2");
        evidence.SubscriptionId.Should().Be("sub-1");
        evidence.EvidenceType.Should().Be("PolicyComplianceSnapshot");
        evidence.EvidenceCategory.Should().Be(EvidenceCategory.PolicyCompliance);
        evidence.ContentHash.Should().NotBeNullOrEmpty();
        evidence.Content.Should().Contain("AC-2");
        evidence.CollectedBy.Should().Contain("Security Posture Intelligence Navigator");
    }

    [Fact]
    public async Task CollectEvidence_AU_Family_ReturnsAuditLogSnapshot()
    {
        _policyService
            .Setup(p => p.GetPolicyStatesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"states\": []}");

        var evidence = await _sut.CollectEvidenceAsync("AU-3", "sub-1");

        evidence.EvidenceType.Should().Be("AuditLogSnapshot");
        evidence.EvidenceCategory.Should().Be(EvidenceCategory.ActivityLog);
    }

    [Fact]
    public async Task CollectEvidence_SC_Family_ReturnsSecurityAssessmentSnapshot()
    {
        _defenderService
            .Setup(d => d.GetAssessmentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"assessments\": []}");
        _defenderService
            .Setup(d => d.GetSecureScoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 80}");

        var evidence = await _sut.CollectEvidenceAsync("SC-7", "sub-1");

        evidence.EvidenceType.Should().Be("SecurityAssessmentSnapshot");
        evidence.EvidenceCategory.Should().Be(EvidenceCategory.SecurityAssessment);
    }

    [Fact]
    public async Task CollectEvidence_SI_Family_ReturnsSecurityAssessmentSnapshot()
    {
        _defenderService
            .Setup(d => d.GetAssessmentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"assessments\": []}");
        _defenderService
            .Setup(d => d.GetSecureScoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 90}");

        var evidence = await _sut.CollectEvidenceAsync("SI-2", "sub-1");

        evidence.EvidenceType.Should().Be("SecurityAssessmentSnapshot");
    }

    [Fact]
    public async Task CollectEvidence_CM_Family_ReturnsConfigurationSnapshot()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"compliant\": 5}");

        var evidence = await _sut.CollectEvidenceAsync("CM-2", "sub-1");

        evidence.EvidenceType.Should().Be("ConfigurationSnapshot");
        evidence.EvidenceCategory.Should().Be(EvidenceCategory.PolicyCompliance);
    }

    [Fact]
    public async Task CollectEvidence_CP_Family_ReturnsRecoverySnapshot()
    {
        _defenderService
            .Setup(d => d.GetAssessmentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"assessments\": []}");
        _defenderService
            .Setup(d => d.GetSecureScoreAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"score\": 75}");

        var evidence = await _sut.CollectEvidenceAsync("CP-9", "sub-1");

        evidence.EvidenceType.Should().Be("RecoverySnapshot");
        evidence.EvidenceCategory.Should().Be(EvidenceCategory.ResourceCompliance);
    }

    [Fact]
    public async Task CollectEvidence_IA_Family_ReturnsPolicyComplianceSnapshot()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"compliant\": 8}");

        var evidence = await _sut.CollectEvidenceAsync("IA-2", "sub-1");

        evidence.EvidenceType.Should().Be("PolicyComplianceSnapshot");
        evidence.EvidenceCategory.Should().Be(EvidenceCategory.PolicyCompliance);
    }

    [Fact]
    public async Task CollectEvidence_Unknown_Family_ReturnsComplianceSnapshot()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"data\": []}");

        var evidence = await _sut.CollectEvidenceAsync("PL-1", "sub-1");

        evidence.EvidenceType.Should().Be("ComplianceSnapshot");
        evidence.EvidenceCategory.Should().Be(EvidenceCategory.Configuration);
    }

    // ─── Persistence ─────────────────────────────────────────────────────

    [Fact]
    public async Task CollectEvidence_PersistsToDatabase()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"compliant\": 10}");

        var evidence = await _sut.CollectEvidenceAsync("AC-2", "sub-1");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var stored = await db.Evidence.FindAsync(evidence.Id);
        stored.Should().NotBeNull();
        stored!.ControlId.Should().Be("AC-2");
        stored.ContentHash.Should().Be(evidence.ContentHash);
    }

    [Fact]
    public async Task GetEvidence_ReturnsMatchingByControlId()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"data\": []}");

        await _sut.CollectEvidenceAsync("AC-2", "sub-1");
        await _sut.CollectEvidenceAsync("AC-2", "sub-2");
        await _sut.CollectEvidenceAsync("SC-7", "sub-1");

        var results = await _sut.GetEvidenceAsync("AC-2");

        results.Should().HaveCount(2);
        results.Should().OnlyContain(e => e.ControlId == "AC-2");
    }

    [Fact]
    public async Task GetEvidence_FiltersBySubscriptionId()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"data\": []}");

        await _sut.CollectEvidenceAsync("AC-2", "sub-1");
        await _sut.CollectEvidenceAsync("AC-2", "sub-2");

        var results = await _sut.GetEvidenceAsync("AC-2", "sub-1");

        results.Should().ContainSingle();
        results[0].SubscriptionId.Should().Be("sub-1");
    }

    [Fact]
    public async Task GetEvidence_ReturnsOrderedByCollectedAtDesc()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"data\": []}");

        await _sut.CollectEvidenceAsync("AC-2", "sub-1");
        await Task.Delay(10); // ensure different timestamps
        await _sut.CollectEvidenceAsync("AC-2", "sub-1");

        var results = await _sut.GetEvidenceAsync("AC-2");

        results.Should().HaveCount(2);
        results[0].CollectedAt.Should().BeOnOrAfter(results[1].CollectedAt);
    }

    [Fact]
    public async Task GetEvidence_ReturnsEmptyForNoMatches()
    {
        var results = await _sut.GetEvidenceAsync("NONE-1");
        results.Should().BeEmpty();
    }

    // ─── Content Hash ─────────────────────────────────────────────────────

    [Fact]
    public void ComputeHash_ReturnsDeterministicSHA256()
    {
        var hash1 = EvidenceStorageService.ComputeHash("hello");
        var hash2 = EvidenceStorageService.ComputeHash("hello");
        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA-256 = 32 bytes = 64 hex chars
    }

    [Fact]
    public void ComputeHash_DifferentContentProducesDifferentHash()
    {
        var hash1 = EvidenceStorageService.ComputeHash("hello");
        var hash2 = EvidenceStorageService.ComputeHash("world");
        hash1.Should().NotBe(hash2);
    }

    // ─── Error Handling ──────────────────────────────────────────────────

    [Fact]
    public async Task CollectEvidence_WhenServiceFails_StillPersistsErrorSnapshot()
    {
        _policyService
            .Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var evidence = await _sut.CollectEvidenceAsync("AC-2", "sub-1");

        evidence.Should().NotBeNull();
        evidence.Content.Should().Contain("Connection failed");
        evidence.Content.Should().Contain("Manual evidence collection may be required");

        // Still persisted
        await using var db = await _dbFactory.CreateDbContextAsync();
        (await db.Evidence.FindAsync(evidence.Id)).Should().NotBeNull();
    }

    // ─── Helper ────────────────────────────────────────────────────────────

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
