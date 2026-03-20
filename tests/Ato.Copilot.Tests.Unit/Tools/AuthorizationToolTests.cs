using System.Text.Json;
using Moq;
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Tools;

/// <summary>
/// Unit tests for Authorization Decision tools (Feature 015 — Phase 10 / US8).
/// Covers T115 (IssueAuthorizationTool), T116 (CreatePoamTool), T117 (ListPoamTool),
/// T118 (GenerateRarTool), T119 (BundleAuthorizationPackageTool),
/// T120 (ShowRiskRegisterTool), T216 (AcceptRiskTool).
/// </summary>
public class AuthorizationToolTests
{
    private readonly Mock<IAuthorizationService> _serviceMock = new();
    private readonly Mock<IDeviationService> _deviationMock = new();

    // ═══════════════════════════════════════════════════════════════════════
    // T122 — IssueAuthorizationTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IssueAuthorization_Ato_ReturnsSuccess()
    {
        // Arrange
        var decision = CreateAuthorizationDecision(AuthorizationDecisionType.Ato);
        _serviceMock
            .Setup(s => s.IssueAuthorizationAsync(
                "sys-1", "ATO", It.IsAny<DateTime?>(), "Low",
                null, null, null, "mcp-user", "MCP User", It.IsAny<CancellationToken>()))
            .ReturnsAsync(decision);

        var tool = CreateIssueAuthorizationTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["decision_type"] = "ATO",
            ["residual_risk_level"] = "Low",
            ["expiration_date"] = "2026-12-31"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("system_id").GetString().Should().Be("sys-1");
        data.GetProperty("decision_type").GetString().Should().Be("Ato");
        data.GetProperty("is_active").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task IssueAuthorization_MissingSystemId_ReturnsError()
    {
        var tool = CreateIssueAuthorizationTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["decision_type"] = "ATO",
            ["residual_risk_level"] = "Low"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task IssueAuthorization_MissingDecisionType_ReturnsError()
    {
        var tool = CreateIssueAuthorizationTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["residual_risk_level"] = "Low"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task IssueAuthorization_InvalidExpirationDate_ReturnsError()
    {
        var tool = CreateIssueAuthorizationTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["decision_type"] = "ATO",
            ["residual_risk_level"] = "Low",
            ["expiration_date"] = "not-a-date"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task IssueAuthorization_ServiceThrows_ReturnsError()
    {
        _serviceMock
            .Setup(s => s.IssueAuthorizationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<RiskAcceptanceInput>?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System 'sys-99' not found."));

        var tool = CreateIssueAuthorizationTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-99",
            ["decision_type"] = "ATO",
            ["residual_risk_level"] = "Low",
            ["expiration_date"] = "2026-12-31"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("AUTHORIZATION_FAILED");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("not found");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T122 — AcceptRiskTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AcceptRisk_ValidInput_ReturnsSuccess()
    {
        var deviation = new Deviation
        {
            Id = "dev-1",
            RegisteredSystemId = "sys-1",
            DeviationType = DeviationType.RiskAcceptance,
            ControlId = "AC-2",
            CatSeverity = CatSeverity.CatII,
            Status = DeviationStatus.Approved,
            Justification = "Justified risk",
            CompensatingControls = "Compensating AC-3",
            ExpirationDate = new DateTime(2026, 12, 31),
            ReviewCycle = "180d",
            RequestedBy = "mcp-user",
            CreatedAt = DateTime.UtcNow,
        };
        _deviationMock
            .Setup(s => s.CreateDeviationAsync(
                "sys-1", It.IsAny<CreateDeviationRequest>(), "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deviation);
        _deviationMock
            .Setup(s => s.ReviewDeviationAsync(
                "dev-1", It.IsAny<ReviewDeviationRequest>(), "mcp-user", "AO", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deviation);

        var tool = CreateAcceptRiskTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["finding_id"] = "find-1",
            ["control_id"] = "AC-2",
            ["cat_severity"] = "CatII",
            ["justification"] = "Justified risk",
            ["expiration_date"] = "2026-12-31",
            ["compensating_control"] = "Compensating AC-3"
        });

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("control_id").GetString().Should().Be("AC-2");
    }

    [Fact]
    public async Task AcceptRisk_MissingFindingId_ReturnsError()
    {
        var tool = CreateAcceptRiskTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "AC-2",
            ["cat_severity"] = "CatII",
            ["justification"] = "test",
            ["expiration_date"] = "2026-12-31"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task AcceptRisk_InvalidExpirationDate_ReturnsError()
    {
        var tool = CreateAcceptRiskTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["finding_id"] = "find-1",
            ["control_id"] = "AC-2",
            ["cat_severity"] = "CatII",
            ["justification"] = "test",
            ["expiration_date"] = "bad-date"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T123 — ShowRiskRegisterTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ShowRiskRegister_ActiveFilter_ReturnsSuccess()
    {
        var register = new RiskRegister
        {
            SystemId = "sys-1",
            TotalAcceptances = 2,
            ActiveCount = 2,
            ExpiredCount = 0,
            RevokedCount = 0,
            Acceptances = new List<RiskAcceptanceDetail>
            {
                new() { Id = "ra-1", ControlId = "AC-2", CatSeverity = "CatII", Justification = "ok", ExpirationDate = DateTime.UtcNow.AddDays(90), AcceptedAt = DateTime.UtcNow, AcceptedBy = "ao", Status = "active" },
                new() { Id = "ra-2", ControlId = "AC-3", CatSeverity = "CatIII", Justification = "low risk", ExpirationDate = DateTime.UtcNow.AddDays(180), AcceptedAt = DateTime.UtcNow, AcceptedBy = "ao", Status = "active" }
            }
        };

        _serviceMock
            .Setup(s => s.GetRiskRegisterAsync("sys-1", "active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(register);

        var tool = CreateShowRiskRegisterTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["status_filter"] = "active"
        });

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("total_acceptances").GetInt32().Should().Be(2);
        data.GetProperty("active_count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ShowRiskRegister_MissingSystemId_ReturnsError()
    {
        var tool = CreateShowRiskRegisterTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T124 — CreatePoamTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreatePoam_ValidInput_ReturnsSuccess()
    {
        var poam = CreatePoamItem();
        _serviceMock
            .Setup(s => s.CreatePoamAsync(
                "sys-1", "Weak config", "CM-6", "CatII", "John Doe",
                It.IsAny<DateTime>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(poam);

        var tool = CreateCreatePoamTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["weakness"] = "Weak config",
            ["control_id"] = "CM-6",
            ["cat_severity"] = "CatII",
            ["poc"] = "John Doe",
            ["scheduled_completion"] = "2026-06-30"
        });

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("control_id").GetString().Should().Be("CM-6");
        data.GetProperty("cat_severity").GetString().Should().Be("CatII");
        data.GetProperty("status").GetString().Should().Be("Ongoing");
    }

    [Fact]
    public async Task CreatePoam_MissingWeakness_ReturnsError()
    {
        var tool = CreateCreatePoamTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["control_id"] = "CM-6",
            ["cat_severity"] = "CatII",
            ["poc"] = "John",
            ["scheduled_completion"] = "2026-06-30"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task CreatePoam_InvalidDate_ReturnsError()
    {
        var tool = CreateCreatePoamTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["weakness"] = "weak",
            ["control_id"] = "CM-6",
            ["cat_severity"] = "CatII",
            ["poc"] = "John",
            ["scheduled_completion"] = "not-a-date"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T124 — ListPoamTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListPoam_ReturnsItems()
    {
        var items = new List<PoamItem> { CreatePoamItem(), CreatePoamItem() };
        _serviceMock
            .Setup(s => s.ListPoamAsync("sys-1", null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var tool = CreateListPoamTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        root.GetProperty("data").GetProperty("total_items").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ListPoam_MissingSystemId_ReturnsError()
    {
        var tool = CreateListPoamTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T125 — GenerateRarTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateRar_ReturnsDocument()
    {
        var rar = new RarDocument
        {
            SystemId = "sys-1",
            AssessmentId = "assess-1",
            GeneratedAt = DateTime.UtcNow,
            Format = "markdown",
            ExecutiveSummary = "5 findings. Aggregate risk: Medium.",
            AggregateRiskLevel = "Medium",
            FamilyRisks = new List<FamilyRiskResult>
            {
                new() { Family = "AC", FamilyName = "Access Control", TotalFindings = 3, OpenFindings = 2, AcceptedFindings = 0, RiskLevel = "Medium" }
            },
            CatBreakdown = new CatBreakdown { CatI = 0, CatII = 2, CatIII = 3 },
            Content = "# RAR\n\nContent here"
        };

        _serviceMock
            .Setup(s => s.GenerateRarAsync("sys-1", "assess-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rar);

        var tool = CreateGenerateRarTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["assessment_id"] = "assess-1"
        });

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("aggregate_risk_level").GetString().Should().Be("Medium");
        data.GetProperty("cat_breakdown").GetProperty("cat_ii").GetInt32().Should().Be(2);
        data.GetProperty("content").GetString().Should().Contain("RAR");
    }

    [Fact]
    public async Task GenerateRar_MissingAssessmentId_ReturnsError()
    {
        var tool = CreateGenerateRarTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T126 — BundleAuthorizationPackageTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BundlePackage_ReturnsDocuments()
    {
        var package = new AuthorizationPackageBundle
        {
            SystemId = "sys-1",
            GeneratedAt = DateTime.UtcNow,
            Format = "markdown",
            IncludesEvidence = false,
            Documents = new List<PackageDocument>
            {
                new() { Name = "System Security Plan", FileName = "ssp.md", DocumentType = "SSP", Content = "# SSP", Status = "generated" },
                new() { Name = "Security Assessment Report", FileName = "sar.md", DocumentType = "SAR", Content = "# SAR", Status = "generated" },
                new() { Name = "Risk Assessment Report", FileName = "rar.md", DocumentType = "RAR", Content = "# RAR", Status = "generated" },
                new() { Name = "Plan of Action and Milestones", FileName = "poam.md", DocumentType = "POAM", Content = "# POAM", Status = "generated" },
                new() { Name = "Customer Responsibility Matrix", FileName = "crm.md", DocumentType = "CRM", Content = "# CRM", Status = "not_available" },
                new() { Name = "Authorization Letter", FileName = "ato-letter.md", DocumentType = "ATO_LETTER", Content = "# ATO Letter", Status = "generated" }
            }
        };

        _serviceMock
            .Setup(s => s.BundlePackageAsync("sys-1", null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);

        var tool = CreateBundleAuthorizationPackageTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1"
        });

        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("document_count").GetInt32().Should().Be(6);
        data.GetProperty("system_id").GetString().Should().Be("sys-1");
    }

    [Fact]
    public async Task BundlePackage_MissingSystemId_ReturnsError()
    {
        var tool = CreateBundleAuthorizationPackageTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task BundlePackage_ServiceThrows_ReturnsError()
    {
        _serviceMock
            .Setup(s => s.BundlePackageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System 'sys-99' not found."));

        var tool = CreateBundleAuthorizationPackageTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-99"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("BUNDLE_PACKAGE_FAILED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Factory methods
    // ═══════════════════════════════════════════════════════════════════════

    private IssueAuthorizationTool CreateIssueAuthorizationTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<IssueAuthorizationTool>>());

    private AcceptRiskTool CreateAcceptRiskTool() =>
        new(_deviationMock.Object, Mock.Of<ILogger<AcceptRiskTool>>());

    private ShowRiskRegisterTool CreateShowRiskRegisterTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<ShowRiskRegisterTool>>());

    private CreatePoamTool CreateCreatePoamTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<CreatePoamTool>>());

    private ListPoamTool CreateListPoamTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<ListPoamTool>>());

    private GenerateRarTool CreateGenerateRarTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<GenerateRarTool>>());

    private BundleAuthorizationPackageTool CreateBundleAuthorizationPackageTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<BundleAuthorizationPackageTool>>());

    // ═══════════════════════════════════════════════════════════════════════
    // Test data helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static AuthorizationDecision CreateAuthorizationDecision(
        AuthorizationDecisionType type = AuthorizationDecisionType.Ato)
    {
        return new AuthorizationDecision
        {
            RegisteredSystemId = "sys-1",
            DecisionType = type,
            DecisionDate = DateTime.UtcNow,
            ExpirationDate = DateTime.UtcNow.AddYears(3),
            ResidualRiskLevel = ComplianceRiskLevel.Low,
            ComplianceScoreAtDecision = 95.0,
            FindingsAtDecision = "{\"catI\":0,\"catII\":1,\"catIII\":3}",
            IssuedBy = "mcp-user",
            IssuedByName = "MCP User",
            IsActive = true
        };
    }

    private static RiskAcceptance CreateRiskAcceptance()
    {
        return new RiskAcceptance
        {
            AuthorizationDecisionId = Guid.NewGuid().ToString(),
            FindingId = "find-1",
            ControlId = "AC-2",
            CatSeverity = CatSeverity.CatII,
            Justification = "Justified risk",
            CompensatingControl = "Compensating AC-3",
            ExpirationDate = DateTime.UtcNow.AddDays(180),
            AcceptedBy = "mcp-user",
            AcceptedAt = DateTime.UtcNow,
            IsActive = true
        };
    }

    private static PoamItem CreatePoamItem()
    {
        return new PoamItem
        {
            RegisteredSystemId = "sys-1",
            Weakness = "Weak configuration",
            WeaknessSource = "Manual",
            SecurityControlNumber = "CM-6",
            CatSeverity = CatSeverity.CatII,
            PointOfContact = "John Doe",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(90),
            Status = PoamStatus.Ongoing,
            CreatedAt = DateTime.UtcNow
        };
    }
}
