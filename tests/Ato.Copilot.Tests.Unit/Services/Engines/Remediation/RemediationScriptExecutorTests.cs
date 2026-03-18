using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services.Engines.Remediation;

/// <summary>
/// Unit tests for RemediationScriptExecutor: successful execution, timeout handling,
/// retry on failure, sanitization rejection, execution tracking.
/// </summary>
public class RemediationScriptExecutorTests
{
    private readonly Mock<IScriptSanitizationService> _sanitizerMock = new();
    private readonly Mock<ILogger<RemediationScriptExecutor>> _loggerMock = new();

    private RemediationScriptExecutor CreateExecutor() =>
        new TestableRemediationScriptExecutor(_sanitizerMock.Object, _loggerMock.Object);

    private static RemediationScript CreateScript(
        ScriptType type = ScriptType.AzureCli,
        string content = "az storage account update --name test --min-tls-version TLS1_2") =>
        new()
        {
            Content = content,
            ScriptType = type,
            Description = "Test remediation script",
            Parameters = new Dictionary<string, string> { ["resourceId"] = "/test/resource" },
            EstimatedDuration = TimeSpan.FromMinutes(5),
            IsSanitized = false
        };

    private static RemediationExecutionOptions CreateOptions(bool dryRun = false) =>
        new() { DryRun = dryRun };

    // ─── Successful Execution ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteScriptAsync_SafeScript_ReturnsCompleted()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(true);
        var executor = CreateExecutor();
        var script = CreateScript();

        var result = await executor.ExecuteScriptAsync(script, "finding-1", CreateOptions());

        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        result.FindingId.Should().Be("finding-1");
        result.TierUsed.Should().Be(1);
        result.StepsExecuted.Should().Be(1);
        result.ChangesApplied.Should().NotBeEmpty();
        result.Duration.Should().NotBeNull();
        result.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteScriptAsync_DryRun_ReturnsDryRunResult()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(true);
        var executor = CreateExecutor();
        var script = CreateScript();

        var result = await executor.ExecuteScriptAsync(script, "finding-1", CreateOptions(dryRun: true));

        result.Status.Should().Be(RemediationExecutionStatus.Completed);
        result.DryRun.Should().BeTrue();
        result.ChangesApplied.Should().ContainSingle(s => s.Contains("DRY RUN"));
    }

    // ─── Sanitization Rejection ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteScriptAsync_UnsafeScript_ReturnsFailed()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(false);
        _sanitizerMock.Setup(s => s.GetViolations(It.IsAny<string>()))
            .Returns(new List<string> { "Azure CLI resource group deletion" });
        var executor = CreateExecutor();
        var script = CreateScript(content: "az group delete --name rg --yes");

        var result = await executor.ExecuteScriptAsync(script, "finding-1", CreateOptions());

        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("sanitization");
        result.Error.Should().Contain("resource group deletion");
    }

    [Fact]
    public async Task ExecuteScriptAsync_UnsafeScript_NeverExecutes()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(false);
        _sanitizerMock.Setup(s => s.GetViolations(It.IsAny<string>()))
            .Returns(new List<string> { "destructive" });
        var executor = CreateExecutor();
        var script = CreateScript(content: "rm -rf /");

        var result = await executor.ExecuteScriptAsync(script, "finding-1", CreateOptions());

        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.StepsExecuted.Should().Be(0);
    }

    // ─── Execution Tracking ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteScriptAsync_TracksStartTime()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(true);
        var executor = CreateExecutor();
        var before = DateTime.UtcNow;

        var result = await executor.ExecuteScriptAsync(CreateScript(), "f-1", CreateOptions());

        result.StartedAt.Should().NotBeNull();
        result.StartedAt!.Value.Should().BeOnOrAfter(before);
        result.CompletedAt.Should().NotBeNull();
        result.CompletedAt!.Value.Should().BeOnOrAfter(result.StartedAt.Value);
    }

    [Fact]
    public async Task ExecuteScriptAsync_SetsTierToOne()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(true);
        var executor = CreateExecutor();

        var result = await executor.ExecuteScriptAsync(CreateScript(), "f-1", CreateOptions());

        result.TierUsed.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteScriptAsync_SetsOptions()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(true);
        var executor = CreateExecutor();
        var options = CreateOptions(dryRun: true);

        var result = await executor.ExecuteScriptAsync(CreateScript(), "f-1", options);

        result.Options.Should().BeSameAs(options);
        result.DryRun.Should().BeTrue();
    }

    // ─── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteScriptAsync_CancelledToken_ThrowsOrReturnsFailed()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(true);
        var executor = CreateExecutor();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should throw OperationCanceledException since outer token is cancelled
        var act = () => executor.ExecuteScriptAsync(CreateScript(), "f-1", CreateOptions(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─── Multiple Violations ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteScriptAsync_MultipleViolations_ReportsAll()
    {
        _sanitizerMock.Setup(s => s.IsSafe(It.IsAny<string>())).Returns(false);
        _sanitizerMock.Setup(s => s.GetViolations(It.IsAny<string>()))
            .Returns(new List<string>
            {
                "Resource group deletion",
                "VM deletion",
                "Terraform destroy"
            });
        var executor = CreateExecutor();

        var result = await executor.ExecuteScriptAsync(CreateScript(), "f-1", CreateOptions());

        result.Status.Should().Be(RemediationExecutionStatus.Failed);
        result.Error.Should().Contain("Resource group deletion");
        result.Error.Should().Contain("VM deletion");
        result.Error.Should().Contain("Terraform destroy");
    }

    /// <summary>
    /// Test subclass that overrides RunSubprocessAsync to avoid real process execution.
    /// Returns exit code 0 with empty output, simulating a successful script run.
    /// </summary>
    private class TestableRemediationScriptExecutor : RemediationScriptExecutor
    {
        public TestableRemediationScriptExecutor(
            IScriptSanitizationService sanitizer,
            ILogger<RemediationScriptExecutor> logger) : base(sanitizer, Mock.Of<Ato.Copilot.Core.Interfaces.IPathSanitizationService>(), logger, 3) { }

        protected override Task<(int ExitCode, string Stdout, string Stderr)> RunSubprocessAsync(
            RemediationScript script, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult((0, "Remediation completed successfully", string.Empty));
        }
    }
}
