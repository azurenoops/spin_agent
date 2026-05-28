using System.Text.Json;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Feature 051 T095 — production implementation of
/// <see cref="ILoginAuditArchiveSink"/> backed by an Azure Storage
/// append-blob. One blob per month at
/// <c>{container}/{yyyy}/{MM}/login-audit.jsonl</c>; rows from a batch
/// are appended in JSON-Lines form to the existing append-blob (creating
/// it on first write of the month).
/// </summary>
/// <remarks>
/// <para>
/// Append blobs are the natural fit for immutable audit-trail cold
/// storage — once a row is appended the bytes cannot be overwritten or
/// re-ordered, and standard ImmutableBlobPolicy locks the container
/// after the retention window. The factory delegate in the constructor
/// lets unit tests inject a mock <see cref="AppendBlobClient"/> without
/// touching the network.
/// </para>
/// <para>
/// Transient failures (HTTP 5xx, network errors) retry up to three
/// times with exponential backoff (200ms, 400ms, 800ms). 4xx responses
/// (auth, validation, container missing) are surfaced immediately — the
/// hosted service catches the throw and preserves the rows in the hot
/// table.
/// </para>
/// </remarks>
public sealed class AzureBlobAppendArchiveSink : ILoginAuditArchiveSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const int MaxAttempts = 3;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(200);

    private readonly IOptions<AuthOptions> _options;
    private readonly ILogger<AzureBlobAppendArchiveSink> _logger;
    private readonly Func<string, AppendBlobClient> _clientFactory;

    /// <summary>
    /// Production constructor. Builds an <see cref="AppendBlobClient"/>
    /// per blob path via <see cref="DefaultAzureCredential"/>.
    /// </summary>
    public AzureBlobAppendArchiveSink(
        IOptions<AuthOptions> options,
        ILogger<AzureBlobAppendArchiveSink> logger)
        : this(options, logger, clientFactory: null)
    {
    }

    /// <summary>
    /// Constructor with a factory hook for unit tests. Production callers
    /// should use the two-argument constructor — the
    /// <paramref name="clientFactory"/> is intended for tests that want
    /// to inject a <see cref="Moq.Mock{T}"/> wrapper around
    /// <see cref="AppendBlobClient"/> without round-tripping to Azure.
    /// </summary>
    public AzureBlobAppendArchiveSink(
        IOptions<AuthOptions> options,
        ILogger<AzureBlobAppendArchiveSink> logger,
        Func<string, AppendBlobClient>? clientFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clientFactory = clientFactory ?? BuildDefaultClient;
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

        var now = DateTimeOffset.UtcNow;
        var blobPath = $"{now.Year:D4}/{now.Month:D2}/login-audit.jsonl";
        var client = _clientFactory(blobPath);

        // Serialize the entire batch into a single memory stream so the
        // append blob sees one atomic AppendBlock call per batch. JSONL
        // shape — each line is a standalone JSON object.
        using var payload = new MemoryStream();
        await using (var writer = new StreamWriter(payload, leaveOpen: true))
        {
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                var json = JsonSerializer.Serialize(row, JsonOptions);
                await writer.WriteLineAsync(json);
            }
            await writer.FlushAsync(ct);
        }
        payload.Position = 0;

        await EnsureAppendBlobExistsAsync(client, ct).ConfigureAwait(false);

        Exception? lastError = null;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                payload.Position = 0;
                await client.AppendBlockAsync(payload, cancellationToken: ct)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "AzureBlobAppendArchiveSink wrote {RowCount} rows to {BlobUri} (attempt {Attempt})",
                    rows.Count, client.Uri, attempt);
                return client.Uri.ToString();
            }
            catch (RequestFailedException ex) when (IsTransient(ex.Status) && attempt < MaxAttempts)
            {
                lastError = ex;
                var delay = InitialBackoff * Math.Pow(2, attempt - 1);
                _logger.LogWarning(ex,
                    "AzureBlobAppendArchiveSink transient failure on attempt {Attempt}/{Max} (status={Status}); retrying in {Delay}",
                    attempt, MaxAttempts, ex.Status, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (RequestFailedException ex)
            {
                // 4xx (or final 5xx attempt) — surface immediately so
                // the hosted service preserves the rows for next run.
                _logger.LogError(ex,
                    "AzureBlobAppendArchiveSink non-retryable failure (status={Status})",
                    ex.Status);
                throw;
            }
        }

        throw lastError
            ?? new InvalidOperationException("AzureBlobAppendArchiveSink exhausted retries.");
    }

    private async Task EnsureAppendBlobExistsAsync(
        AppendBlobClient client,
        CancellationToken ct)
    {
        try
        {
            await client.CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // 409 Conflict = blob already exists with incompatible type.
            // This is non-retryable. Re-throw.
            throw;
        }
    }

    private static bool IsTransient(int status) =>
        status >= 500 && status < 600;

    private AppendBlobClient BuildDefaultClient(string blobPath)
    {
        var archive = _options.Value.Archive;
        if (string.IsNullOrWhiteSpace(archive.AzureBlobAccountUrl))
        {
            throw new InvalidOperationException(
                "Auth:Archive:AzureBlobAccountUrl is required for AzureBlobAppendArchiveSink.");
        }
        if (string.IsNullOrWhiteSpace(archive.AzureBlobContainer))
        {
            throw new InvalidOperationException(
                "Auth:Archive:AzureBlobContainer is required for AzureBlobAppendArchiveSink.");
        }

        var serviceClient = new BlobServiceClient(
            new Uri(archive.AzureBlobAccountUrl),
            new DefaultAzureCredential());
        var container = serviceClient.GetBlobContainerClient(archive.AzureBlobContainer);
        return container.GetAppendBlobClient(blobPath);
    }
}
