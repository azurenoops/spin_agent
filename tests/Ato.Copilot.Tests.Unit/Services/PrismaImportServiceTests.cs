// ═══════════════════════════════════════════════════════════════════════════
// Feature 019 — Prisma Cloud Scan Import: Service Tests
// TDD: Tests written FIRST (red), implementation makes them green.
// ═══════════════════════════════════════════════════════════════════════════

using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Services.ScanImport;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for Prisma Cloud import operations on <see cref="ScanImportService"/>.
/// Covers CSV import flow: finding creation, status mapping, severity mapping,
/// effectiveness upsert, evidence creation, conflict resolution, and dry-run.
/// </summary>
public class PrismaImportServiceTests : IDisposable
{
    // ─── Constants ───────────────────────────────────────────────────────────

    private const string TestSystemId = "sys-prisma-test";
    private const string TestSystemName = "ACME Portal";
    private const string TestAssessmentId = "assess-prisma-test";
    private const string TestImporter = "isso@contoso.com";
    private const string TestSubscriptionId = "a1b2c3d4-5678-90ab-cdef-1234567890ab";

    // ─── Service Setup ───────────────────────────────────────────────────────

    private readonly ServiceProvider _serviceProvider;
    private readonly ScanImportService _service;
    private readonly Mock<IStigKnowledgeService> _stigServiceMock;
    private readonly Mock<IBaselineService> _baselineServiceMock;
    private readonly Mock<IRmfLifecycleService> _rmfServiceMock;
    private readonly Mock<IAssessmentArtifactService> _artifactServiceMock;
    private readonly Mock<ICklParser> _cklParserMock;
    private readonly Mock<IXccdfParser> _xccdfParserMock;
    private readonly Mock<ICklGenerator> _cklGeneratorMock;
    private readonly Mock<ISystemSubscriptionResolver> _subscriptionResolverMock;
    private readonly PrismaCsvParser _csvParser;

    public PrismaImportServiceTests()
    {
        _stigServiceMock = new Mock<IStigKnowledgeService>();
        _baselineServiceMock = new Mock<IBaselineService>();
        _rmfServiceMock = new Mock<IRmfLifecycleService>();
        _artifactServiceMock = new Mock<IAssessmentArtifactService>();
        _cklParserMock = new Mock<ICklParser>();
        _xccdfParserMock = new Mock<IXccdfParser>();
        _cklGeneratorMock = new Mock<ICklGenerator>();
        _subscriptionResolverMock = new Mock<ISystemSubscriptionResolver>();
        _csvParser = new PrismaCsvParser(NullLogger<PrismaCsvParser>.Instance);

        var services = new ServiceCollection();
        var dbName = $"PrismaImportTests_{Guid.NewGuid()}";
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _serviceProvider = services.BuildServiceProvider();

        // Initialize DB & seed
        using var initScope = _serviceProvider.CreateScope();
        var initCtx = initScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        initCtx.Database.EnsureCreated();

        initCtx.Assessments.Add(new ComplianceAssessment
        {
            Id = TestAssessmentId,
            RegisteredSystemId = TestSystemId,
            Framework = "NIST80053",
            Status = AssessmentStatus.InProgress,
            InitiatedBy = TestImporter
        });
        initCtx.SaveChanges();

        // Setup subscription resolver mock
        _subscriptionResolverMock
            .Setup(r => r.ResolveAsync(TestSubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestSystemId);

        // Default system exists
        _rmfServiceMock
            .Setup(r => r.GetSystemAsync(TestSystemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.Found(new RegisteredSystem
            {
                Id = TestSystemId,
                Name = TestSystemName,
                CurrentRmfStep = RmfPhase.Assess,
                HostingEnvironment = "Azure Government",
                CreatedBy = "admin"
            }));

        // Default baseline
        _baselineServiceMock
            .Setup(b => b.GetBaselineAsync(TestSystemId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlBaseline
            {
                Id = "bl-prisma-test",
                RegisteredSystemId = TestSystemId,
                BaselineLevel = "Moderate",
                ControlIds = new List<string> { "SC-28", "SC-12", "AU-2", "AC-2", "SC-7", "CM-6", "SC-8", "IA-5(1)" },
                CreatedBy = "admin"
            });

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _service = new ScanImportService(
            scopeFactory,
            _stigServiceMock.Object,
            _baselineServiceMock.Object,
            _rmfServiceMock.Object,
            _artifactServiceMock.Object,
            _cklParserMock.Object,
            _xccdfParserMock.Object,
            _cklGeneratorMock.Object,
            _subscriptionResolverMock.Object,
            _csvParser,
            new PrismaApiJsonParser(NullLogger<PrismaApiJsonParser>.Instance),
            Mock.Of<INessusParser>(),
            Mock.Of<INessusControlMapper>(),
            NullLogger<ScanImportService>.Instance);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    // ─── Helper Methods ──────────────────────────────────────────────────────

    private static byte[] CsvBytes(string csv) => System.Text.Encoding.UTF8.GetBytes(csv);

    private static string ValidCsvHeader =>
        "Alert ID,Status,Policy Name,Policy Type,Severity,Cloud Type,Account Name,Account ID,Region,Resource Name,Resource ID,Resource Type,Alert Time,Resolution Reason,Resolution Time,Compliance Standard,Compliance Requirement,Compliance Section";

    private static string SimpleCsvWithNist(string alertId = "P-100", string status = "open",
        string policyName = "Test Policy", string severity = "high",
        string accountId = "a1b2c3d4-5678-90ab-cdef-1234567890ab",
        string nistControl = "SC-28")
    {
        return ValidCsvHeader + "\n" +
            $"{alertId},{status},{policyName},config,{severity},azure,Prod,{accountId},eastus,res1,/subs/{accountId}/rg/res1,Microsoft.Storage/storageAccounts,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,{nistControl},{nistControl} Description";
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US1: ImportPrismaCsvAsync Tests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportPrismaCsvAsync_WithExplicitSystemId_CreatesFindings()
    {
        var csv = SimpleCsvWithNist();
        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Imports.Should().ContainSingle();
        result.Imports[0].FindingsCreated.Should().BeGreaterThan(0);
        result.Imports[0].SystemId.Should().Be(TestSystemId);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_WithExplicitSystemId_SetsCorrectImportType()
    {
        var csv = SimpleCsvWithNist();
        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        // Verify ScanImportRecord has PrismaCsv type
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var importRecord = await ctx.ScanImportRecords
            .FirstOrDefaultAsync(r => r.Id == result.Imports[0].ImportRecordId);
        importRecord.Should().NotBeNull();
        importRecord!.ImportType.Should().Be(ScanImportType.PrismaCsv);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_CreatesFindingWithCorrectSource()
    {
        var csv = SimpleCsvWithNist();
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding.Should().NotBeNull();
        finding!.ScanSource.Should().Be(ScanSourceType.Cloud);
        finding.StigFinding.Should().BeFalse();
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_DryRun_DoesNotPersist()
    {
        var csv = SimpleCsvWithNist();
        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, true, TestImporter);

        result.Imports.Should().ContainSingle();
        result.Imports[0].IsDryRun.Should().BeTrue();
        result.Imports[0].FindingsCreated.Should().BeGreaterThan(0);

        // Nothing persisted
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var findings = await ctx.Findings
            .Where(f => f.Source == "Prisma Cloud").ToListAsync();
        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_UnmappedPolicy_CreatesWarning()
    {
        // CSV with no compliance standard columns → unmapped policy
        var csv = ValidCsvHeader + "\n" +
            "P-200,open,Custom Policy,anomaly,informational,azure,Prod," +
            TestSubscriptionId + ",eastus,res1,/r1,T1,2026-01-01T00:00:00Z,,,,,";

        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Imports[0].UnmappedPolicies.Should().BeGreaterThan(0);
        result.Imports[0].Warnings.Should().Contain(w => w.Contains("no NIST 800-53 mapping"));
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_ResolvedAlert_MapsFindingStatusToRemediated()
    {
        var csv = ValidCsvHeader + "\n" +
            "P-300,resolved,Policy R,config,medium,azure,Prod," +
            TestSubscriptionId + ",eastus,res1,/subs/" + TestSubscriptionId + "/rg/res1,Microsoft.Sql/servers,2026-01-01T00:00:00Z,Resolved,2026-01-10T00:00:00Z,NIST 800-53 Rev 5,AU-2,AU-2 Event Logging";

        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Remediated);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_DismissedAlert_MapsFindingStatusToAccepted()
    {
        var csv = ValidCsvHeader + "\n" +
            "P-301,dismissed,Policy D,config,low,azure,Prod," +
            TestSubscriptionId + ",eastus,res1,/r1,T1,2026-01-01T00:00:00Z,Dismissed,2026-01-05T00:00:00Z,NIST 800-53 Rev 5,AC-2,AC-2 Account Mgmt";

        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Accepted);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_SnoozedAlert_MapsFindingStatusToOpenWithNote()
    {
        var csv = ValidCsvHeader + "\n" +
            "P-302,snoozed,Policy S,network,critical,azure,Prod," +
            TestSubscriptionId + ",eastus,res1,/r1,T1,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-7,SC-7 Boundary";

        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding.Should().NotBeNull();
        finding!.Status.Should().Be(FindingStatus.Open);
        finding.Description.Should().Contain("snoozed", Exactly.Once());
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_MultiControlAlert_CreatesOneFindingWithMultipleEffectiveness()
    {
        // One alert mapping to 2 NIST controls
        var csv = ValidCsvHeader + "\n" +
            "P-400,open,Multi Control,config,high,azure,Prod," +
            TestSubscriptionId + ",eastus,res1,/r1,T1,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Protection\n" +
            "P-400,open,Multi Control,config,high,azure,Prod," +
            TestSubscriptionId + ",eastus,res1,/r1,T1,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-12,SC-12 Key Mgmt";

        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        // One finding per alert (not per control)
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var findings = await ctx.Findings
            .Where(f => f.Source == "Prisma Cloud").ToListAsync();
        findings.Should().ContainSingle();

        // Multiple effectiveness records (one per control in baseline)
        var effectiveness = await ctx.ControlEffectivenessRecords
            .Where(e => e.RegisteredSystemId == TestSystemId).ToListAsync();
        effectiveness.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_CreatesEvidenceWithSha256Hash()
    {
        var csv = SimpleCsvWithNist();
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var evidence = await ctx.Evidence
            .FirstOrDefaultAsync(e => e.EvidenceType == "CloudScanResult");
        evidence.Should().NotBeNull();
        evidence!.CollectionMethod.Should().Be("Automated");
        evidence.ContentHash.Should().StartWith("sha256:");
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_ConflictResolutionSkip_SkipsDuplicates()
    {
        var csv = SimpleCsvWithNist(alertId: "P-500");

        // First import
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        // Second import — same alert
        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test2.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Imports[0].SkippedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_FileTooLarge_ReturnsError()
    {
        // Create file exceeding 25MB
        var largeContent = new byte[26 * 1024 * 1024];
        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, largeContent, "large.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().Contain("25");
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_SeverityMapping_UsesPrismaSeverityMapper()
    {
        var csv = SimpleCsvWithNist(severity: "critical");
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding.Should().NotBeNull();
        finding!.Severity.Should().Be(FindingSeverity.Critical);
        finding.CatSeverity.Should().Be(CatSeverity.CatI);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_SetsCorrectTitleFromPolicyName()
    {
        var csv = SimpleCsvWithNist(policyName: "Azure Storage encryption not enabled");
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding.Should().NotBeNull();
        finding!.Title.Should().Be("Azure Storage encryption not enabled");
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_PopulatesScanImportFinding_PrismaFields()
    {
        var csv = SimpleCsvWithNist(alertId: "P-600");
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var importFinding = await ctx.ScanImportFindings
            .FirstOrDefaultAsync(f => f.PrismaAlertId == "P-600");
        importFinding.Should().NotBeNull();
        importFinding!.PrismaPolicyName.Should().NotBeNullOrWhiteSpace();
        importFinding.CloudResourceType.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_SystemNotFound_ReturnsError()
    {
        _rmfServiceMock
            .Setup(r => r.GetSystemAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.NotFound("nonexistent"));

        var csv = SimpleCsvWithNist();
        var result = await _service.ImportPrismaCsvAsync(
            "nonexistent", TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_NoSystemId_AutoResolvesFromSubscription()
    {
        var csv = SimpleCsvWithNist(accountId: TestSubscriptionId);
        var result = await _service.ImportPrismaCsvAsync(
            null, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Imports.Should().ContainSingle();
        result.Imports[0].SystemId.Should().Be(TestSystemId);
        _subscriptionResolverMock.Verify(
            r => r.ResolveAsync(TestSubscriptionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_NonAzureWithoutExplicitSystem_SkipsWithWarning()
    {
        // AWS alert with no explicit system_id → should be skipped
        var csv = ValidCsvHeader + "\n" +
            "P-700,open,AWS Policy,config,high,aws,AWS-Prod,123456789012,us-east-1,bucket-1,arn:aws:s3:::bucket-1,AWS.S3.Bucket,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Protection";

        var result = await _service.ImportPrismaCsvAsync(
            null, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.SkippedNonAzure.Should().NotBeNull();
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_NonAzureWithExplicitSystem_ImportsAll()
    {
        // AWS alert WITH explicit system_id → should be imported
        var csv = ValidCsvHeader + "\n" +
            "P-701,open,AWS Policy,config,high,aws,AWS-Prod,123456789012,us-east-1,bucket-1,arn:aws:s3:::bucket-1,AWS.S3.Bucket,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Protection";

        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Imports.Should().ContainSingle();
        result.Imports[0].FindingsCreated.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_ConflictResolutionOverwrite_ReplacesExisting()
    {
        var csv = SimpleCsvWithNist(alertId: "P-800", severity: "high");

        // First import
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "test.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        // Second import with Overwrite and changed severity
        var csv2 = SimpleCsvWithNist(alertId: "P-800", severity: "critical");
        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv2), "test2.csv",
            ImportConflictResolution.Overwrite, false, TestImporter);

        result.Imports[0].FindingsUpdated.Should().BeGreaterThan(0);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding!.Severity.Should().Be(FindingSeverity.Critical);
    }

    [Fact]
    public async Task ImportPrismaCsvAsync_InvalidCsv_ReturnsParseError()
    {
        var invalidCsv = CsvBytes("not,a,valid,csv\nwithout,proper,headers,here");
        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, invalidCsv, "bad.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().NotBeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // API JSON Import Tests (US2 — T026)
    // ═════════════════════════════════════════════════════════════════════════

    private static byte[] JsonBytes(string json) => System.Text.Encoding.UTF8.GetBytes(json);

    private static byte[] LoadTestFile(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        return File.ReadAllBytes(path);
    }

    private static string SimpleApiJson(string alertId = "P-100", string status = "open",
        string policyName = "Test Policy", string severity = "high",
        string accountId = "a1b2c3d4-5678-90ab-cdef-1234567890ab",
        string nistControl = "SC-28", bool remediable = false,
        string? cliScript = null, string? recommendation = null)
    {
        var remediation = cliScript != null
            ? $$"""
              , "remediation": { "cliScriptTemplate": "{{cliScript}}" }
              """
            : "";

        var rec = recommendation != null
            ? $", \"recommendation\": \"{recommendation}\""
            : "";

        return $$"""
        [{
          "id": "{{alertId}}",
          "status": "{{status}}",
          "alertTime": 1740652800000,
          "policy": {
            "policyId": "pol-001",
            "name": "{{policyName}}",
            "policyType": "config",
            "severity": "{{severity}}",
            "remediable": {{remediable.ToString().ToLower()}}{{rec}}{{remediation}},
            "labels": ["CSPM", "Azure"],
            "complianceMetadata": [
              { "standardName": "NIST 800-53 Rev 5", "requirementId": "{{nistControl}}" }
            ]
          },
          "resource": {
            "id": "/subs/{{accountId}}/rg/res1",
            "name": "res1",
            "resourceType": "Microsoft.Storage/storageAccounts",
            "region": "eastus",
            "cloudType": "azure",
            "accountId": "{{accountId}}",
            "accountName": "Prod"
          },
          "history": [
            { "modifiedBy": "System", "modifiedOn": 1740652800000, "reason": "NEW_ALERT", "status": "open" }
          ]
        }]
        """;
    }

    [Fact]
    public async Task ImportPrismaApiAsync_ExplicitSystem_CreatesFindings()
    {
        var json = SimpleApiJson();
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().BeNull();
        result.Imports.Should().ContainSingle();
        result.Imports[0].FindingsCreated.Should().Be(1);
        result.TotalProcessed.Should().Be(1);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding.Should().NotBeNull();
    }

    [Fact]
    public async Task ImportPrismaApiAsync_PopulatesRemediationGuidance()
    {
        var json = SimpleApiJson(recommendation: "Navigate to Azure Portal and fix it");
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().BeNull();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding!.RemediationGuidance.Should().Contain("Navigate to Azure Portal");
    }

    [Fact]
    public async Task ImportPrismaApiAsync_PopulatesRemediationScript()
    {
        var json = SimpleApiJson(cliScript: "az storage account update --name test", remediable: true);
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().BeNull();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings.FirstOrDefaultAsync(f => f.Source == "Prisma Cloud");
        finding!.RemediationScript.Should().Contain("az storage account update");
        finding.AutoRemediable.Should().BeTrue();
    }

    [Fact]
    public async Task ImportPrismaApiAsync_TracksRemediableCount()
    {
        var json = SimpleApiJson(remediable: true, cliScript: "az update");
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Imports[0].RemediableCount.Should().Be(1);
        result.Imports[0].CliScriptsExtracted.Should().Be(1);
    }

    [Fact]
    public async Task ImportPrismaApiAsync_TracksPolicyLabels()
    {
        var json = SimpleApiJson();
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Imports[0].PolicyLabelsFound.Should().NotBeNull();
        result.Imports[0].PolicyLabelsFound.Should().Contain("CSPM");
        result.Imports[0].PolicyLabelsFound.Should().Contain("Azure");
    }

    [Fact]
    public async Task ImportPrismaApiAsync_TracksAlertsWithHistory()
    {
        var json = SimpleApiJson();
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.Imports[0].AlertsWithHistory.Should().Be(1);
    }

    [Fact]
    public async Task ImportPrismaApiAsync_ImportType_Is_PrismaApi()
    {
        var json = SimpleApiJson();
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var importRecord = await ctx.ScanImportRecords.FirstOrDefaultAsync();
        importRecord.Should().NotBeNull();
        importRecord!.ImportType.Should().Be(ScanImportType.PrismaApi);
    }

    [Fact]
    public async Task ImportPrismaApiAsync_SampleFixture_Processes5Alerts()
    {
        var bytes = LoadTestFile("sample-prisma-api.json");
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, bytes, "sample-prisma-api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().BeNull();
        result.TotalProcessed.Should().Be(5);
        result.Imports[0].TotalAlerts.Should().Be(5);
        // P-20001: open, P-20002: resolved, P-20003: dismissed, P-20004: open, P-20005: open
        result.Imports[0].OpenCount.Should().Be(3);
        result.Imports[0].ResolvedCount.Should().Be(1);
        result.Imports[0].DismissedCount.Should().Be(1);
    }

    [Fact]
    public async Task ImportPrismaApiAsync_InvalidJson_ReturnsParseError()
    {
        var invalidJson = JsonBytes("not valid json at all");
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, invalidJson, "bad.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().NotBeNull();
        result.ErrorMessage.Should().Contain("JSON parse error");
    }

    [Fact]
    public async Task ImportPrismaApiAsync_AutoResolve_UsesSubscriptionResolver()
    {
        var json = SimpleApiJson();
        var result = await _service.ImportPrismaApiAsync(
            null, null, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().BeNull();
        result.Imports.Should().ContainSingle();
        result.Imports[0].SystemId.Should().Be(TestSystemId);
    }

    [Fact]
    public async Task ImportPrismaApiAsync_DryRun_DoesNotPersist()
    {
        var json = SimpleApiJson();
        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, true, TestImporter);

        result.ErrorMessage.Should().BeNull();
        result.Imports[0].FindingsCreated.Should().Be(1);
        result.Imports[0].IsDryRun.Should().BeTrue();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var findings = await ctx.Findings.Where(f => f.Source == "Prisma Cloud").ToListAsync();
        findings.Should().BeEmpty("dry run should not persist findings");
    }

    [Fact]
    public async Task ImportPrismaApiAsync_CreatesEvidence()
    {
        var json = SimpleApiJson();
        await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var evidence = await ctx.Evidence
            .FirstOrDefaultAsync(e => e.EvidenceType == "CloudScanResult");
        evidence.Should().NotBeNull();
        evidence!.Description.Should().Contain("PrismaApi");
    }

    [Fact]
    public async Task ImportPrismaApiAsync_SystemNotFound_ReturnsError()
    {
        var json = SimpleApiJson();
        var result = await _service.ImportPrismaApiAsync(
            "nonexistent-system", TestAssessmentId, JsonBytes(json), "api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        result.ErrorMessage.Should().Contain("not found");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // T049: Downstream Artifact Integration — Prisma findings in SAR/ConMon/RAR
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PrismaImport_FindingsHaveCorrectAssessmentId_ForDownstreamQueries()
    {
        await SeedPrismaImportAsync(alertId: "P-DS01", fileName: "downstream.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var findings = await ctx.Findings
            .Where(f => f.Source == "Prisma Cloud" && f.AssessmentId == TestAssessmentId)
            .ToListAsync();

        findings.Should().NotBeEmpty("Prisma findings should link to the assessment for SAR/RAR/ConMon to query");
        findings.Should().OnlyContain(f => f.AssessmentId == TestAssessmentId);
    }

    [Fact]
    public async Task PrismaImport_CreatesEffectivenessRecords_ForSarGeneration()
    {
        await SeedPrismaImportAsync(alertId: "P-DS02", nistControl: "SC-28", fileName: "sar-eff.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var effectiveness = await ctx.ControlEffectivenessRecords
            .Where(e => e.RegisteredSystemId == TestSystemId &&
                        e.AssessmentId == TestAssessmentId &&
                        e.ControlId == "SC-28")
            .FirstOrDefaultAsync();

        effectiveness.Should().NotBeNull("SAR aggregates effectiveness records created by import");
        effectiveness!.AssessmentMethod.Should().Be("Test");
        effectiveness.EvidenceIds.Should().NotBeEmpty("evidence should link to import");
    }

    [Fact]
    public async Task PrismaImport_OpenFindings_QueryableBySystem_ForConMonReport()
    {
        // ConMon queries: Findings where assessment.RegisteredSystemId matches
        await SeedPrismaImportAsync(alertId: "P-DS03", status: "open", fileName: "conmon-open.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var openFindings = await ctx.Findings.CountAsync(f =>
            ctx.Assessments.Any(a => a.Id == f.AssessmentId && a.RegisteredSystemId == TestSystemId) &&
            (f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress));

        openFindings.Should().BeGreaterThan(0,
            "ConMon report counts open findings by system — Prisma imports should contribute");
    }

    [Fact]
    public async Task PrismaImport_ResolvedFindings_QueryableBySystem_ForConMonReport()
    {
        await SeedPrismaImportAsync(alertId: "P-DS04", status: "resolved", fileName: "conmon-res.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var resolvedFindings = await ctx.Findings.CountAsync(f =>
            ctx.Assessments.Any(a => a.Id == f.AssessmentId && a.RegisteredSystemId == TestSystemId) &&
            (f.Status == FindingStatus.Remediated || f.Status == FindingStatus.FalsePositive));

        resolvedFindings.Should().BeGreaterThan(0,
            "ConMon report counts resolved findings — Prisma 'resolved' maps to Remediated status");
    }

    [Fact]
    public async Task PrismaImport_FindingsQueryableByAssessment_ForRarGeneration()
    {
        // RAR queries: db.Findings.Where(f => f.AssessmentId == assessmentId)
        await SeedPrismaImportAsync(alertId: "P-DS05", status: "open", fileName: "rar.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var findings = await ctx.Findings
            .Where(f => f.AssessmentId == TestAssessmentId)
            .ToListAsync();

        var prismaFinding = findings.FirstOrDefault(f => f.Source == "Prisma Cloud");
        prismaFinding.Should().NotBeNull("RAR groups findings by ControlFamily — Prisma findings must be queryable");
        prismaFinding!.ControlFamily.Should().NotBeNullOrEmpty("ControlFamily is used for RAR per-family risk aggregation");
    }

    [Fact]
    public async Task PrismaImport_EvidenceCreated_ForAuthorizationPackage()
    {
        await SeedPrismaImportAsync(alertId: "P-DS06", fileName: "authpkg.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var evidence = await ctx.Evidence
            .Where(e => e.EvidenceType == "CloudScanResult" && e.AssessmentId == TestAssessmentId)
            .FirstOrDefaultAsync();

        evidence.Should().NotBeNull("Authorization package bundles evidence — Prisma import creates CloudScanResult evidence");
        evidence!.ContentHash.Should().StartWith("sha256:", "file hash should be present for integrity verification");
    }

    [Fact]
    public async Task PrismaImport_MultiSystem_Dashboard_FindingsScoped()
    {
        // Dashboard queries open findings per system via assessment join
        await SeedPrismaImportAsync(alertId: "P-DS07", status: "open", fileName: "dash.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Simulate the dashboard query for our system
        var systemOpenCount = await ctx.Findings.CountAsync(f =>
            ctx.Assessments.Any(a => a.Id == f.AssessmentId && a.RegisteredSystemId == TestSystemId) &&
            (f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress));

        systemOpenCount.Should().BeGreaterThan(0,
            "Multi-system dashboard should reflect Prisma-sourced open findings for the system");
    }

    [Fact]
    public async Task PrismaImport_DismissedFinding_MapsToAccepted_ForPoamConsideration()
    {
        await SeedPrismaImportAsync(alertId: "P-DS08", status: "dismissed", fileName: "poam.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var finding = await ctx.Findings
            .FirstOrDefaultAsync(f => f.Source == "Prisma Cloud" && f.Status == FindingStatus.Accepted);

        finding.Should().NotBeNull("Dismissed Prisma alerts should map to Accepted status for POA&M tracking");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US3: ListPrismaPoliciesAsync Tests (T036)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Seed a Prisma CSV import so we have data for policy/trend queries.</summary>
    private async Task SeedPrismaImportAsync(string alertId = "P-100", string status = "open",
        string policyName = "Test Policy", string severity = "high",
        string nistControl = "SC-28", string fileName = "seed.csv")
    {
        var csv = SimpleCsvWithNist(alertId, status, policyName, severity, nistControl: nistControl);
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), fileName,
            ImportConflictResolution.Skip, false, TestImporter);
    }

    [Fact]
    public async Task ListPrismaPoliciesAsync_ReturnsUniquePolicies()
    {
        await SeedPrismaImportAsync(alertId: "P-200", policyName: "Storage Encryption");
        await SeedPrismaImportAsync(alertId: "P-201", policyName: "SQL Auditing");

        var result = await _service.ListPrismaPoliciesAsync(TestSystemId);

        result.SystemId.Should().Be(TestSystemId);
        result.TotalPolicies.Should().Be(2);
        result.Policies.Should().HaveCount(2);
        result.Policies.Select(p => p.PolicyName).Should().Contain("Storage Encryption");
        result.Policies.Select(p => p.PolicyName).Should().Contain("SQL Auditing");
    }

    [Fact]
    public async Task ListPrismaPoliciesAsync_IncludesNistControlMappings()
    {
        await SeedPrismaImportAsync(alertId: "P-300", policyName: "Encryption Policy", nistControl: "SC-28");

        var result = await _service.ListPrismaPoliciesAsync(TestSystemId);

        result.Policies.Should().ContainSingle();
        result.Policies[0].NistControlIds.Should().Contain("SC-28");
    }

    [Fact]
    public async Task ListPrismaPoliciesAsync_IncludesStatusCounts()
    {
        await SeedPrismaImportAsync(alertId: "P-400", status: "open", policyName: "Count Policy");
        await SeedPrismaImportAsync(alertId: "P-401", status: "resolved", policyName: "Count Policy");

        var result = await _service.ListPrismaPoliciesAsync(TestSystemId);

        var policy = result.Policies.First(p => p.PolicyName == "Count Policy");
        policy.OpenCount.Should().Be(1);
        policy.ResolvedCount.Should().Be(1);
    }

    [Fact]
    public async Task ListPrismaPoliciesAsync_IncludesAffectedResourceTypes()
    {
        await SeedPrismaImportAsync(alertId: "P-500", policyName: "Resource Policy");

        var result = await _service.ListPrismaPoliciesAsync(TestSystemId);

        result.Policies[0].AffectedResourceTypes.Should().NotBeEmpty();
        result.Policies[0].AffectedResourceTypes.Should().Contain("Microsoft.Storage/storageAccounts");
    }

    [Fact]
    public async Task ListPrismaPoliciesAsync_NoPrismaImports_ReturnsEmptyPolicies()
    {
        var result = await _service.ListPrismaPoliciesAsync(TestSystemId);

        result.SystemId.Should().Be(TestSystemId);
        result.TotalPolicies.Should().Be(0);
        result.Policies.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPrismaPoliciesAsync_SystemNotFound_Throws()
    {
        _rmfServiceMock
            .Setup(r => r.GetSystemAsync("bad-sys", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.NotFound("bad-sys"));

        var act = () => _service.ListPrismaPoliciesAsync("bad-sys");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // T048: Integration — ListImportsAsync / GetImportSummaryAsync with Prisma
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListImportsAsync_ReturnsPrismaCsvWithCorrectImportType()
    {
        await SeedPrismaImportAsync(alertId: "P-LI01", fileName: "list-csv.csv");

        var (records, total) = await _service.ListImportsAsync(
            TestSystemId, 1, 20, null, null, false, null, null);

        total.Should().BeGreaterThan(0);
        var prismaRec = records.FirstOrDefault(r => r.ImportType == ScanImportType.PrismaCsv);
        prismaRec.Should().NotBeNull();
        prismaRec!.FileName.Should().Be("list-csv.csv");
        prismaRec.TotalEntries.Should().Be(1);
    }

    [Fact]
    public async Task ListImportsAsync_ReturnsPrismaApiWithCorrectImportType()
    {
        var json = SimpleApiJson(alertId: "P-LI02");
        await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "list-api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        var (records, _) = await _service.ListImportsAsync(
            TestSystemId, 1, 20, null, null, false, null, null);

        var apiRec = records.FirstOrDefault(r => r.ImportType == ScanImportType.PrismaApi);
        apiRec.Should().NotBeNull();
        apiRec!.FileName.Should().Be("list-api.json");
    }

    [Fact]
    public async Task ListImportsAsync_FiltersByImportType_PrismaCsv()
    {
        await SeedPrismaImportAsync(alertId: "P-LI03", fileName: "filter-csv.csv");
        var json = SimpleApiJson(alertId: "P-LI04");
        await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "filter-api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        var (records, total) = await _service.ListImportsAsync(
            TestSystemId, 1, 20, null, "PrismaCsv", false, null, null);

        total.Should().BeGreaterThan(0);
        records.Should().OnlyContain(r => r.ImportType == ScanImportType.PrismaCsv);
    }

    [Fact]
    public async Task ListImportsAsync_FiltersByImportType_PrismaApi()
    {
        await SeedPrismaImportAsync(alertId: "P-LI05", fileName: "filter-csv2.csv");
        var json = SimpleApiJson(alertId: "P-LI06");
        await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "filter-api2.json",
            ImportConflictResolution.Skip, false, TestImporter);

        var (records, total) = await _service.ListImportsAsync(
            TestSystemId, 1, 20, null, "PrismaApi", false, null, null);

        total.Should().BeGreaterThan(0);
        records.Should().OnlyContain(r => r.ImportType == ScanImportType.PrismaApi);
    }

    [Fact]
    public async Task GetImportSummaryAsync_PrismaCsv_ReturnsFindingsWithPrismaFields()
    {
        await SeedPrismaImportAsync(alertId: "P-SUM01", policyName: "Summary Policy",
            fileName: "summary-csv.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var importRec = await ctx.ScanImportRecords
            .FirstOrDefaultAsync(r => r.ImportType == ScanImportType.PrismaCsv);
        importRec.Should().NotBeNull();

        var summary = await _service.GetImportSummaryAsync(importRec!.Id);
        summary.Should().NotBeNull();

        var (record, findings) = summary!.Value;
        record.ImportType.Should().Be(ScanImportType.PrismaCsv);
        findings.Should().NotBeEmpty();
        var f = findings.First();
        f.PrismaAlertId.Should().Be("P-SUM01");
        f.PrismaPolicyName.Should().Be("Summary Policy");
        f.CloudResourceType.Should().Be("Microsoft.Storage/storageAccounts");
        f.CloudResourceId.Should().NotBeNullOrEmpty();
        f.CloudAccountId.Should().Be(TestSubscriptionId);
    }

    [Fact]
    public async Task GetImportSummaryAsync_PrismaApi_ReturnsFindingsWithPrismaFields()
    {
        var json = SimpleApiJson(alertId: "P-SUM02", policyName: "API Summary Policy");
        await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, JsonBytes(json), "summary-api.json",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var importRec = await ctx.ScanImportRecords
            .FirstOrDefaultAsync(r => r.ImportType == ScanImportType.PrismaApi);
        importRec.Should().NotBeNull();

        var summary = await _service.GetImportSummaryAsync(importRec!.Id);
        summary.Should().NotBeNull();

        var (record, findings) = summary!.Value;
        record.ImportType.Should().Be(ScanImportType.PrismaApi);
        findings.Should().NotBeEmpty();
        var f = findings.First();
        f.PrismaAlertId.Should().Be("P-SUM02");
        f.PrismaPolicyName.Should().Be("API Summary Policy");
        f.CloudResourceType.Should().Be("Microsoft.Storage/storageAccounts");
    }

    [Fact]
    public async Task GetImportSummaryAsync_PrismaImport_HasCorrectCounts()
    {
        // Import CSV with 2 rows: one open, one resolved
        var csv = ValidCsvHeader + "\n" +
            $"P-SUM03,open,Count Policy,config,high,azure,Prod,{TestSubscriptionId},eastus,res1,/subs/{TestSubscriptionId}/rg/res1,Microsoft.Storage/storageAccounts,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Desc\n" +
            $"P-SUM04,resolved,Count Policy,config,medium,azure,Prod,{TestSubscriptionId},eastus,res2,/subs/{TestSubscriptionId}/rg/res2,Microsoft.Sql/servers,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Desc";
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "count-csv.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var importRec = await ctx.ScanImportRecords
            .FirstOrDefaultAsync(r => r.FileName == "count-csv.csv");

        var summary = await _service.GetImportSummaryAsync(importRec!.Id);
        var (record, findings) = summary!.Value;

        record.TotalEntries.Should().Be(2);
        record.OpenCount.Should().Be(1);
        record.FindingsCreated.Should().Be(2);
        findings.Should().HaveCount(2);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // US3: GetPrismaTrendAsync Tests (T037 + T046)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPrismaTrendAsync_TwoImports_ShowsNewAndResolvedFindings()
    {
        // First import with P-600 open
        await SeedPrismaImportAsync(alertId: "P-600", status: "open", policyName: "Trend Policy",
            fileName: "import1.csv");
        // Second import with P-600 resolved + P-601 new
        var csv2lines = ValidCsvHeader + "\n" +
            $"P-600,resolved,Trend Policy,config,high,azure,Prod,{TestSubscriptionId},eastus,res1,/subs/{TestSubscriptionId}/rg/res1,Microsoft.Storage/storageAccounts,2026-02-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Desc\n" +
            $"P-601,open,Trend Policy,config,high,azure,Prod,{TestSubscriptionId},eastus,res2,/subs/{TestSubscriptionId}/rg/res2,Microsoft.Storage/storageAccounts,2026-02-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Desc";
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv2lines), "import2.csv",
            ImportConflictResolution.Overwrite, false, TestImporter);

        var result = await _service.GetPrismaTrendAsync(TestSystemId, null, null);

        result.SystemId.Should().Be(TestSystemId);
        result.Imports.Should().HaveCount(2);
        result.NewFindings.Should().Be(1); // P-601 is new
        result.ResolvedFindings.Should().Be(0); // P-600 still in latest (resolved status)
        result.PersistentFindings.Should().Be(1); // P-600 in both
    }

    [Fact]
    public async Task GetPrismaTrendAsync_CalculatesRemediationRate()
    {
        await SeedPrismaImportAsync(alertId: "P-700", status: "open", policyName: "Rate Policy",
            fileName: "rate1.csv");
        // Second import: P-700 still open, P-701 new
        var csv2 = ValidCsvHeader + "\n" +
            $"P-700,open,Rate Policy,config,high,azure,Prod,{TestSubscriptionId},eastus,res1,/subs/{TestSubscriptionId}/rg/res1,Microsoft.Storage/storageAccounts,2026-02-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28\n" +
            $"P-701,open,Rate Policy,config,high,azure,Prod,{TestSubscriptionId},eastus,res2,/subs/{TestSubscriptionId}/rg/res2,Microsoft.Storage/storageAccounts,2026-02-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28";
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv2), "rate2.csv",
            ImportConflictResolution.Overwrite, false, TestImporter);

        var result = await _service.GetPrismaTrendAsync(TestSystemId, null, null);

        // 1 persistent (P-700), 1 new (P-701), 0 resolved → rate = 0/(0+1) = 0%
        result.RemediationRate.Should().Be(0m);
    }

    [Fact]
    public async Task GetPrismaTrendAsync_WithGroupByResourceType_ReturnsBreakdown()
    {
        await SeedPrismaImportAsync(alertId: "P-800", policyName: "Group Policy",
            fileName: "group1.csv");
        await SeedPrismaImportAsync(alertId: "P-801", policyName: "Group Policy",
            fileName: "group2.csv");

        var result = await _service.GetPrismaTrendAsync(TestSystemId, null, "resource_type");

        result.ResourceTypeBreakdown.Should().NotBeNull();
        result.NistControlBreakdown.Should().BeNull();
    }

    [Fact]
    public async Task GetPrismaTrendAsync_WithGroupByNistControl_ReturnsBreakdown()
    {
        await SeedPrismaImportAsync(alertId: "P-900", policyName: "NIST Group",
            nistControl: "SC-28", fileName: "nist1.csv");
        await SeedPrismaImportAsync(alertId: "P-901", policyName: "NIST Group",
            nistControl: "SC-28", fileName: "nist2.csv");

        var result = await _service.GetPrismaTrendAsync(TestSystemId, null, "nist_control");

        result.NistControlBreakdown.Should().NotBeNull();
        result.ResourceTypeBreakdown.Should().BeNull();
    }

    [Fact]
    public async Task GetPrismaTrendAsync_SingleImport_ReturnsSnapshot()
    {
        await SeedPrismaImportAsync(alertId: "P-1000", policyName: "Snapshot Policy",
            fileName: "snap.csv");

        var result = await _service.GetPrismaTrendAsync(TestSystemId, null, null);

        result.Imports.Should().HaveCount(1);
        result.NewFindings.Should().Be(1);
        result.ResolvedFindings.Should().Be(0);
        result.PersistentFindings.Should().Be(0);
    }

    [Fact]
    public async Task GetPrismaTrendAsync_NoPrismaImports_Throws()
    {
        var act = () => _service.GetPrismaTrendAsync(TestSystemId, null, null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No Prisma imports found*");
    }

    [Fact]
    public async Task GetPrismaTrendAsync_SpecificImportIds_UsesThose()
    {
        await SeedPrismaImportAsync(alertId: "P-1100", fileName: "specific1.csv");
        await SeedPrismaImportAsync(alertId: "P-1101", fileName: "specific2.csv");
        await SeedPrismaImportAsync(alertId: "P-1102", fileName: "specific3.csv");

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var importRecords = await ctx.ScanImportRecords
            .Where(r => r.ImportType == ScanImportType.PrismaCsv)
            .OrderBy(r => r.ImportedAt)
            .ToListAsync();

        // Ask for just the first two (skip the third)
        var ids = new List<string> { importRecords[0].Id, importRecords[1].Id };
        var result = await _service.GetPrismaTrendAsync(TestSystemId, ids, null);

        result.Imports.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPrismaTrendAsync_SystemNotFound_Throws()
    {
        _rmfServiceMock
            .Setup(r => r.GetSystemAsync("bad-sys", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSystemResult.NotFound("bad-sys"));

        var act = () => _service.GetPrismaTrendAsync("bad-sys", null, null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetPrismaTrendAsync_EmptyImport_ReturnsValidResult()
    {
        // Import with no NIST → findings still created  
        var csv = ValidCsvHeader + "\n" +
            $"P-1200,open,Empty Policy,config,high,azure,Prod,{TestSubscriptionId},eastus,res1,/subs/{TestSubscriptionId}/rg/res1,Microsoft.Storage/storageAccounts,2026-02-01T00:00:00Z,,,CIS Azure,1.1,CIS Test";
        await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, CsvBytes(csv), "empty.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        var result = await _service.GetPrismaTrendAsync(TestSystemId, null, null);

        result.Imports.Should().HaveCount(1);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // T051: Performance — 500-alert CSV import
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportPrismaCsvAsync_500Alerts_CompletesWithinPerformanceBudget()
    {
        // Generate 500-alert CSV
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ValidCsvHeader);
        for (int i = 1; i <= 500; i++)
        {
            var status = i % 5 == 0 ? "resolved" : "open";
            var severity = i % 3 == 0 ? "high" : (i % 3 == 1 ? "medium" : "low");
            sb.AppendLine($"P-PERF{i:D4},{status},Perf Policy {i % 20},config,{severity},azure,Prod,{TestSubscriptionId},eastus,res{i},/subs/{TestSubscriptionId}/rg/res{i},Microsoft.Storage/storageAccounts,2026-01-01T00:00:00Z,,,NIST 800-53 Rev 5,SC-28,SC-28 Test");
        }

        var csvBytes = CsvBytes(sb.ToString());
        var memBefore = GC.GetTotalMemory(true);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await _service.ImportPrismaCsvAsync(
            TestSystemId, TestAssessmentId, csvBytes, "perf-500.csv",
            ImportConflictResolution.Skip, false, TestImporter);

        sw.Stop();
        var memAfter = GC.GetTotalMemory(false);
        var memDelta = memAfter - memBefore;

        result.ErrorMessage.Should().BeNull();
        result.TotalProcessed.Should().Be(500);
        result.Imports[0].FindingsCreated.Should().Be(500);
        sw.Elapsed.TotalSeconds.Should().BeLessThan(15, "500-alert CSV import should complete within 15 seconds");
        memDelta.Should().BeLessThan(512L * 1024 * 1024, "memory delta should stay under 512 MB");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // T052: Performance — 500-alert API JSON import
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImportPrismaApiAsync_500Alerts_CompletesWithinPerformanceBudget()
    {
        // Generate 500-alert JSON array
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        for (int i = 1; i <= 500; i++)
        {
            if (i > 1) sb.Append(',');
            var status = i % 5 == 0 ? "resolved" : "open";
            var severity = i % 3 == 0 ? "high" : (i % 3 == 1 ? "medium" : "low");
            sb.Append($$"""
            {
              "id": "P-JPERF{{i:D4}}",
              "status": "{{status}}",
              "alertTime": 1740652800000,
              "policy": {
                "policyId": "pol-perf-{{i}}",
                "name": "Perf JSON Policy {{i % 20}}",
                "policyType": "config",
                "severity": "{{severity}}",
                "remediable": false,
                "labels": ["Perf"],
                "complianceMetadata": [
                  { "standardName": "NIST 800-53 Rev 5", "requirementId": "SC-28" }
                ]
              },
              "resource": {
                "id": "/subs/{{TestSubscriptionId}}/rg/res{{i}}",
                "name": "res{{i}}",
                "resourceType": "Microsoft.Storage/storageAccounts",
                "region": "eastus",
                "cloudType": "azure",
                "accountId": "{{TestSubscriptionId}}",
                "accountName": "Prod"
              }
            }
            """);
        }
        sb.Append(']');

        var jsonBytes = JsonBytes(sb.ToString());
        var memBefore = GC.GetTotalMemory(true);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await _service.ImportPrismaApiAsync(
            TestSystemId, TestAssessmentId, jsonBytes, "perf-500.json",
            ImportConflictResolution.Skip, false, TestImporter);

        sw.Stop();
        var memAfter = GC.GetTotalMemory(false);
        var memDelta = memAfter - memBefore;

        result.ErrorMessage.Should().BeNull();
        result.TotalProcessed.Should().Be(500);
        result.Imports[0].FindingsCreated.Should().Be(500);
        sw.Elapsed.TotalSeconds.Should().BeLessThan(10, "500-alert JSON import should complete within 10 seconds");
        memDelta.Should().BeLessThan(512L * 1024 * 1024, "memory delta should stay under 512 MB");
    }
}
