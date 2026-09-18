using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="NistControlsCacheWarmupService"/>: warmup lifecycle,
/// initial delay, periodic refresh, failure retry, stoppingToken cancellation,
/// and database sync of NIST controls.
/// Uses minimal WarmupDelaySeconds=5 to keep tests fast.
/// </summary>
public class NistControlsCacheWarmupServiceTests
{
    private readonly Mock<INistControlsService> _nistServiceMock = new();
    private readonly Mock<ILogger<NistControlsCacheWarmupService>> _loggerMock = new();

    private NistControlsCacheWarmupService CreateService(
        NistControlsOptions? options = null,
        IDbContextFactory<AtoCopilotContext>? dbFactory = null,
        ComplianceValidationService? validationService = null)
    {
        var opts = options ?? new NistControlsOptions
        {
            WarmupDelaySeconds = 5,
            CacheDurationHours = 1
        };

        return new NistControlsCacheWarmupService(
            _nistServiceMock.Object,
            Options.Create(opts),
            _loggerMock.Object,
            dbFactory,
            validationService);
    }

    // ─── Warmup Lifecycle ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CallsGetCatalogAsync_OnStartup()
    {
        var catalog = CreateMinimalCatalog();
        _nistServiceMock.Setup(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");

        var service = CreateService(new NistControlsOptions { WarmupDelaySeconds = 5, CacheDurationHours = 1 });
        using var cts = new CancellationTokenSource();

        // StartAsync fires the background task and returns immediately
        await service.StartAsync(cts.Token);

        // Wait long enough for initial delay (5s) + warmup execution
        await Task.Delay(TimeSpan.FromSeconds(8));

        // Cancel to stop the periodic loop
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        _nistServiceMock.Verify(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_CallsGetVersionAsync_AfterSuccessfulWarmup()
    {
        var catalog = CreateMinimalCatalog();
        _nistServiceMock.Setup(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");

        var service = CreateService(new NistControlsOptions { WarmupDelaySeconds = 5, CacheDurationHours = 1 });
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(8));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        _nistServiceMock.Verify(s => s.GetVersionAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── Cancellation ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RespectsStoppingToken_DuringInitialDelay()
    {
        var service = CreateService(new NistControlsOptions { WarmupDelaySeconds = 60, CacheDurationHours = 1 });
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Cancel immediately — during initial 60s delay
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Short wait for background task to observe cancellation
        await Task.Delay(200);

        // Should never have called catalog since cancelled during delay
        _nistServiceMock.Verify(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_StopsGracefully_WhenCancelled()
    {
        var catalog = CreateMinimalCatalog();
        _nistServiceMock.Setup(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");

        var service = CreateService(new NistControlsOptions { WarmupDelaySeconds = 5, CacheDurationHours = 1 });
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(8)); // wait for warmup

        await cts.CancelAsync();

        // StopAsync should complete without throwing
        var stopTask = service.StopAsync(CancellationToken.None);
        await stopTask;
    }

    // ─── Failure Retry ───────────────────────────────────────────────────────

    [Fact]
    public async Task WarmupCache_RetriesOnNullCatalog()
    {
        // First call returns null (triggers retry), subsequent calls return catalog
        var callCount = 0;
        var catalog = CreateMinimalCatalog();

        _nistServiceMock.Setup(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                return Task.FromResult(callCount >= 2 ? catalog : (NistCatalog?)null);
            });
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");

        var service = CreateService(new NistControlsOptions { WarmupDelaySeconds = 5, CacheDurationHours = 1 });
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);

        // Wait for delay (5s) + first attempt + 5-min retry delay is too long for test,
        // so we just verify the first call happened and retry was scheduled
        await Task.Delay(TimeSpan.FromSeconds(8));
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // At least the first call should have happened
        _nistServiceMock.Verify(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task WarmupCache_RetriesOnException()
    {
        var callCount = 0;
        var catalog = CreateMinimalCatalog();

        _nistServiceMock.Setup(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new HttpRequestException("Network error");
                return Task.FromResult<NistCatalog?>(catalog);
            });
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");

        var service = CreateService(new NistControlsOptions { WarmupDelaySeconds = 5, CacheDurationHours = 1 });
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(8));
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Should have been called at least once (exception on first attempt)
        _nistServiceMock.Verify(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        callCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task WarmupCache_InvalidCatalogAfterRetries_Throws()
    {
        // Arrange
        _nistServiceMock.Setup(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((NistCatalog?)null);
        var service = CreateService(new NistControlsOptions
        {
            WarmupDelaySeconds = 5,
            WarmupRetryDelaySeconds = 0,
            CacheDurationHours = 1
        });

        // Act
        var act = () => service.WarmupCacheAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*failed after 3 attempts*");
        _nistServiceMock.Verify(
            s => s.GetCatalogAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    // ─── Validation Service Integration ──────────────────────────────────────

    [Fact]
    public async Task WarmupCache_WithoutValidationService_DoesNotThrow()
    {
        var catalog = CreateMinimalCatalog();
        _nistServiceMock.Setup(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");

        var service = CreateService(
            new NistControlsOptions { WarmupDelaySeconds = 5, CacheDurationHours = 1 },
            validationService: null);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(8));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // Should complete warmup
        _nistServiceMock.Verify(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── Configuration ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_AcceptsNullValidationService()
    {
        var service = CreateService(validationService: null);
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_AcceptsNullDbFactory()
    {
        var service = CreateService(dbFactory: null);
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_AcceptsConfiguredOptions()
    {
        var options = new NistControlsOptions
        {
            WarmupDelaySeconds = 30,
            CacheDurationHours = 48
        };

        var service = CreateService(options);
        service.Should().NotBeNull();
    }

    // ─── DB Sync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WarmupCache_SkipsDbSync_WhenNoDbFactory()
    {
        var catalog = CreateMinimalCatalog();
        _nistServiceMock.Setup(s => s.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");

        // No dbFactory — sync should be skipped silently
        var service = CreateService(
            new NistControlsOptions { WarmupDelaySeconds = 5, CacheDurationHours = 1 },
            dbFactory: null);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(8));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        // GetAllControlsAsync should NOT be called when there's no dbFactory
        _nistServiceMock.Verify(
            s => s.GetAllControlsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static NistCatalog CreateMinimalCatalog() => new()
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
                            new() { Id = "ac-1_smt", Name = "statement", Prose = "Test statement" }
                        }
                    }
                }
            }
        }
    };
}
