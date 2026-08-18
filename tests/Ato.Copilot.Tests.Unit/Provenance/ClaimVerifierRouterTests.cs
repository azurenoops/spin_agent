using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Models.Provenance;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Provenance;

/// <summary>
/// Unit tests for <see cref="ClaimVerifierRouter"/> (#2753).
///
/// Verifies routing rules, auto-rollback, LLM-outage (#2780) handling,
/// and shadow-log fire-and-forget behaviour.
/// </summary>
public sealed class ClaimVerifierRouterTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static DebertaNliResult MakeDeberta(
        string verdict = "supported",
        double confidence = 0.92,
        double topMargin = 0.81,
        long latencyMs = 12) =>
        new() { Verdict = verdict, Confidence = confidence, TopMargin = topMargin, LatencyMs = latencyMs };

    private static LlmVerifierResult MakeLlm(
        string verdict = "supported",
        double confidence = 0.90,
        long latencyMs = 150) =>
        new() { Verdict = verdict, Confidence = confidence, LatencyMs = latencyMs };

    private static ClaimVerifierRouter BuildRouter(
        IDeBertaNliVerifier deberta,
        ILlmClaimVerifier llm,
        IClassifierShadowLogger? shadow = null,
        bool debertaModeOn = true,
        double tau = 0.5)
    {
        shadow ??= new NullClassifierShadowLogger();
        return new ClaimVerifierRouter(
            deberta, llm, shadow,
            NullLogger<ClaimVerifierRouter>.Instance,
            debertaModeOn, tau);
    }

    // ─── Routing: DEBERTA_NLI_MODE OFF ───────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_RoutesToLlm_WhenDebertaModeOff()
    {
        // Arrange
        var deberta = new Mock<IDeBertaNliVerifier>();
        deberta.Setup(d => d.InferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeDeberta("supported", topMargin: 0.9));

        var llm = new Mock<ILlmClaimVerifier>();
        llm.Setup(l => l.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeLlm("refuted")); // LLM disagrees — should win when mode is OFF

        var sut = BuildRouter(deberta.Object, llm.Object, debertaModeOn: false);

        // Act
        var result = await sut.VerifyAsync("claim", "evidence");

        // Assert
        result.Verdict.Should().Be("refuted");
        result.Path.Should().Be(VerificationPath.LlmFallback);
        llm.Verify(l => l.VerifyAsync("claim", "evidence", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Routing: clear-cut → DeBERTa fast-path ──────────────────────────────

    [Fact]
    public async Task VerifyAsync_RoutesToDeberta_WhenModOnAndMarginMeetsTau()
    {
        // Arrange — margin 0.82 ≥ τ 0.5
        var deberta = new Mock<IDeBertaNliVerifier>();
        deberta.Setup(d => d.InferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeDeberta("refuted", confidence: 0.93, topMargin: 0.82));

        var llm = new Mock<ILlmClaimVerifier>();

        var sut = BuildRouter(deberta.Object, llm.Object, debertaModeOn: true, tau: 0.5);

        // Act
        var result = await sut.VerifyAsync("claim", "evidence");

        // Assert
        result.Verdict.Should().Be("refuted");
        result.Path.Should().Be(VerificationPath.DebertaFastPath);
        llm.Verify(l => l.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "LLM must not be called on the clear-cut fast-path");
    }

    // ─── Routing: ambiguous → LLM fallback ───────────────────────────────────

    [Fact]
    public async Task VerifyAsync_RoutesToLlm_WhenMarginBelowTau()
    {
        // Arrange — margin 0.3 < τ 0.5
        var deberta = new Mock<IDeBertaNliVerifier>();
        deberta.Setup(d => d.InferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeDeberta("tangential", topMargin: 0.3));

        var llm = new Mock<ILlmClaimVerifier>();
        llm.Setup(l => l.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeLlm("insufficient"));

        var sut = BuildRouter(deberta.Object, llm.Object, debertaModeOn: true, tau: 0.5);

        // Act
        var result = await sut.VerifyAsync("claim", "evidence");

        // Assert
        result.Verdict.Should().Be("insufficient");
        result.Path.Should().Be(VerificationPath.LlmFallback);
    }

    // ─── Auto-rollback ────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_RoutesToLlm_WhenAutoRollbackIsActive()
    {
        // Arrange — mode ON, margin clear-cut, but rollback tripped
        var deberta = new Mock<IDeBertaNliVerifier>();
        deberta.Setup(d => d.InferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeDeberta("supported", topMargin: 0.9));

        var llm = new Mock<ILlmClaimVerifier>();
        llm.Setup(l => l.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeLlm("supported"));

        var sut = BuildRouter(deberta.Object, llm.Object, debertaModeOn: true, tau: 0.5);
        sut.TripAutoRollback("contradicted-class precision dropped below 0.95 in monitoring");

        // Act
        var result = await sut.VerifyAsync("claim", "evidence");

        // Assert
        result.Path.Should().Be(VerificationPath.AutoRollbackToLlm);
        sut.IsAutoRollbackActive.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_RestoresFastPath_WhenRollbackIsCleared()
    {
        // Arrange
        var deberta = new Mock<IDeBertaNliVerifier>();
        deberta.Setup(d => d.InferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeDeberta("supported", topMargin: 0.9));

        var llm = new Mock<ILlmClaimVerifier>();
        llm.Setup(l => l.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeLlm("supported"));

        var sut = BuildRouter(deberta.Object, llm.Object, debertaModeOn: true, tau: 0.5);
        sut.TripAutoRollback("test");
        sut.ClearAutoRollback();

        // Act
        var result = await sut.VerifyAsync("claim", "evidence");

        // Assert — rollback cleared → fast-path restored
        result.Path.Should().Be(VerificationPath.DebertaFastPath);
        sut.IsAutoRollbackActive.Should().BeFalse();
    }

    // ─── LLM outage (#2780) ───────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_MarksLlmUnavailable_WhenLlmThrowsUnavailableException()
    {
        // Arrange — DeBERTa returns low margin → LLM fallback → LLM throws
        var deberta = new Mock<IDeBertaNliVerifier>();
        deberta.Setup(d => d.InferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(MakeDeberta("tangential", topMargin: 0.2));

        var llm = new Mock<ILlmClaimVerifier>();
        llm.Setup(l => l.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new LlmVerifierUnavailableException("Anthropic credits exhausted (#2780)"));

        var sut = BuildRouter(deberta.Object, llm.Object, debertaModeOn: true, tau: 0.5);

        // Act
        Func<Task> act = () => sut.VerifyAsync("claim", "evidence");

        // Assert — exception surfaces to caller AND comparator is flagged unavailable
        await act.Should().ThrowAsync<LlmVerifierUnavailableException>();
        sut.IsLlmComparatorAvailable.Should().BeFalse(
            because: "router must mark LLM unavailable on #2780 so agreement metrics are suspended");
    }

    [Fact]
    public void MarkLlmComparatorAvailable_ClearsUnavailableFlag()
    {
        // Arrange
        var sut = BuildRouter(Mock.Of<IDeBertaNliVerifier>(), Mock.Of<ILlmClaimVerifier>());
        sut.MarkLlmComparatorUnavailable();

        // Act
        sut.MarkLlmComparatorAvailable();

        // Assert
        sut.IsLlmComparatorAvailable.Should().BeTrue();
    }

    // ─── DeBERTa failure degradation ─────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_DegradesToLlm_WhenDebertaInferenceFails()
    {
        // Arrange — DeBERTa throws; should degrade to "insufficient" (margin=0) → LLM fallback
        var deberta = new Mock<IDeBertaNliVerifier>();
        deberta.Setup(d => d.InferAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("DeBERTa model not loaded"));

        var llm = new Mock<ILlmClaimVerifier>();
        llm.Setup(l => l.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeLlm("supported"));

        var sut = BuildRouter(deberta.Object, llm.Object, debertaModeOn: true, tau: 0.5);

        // Act — must not throw
        var result = await sut.VerifyAsync("claim", "evidence");

        // Assert
        result.Path.Should().Be(VerificationPath.LlmFallback,
            because: "DeBERTa failure degrades to margin=0, which is below τ, forcing LLM fallback");
        result.Verdict.Should().Be("supported");
    }
}
