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

public class DocumentGenerationServiceTests : IDisposable
{
    private readonly Mock<INistControlsService> _nistService;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly DocumentGenerationService _sut;
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;

    public DocumentGenerationServiceTests()
    {
        _nistService = new Mock<INistControlsService>();
        var dbName = $"DocGenTests_{Guid.NewGuid()}";
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _dbFactory = new InMemoryDbContextFactory(_dbOptions);
        var logger = Mock.Of<ILogger<DocumentGenerationService>>();

        _sut = new DocumentGenerationService(_dbFactory, _nistService.Object, logger);
    }

    public void Dispose()
    {
        using var db = new AtoCopilotContext(_dbOptions);
        db.Database.EnsureDeleted();
    }

    // ─── SSP Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateDocument_SSP_ReturnsValidMarkdownDocument()
    {
        await SeedAssessmentWithFindings();

        var doc = await _sut.GenerateDocumentAsync("SSP", "sub-1", "NIST80053", "Test System");

        doc.DocumentType.Should().Be("SSP");
        doc.SystemName.Should().Be("Test System");
        doc.Framework.Should().Be("NIST80053");
        doc.Content.Should().Contain("# System Security Plan (SSP)");
        doc.Content.Should().Contain("Test System");
        doc.Content.Should().Contain("Compliance Score");
        doc.Content.Should().Contain("Control Implementation Status");
        doc.GeneratedBy.Should().Contain("ATO Copilot");
    }

    [Fact]
    public async Task GenerateDocument_SSP_NoAssessment_ShowsNoDataMessage()
    {
        var doc = await _sut.GenerateDocumentAsync("SSP", "sub-1");

        doc.Content.Should().Contain("No assessment data available");
    }

    [Fact]
    public async Task GenerateDocument_SSP_IncludesFindingsByFamily()
    {
        await SeedAssessmentWithFindings();

        var doc = await _sut.GenerateDocumentAsync("SSP", "sub-1");

        doc.Content.Should().Contain("AC");
        doc.Content.Should().Contain("Access Control");
        doc.Content.Should().Contain("SC");
    }

    // ─── SAR Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateDocument_SAR_ReturnsSecurityAssessmentReport()
    {
        await SeedAssessmentWithFindings();

        var doc = await _sut.GenerateDocumentAsync("SAR", "sub-1");

        doc.DocumentType.Should().Be("SAR");
        doc.Content.Should().Contain("# Security Assessment Report (SAR)");
        doc.Content.Should().Contain("Executive Summary");
        doc.Content.Should().Contain("Assessment Methodology");
        doc.Content.Should().Contain("Findings Summary");
        doc.Content.Should().Contain("Recommendation");
    }

    [Fact]
    public async Task GenerateDocument_SAR_HighScore_RecommendsATO()
    {
        await SeedAssessment(95.0);

        var doc = await _sut.GenerateDocumentAsync("SAR", "sub-1");

        doc.Content.Should().Contain("Authorization to Operate (ATO)");
    }

    [Fact]
    public async Task GenerateDocument_SAR_LowScore_RequiresRemediation()
    {
        await SeedAssessment(50.0);

        var doc = await _sut.GenerateDocumentAsync("SAR", "sub-1");

        doc.Content.Should().Contain("requires additional remediation");
    }

    [Fact]
    public async Task GenerateDocument_SAR_ShowsSeverityBreakdown()
    {
        await SeedAssessmentWithFindings();

        var doc = await _sut.GenerateDocumentAsync("SAR", "sub-1");

        doc.Content.Should().Contain("Critical");
        doc.Content.Should().Contain("High");
        doc.Content.Should().Contain("Medium");
        doc.Content.Should().Contain("Low");
    }

    // ─── POA&M Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateDocument_POAM_ReturnsActionPlan()
    {
        await SeedAssessmentWithFindings();

        var doc = await _sut.GenerateDocumentAsync("POAM", "sub-1");

        doc.DocumentType.Should().Be("POAM");
        doc.Content.Should().Contain("# Plan of Action and Milestones (POA&M)");
        doc.Content.Should().Contain("Open Findings");
    }

    [Fact]
    public async Task GenerateDocument_POAM_NoOpenFindings_ShowsCompliantMessage()
    {
        await SeedAssessmentWithRemediatedFindings();

        var doc = await _sut.GenerateDocumentAsync("POAM", "sub-1");

        doc.Content.Should().Contain("No open findings");
    }

    [Fact]
    public async Task GenerateDocument_POAM_IncludesRemediationDetails()
    {
        await SeedAssessmentWithFindings();

        var doc = await _sut.GenerateDocumentAsync("POAM", "sub-1");

        doc.Content.Should().Contain("Remediation Details");
        doc.Content.Should().Contain("Remediation Guidance");
    }

    // ─── Document Type Normalization ──────────────────────────────────────

    [Theory]
    [InlineData("ssp", "SSP")]
    [InlineData("System Security Plan", "SSP")]
    [InlineData("sar", "SAR")]
    [InlineData("Security Assessment Report", "SAR")]
    [InlineData("poam", "POAM")]
    [InlineData("POA&M", "POAM")]
    [InlineData("Plan of Action", "POAM")]
    public void NormalizeDocumentType_ParsesVariations(string input, string expected)
    {
        var result = DocumentGenerationService.NormalizeDocumentType(input);
        result.Should().Be(expected);
    }

    // ─── Persistence ──────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateDocument_PersistsToDatabase()
    {
        await SeedAssessment(80.0);

        var doc = await _sut.GenerateDocumentAsync("SSP", "sub-1");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var stored = await db.Documents.FindAsync(doc.Id);
        stored.Should().NotBeNull();
        stored!.DocumentType.Should().Be("SSP");
        stored.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateDocument_SetsMetadata()
    {
        await SeedAssessment(80.0);

        var doc = await _sut.GenerateDocumentAsync("SSP", "sub-1", "NIST80053", "My System");

        doc.Metadata.Should().NotBeNull();
        doc.Metadata.PreparedBy.Should().Be("ATO Copilot");
        doc.Metadata.SystemDescription.Should().Contain("My System");
        doc.Metadata.DateRange.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateDocument_DefaultsFrameworkAndSystemName()
    {
        // Empty DB: no systems seeded. Fix #555 changed the default to resolve from DB;
        // when no systems exist the fallback is "Unknown System", not the old hardcoded
        // "Azure Government System". The framework default is still "NIST80053".
        var doc = await _sut.GenerateDocumentAsync("SSP");

        doc.Framework.Should().Be("NIST80053");
        doc.SystemName.Should().Be("Unknown System");
    }

    [Fact]
    public async Task GenerateDocument_LinksToAssessment()
    {
        var assessmentId = await SeedAssessment(80.0);

        var doc = await _sut.GenerateDocumentAsync("SSP", "sub-1");

        doc.AssessmentId.Should().Be(assessmentId);
    }

    // ─── Unsupported Type ─────────────────────────────────────────────────

    [Fact]
    public async Task GenerateDocument_UnsupportedType_ThrowsArgumentException()
    {
        var act = () => _sut.GenerateDocumentAsync("INVALID");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private async Task<string> SeedAssessment(double score)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "sub-1",
            Framework = "NIST80053",
            ComplianceScore = score,
            TotalControls = 100,
            PassedControls = (int)score,
            FailedControls = 100 - (int)score,
            Status = AssessmentStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();
        return assessment.Id;
    }

    private async Task SeedAssessmentWithFindings()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "sub-1",
            Framework = "NIST80053",
            ComplianceScore = 75.0,
            TotalControls = 100,
            PassedControls = 75,
            FailedControls = 25,
            Status = AssessmentStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };
        db.Assessments.Add(assessment);

        db.Findings.AddRange(
            new ComplianceFinding
            {
                AssessmentId = assessment.Id,
                ControlId = "AC-2",
                ControlFamily = "AC",
                Title = "Missing MFA on admin accounts",
                Description = "Admin accounts lacking MFA",
                Severity = FindingSeverity.Critical,
                Status = FindingStatus.Open,
                ResourceId = "/subscriptions/sub-1/rg/vm1",
                RemediationGuidance = "Enable MFA for all admin accounts"
            },
            new ComplianceFinding
            {
                AssessmentId = assessment.Id,
                ControlId = "SC-7",
                ControlFamily = "SC",
                Title = "Open NSG rule detected",
                Description = "NSG allows inbound from 0.0.0.0/0",
                Severity = FindingSeverity.High,
                Status = FindingStatus.Open,
                ResourceId = "/subscriptions/sub-1/rg/nsg1",
                RemediationGuidance = "Restrict inbound rules"
            },
            new ComplianceFinding
            {
                AssessmentId = assessment.Id,
                ControlId = "AU-3",
                ControlFamily = "AU",
                Title = "Diagnostic settings not enabled",
                Description = "Resource missing diagnostic settings",
                Severity = FindingSeverity.Medium,
                Status = FindingStatus.Open,
                ResourceId = "/subscriptions/sub-1/rg/storage1",
                RemediationGuidance = "Enable diagnostic settings"
            }
        );

        await db.SaveChangesAsync();
    }

    private async Task SeedAssessmentWithRemediatedFindings()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var assessment = new ComplianceAssessment
        {
            SubscriptionId = "sub-1",
            Framework = "NIST80053",
            ComplianceScore = 100.0,
            TotalControls = 10,
            PassedControls = 10,
            FailedControls = 0,
            Status = AssessmentStatus.Completed
        };
        db.Assessments.Add(assessment);

        db.Findings.Add(new ComplianceFinding
        {
            AssessmentId = assessment.Id,
            ControlId = "AC-2",
            ControlFamily = "AC",
            Title = "Fixed finding",
            Description = "Already remediated",
            Severity = FindingSeverity.Low,
            Status = FindingStatus.Remediated,
            ResourceId = "/subscriptions/sub-1/rg/vm1"
        });

        await db.SaveChangesAsync();
    }

    // ─── WM-BUG-2 regression: impact level and hosting environment ────────

    /// <summary>
    /// Regression test for WM-BUG-2: GenerateDocumentAsync must derive the NIST
    /// impact level from SecurityCategorization.NistBaseline, not hardcode "High".
    /// </summary>
    [Theory]
    [InlineData(ImpactValue.Low,      "Low")]
    [InlineData(ImpactValue.Moderate, "Moderate")]
    [InlineData(ImpactValue.High,     "High")]
    public async Task GenerateDocumentAsync_ImpactLevel_DerivedFromSecurityCategorization(
        ImpactValue cia, string expectedNistBaseline)
    {
        using var db = new AtoCopilotContext(_dbOptions);

        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"WM-BUG-2-System-{cia}",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Commercial",
            CreatedBy = "test",
            IsActive = true
        };
        var sc = new SecurityCategorization
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            CategorizedBy = "test",
            InformationTypes = new List<InformationType>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Test Info Type",
                    Sp80060Id = "C.3.5.8",
                    ConfidentialityImpact = cia,
                    IntegrityImpact     = cia,
                    AvailabilityImpact  = cia,
                    
                }
            }
        };
        system.SecurityCategorization = sc;
        db.RegisteredSystems.Add(system);

        // An assessment is required so GenerateSspAsync resolves the categorization via RegisteredSystemId
        var assessment = new ComplianceAssessment
        {
            Id = Guid.NewGuid().ToString(),
            SubscriptionId = $"sub-wm2-{cia}",
            RegisteredSystemId = system.Id,
            Framework = "NIST80053",
            TotalControls = 1,
            PassedControls = 1,
            FailedControls = 0,
            NotAssessedControls = 0,
            ComplianceScore = 100,
            AssessedAt = DateTime.UtcNow
        };
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync();

        var doc = await _sut.GenerateDocumentAsync(
            "SSP", systemName: system.Name, cancellationToken: CancellationToken.None);

        doc.Content.Should().Contain(expectedNistBaseline,
            $"the document must reflect the {expectedNistBaseline} NIST baseline — never hardcode 'High' (WM-BUG-2)");
    }

    /// <summary>
    /// Regression test for WM-BUG-2: hosting environment must be read from
    /// RegisteredSystem.HostingEnvironment, not hardcoded to "Azure Government".
    /// </summary>
    [Theory]
    [InlineData("Azure Commercial")]
    [InlineData("On-Premises")]
    [InlineData("Hybrid")]
    public async Task GenerateDocumentAsync_HostingEnvironment_DerivedFromSystem(string hostingEnv)
    {
        using var db = new AtoCopilotContext(_dbOptions);

        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"WM-BUG-2-Hosting-{hostingEnv.Replace(" ", "-")}",
            SystemType = SystemType.Enclave,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = hostingEnv,
            CreatedBy = "test",
            IsActive = true
        };
        var sc = new SecurityCategorization
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            CategorizedBy = "test",
            InformationTypes = new List<InformationType>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Test Info Type",
                    Sp80060Id = "C.3.5.8",
                    ConfidentialityImpact = ImpactValue.Low,
                    IntegrityImpact     = ImpactValue.Low,
                    AvailabilityImpact  = ImpactValue.Low,
                    
                }
            }
        };
        system.SecurityCategorization = sc;
        db.RegisteredSystems.Add(system);

        var assessment2 = new ComplianceAssessment
        {
            Id = Guid.NewGuid().ToString(),
            SubscriptionId = $"sub-wm2-host",
            RegisteredSystemId = system.Id,
            Framework = "NIST80053",
            TotalControls = 1,
            PassedControls = 1,
            FailedControls = 0,
            NotAssessedControls = 0,
            ComplianceScore = 100,
            AssessedAt = DateTime.UtcNow
        };
        db.Assessments.Add(assessment2);
        await db.SaveChangesAsync();

        var doc = await _sut.GenerateDocumentAsync(
            "SSP", systemName: system.Name, cancellationToken: CancellationToken.None);

        doc.Content.Should().Contain(hostingEnv,
            $"hosting environment '{hostingEnv}' must appear in the document — not hardcoded 'Azure Government' (WM-BUG-2)");
        doc.Content.Should().NotContain("Azure Government",
            "the SSP must not hardcode 'Azure Government' when the system uses a different environment (WM-BUG-2)");
    }

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
