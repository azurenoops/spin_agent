using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services.Engines.Remediation;

/// <summary>
/// Unit tests for AzureArmRemediationService: snapshot capture, ARM operations,
/// restore from snapshot, dry-run, resource not found handling.
/// Note: ArmClient operations are tested via integration tests; unit tests verify
/// orchestration logic, operation mapping, and error handling.
/// </summary>
public class AzureArmRemediationServiceTests
{
    private readonly Mock<ILogger<AzureArmRemediationService>> _loggerMock = new();

    private static ComplianceFinding CreateFinding(
        string controlId = "SC-8",
        string family = "SC",
        string title = "TLS version too low",
        RemediationType remType = RemediationType.ResourceConfiguration) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ControlId = controlId,
            ControlFamily = family,
            Title = title,
            Description = $"Non-compliance for {controlId}",
            Severity = FindingSeverity.High,
            Status = FindingStatus.Open,
            ResourceId = "/subscriptions/sub-1/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/test",
            ResourceType = "Microsoft.Storage/storageAccounts",
            RemediationGuidance = $"Fix {controlId}",
            RemediationType = remType,
            AutoRemediable = true,
            Source = "PolicyInsights",
            SubscriptionId = "sub-1"
        };

    private static RemediationExecutionOptions CreateOptions(bool dryRun = true) =>
        new() { DryRun = dryRun };

    // ─── DetermineArmOperation (tested via ExecuteArmRemediationAsync dry run) ─

    [Fact]
    public async Task ExecuteArmRemediationAsync_TlsFinding_DryRun_ReturnsTlsOperation()
    {
        // ArmClient is not used in dry-run mode; pass a mock
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding("SC-8", "SC", "TLS version is below minimum");

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        result.DryRun.Should().BeTrue();
        result.TierUsed.Should().Be(3);
        result.ChangesApplied.Should().ContainSingle(s => s.Contains("TlsVersionUpdate"));
    }

    [Fact]
    public async Task ExecuteArmRemediationAsync_HttpsFinding_DryRun_ReturnsHttpsOperation()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding("SC-8", "SC", "HTTPS not enforced");

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.ChangesApplied.Should().ContainSingle(s => s.Contains("HttpsEnforcement"));
    }

    [Fact]
    public async Task ExecuteArmRemediationAsync_EncryptionFinding_DryRun_ReturnsEncryptionOperation()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding("SC-28", "SC", "Encryption at rest not enabled");

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.ChangesApplied.Should().ContainSingle(s => s.Contains("Encryption"));
    }

    [Fact]
    public async Task ExecuteArmRemediationAsync_NsgFinding_DryRun_ReturnsNsgOperation()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding("SC-7", "SC", "NSG rules too permissive");

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.ChangesApplied.Should().ContainSingle(s => s.Contains("NsgConfiguration"));
    }

    [Fact]
    public async Task ExecuteArmRemediationAsync_DiagnosticFinding_DryRun_ReturnsDiagnosticOperation()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding("AU-2", "AU", "Diagnostic logging not enabled");

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.ChangesApplied.Should().ContainSingle(s => s.Contains("DiagnosticSettings"));
    }

    [Fact]
    public async Task ExecuteArmRemediationAsync_AlertFinding_DryRun_ReturnsAlertOperation()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding("AU-5", "AU", "Alert rules not configured");

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.ChangesApplied.Should().ContainSingle(s => s.Contains("AlertRules"));
    }

    [Fact]
    public async Task ExecuteArmRemediationAsync_RetentionFinding_DryRun_ReturnsRetentionOperation()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding("AU-11", "AU", "Retention period below 90 days");

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.ChangesApplied.Should().ContainSingle(s => s.Contains("LogRetention"));
    }

    [Fact]
    public async Task ExecuteArmRemediationAsync_PolicyAssignment_DryRun_ReturnsPolicyOperation()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding("CM-2", "CM", "Policy not assigned", RemediationType.PolicyAssignment);

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.ChangesApplied.Should().ContainSingle(s => s.Contains("PolicyAssignment"));
    }

    // ─── Execution Tracking ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteArmRemediationAsync_DryRun_SetsCorrectMetadata()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding();

        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: true));

        result.FindingId.Should().Be(finding.Id);
        result.SubscriptionId.Should().Be("sub-1");
        result.TierUsed.Should().Be(3);
        result.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.CompletedAt.Should().NotBeNull();
        result.Duration.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteArmRemediationAsync_LiveOperationWithoutImplementation_ReturnsFailure()
    {
        // Arrange
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var finding = CreateFinding();

        // Act
        var result = await service.ExecuteArmRemediationAsync(finding, CreateOptions(dryRun: false));

        // Assert
        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("not implemented");
        result.ChangesApplied.Should().BeEmpty();
        result.StepsExecuted.Should().Be(0);
        result.CompletedAt.Should().NotBeNull();
        result.Duration.Should().NotBeNull();
    }

    // ─── RestoreFromSnapshotAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RestoreFromSnapshotAsync_ValidSnapshotWithoutImplementation_ReturnsFailure()
    {
        // Arrange
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);
        var resourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/test";
        var snapshot = """{"resourceId": "/test", "capturedAt": "2024-01-01", "properties": null}""";

        // Act
        var result = await service.RestoreFromSnapshotAsync(resourceId, snapshot);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not implemented");
        result.RollbackSteps.Should().BeEmpty();
        result.RestoredSnapshot.Should().BeNull();
    }

    [Fact]
    public async Task RestoreFromSnapshotAsync_EmptySnapshot_ReturnsFailure()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);

        var result = await service.RestoreFromSnapshotAsync("/test/resource", "");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No snapshot");
    }

    [Fact]
    public async Task RestoreFromSnapshotAsync_NullSnapshot_ReturnsFailure()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);

        var result = await service.RestoreFromSnapshotAsync("/test/resource", null!);

        result.Success.Should().BeFalse();
    }

    // ─── CaptureResourceSnapshotAsync ─────────────────────────────────────────

    [Fact]
    public async Task CaptureResourceSnapshotAsync_EmptyResourceId_ReturnsNull()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);

        var snapshot = await service.CaptureResourceSnapshotAsync("");

        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task CaptureResourceSnapshotAsync_WhitespaceResourceId_ReturnsNull()
    {
        var mockArmClient = new Mock<ArmClient>();
        var service = new AzureArmRemediationService(mockArmClient.Object, _loggerMock.Object);

        var snapshot = await service.CaptureResourceSnapshotAsync("   ");

        snapshot.Should().BeNull();
    }
}
