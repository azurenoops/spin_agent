using System.Text.Json;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Feature 051 T093 — file-system implementation of
/// <see cref="ILoginAuditArchiveSink"/>. Writes one JSON-Lines file per
/// batch under <c>{FileSystemRoot}/{yyyy}/{MM}/login-audit-{guid}.jsonl</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by the development environment and CI tests where Azurite /
/// Azure Storage are not available. The production sink is
/// <c>AzureBlobAppendArchiveSink</c>; selection is driven by
/// <c>Auth:Archive:Sink</c> in
/// <see cref="AuthArchiveOptions"/>.
/// </para>
/// <para>
/// Each invocation creates a NEW file (suffixed with a fresh GUID) so two
/// overlapping batches never overwrite each other. The file path encodes
/// the year and month from <see cref="DateTimeOffset.UtcNow"/> at write
/// time — NOT from the rows' <see cref="LoginAuditEvent.OccurredAt"/> —
/// because the daily archive job runs against rows older than 13 months
/// and grouping by row date would scatter the output across many
/// directories.
/// </para>
/// </remarks>
public sealed class FileSystemArchiveSink : ILoginAuditArchiveSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IOptions<AuthOptions> _options;
    private readonly ILogger<FileSystemArchiveSink> _logger;

    public FileSystemArchiveSink(
        IOptions<AuthOptions> options,
        ILogger<FileSystemArchiveSink> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> WriteBatchAsync(
        IReadOnlyList<LoginAuditEvent> rows,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var archive = _options.Value.Archive;
        var root = string.IsNullOrWhiteSpace(archive.FileSystemRoot)
            ? "./archive"
            : archive.FileSystemRoot;

        var now = DateTimeOffset.UtcNow;
        var dir = Path.Combine(
            root,
            now.Year.ToString("D4"),
            now.Month.ToString("D2"));
        Directory.CreateDirectory(dir);

        var fileName = $"login-audit-{Guid.NewGuid():N}.jsonl";
        var fullPath = Path.GetFullPath(Path.Combine(dir, fileName));

        await using (var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        await using (var writer = new StreamWriter(stream))
        {
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                var json = JsonSerializer.Serialize(row, JsonOptions);
                await writer.WriteLineAsync(json.AsMemory(), ct);
            }
            await writer.FlushAsync(ct);
        }

        _logger.LogInformation(
            "FileSystemArchiveSink wrote {RowCount} rows to {Path}",
            rows.Count, fullPath);

        return fullPath;
    }
}
