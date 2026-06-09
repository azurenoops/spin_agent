// ═══════════════════════════════════════════════════════════════════════════
// Feature 204 (UF-005) — T-063-16: Background import job queue
// Implements the Channel<T>-backed queue used by ScanImportEndpoints
// and drained by ScanImportBackgroundWorker.
// ═══════════════════════════════════════════════════════════════════════════

using System.Threading.Channels;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Represents a single enqueued scan import job.
/// </summary>
public sealed class ScanImportJob
{
    /// <summary>Unique job ID (matches the ScanImportRecord.Id that was pre-created).</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>FK to RegisteredSystem.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Raw file bytes (full content held in memory; max 256 MB enforced by Kestrel).</summary>
    public byte[] FileContent { get; set; } = Array.Empty<byte>();

    /// <summary>Original file name as uploaded.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Detected import type (Ckl, Xccdf, NessusXml).</summary>
    public string ImportType { get; set; } = string.Empty;

    /// <summary>Identity of the uploading user.</summary>
    public string ImportedBy { get; set; } = string.Empty;
}

/// <summary>
/// Bounded Channel-backed queue for scan import jobs.
/// Singleton — shared between <see cref="ScanImportEndpoints"/> and
/// <see cref="ScanImportBackgroundWorker"/>.
/// </summary>
public sealed class ScanImportQueue
{
    // Up to 64 pending jobs before POST /import starts waiting.
    private readonly Channel<ScanImportJob> _channel =
        Channel.CreateBounded<ScanImportJob>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelWriter<ScanImportJob> Writer => _channel.Writer;
    public ChannelReader<ScanImportJob> Reader => _channel.Reader;

    /// <summary>Non-blocking enqueue. Returns false if the channel is full.</summary>
    public bool TryEnqueue(ScanImportJob job) => _channel.Writer.TryWrite(job);

    /// <summary>Async enqueue — waits if the channel is full (up to 64 capacity).</summary>
    public ValueTask EnqueueAsync(ScanImportJob job, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(job, ct);
}
