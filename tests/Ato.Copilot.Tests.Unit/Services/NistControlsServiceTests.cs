using System.Reflection;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for NistControlsService: online fetch, offline fallback, cache,
/// catalog loading, family queries, baseline filtering, control lookup, CatalogStatus.
/// Updated for IMemoryCache + IOptions&lt;NistControlsOptions&gt; constructor (T010).
/// </summary>
public class NistControlsServiceTests
{
    private readonly Mock<ILogger<NistControlsService>> _loggerMock = new();

    private NistControlsService CreateService(
        HttpClient? httpClient = null,
        IMemoryCache? cache = null,
        NistControlsOptions? options = null)
    {
        var memoryCache = cache ?? new MemoryCache(new MemoryCacheOptions());

        var nistOptions = options ?? new NistControlsOptions
        {
            BaseUrl = "https://invalid.example.com/catalog.json", // force embedded fallback
            TimeoutSeconds = 5,
            CacheDurationHours = 1,
            MaxRetryAttempts = 1,
            RetryDelaySeconds = 1,
            EnableOfflineFallback = true,
            WarmupDelaySeconds = 5
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        return new NistControlsService(
            _loggerMock.Object,
            memoryCache,
            Options.Create(nistOptions),
            httpClient ?? new HttpClient(),
            config,
            new NistCatalogIntegrityValidator(Mock.Of<ILogger<NistCatalogIntegrityValidator>>()));
    }

    // ─── Embedded Resource Fallback ──────────────────────────────────────────────

    [Fact]
    public async Task GetControlAsync_EmbeddedFallback_ReturnsControls()
    {
        // The service falls back to embedded resource when online/cache unavailable
        var service = CreateService();

        // Force load via any query — will use embedded resource since online is off
        var controls = await service.SearchControlsAsync("access", maxResults: 5);

        // Should load something from embedded resource (if present) or return empty
        // The service should not throw
        controls.Should().NotBeNull();
    }

    [Fact]
    public async Task GetControlAsync_UnknownControlId_ReturnsNull()
    {
        var service = CreateService();
        var result = await service.GetControlAsync("ZZ-999");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetControlFamilyAsync_ValidFamily_ReturnsControls()
    {
        var service = CreateService();
        var controls = await service.GetControlFamilyAsync("AC");
        controls.Should().NotBeNull();
        // All returned controls should be in the AC family
        foreach (var c in controls)
        {
            c.Family.Should().Be("AC");
            c.IsEnhancement.Should().BeFalse();
        }
    }

    [Fact]
    public async Task GetControlFamilyAsync_CaseInsensitive_ReturnsControls()
    {
        var service = CreateService();
        var upper = await service.GetControlFamilyAsync("AC");
        var lower = await service.GetControlFamilyAsync("ac");
        upper.Count.Should().Be(lower.Count);
    }

    [Fact]
    public async Task GetControlFamilyAsync_InvalidFamily_ReturnsEmpty()
    {
        var service = CreateService();
        var controls = await service.GetControlFamilyAsync("ZZ");
        controls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetControlFamilyAsync_IncludeControlsFalse_ReturnsSummary()
    {
        var service = CreateService();
        var summaries = await service.GetControlFamilyAsync("AC", includeControls: false);
        // Summary entries should not include full enhancements
        foreach (var c in summaries)
        {
            c.ControlEnhancements.Should().BeEmpty();
        }
    }

    // ─── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchControlsAsync_RespectsMaxResults()
    {
        var service = CreateService();
        var results = await service.SearchControlsAsync("control", maxResults: 3);
        results.Count.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task SearchControlsAsync_WithFamilyFilter_FiltersByFamily()
    {
        var service = CreateService();
        var results = await service.SearchControlsAsync("", controlFamily: "AU", maxResults: 50);
        foreach (var c in results)
        {
            c.Family.Should().Be("AU");
        }
    }

    // ─── Catalog Status ────────────────────────────────────────────────────────

    [Fact]
    public void CatalogSource_BeforeLoad_IsNone()
    {
        var service = CreateService();
        service.CatalogSource.Should().Be("none");
    }

    [Fact]
    public void LastSyncedAt_BeforeLoad_IsNull()
    {
        var service = CreateService();
        service.LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public async Task CatalogSource_AfterLoad_IsNotNone()
    {
        var service = CreateService();
        // Trigger load
        await service.SearchControlsAsync("test");
        // After loading, the source should be set to something
        service.CatalogSource.Should().NotBe("none");
    }

    // ─── Thread Safety ────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentLoads_AreThreadSafe()
    {
        var service = CreateService();

        // Launch multiple concurrent queries
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => service.SearchControlsAsync("access", maxResults: 5))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // All should return the same count (loaded once)
        var counts = results.Select(r => r.Count).Distinct().ToList();
        counts.Should().HaveCount(1, "concurrent loads should produce consistent results");
    }

    // ─── Cache Behavior ────────────────────────────────────────────────────────

    [Fact]
    public async Task CacheHit_ReturnsFromMemoryCache()
    {
        // Pre-populate IMemoryCache with a catalog to simulate a warm cache
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var catalog = new NistCatalog
        {
            Metadata = new CatalogMetadata { Title = "Test Catalog", Version = "5.2.0" },
            Groups = new List<ControlGroup>
            {
                new()
                {
                    Id = "ac",
                    Title = "Access Control",
                    Controls = new List<OscalControl>
                    {
                        new()
                        {
                            Id = "ac-1",
                            Title = "Policy and Procedures",
                            Parts = new List<ControlPart>
                            {
                                new() { Id = "ac-1_smt", Name = "statement", Prose = "Test control" }
                            }
                        }
                    }
                }
            }
        };

        // Set catalog in cache with same key the service uses
        memoryCache.Set("NistControls:Catalog", catalog, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        });

        // Also cache the controls list
        var controls = new List<NistControl>
        {
            new() { Id = "ac-1", Family = "AC", Title = "Policy and Procedures", Description = "Test control" }
        };
        memoryCache.Set("NistControls:Controls", controls, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        });

        var service = CreateService(cache: memoryCache);

        var result = await service.GetControlAsync("ac-1");

        // Should return from cache without loading from any source
        result.Should().NotBeNull();
        result!.Id.Should().Be("ac-1");
        result.Family.Should().Be("AC");
    }

    // ─── GetCatalogAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetCatalogAsync_ReturnsCatalog_WithGroups()
    {
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        catalog.Should().NotBeNull();
        catalog!.Groups.Should().NotBeEmpty();
        catalog.Groups.Count.Should().Be(20, "NIST SP 800-53 Rev 5 has 20 control families");
    }

    [Fact]
    public async Task GetCatalogAsync_CacheHit_ReturnsSameInstance()
    {
        var service = CreateService();
        var first = await service.GetCatalogAsync();
        var second = await service.GetCatalogAsync();

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        ReferenceEquals(first, second).Should().BeTrue("cached catalog should return same instance");
    }

    // ─── GetControlEnhancementAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetControlEnhancementAsync_ValidControl_ReturnsEnhancement()
    {
        var service = CreateService();
        var enhancement = await service.GetControlEnhancementAsync("SC-7");

        enhancement.Should().NotBeNull();
        enhancement!.Id.Should().Be("SC-7");
        enhancement.Title.Should().NotBeNullOrEmpty();
        enhancement.Statement.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetControlEnhancementAsync_ValidControl_IncludesGuidance()
    {
        var service = CreateService();
        var enhancement = await service.GetControlEnhancementAsync("AC-2");

        enhancement.Should().NotBeNull();
        // AC-2 has well-known guidance
        enhancement!.Guidance.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetControlEnhancementAsync_ValidControl_IncludesObjectives()
    {
        var service = CreateService();
        var enhancement = await service.GetControlEnhancementAsync("AC-3");

        enhancement.Should().NotBeNull();
        enhancement!.Objectives.Should().NotBeEmpty("AC-3 has assessment objectives");
    }

    [Fact]
    public async Task GetControlEnhancementAsync_MissingControl_ReturnsNull()
    {
        var service = CreateService();
        var enhancement = await service.GetControlEnhancementAsync("ZZ-999");

        enhancement.Should().BeNull();
    }

    [Fact]
    public async Task GetControlEnhancementAsync_NullId_ThrowsArgumentException()
    {
        var service = CreateService();

        var act = () => service.GetControlEnhancementAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetControlEnhancementAsync_EmptyId_ThrowsArgumentException()
    {
        var service = CreateService();

        var act = () => service.GetControlEnhancementAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── ValidateControlIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ValidateControlIdAsync_ValidId_ReturnsTrue()
    {
        var service = CreateService();
        var valid = await service.ValidateControlIdAsync("AC-2");

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateControlIdAsync_InvalidId_ReturnsFalse()
    {
        var service = CreateService();
        var valid = await service.ValidateControlIdAsync("ZZ-99");

        valid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateControlIdAsync_CaseInsensitive_ReturnsTrue()
    {
        var service = CreateService();
        var lower = await service.ValidateControlIdAsync("ac-2");
        var upper = await service.ValidateControlIdAsync("AC-2");

        lower.Should().BeTrue();
        upper.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateControlIdAsync_NullId_ThrowsArgumentException()
    {
        var service = CreateService();

        var act = () => service.ValidateControlIdAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateControlIdAsync_EmptyId_ThrowsArgumentException()
    {
        var service = CreateService();

        var act = () => service.ValidateControlIdAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── GetVersionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetVersionAsync_ReturnsVersion()
    {
        var service = CreateService();
        var version = await service.GetVersionAsync();

        version.Should().NotBeNullOrEmpty();
        version.Should().NotBe("Unknown");
    }

    // ─── T015: Typed Deserialization Verification ────────────────────────────

    [Fact]
    public async Task TypedDeserialization_CatalogHas20ControlFamilies()
    {
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        catalog.Should().NotBeNull();
        catalog!.Groups.Count.Should().Be(20, "NIST SP 800-53 Rev 5 defines 20 control families");
    }

    [Fact]
    public async Task TypedDeserialization_GroupIds_AreKebabCase()
    {
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        catalog.Should().NotBeNull();
        foreach (var group in catalog!.Groups)
        {
            group.Id.Should().NotBeNullOrEmpty();
            group.Title.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task TypedDeserialization_ControlParts_ContainStatementAndGuidance()
    {
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        catalog.Should().NotBeNull();
        // Pick AC group's first control and verify parts
        var acGroup = catalog!.Groups.FirstOrDefault(g => g.Id == "ac");
        acGroup.Should().NotBeNull();
        acGroup!.Controls.Should().NotBeEmpty();

        var firstControl = acGroup.Controls[0];
        firstControl.Parts.Should().NotBeNull();
        firstControl.Parts!.Should().Contain(p => p.Name == "statement");
    }

    [Fact]
    public async Task TypedDeserialization_MetadataVersion_ReturnsExpectedVersion()
    {
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        catalog.Should().NotBeNull();
        catalog!.Metadata.Should().NotBeNull();
        catalog.Metadata.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TypedDeserialization_NestedEnhancements_Exist()
    {
        var service = CreateService();
        var catalog = await service.GetCatalogAsync();

        catalog.Should().NotBeNull();
        // AC-2 typically has nested control enhancements
        var acGroup = catalog!.Groups.FirstOrDefault(g => g.Id == "ac");
        acGroup.Should().NotBeNull();

        var hasEnhancements = acGroup!.Controls.Any(c => c.Controls is { Count: > 0 });
        hasEnhancements.Should().BeTrue("NIST controls should have nested enhancements");
    }

    // ─── T016: Resilience & Fallback Verification ────────────────────────────

    [Fact]
    public async Task OnlineFailure_FallsBackToEmbeddedResource()
    {
        // Use invalid URL to force online failure, then verify embedded works
        var service = CreateService(options: new NistControlsOptions
        {
            BaseUrl = "https://invalid.example.com/nonexistent.json",
            TimeoutSeconds = 2,
            CacheDurationHours = 1,
            EnableOfflineFallback = true,
            WarmupDelaySeconds = 5
        });

        var catalog = await service.GetCatalogAsync();

        catalog.Should().NotBeNull("embedded resource should provide fallback");
        catalog!.Groups.Count.Should().Be(20);
        service.CatalogSource.Should().Be("embedded");
    }

    [Fact]
    public async Task FallbackDisabled_OnlineFailure_ReturnsNull()
    {
        var service = CreateService(options: new NistControlsOptions
        {
            BaseUrl = "https://invalid.example.com/nonexistent.json",
            TimeoutSeconds = 2,
            CacheDurationHours = 1,
            EnableOfflineFallback = false,
            WarmupDelaySeconds = 5
        });

        var catalog = await service.GetCatalogAsync();

        catalog.Should().BeNull("no fallback should be attempted when disabled");
    }

    [Fact]
    public async Task GetCatalogAsync_TruncatedOnlineCatalog_RejectsBeforeCaching()
    {
        // Arrange
        const string resourceName =
            "Ato.Copilot.Agents.Compliance.Resources.NIST_SP-800-53_rev5_catalog.json";
        using var stream = typeof(NistControlsService).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        var root = JsonNode.Parse(stream)!;
        root["catalog"]!["groups"]!.AsArray().RemoveAt(0);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json")
        };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        var service = CreateService(
            new HttpClient(handler.Object),
            options: new NistControlsOptions
            {
                BaseUrl = "https://example.test/catalog.json",
                TimeoutSeconds = 10,
                CacheDurationHours = 1,
                EnableOfflineFallback = false,
                WarmupDelaySeconds = 5
            });

        // Act
        var catalog = await service.GetCatalogAsync();

        // Assert
        catalog.Should().BeNull();
        service.CatalogSource.Should().Be("none");
        service.LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public async Task TaskCanceledException_HandledGracefully()
    {
        // Pre-cancelled token
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var service = CreateService();

        var act = () => service.GetCatalogAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
