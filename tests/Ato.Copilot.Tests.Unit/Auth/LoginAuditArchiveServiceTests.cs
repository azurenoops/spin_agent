using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Auth;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T097 [US10] — contract test for
/// <see cref="LoginAuditArchiveService"/>. Pins the four behaviours from
/// <c>contracts/internal-services.md § 4.4</c>:
/// <list type="bullet">
///   <item>Drains 2,500 over-retention rows in three 1,000-row batches
///         (1000 + 1000 + 500) and leaves newer rows untouched.</item>
///   <item>A sink failure aborts the batch + retains rows in the hot
///         table (no <c>SaveChanges</c> after the throw).</item>
///   <item>The <c>UntilNext</c> schedule helper produces a positive
///         delay that points at the configured hour.</item>
///   <item>Cancellation via <see cref="IHostedService.StopAsync"/>
///         exits cleanly.</item>
/// </list>
/// </summary>
public sealed class LoginAuditArchiveServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AtoCopilotContext> _options;
    private readonly StubDbContextFactory _factory;

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public LoginAuditArchiveServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(_connection)
            .Options;

        _factory = new StubDbContextFactory(_options);

        using var ctx = new AtoCopilotContext(_options);
        ctx.Database.EnsureCreated();
        ctx.Tenants.Add(new Tenant
        {
            Id = TenantA,
            DisplayName = "Tenant A",
            CreatedBy = "test",
        });
        ctx.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    // ─── Happy path — drain 2,500 in three batches ─────────────────────

    [Fact]
    public async Task ArchiveOnceAsync_Drains2500InThreeBatches_OldRowsOnly()
    {
        // Arrange — 2,500 rows older than the cutoff + 100 newer.
        var oldAnchor = DateTimeOffset.UtcNow.AddDays(-400);
        var freshAnchor = DateTimeOffset.UtcNow.AddDays(-10);
        await SeedRowsAsync(TenantA, count: 2500, baseTime: oldAnchor);
        await SeedRowsAsync(TenantA, count: 100, baseTime: freshAnchor);

        var sinkBatches = new List<int>();
        var sink = new Mock<ILoginAuditArchiveSink>(MockBehavior.Strict);
        sink.Setup(s => s.WriteBatchAsync(
                It.IsAny<IReadOnlyList<LoginAuditEvent>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<LoginAuditEvent>, CancellationToken>(
                (rows, _) => sinkBatches.Add(rows.Count))
            .ReturnsAsync("ok");

        var sut = NewSut(sink.Object);

        // Act — drive ONE archive cycle directly (bypass the timer).
        await sut.ArchiveOnceAsync(CancellationToken.None);

        // Assert — three batches of {1000, 1000, 500}.
        sinkBatches.Should().Equal(new[] {
            LoginAuditArchiveService.BatchSize,
            LoginAuditArchiveService.BatchSize,
            500,
        }, "2,500 over-retention rows must drain in three 1,000-row batches.");

        // Newer rows untouched, older rows gone.
        await using var verify = new AtoCopilotContext(_options);
        var remaining = await verify.LoginAuditEvents
            .IgnoreQueryFilters()
            .CountAsync();
        remaining.Should().Be(100,
            "only the 100 rows within the 13-month retention window must remain.");
    }

    // ─── Sink failure aborts batch, retains rows ───────────────────────

    [Fact]
    public async Task ArchiveOnceAsync_SinkFailure_AbortsBatch_RetainsRows()
    {
        // Arrange — 1,200 over-retention rows. First batch succeeds,
        // second throws. Expected: 1,000 archived, 200 remain (we never
        // touched the second batch's hot rows after the sink threw).
        await SeedRowsAsync(TenantA, count: 1200,
            baseTime: DateTimeOffset.UtcNow.AddDays(-400));

        int sinkCalls = 0;
        var sink = new Mock<ILoginAuditArchiveSink>(MockBehavior.Strict);
        sink.Setup(s => s.WriteBatchAsync(
                It.IsAny<IReadOnlyList<LoginAuditEvent>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyList<LoginAuditEvent>, CancellationToken>((_, _) =>
            {
                sinkCalls++;
                return sinkCalls == 1
                    ? Task.FromResult("ok")
                    : throw new InvalidOperationException("simulated sink failure");
            });

        var sut = NewSut(sink.Object);

        // Act
        await sut.ArchiveOnceAsync(CancellationToken.None);

        // Assert — exactly two sink calls (the second threw); 200 rows
        // remain in the hot table because the failed batch was NOT
        // deleted.
        sinkCalls.Should().Be(2,
            "the second batch attempt happens before the throw is caught.");

        await using var verify = new AtoCopilotContext(_options);
        var remaining = await verify.LoginAuditEvents
            .IgnoreQueryFilters()
            .CountAsync();
        remaining.Should().Be(200,
            "the failed batch's rows MUST remain in the hot table for the next cycle.");
    }

    // ─── Schedule — UntilNext positive delay against configured hour ──

    [Theory]
    [InlineData(2)]
    [InlineData(13)]
    [InlineData(23)]
    public void UntilNext_ReturnsPositiveDelay_TargetingConfiguredHour(int runHourUtc)
    {
        // Arrange
        var now = new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero);

        // Act
        var delay = LoginAuditArchiveService.UntilNext(now, runHourUtc);

        // Assert
        delay.Should().BeGreaterThan(TimeSpan.Zero);
        var nextRun = now + delay;
        nextRun.Hour.Should().Be(runHourUtc);
        nextRun.Minute.Should().Be(0);
        nextRun.Second.Should().Be(0);
    }

    [Fact]
    public void UntilNext_PastTodayHour_RollsToTomorrow()
    {
        // Arrange — at 14:00, run hour 02 is past today; expected next
        // run is tomorrow at 02:00.
        var now = new DateTimeOffset(2026, 5, 28, 14, 0, 0, TimeSpan.Zero);

        // Act
        var delay = LoginAuditArchiveService.UntilNext(now, runHourUtc: 2);

        // Assert
        var nextRun = now + delay;
        nextRun.Date.Should().Be(now.Date.AddDays(1));
        nextRun.Hour.Should().Be(2);
    }

    // ─── Cancellation — StopAsync exits cleanly ────────────────────────

    [Fact]
    public async Task StopAsync_AfterStart_ExitsCleanly()
    {
        // Arrange — empty hot table, sink set up loosely.
        var sink = new Mock<ILoginAuditArchiveSink>();
        var sut = NewSut(sink.Object);

        using var cts = new CancellationTokenSource();

        // Act — start the BackgroundService then immediately cancel via
        // StopAsync. The Task.Delay inside ExecuteAsync respects the
        // stopping token and unwinds without throwing.
        await sut.StartAsync(cts.Token);
        await sut.StopAsync(cts.Token);

        // Assert — no exception escaped. Sink should NOT have been
        // called because the wall-clock delay would not yet have
        // elapsed; even if it had, an empty hot table is a no-op.
        sink.Verify(s => s.WriteBatchAsync(
                It.IsAny<IReadOnlyList<LoginAuditEvent>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private LoginAuditArchiveService NewSut(ILoginAuditArchiveSink sink)
    {
        var options = Options.Create(new AuthOptions
        {
            Archive = new AuthArchiveOptions
            {
                Sink = ArchiveSinkKind.FileSystem,
                RunHourUtc = 2,
            },
        });
        return new LoginAuditArchiveService(
            _factory,
            sink,
            options,
            NullLogger<LoginAuditArchiveService>.Instance);
    }

    private async Task SeedRowsAsync(
        Guid tenantId,
        int count,
        DateTimeOffset baseTime)
    {
        await using var db = new AtoCopilotContext(_options);

        // SQLite EnsureCreated is single-fixture; we batch inserts in
        // chunks to keep memory bounded and the test fast.
        const int chunk = 500;
        for (int offset = 0; offset < count; offset += chunk)
        {
            int take = Math.Min(chunk, count - offset);
            for (int i = 0; i < take; i++)
            {
                db.LoginAuditEvents.Add(new LoginAuditEvent
                {
                    Id = Guid.NewGuid(),
                    EventType = LoginAuditEventType.LoginSuccess,
                    Oid = $"oid-{offset + i}",
                    Tid = null,
                    EffectiveTenantId = tenantId,
                    CorrelationId = $"corr-{offset + i}",
                    SourceIp = "10.0.0.1",
                    UserAgent = "Mozilla/5.0",
                    Surface = LoginSurface.Dashboard,
                    OccurredAt = baseTime.AddSeconds(offset + i),
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }

    private sealed class StubDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;

        public StubDbContextFactory(DbContextOptions<AtoCopilotContext> options)
        {
            _options = options;
        }

        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
