using Ato.Copilot.Core.Models.Auth;

namespace Ato.Copilot.Core.Interfaces.Auth;

/// <summary>
/// Feature 051 — plug-in point for the cold-archive storage implementation
/// used by <see cref="ILoginAuditArchiveService"/>. Per
/// <c>contracts/internal-services.md § 4.2</c>:
/// </summary>
/// <remarks>
/// <para>
/// Each call writes ONE batch of <see cref="LoginAuditEvent"/> rows to the
/// cold tier. The sink owns the on-disk / on-blob format and is responsible
/// for idempotency on retry (the hosted service does NOT track which rows
/// have already been archived — once a row leaves the hot table the only
/// record is in the sink).
/// </para>
/// <para>
/// Two implementations ship per R3: <c>FileSystemArchiveSink</c> (dev / CI)
/// and <c>AzureBlobAppendArchiveSink</c> (prod, AzureUSGovernment storage
/// account, immutable container). DI selection is driven by the
/// <c>Auth:Archive:Sink</c> configuration value in
/// <see cref="Ato.Copilot.Core.Configuration.Auth.AuthArchiveOptions"/>.
/// </para>
/// <para>
/// Throwing from <see cref="WriteBatchAsync"/> aborts the current batch and
/// preserves the rows in the hot table so the next run can retry. The
/// hosted service catches the throw and logs at <c>Warning</c>.
/// </para>
/// </remarks>
public interface ILoginAuditArchiveSink
{
    /// <summary>
    /// Persist one batch of rows to the cold-archive sink. Returns a
    /// descriptive identifier of the archive location (file path or
    /// blob URL) on success — used for structured logging by the
    /// hosted service. An empty input is a no-op that returns an
    /// empty string; callers should not invoke with an empty batch
    /// but the contract does not throw on it.
    /// </summary>
    /// <param name="rows">The rows to archive. Never <c>null</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> WriteBatchAsync(
        IReadOnlyList<LoginAuditEvent> rows,
        CancellationToken ct);
}
