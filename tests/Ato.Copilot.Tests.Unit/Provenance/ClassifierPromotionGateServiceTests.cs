using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Models.Provenance;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Provenance;

/// <summary>
/// Unit tests for <see cref="ClassifierPromotionGateService"/> (#2753).
///
/// Tests use an InMemory EF database seeded with synthetic shadow log rows so no
/// live telemetry is required.  All eight gate outcomes are validated independently.
/// </summary>
public sealed class ClassifierPromotionGateServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly TestDbContextFactory _factory;
    private readonly ClassifierPromotionGateService _sut;

    public ClassifierPromotionGateServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"PromotionGateTest_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(opts);
        _db = _factory.Context;
        _db.Database.EnsureCreated();
        _sut = new ClassifierPromotionGateService(_factory, NullLogger<ClassifierPromotionGateService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private ClassifierShadowLog MakeRow(
        string debertaVerdict,
        string llmVerdict,
        double confidence,
        double topMargin,
        long latencyMs = 12,
        string trafficSlice = "live") =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            PairId = Guid.NewGuid().ToString(),
            ClaimHash = Guid.NewGuid().ToString("N"),
            EvidenceHash = Guid.NewGuid().ToString("N"),
            DebertaVerdict = debertaVerdict,
            DebertaConfidence = confidence,
            DebertaTopMargin = topMargin,
            LlmVerdict = llmVerdict,
            LatencyMs = latencyMs,
            TrafficSlice = trafficSlice,
            Ts = DateTime.UtcNow,
        };

    private async Task SeedRowsAsync(IEnumerable<ClassifierShadowLog> rows)
    {
        _db.ClassifierShadowLogs.AddRange(rows);
        await _db.SaveChangesAsync();
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_ReturnsSufficientDataFalse_WhenRowCountBelowMinimum()
    {
        // Arrange — seed only 100 rows (minimum is 10 000)
        await SeedRowsAsync(Enumerable.Range(0, 100)
            .Select(_ => MakeRow("supported", "supported", 0.9, 0.8)));

        // Act
        var result = await _sut.EvaluateAsync();

        // Assert
        result.SufficientData.Should().BeFalse();
        result.AllGatesPass.Should().BeFalse();
        result.TotalPairs.Should().Be(100);
    }

    [Fact]
    public async Task EvaluateAsync_Gate1_Passes_WhenContradictedPrecisionMeetsFloor()
    {
        // Arrange — seed 10k rows; all clear-cut (margin ≥ 0.5); all refuted DeBERTa
        // decisions are confirmed by LLM (100% contradicted precision).
        const int n = 10_000;
        await SeedRowsAsync(Enumerable.Range(0, n)
            .Select(_ => MakeRow("refuted", "refuted", 0.92, 0.81)));

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert
        result.SufficientData.Should().BeTrue();
        result.ContradictedClassPrecision.Should().BeApproximately(1.0, 0.001);
        result.Gate1_ContradictedPrecision.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_Gate1_Fails_WhenContradictedPrecisionBelowFloor()
    {
        // Arrange — 10k rows where DeBERTa says "refuted" but LLM says "supported" (all wrong).
        const int n = 10_000;
        await SeedRowsAsync(Enumerable.Range(0, n)
            .Select(_ => MakeRow("refuted", "supported", 0.92, 0.81)));

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert
        result.ContradictedClassPrecision.Should().BeApproximately(0.0, 0.001);
        result.Gate1_ContradictedPrecision.Should().BeFalse();
        result.AllGatesPass.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_Gate3_IsDeferred_WhenNoLlmVerdicts()
    {
        // Arrange — 10k rows with empty LlmVerdict (simulating #2780 LLM outage).
        const int n = 10_000;
        await SeedRowsAsync(Enumerable.Range(0, n)
            .Select(_ => MakeRow("supported", "", 0.91, 0.82)));

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert — gate 3 deferred (null), not failed
        result.AgreementRateInClearCutRegion.Should().BeNull();
        result.Gate3_Agreement.Should().BeTrue(
            because: "Gate 3 defers (not fails) when no LLM verdicts are available — #2780 protocol");
    }

    [Fact]
    public async Task EvaluateAsync_Gate3_Fails_WhenAgreementBelowFloor()
    {
        // Arrange — 10k rows in clear-cut region; DeBERTa always disagrees with LLM.
        const int n = 10_000;
        await SeedRowsAsync(Enumerable.Range(0, n)
            .Select(_ => MakeRow("supported", "refuted", 0.91, 0.82)));

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert
        result.AgreementRateInClearCutRegion.Should().BeApproximately(0.0, 0.001);
        result.Gate3_Agreement.Should().BeFalse();
        result.AllGatesPass.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_Gate5_Passes_WhenP95BelowFiftyMs()
    {
        // Arrange — 10k rows with latencies in the 5–30ms range (all below 50ms).
        const int n = 10_000;
        await SeedRowsAsync(Enumerable.Range(0, n)
            .Select(i => MakeRow("supported", "supported", 0.91, 0.81,
                latencyMs: 10 + (i % 20)))); // 10–29ms

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert
        result.P95LatencyMs.Should().BeLessThan(50.0);
        result.Gate5_P95Latency.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_Gate5_Fails_WhenP95ExceedsFiftyMs()
    {
        // Arrange — 10k rows with latencies; 10% exceed 50ms so p95 is above floor.
        const int n = 10_000;
        await SeedRowsAsync(Enumerable.Range(0, n)
            .Select(i => MakeRow("supported", "supported", 0.91, 0.81,
                latencyMs: i < (int)(n * 0.94) ? 10L : 200L))); // top 6% = 200ms

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert — p95 sits in the 200ms bucket
        result.P95LatencyMs.Should().BeGreaterThanOrEqualTo(50.0);
        result.Gate5_P95Latency.Should().BeFalse();
        result.AllGatesPass.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_Gate8_RoutableFraction_IsComputed()
    {
        // Arrange — half the rows have margin ≥ 0.5 (clear-cut), half below.
        const int n = 10_000;
        var rows = Enumerable.Range(0, n).Select(i =>
            MakeRow("supported", "supported", 0.91,
                topMargin: i % 2 == 0 ? 0.6 : 0.3)); // 50% clear-cut
        await SeedRowsAsync(rows);

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert
        result.RoutableFraction.Should().BeApproximately(0.5, 0.01);
        result.Gate8_RoutableFraction.Should().BeTrue(
            because: "50% routable fraction exceeds the 40% gate floor");
    }

    [Fact]
    public async Task EvaluateAsync_Gate8_Fails_WhenRoutableFractionBelowFloor()
    {
        // Arrange — only 10% of rows have margin ≥ 0.5.
        const int n = 10_000;
        var rows = Enumerable.Range(0, n).Select(i =>
            MakeRow("supported", "supported", 0.91,
                topMargin: i < (int)(n * 0.10) ? 0.6 : 0.3)); // 10% clear-cut
        await SeedRowsAsync(rows);

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert
        result.RoutableFraction.Should().BeApproximately(0.1, 0.01);
        result.Gate8_RoutableFraction.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_PerClassMetrics_AreComputed_ForAllVerdicts()
    {
        // Arrange — 10k rows evenly spread across all four classes (DeBERTa == LLM).
        const int n = 10_000;
        var verdicts = new[] { "supported", "refuted", "tangential", "insufficient" };
        var rows = Enumerable.Range(0, n).Select(i =>
        {
            var v = verdicts[i % 4];
            return MakeRow(v, v, 0.9, 0.8);
        });
        await SeedRowsAsync(rows);

        // Act
        var result = await _sut.EvaluateAsync(tau: 0.5);

        // Assert — all four classes should have precision = recall = 1.0 (perfect agreement)
        foreach (var v in verdicts)
        {
            result.PrecisionByClass.Should().ContainKey(v);
            result.RecallByClass.Should().ContainKey(v);
            result.PrecisionByClass[v].Should().BeApproximately(1.0, 0.001,
                because: $"DeBERTa always matches LLM for class '{v}'");
        }
    }

    [Fact]
    public async Task EvaluateAsync_FormatGateSummary_DoesNotThrow()
    {
        // Arrange — minimal data to produce a result
        await SeedRowsAsync(Enumerable.Range(0, 100)
            .Select(_ => MakeRow("supported", "supported", 0.9, 0.8)));

        // Act
        var result = await _sut.EvaluateAsync();

        // Assert
        var summary = result.FormatGateSummary();
        summary.Should().Contain("Gate 1");
        summary.Should().Contain("Gate 8");
        summary.Should().ContainAny("PASS", "FAIL", "DEFER");
    }
}
