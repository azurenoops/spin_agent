using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Feature 051 T098 — daily background job that moves
/// <see cref="LoginAuditEvent"/> rows older than 13 months from the hot
/// <c>LoginAuditEvents</c> table to the configured
/// <see cref="ILoginAuditArchiveSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="ILoginAuditArchiveService"/> and extends
/// <see cref="BackgroundService"/>. Per
/// <c>contracts/internal-services.md § 4.4</c>:
/// </para>
/// <list type="number">
///   <item>Wait until the next <see cref="AuthArchiveOptions.RunHourUtc"/>
///         wall-clock occurrence (default 02:00 UTC).</item>
///   <item>Compute <c>cutoff = UtcNow - 395 days</c> (13 months \u2248 395
///         days; slight over-retention is intentional per AU-11).</item>
///   <item>Loop on 1,000-row batches ordered by <c>OccurredAt</c> ASC.
///         Each batch is fetched with <c>.IgnoreQueryFilters()</c> because
///         the archive job is tenant-agnostic — it scans across every
///         tenant including SYSTEM_TENANT_ID.</item>
///   <item>Send the batch to the sink. On success, <c>RemoveRange</c> +
///         <c>SaveChangesAsync</c>. On exception, log and abort the
///         inner loop so rows remain in the hot table for next run.</item>
///   <item>Sleep until the next <c>RunHourUtc</c>.</item>
/// </list>
/// <para>
/// The sink owns idempotency on its side (append-blob is naturally
/// idempotent on retry; FileSystemArchiveSink mints a new GUID-suffixed
/// filename per call). Once removed from the hot table, the only record
/// of an archived row is in the cold archive.
/// </para>
/// </remarks>
public sealed class LoginAuditArchiveService
    : BackgroundService, ILoginAuditArchiveService
{
    /// <summary>13 months \u2248 395 days per data-model.md § 1.6.</summary>
    private static readonly TimeSpan HotRetention = TimeSpan.FromDays(395);

    /// <summary>Batch size per archive cycle iteration (R3).</summary>
    public const int BatchSize = 1000;

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILoginAuditArchiveSink _sink;
    private readonly IOptions<AuthOptions> _options;
    private readonly ILogger<LoginAuditArchiveService> _logger;

    public LoginAuditArchiveService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILoginAuditArchiveSink sink,
        IOptions<AuthOptions> options,
        ILogger<LoginAuditArchiveService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runHourUtc = NormalizeHour(_options.Value.Archive.RunHourUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = UntilNext(DateTimeOffset.UtcNow, runHourUtc);
            _logger.LogInformation(
                "LoginAuditArchiveService idle until next {RunHourUtc:D2}:00 UTC (in {Delay})",
                runHourUtc, delay);

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await ArchiveOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Catch-all so a one-time exception does not crash the
                // background loop — the next iteration retries.
                _logger.LogError(ex,
                    "LoginAuditArchiveService cycle failed; will retry next run.");
            }
        }
    }

    /// <summary>
    /// Single archive cycle: drain rows older than the cutoff in
    /// 1,000-row batches until either the table is empty for that
    /// window or a sink failure aborts the inner loop. Exposed publicly
    /// so unit tests can drive a cycle without waiting on the wall-clock
    /// timer.
    /// </summary>
    public async Task ArchiveOnceAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - HotRetention;
        int batchNo = 0;

        while (!ct.IsCancellationRequested)
        {
            await using var db = await _contextFactory
                .CreateDbContextAsync(ct)
                .ConfigureAwait(false);

            var batch = await FetchOverRetentionBatchAsync(db, cutoff, ct)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                _logger.LogDebug(
                    "LoginAuditArchiveService cycle complete after {BatchCount} batch(es); no rows older than {Cutoff}.",
                    batchNo, cutoff);
                return;
            }

            try
            {
                var location = await _sink
                    .WriteBatchAsync(batch, ct)
                    .ConfigureAwait(false);

                db.LoginAuditEvents.RemoveRange(batch);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                batchNo++;
                _logger.LogInformation(
                    "LoginAuditArchiveService archived batch {BatchNo} ({RowCount} rows) to {Location}",
                    batchNo, batch.Count, location);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sink failure: log + abort the inner loop so rows
                // remain in the hot table for the next cycle.
                _logger.LogWarning(ex,
                    "LoginAuditArchiveService sink failure on batch {BatchNo}; aborting cycle.",
                    batchNo + 1);
                return;
            }
        }
    }

    /// <summary>
    /// Fetch the next batch of rows whose <see cref="LoginAuditEvent.OccurredAt"/>
    /// is older than <paramref name="cutoff"/>.
    /// </summary>
    /// <remarks>
    /// SQL Server translates <c>OrderBy(OccurredAt).Where(OccurredAt &lt;
    /// cutoff).Take(BatchSize)</c> directly. SQLite (dev / test) cannot
    /// translate <c>DateTimeOffset</c> in <c>ORDER BY</c> or relational
    /// predicates, so we fall back to a client-side filter over a
    /// bounded pull. The bound is generous enough for unit tests but is
    /// NOT a production code path.
    /// </remarks>
    private static async Task<List<LoginAuditEvent>> FetchOverRetentionBatchAsync(
        AtoCopilotContext db,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        if (db.Database.IsSqlite())
        {
            // Tests: pull a generous cap and filter client-side. Bounded
            // by 2 * BatchSize so a 2,500-row test still drains in three
            // {1000, 1000, 500} iterations.
            var pulled = await db.LoginAuditEvents
                .IgnoreQueryFilters()
                .Take(BatchSize * 2)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return pulled
                .Where(e => e.OccurredAt < cutoff)
                .OrderBy(e => e.OccurredAt)
                .Take(BatchSize)
                .ToList();
        }

        // Production (SQL Server): server-side filter + order + take.
        return await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.OccurredAt < cutoff)
            .OrderBy(e => e.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compute the <see cref="TimeSpan"/> from <paramref name="now"/>
    /// until the next occurrence of <paramref name="runHourUtc"/>:00 UTC.
    /// Exposed publicly so the schedule logic can be tested without
    /// running the background loop.
    /// </summary>
    public static TimeSpan UntilNext(DateTimeOffset now, int runHourUtc)
    {
        runHourUtc = NormalizeHour(runHourUtc);
        var nextRun = new DateTimeOffset(
            now.Year, now.Month, now.Day, runHourUtc, 0, 0, TimeSpan.Zero);
        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }
        return nextRun - now;
    }

    private static int NormalizeHour(int hour) =>
        hour < 0 ? 0 : hour > 23 ? 2 : hour;
}
