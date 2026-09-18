using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Tests.Integration.Tools;

/// <summary>
/// Integration tests for NistControlSearchTool and NistControlExplainerTool MCP endpoints.
/// Uses real NistControlsService with embedded OSCAL catalog (no HTTP calls).
/// Validates full-stack JSON-RPC-style request/response roundtrip per Constitution Principle III.
/// </summary>
public class NistControlMcpToolIntegrationTests : IDisposable
{
    private readonly NistControlsService _nistService;
    private readonly NistControlSearchTool _searchTool;
    private readonly NistControlExplainerTool _explainerTool;
    private readonly MemoryCache _cache;

    public NistControlMcpToolIntegrationTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());

        var options = Options.Create(new NistControlsOptions
        {
            BaseUrl = "https://invalid.example.com/catalog.json",
            CacheDurationHours = 24,
            EnableOfflineFallback = true
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agents:Compliance:NistControls:BaseUrl"] = "https://invalid.example.com/catalog.json",
                ["Agents:Compliance:NistControls:CacheDurationHours"] = "24"
            })
            .Build();

        _nistService = new NistControlsService(
            Mock.Of<ILogger<NistControlsService>>(),
            _cache,
            options,
            new HttpClient(),
            config,
            new NistCatalogIntegrityValidator(Mock.Of<ILogger<NistCatalogIntegrityValidator>>()));

        _searchTool = new NistControlSearchTool(_nistService, Mock.Of<ILogger<NistControlSearchTool>>());
        _explainerTool = new NistControlExplainerTool(_nistService, Mock.Of<ILogger<NistControlExplainerTool>>());
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── SearchTool Integration ──────────────────────────────────────────────

    [Fact]
    public async Task SearchTool_FindsEncryptionControls_FromEmbeddedCatalog()
    {
        var args = new Dictionary<string, object?> { ["query"] = "cryptographic" };

        var result = await _searchTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("success");
        var totalMatches = root.GetProperty("data").GetProperty("total_matches").GetInt32();
        totalMatches.Should().BeGreaterThan(0, "the embedded catalog should contain cryptographic controls");

        var controls = root.GetProperty("data").GetProperty("controls");
        controls.GetArrayLength().Should().BeGreaterThan(0);
        // Verify each result has the expected envelope fields
        var first = controls[0];
        first.TryGetProperty("id", out _).Should().BeTrue();
        first.TryGetProperty("title", out _).Should().BeTrue();
        first.TryGetProperty("family", out _).Should().BeTrue();
        first.TryGetProperty("excerpt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SearchTool_FamilyFilter_ReturnsOnlyMatchingFamily()
    {
        var args = new Dictionary<string, object?>
        {
            ["query"] = "access",
            ["family"] = "AC"
        };

        var result = await _searchTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        var controls = doc.RootElement.GetProperty("data").GetProperty("controls");

        foreach (var control in controls.EnumerateArray())
        {
            control.GetProperty("family").GetString().Should().Be("AC");
        }
    }

    [Fact]
    public async Task SearchTool_NoResults_ReturnsFriendlyMessage()
    {
        var args = new Dictionary<string, object?> { ["query"] = "zzz_nonexistent_completely_random_xyz" };

        var result = await _searchTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("success");
        root.GetProperty("data").GetProperty("total_matches").GetInt32().Should().Be(0);
        root.GetProperty("data").GetProperty("message").GetString()
            .Should().Contain("No controls found");
    }

    [Fact]
    public async Task SearchTool_MaxResults_LimitsOutput()
    {
        var args = new Dictionary<string, object?>
        {
            ["query"] = "control",
            ["max_results"] = 3
        };

        var result = await _searchTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        var controls = doc.RootElement.GetProperty("data").GetProperty("controls");

        controls.GetArrayLength().Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task SearchTool_ResponseHasCorrectEnvelopeStructure()
    {
        var args = new Dictionary<string, object?> { ["query"] = "audit" };

        var result = await _searchTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        // Validate envelope structure: status, data, metadata
        root.TryGetProperty("status", out _).Should().BeTrue();
        root.TryGetProperty("data", out _).Should().BeTrue();
        root.TryGetProperty("metadata", out _).Should().BeTrue();
        root.GetProperty("metadata").GetProperty("tool").GetString().Should().Be("search_nist_controls");
        root.GetProperty("metadata").TryGetProperty("execution_time_ms", out _).Should().BeTrue();
        root.GetProperty("metadata").TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    // ─── ExplainerTool Integration ───────────────────────────────────────────

    [Fact]
    public async Task ExplainerTool_ValidControl_ReturnsDetailedExplanation()
    {
        var args = new Dictionary<string, object?> { ["control_id"] = "AC-3" };

        var result = await _explainerTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("success");
        var data = root.GetProperty("data");
        data.GetProperty("control_id").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("statement").GetString().Should().NotBeNullOrEmpty();
        data.TryGetProperty("guidance", out _).Should().BeTrue();
        data.TryGetProperty("objectives", out _).Should().BeTrue();
        data.TryGetProperty("catalog_version", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExplainerTool_InvalidControlId_ReturnsControlNotFound()
    {
        var args = new Dictionary<string, object?> { ["control_id"] = "ZZ-999" };

        var result = await _explainerTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("errorCode").GetString().Should().Be("CONTROL_NOT_FOUND");
        root.GetProperty("message").GetString().Should().Contain("ZZ-999");
        root.GetProperty("suggestion").GetString().Should().Contain("search_nist_controls");
    }

    [Fact]
    public async Task ExplainerTool_NullControlId_ReturnsInvalidInput()
    {
        var args = new Dictionary<string, object?> { ["control_id"] = null };

        var result = await _explainerTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);

        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task ExplainerTool_ResponseHasCorrectEnvelopeStructure()
    {
        var args = new Dictionary<string, object?> { ["control_id"] = "AU-2" };

        var result = await _explainerTool.ExecuteCoreAsync(args);
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        root.TryGetProperty("status", out _).Should().BeTrue();
        root.TryGetProperty("data", out _).Should().BeTrue();
        root.TryGetProperty("metadata", out _).Should().BeTrue();
        root.GetProperty("metadata").GetProperty("tool").GetString().Should().Be("explain_nist_control");
        root.GetProperty("metadata").TryGetProperty("execution_time_ms", out _).Should().BeTrue();
    }

    // ─── Cross-Tool Integration ──────────────────────────────────────────────

    [Fact]
    public async Task SearchThenExplain_FullWorkflow()
    {
        // Step 1: Search for controls
        var searchArgs = new Dictionary<string, object?> { ["query"] = "access control" };
        var searchResult = await _searchTool.ExecuteCoreAsync(searchArgs);
        var searchDoc = JsonDocument.Parse(searchResult);

        searchDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        var controls = searchDoc.RootElement.GetProperty("data").GetProperty("controls");
        controls.GetArrayLength().Should().BeGreaterThan(0);

        // Step 2: Take first result and explain it
        var firstControlId = controls[0].GetProperty("id").GetString()!;
        var explainArgs = new Dictionary<string, object?> { ["control_id"] = firstControlId };
        var explainResult = await _explainerTool.ExecuteCoreAsync(explainArgs);
        var explainDoc = JsonDocument.Parse(explainResult);

        explainDoc.RootElement.GetProperty("status").GetString().Should().Be("success");
        explainDoc.RootElement.GetProperty("data").GetProperty("control_id").GetString()
            .Should().NotBeNullOrEmpty();
    }
}
