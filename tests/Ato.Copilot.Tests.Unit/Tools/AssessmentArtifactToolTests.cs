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
/// Unit tests for Assessment Artifact tools (Feature 015 — Phase 9 / US7).
/// Covers T104 (AssessControlTool), T105 (TakeSnapshotTool),
/// T106 (CompareSnapshotsTool), T107 (Evidence tools), and T213 (GenerateSarTool).
/// </summary>
public class AssessmentArtifactToolTests
{
    private readonly Mock<IAssessmentArtifactService> _serviceMock = new();

    // ═══════════════════════════════════════════════════════════════════════
    // T104 — AssessControlTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AssessControl_Satisfied_ReturnsSuccess()
    {
        // Arrange
        var effectiveness = CreateControlEffectiveness("AC-2", EffectivenessDetermination.Satisfied);
        _serviceMock
            .Setup(s => s.AssessControlAsync(
                "assess-1", "AC-2", "Satisfied", "Test", It.IsAny<List<string>?>(),
                "Looks good", null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(effectiveness);

        var tool = CreateAssessControlTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = "assess-1",
            ["control_id"] = "AC-2",
            ["determination"] = "Satisfied",
            ["method"] = "Test",
            ["notes"] = "Looks good"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("control_id").GetString().Should().Be("AC-2");
        data.GetProperty("determination").GetString().Should().Be("Satisfied");
    }

    [Fact]
    public async Task AssessControl_SatisfiedWithNoValidationLinks_ReturnsNonBlockingWarning()
    {
        // Arrange
        var effectiveness = CreateControlEffectiveness("AC-2", EffectivenessDetermination.Satisfied);
        _serviceMock
            .Setup(service => service.AssessControlAsync(
                "assess-1", "AC-2", "Satisfied", null, It.IsAny<List<string>?>(),
                null, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(effectiveness);
        var validationLinks = new Mock<IControlValidationLinkService>();
        validationLinks
            .Setup(service => service.GetLinksAsync(effectiveness.RegisteredSystemId, "AC-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var tool = new AssessControlTool(
            _serviceMock.Object,
            Mock.Of<ILogger<AssessControlTool>>(),
            validationLinks.Object);

        // Act
        var result = await tool.ExecuteCoreAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = "assess-1",
            ["control_id"] = "AC-2",
            ["determination"] = "Satisfied",
        });
        using var document = JsonDocument.Parse(result);

        // Assert
        document.RootElement.GetProperty("status").GetString().Should().Be("success");
        document.RootElement.GetProperty("warnings")[0].GetString()
            .Should().Be("No validation links attached to this control. Consider adding evidence.");
    }

    [Fact]
    public async Task AssessControl_OtherThanSatisfied_WithCat_ReturnsSuccess()
    {
        // Arrange
        var effectiveness = CreateControlEffectiveness("AC-3", EffectivenessDetermination.OtherThanSatisfied, CatSeverity.CatII);
        _serviceMock
            .Setup(s => s.AssessControlAsync(
                "assess-1", "AC-3", "OtherThanSatisfied", null, It.IsAny<List<string>?>(),
                null, "CatII", "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(effectiveness);

        var tool = CreateAssessControlTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = "assess-1",
            ["control_id"] = "AC-3",
            ["determination"] = "OtherThanSatisfied",
            ["cat_severity"] = "CatII"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("control_id").GetString().Should().Be("AC-3");
        data.GetProperty("cat_severity").GetString().Should().Be("CatII");
    }

    [Fact]
    public async Task AssessControl_OtherThanSatisfied_MissingCat_ReturnsError()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.AssessControlAsync(
                "assess-1", "AC-3", "OtherThanSatisfied", null, It.IsAny<List<string>?>(),
                null, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("CAT severity is required for OtherThanSatisfied determinations"));

        var tool = CreateAssessControlTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = "assess-1",
            ["control_id"] = "AC-3",
            ["determination"] = "OtherThanSatisfied"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("message").GetString().Should().Contain("CAT severity");
    }

    [Fact]
    public async Task AssessControl_MissingRequiredParams_ReturnsError()
    {
        var tool = CreateAssessControlTool();

        // Act — missing assessment_id
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["control_id"] = "AC-2",
            ["determination"] = "Satisfied"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task AssessControl_WithEvidenceIds_ParsesCorrectly()
    {
        // Arrange
        var effectiveness = CreateControlEffectiveness("AC-5", EffectivenessDetermination.Satisfied);
        effectiveness.EvidenceIds = new List<string> { "ev-1", "ev-2" };
        _serviceMock
            .Setup(s => s.AssessControlAsync(
                "assess-1", "AC-5", "Satisfied", null, It.IsAny<List<string>?>(),
                null, null, "mcp-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(effectiveness);

        var tool = CreateAssessControlTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = "assess-1",
            ["control_id"] = "AC-5",
            ["determination"] = "Satisfied",
            ["evidence_ids"] = "ev-1,ev-2"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T105 — TakeSnapshotTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TakeSnapshot_ReturnsSnapshotWithHash()
    {
        // Arrange
        var hash = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var snapshot = CreateSnapshot(Guid.NewGuid(), 85.0, hash);
        _serviceMock
            .Setup(s => s.TakeSnapshotAsync("sys-1", "assess-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var tool = CreateTakeSnapshotTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["assessment_id"] = "assess-1"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("snapshot_id").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("integrity_hash").GetString().Should().HaveLength(64);
        data.GetProperty("is_immutable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task TakeSnapshot_MissingSystemId_ReturnsError()
    {
        var tool = CreateTakeSnapshotTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = "assess-1"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task TakeSnapshot_SystemNotFound_ReturnsError()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.TakeSnapshotAsync("bad-sys", "assess-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("System not found: bad-sys"));

        var tool = CreateTakeSnapshotTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "bad-sys",
            ["assessment_id"] = "assess-1"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("not found");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T106 — CompareSnapshotsTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareSnapshots_IdenticalSnapshots_ReturnsZeroDelta()
    {
        // Arrange
        var comparison = new SnapshotComparison
        {
            SnapshotA = new SnapshotSummary { Id = "snap-a", ComplianceScore = 85.0, TotalControls = 10, PassedControls = 8, FailedControls = 2, IntegrityHash = "hash1" },
            SnapshotB = new SnapshotSummary { Id = "snap-b", ComplianceScore = 85.0, TotalControls = 10, PassedControls = 8, FailedControls = 2, IntegrityHash = "hash2" },
            ScoreDelta = 0.0,
            UnchangedCount = 10,
            NewFindings = 0,
            ResolvedFindings = 0
        };
        _serviceMock
            .Setup(s => s.CompareSnapshotsAsync("snap-1", "snap-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(comparison);

        var tool = CreateCompareSnapshotsTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["snapshot_id_a"] = "snap-1",
            ["snapshot_id_b"] = "snap-2"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("score_delta").GetDouble().Should().Be(0.0);
        data.GetProperty("unchanged_count").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task CompareSnapshots_DifferentSnapshots_ShowsChanges()
    {
        // Arrange
        var comparison = new SnapshotComparison
        {
            SnapshotA = new SnapshotSummary { Id = "snap-1", ComplianceScore = 70.0, TotalControls = 10, PassedControls = 7, FailedControls = 3, IntegrityHash = "hashA" },
            SnapshotB = new SnapshotSummary { Id = "snap-2", ComplianceScore = 90.0, TotalControls = 10, PassedControls = 9, FailedControls = 1, IntegrityHash = "hashB" },
            ScoreDelta = 20.0,
            NewlySatisfied = new List<string> { "AC-3", "AU-6" },
            NewlyOtherThanSatisfied = new List<string>(),
            UnchangedCount = 8,
            NewFindings = 0,
            ResolvedFindings = 2
        };
        _serviceMock
            .Setup(s => s.CompareSnapshotsAsync("snap-1", "snap-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(comparison);

        var tool = CreateCompareSnapshotsTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["snapshot_id_a"] = "snap-1",
            ["snapshot_id_b"] = "snap-2"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("score_delta").GetDouble().Should().Be(20.0);
        data.GetProperty("newly_satisfied").GetArrayLength().Should().Be(2);
        data.GetProperty("resolved_findings").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task CompareSnapshots_MissingSnapshot_ReturnsError()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.CompareSnapshotsAsync("snap-1", "nonexistent", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Snapshot not found: nonexistent"));

        var tool = CreateCompareSnapshotsTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["snapshot_id_a"] = "snap-1",
            ["snapshot_id_b"] = "nonexistent"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("not found");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T107 — Evidence Tools Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyEvidence_IntegrityPass_ReturnsVerified()
    {
        // Arrange
        var hash = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var verification = new EvidenceVerificationResult
        {
            EvidenceId = "ev-1",
            ControlId = "AC-2",
            OriginalHash = hash,
            RecomputedHash = hash,
            Status = "verified",
            CollectorIdentity = "sca@example.com",
            CollectionMethod = "automated_scan",
            IntegrityVerifiedAt = DateTime.UtcNow
        };
        _serviceMock
            .Setup(s => s.VerifyEvidenceAsync("ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);

        var tool = CreateVerifyEvidenceTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["evidence_id"] = "ev-1"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("verification_status").GetString().Should().Be("verified");
        data.GetProperty("original_hash").GetString().Should().Be(hash);
        data.GetProperty("recomputed_hash").GetString().Should().Be(hash);
        data.GetProperty("collector_identity").GetString().Should().Be("sca@example.com");
    }

    [Fact]
    public async Task VerifyEvidence_IntegrityFail_ReturnsTampered()
    {
        // Arrange
        var verification = new EvidenceVerificationResult
        {
            EvidenceId = "ev-2",
            ControlId = "AC-3",
            OriginalHash = "aaaa",
            RecomputedHash = "bbbb",
            Status = "tampered",
            CollectorIdentity = "admin@example.com",
            CollectionMethod = "manual_upload"
        };
        _serviceMock
            .Setup(s => s.VerifyEvidenceAsync("ev-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);

        var tool = CreateVerifyEvidenceTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["evidence_id"] = "ev-2"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("verification_status").GetString().Should().Be("tampered");
        data.GetProperty("original_hash").GetString().Should().NotBe(data.GetProperty("recomputed_hash").GetString());
    }

    [Fact]
    public async Task VerifyEvidence_EvidenceNotFound_ReturnsError()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.VerifyEvidenceAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Evidence not found: nonexistent"));

        var tool = CreateVerifyEvidenceTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["evidence_id"] = "nonexistent"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task CheckEvidenceCompleteness_ReturnsReport()
    {
        // Arrange
        var report = new EvidenceCompletenessReport
        {
            SystemId = "sys-1",
            AssessmentId = "assess-1",
            CompletenessPercentage = 75.0,
            TotalControls = 8,
            ControlsWithEvidence = 6,
            ControlsWithoutEvidence = 2,
            ControlsWithUnverifiedEvidence = 1,
            ControlStatuses = new List<ControlEvidenceStatus>
            {
                new() { ControlId = "AC-2", Status = "verified", EvidenceCount = 2, VerifiedCount = 2 },
                new() { ControlId = "AC-3", Status = "unverified", EvidenceCount = 1, VerifiedCount = 0 },
                new() { ControlId = "AC-4", Status = "missing", EvidenceCount = 0, VerifiedCount = 0 }
            }
        };
        _serviceMock
            .Setup(s => s.CheckEvidenceCompletenessAsync("sys-1", "assess-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var tool = CreateCheckEvidenceCompletenessTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["assessment_id"] = "assess-1"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("completeness_percentage").GetDouble().Should().Be(75.0);
        data.GetProperty("total_controls").GetInt32().Should().Be(8);
        data.GetProperty("controls_with_evidence").GetInt32().Should().Be(6);
        data.GetProperty("controls_without_evidence").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task CheckEvidenceCompleteness_WithFamilyFilter_FiltersCorrectly()
    {
        // Arrange
        var report = new EvidenceCompletenessReport
        {
            SystemId = "sys-1",
            CompletenessPercentage = 100.0,
            TotalControls = 3,
            ControlsWithEvidence = 3,
            ControlsWithoutEvidence = 0,
            ControlStatuses = new List<ControlEvidenceStatus>
            {
                new() { ControlId = "AU-2", Status = "verified", EvidenceCount = 1, VerifiedCount = 1 },
                new() { ControlId = "AU-3", Status = "verified", EvidenceCount = 2, VerifiedCount = 2 },
                new() { ControlId = "AU-6", Status = "verified", EvidenceCount = 1, VerifiedCount = 1 }
            }
        };
        _serviceMock
            .Setup(s => s.CheckEvidenceCompletenessAsync("sys-1", null, "AU", It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);

        var tool = CreateCheckEvidenceCompletenessTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["family_filter"] = "AU"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("completeness_percentage").GetDouble().Should().Be(100.0);
        data.GetProperty("control_statuses").GetArrayLength().Should().Be(3);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T213 — GenerateSarTool Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GenerateSar_ReturnsMarkdownDocument()
    {
        // Arrange
        var sar = new SarDocument
        {
            SystemId = "sys-1",
            AssessmentId = "assess-1",
            Format = "markdown",
            Content = "# Security Assessment Report\n\nSystem: Test System",
            ComplianceScore = 87.5,
            ControlsAssessed = 20,
            ControlsSatisfied = 17,
            ControlsOtherThanSatisfied = 3,
            CatIFindings = 0,
            CatIIFindings = 2,
            CatIIIFindings = 1,
            FamilyResults = new List<FamilyAssessmentResult>
            {
                new() { Family = "AC", ControlsAssessed = 10, ControlsSatisfied = 8, ControlsOtherThanSatisfied = 2, CatBreakdown = new() { ["CatII"] = 1, ["CatIII"] = 1 } },
                new() { Family = "AU", ControlsAssessed = 10, ControlsSatisfied = 9, ControlsOtherThanSatisfied = 1, CatBreakdown = new() { ["CatII"] = 1 } }
            }
        };
        _serviceMock
            .Setup(s => s.GenerateSarAsync("sys-1", "assess-1", "markdown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sar);

        var tool = CreateGenerateSarTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["assessment_id"] = "assess-1"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("compliance_score").GetDouble().Should().Be(87.5);
        data.GetProperty("controls_assessed").GetInt32().Should().Be(20);
        data.GetProperty("controls_satisfied").GetInt32().Should().Be(17);
        var catBreakdown = data.GetProperty("cat_breakdown");
        catBreakdown.GetProperty("cat_i").GetInt32().Should().Be(0);
        catBreakdown.GetProperty("cat_ii").GetInt32().Should().Be(2);
        catBreakdown.GetProperty("cat_iii").GetInt32().Should().Be(1);
        data.GetProperty("content").GetString().Should().Contain("Security Assessment Report");
        data.GetProperty("family_results").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GenerateSar_MissingSystemId_ReturnsError()
    {
        var tool = CreateGenerateSarTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["assessment_id"] = "assess-1"
        });

        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
    }

    [Fact]
    public async Task GenerateSar_AssessmentNotFound_ReturnsError()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GenerateSarAsync("sys-1", "bad-assess", "markdown", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Assessment not found: bad-assess"));

        var tool = CreateGenerateSarTool();

        // Act
        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "sys-1",
            ["assessment_id"] = "bad-assess"
        });

        // Assert
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("not found");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tool factory methods
    // ═══════════════════════════════════════════════════════════════════════

    private AssessControlTool CreateAssessControlTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<AssessControlTool>>());

    private TakeSnapshotTool CreateTakeSnapshotTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<TakeSnapshotTool>>());

    private CompareSnapshotsTool CreateCompareSnapshotsTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<CompareSnapshotsTool>>());

    private VerifyEvidenceTool CreateVerifyEvidenceTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<VerifyEvidenceTool>>());

    private CheckEvidenceCompletenessTool CreateCheckEvidenceCompletenessTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<CheckEvidenceCompletenessTool>>());

    private GenerateSarTool CreateGenerateSarTool() =>
        new(_serviceMock.Object, Mock.Of<ILogger<GenerateSarTool>>());

    // ═══════════════════════════════════════════════════════════════════════
    // Test data helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static ControlEffectiveness CreateControlEffectiveness(
        string controlId,
        EffectivenessDetermination determination,
        CatSeverity? catSeverity = null)
    {
        return new ControlEffectiveness
        {
            Id = Guid.NewGuid().ToString(),
            AssessmentId = "assess-1",
            RegisteredSystemId = "sys-1",
            ControlId = controlId,
            Determination = determination,
            AssessmentMethod = "Test",
            AssessorId = "mcp-user",
            AssessedAt = DateTime.UtcNow,
            CatSeverity = catSeverity,
            EvidenceIds = new List<string>()
        };
    }

    private static ComplianceSnapshot CreateSnapshot(Guid id, double score, string hash)
    {
        return new ComplianceSnapshot
        {
            Id = id,
            CapturedAt = DateTimeOffset.UtcNow,
            ComplianceScore = score,
            IntegrityHash = hash,
            IsImmutable = true,
            TotalControls = 10,
            PassedControls = (int)(10 * score / 100.0),
            FailedControls = 10 - (int)(10 * score / 100.0)
        };
    }
}
