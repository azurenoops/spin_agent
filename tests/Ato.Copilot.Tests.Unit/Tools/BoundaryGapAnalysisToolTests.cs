using System.Text.Json;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Tools;

/// <summary>
/// Unit tests for fix(#536) — BoundaryGapAnalysisTool identifier resolution and
/// split error codes (SYSTEM_NOT_FOUND vs NO_BASELINE_SELECTED).
/// </summary>
public class BoundaryGapAnalysisToolTests
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AtoCopilotContext _db;

    public BoundaryGapAnalysisToolTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"BoundaryGapAnalysisToolTests_{Guid.NewGuid()}")
            .Options;
        var dbFactory = new TestDbContextFactory(options);

        // Build a real DI scope so BoundaryGapAnalysisTool can resolve CapabilityService.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbContextFactory<AtoCopilotContext>>(dbFactory);
        services.AddScoped<AtoCopilotContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContext());
        services.AddScoped<NarrativeTemplateService>();
        services.AddScoped<IDeviationService>(_ => Mock.Of<IDeviationService>(d =>
            d.GetWaivedControlsForBoundaryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new List<string>())));
        services.AddScoped<IOrgInheritanceService>(_ => Mock.Of<IOrgInheritanceService>());
        services.AddScoped<CapabilityService>();

        var provider = services.BuildServiceProvider();
        _scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        // Seed via direct context for convenience
        _db = dbFactory.CreateDbContext();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private BoundaryGapAnalysisTool CreateTool() =>
        new(_scopeFactory, Mock.Of<ILogger<BoundaryGapAnalysisTool>>());

    private async Task<RegisteredSystem> SeedSystemAsync(string name, string? acronym = null)
    {
        var sys = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Acronym = acronym,
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionCritical,
            HostingEnvironment = "Azure Government",
            IsActive = true,
        };
        _db.RegisteredSystems.Add(sys);
        await _db.SaveChangesAsync();
        return sys;
    }

    private async Task SeedBaselineAsync(string systemId)
    {
        _db.ControlBaselines.Add(new ControlBaseline
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = systemId,
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-1", "AC-2", "IA-1" },
        });
        await _db.SaveChangesAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>fix(#536): GUID identifier resolves to the correct system.</summary>
    [Fact]
    public async Task ExecuteAsync_ByGuid_ReturnsSuccess()
    {
        var sys = await SeedSystemAsync("ACME Portal", "ACME");
        await SeedBaselineAsync(sys.Id);

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = sys.Id,
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("system_id").GetString().Should().Be(sys.Id);
    }

    /// <summary>fix(#536): Name string resolves correctly (previously failed).</summary>
    [Fact]
    public async Task ExecuteAsync_ByName_ReturnsSuccess()
    {
        var sys = await SeedSystemAsync("ACME Portal", "ACME");
        await SeedBaselineAsync(sys.Id);

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "ACME Portal",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    /// <summary>fix(#536): Acronym string resolves correctly (previously failed).</summary>
    [Fact]
    public async Task ExecuteAsync_ByAcronym_ReturnsSuccess()
    {
        var sys = await SeedSystemAsync("ACME Portal", "ACME");
        await SeedBaselineAsync(sys.Id);

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "ACME",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
    }

    /// <summary>fix(#536): Unknown identifier → SYSTEM_NOT_FOUND (not generic NOT_FOUND).</summary>
    [Fact]
    public async Task ExecuteAsync_UnknownSystem_ReturnsSystemNotFoundErrorCode()
    {
        var result = await CreateTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "does-not-exist",
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("SYSTEM_NOT_FOUND");
    }

    /// <summary>fix(#536): System exists but no baseline → NO_BASELINE_SELECTED (not SYSTEM_NOT_FOUND).</summary>
    [Fact]
    public async Task ExecuteAsync_SystemExistsNoBaseline_ReturnsNoBaselineSelectedErrorCode()
    {
        var sys = await SeedSystemAsync("Baseline-less System", "BLS");
        // Intentionally NOT seeding a baseline.

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = sys.Id,
        });

        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("NO_BASELINE_SELECTED");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
