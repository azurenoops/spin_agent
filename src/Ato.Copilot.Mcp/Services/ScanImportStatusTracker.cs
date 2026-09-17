// ═══════════════════════════════════════════════════════════════════════════
// Feature 204 (UF-005) — T-063-17: In-flight scan import status tracker
// Provides polling support for GET /scans/import/{id}/status and
// SignalR broadcast state. Backed by ConcurrentDictionary (in-process only;
// sufficient because only one MCP instance processes a given job).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Concurrent;

namespace Ato.Copilot.Mcp.Services;

/// <summary>Status of an in-flight or completed scan import job.</summary>
public enum ImportJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Mutable snapshot of a scan import job's progress.</summary>
public sealed class ImportJobState
{
    private readonly CancellationTokenSource _cancellation = new();

    public string JobId { get; set; } = string.Empty;
    public string SystemId { get; set; } = string.Empty;
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Queued;
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CancelRequested { get; set; }

    internal object SyncRoot { get; } = new();
    internal CancellationToken CancellationToken => _cancellation.Token;
    internal void Cancel() => _cancellation.Cancel();
}

/// <summary>
/// Singleton registry for in-flight and recently-completed scan import jobs.
/// </summary>
public sealed class ScanImportStatusTracker
{
    private readonly ConcurrentDictionary<string, ImportJobState> _jobs = new(StringComparer.Ordinal);

    /// <summary>Register a new job in Queued state.</summary>
    public ImportJobState Register(string jobId, string systemId = "")
    {
        var state = new ImportJobState
        {
            JobId = jobId,
            SystemId = systemId,
            Status = ImportJobStatus.Queued,
        };
        _jobs[jobId] = state;
        return state;
    }

    /// <summary>Try to get job state; returns null if not found.</summary>
    public ImportJobState? TryGet(string jobId) =>
        _jobs.TryGetValue(jobId, out var s) ? s : null;

    /// <summary>Try to get job state for the system that owns it.</summary>
    public ImportJobState? TryGet(string systemId, string jobId) =>
        _jobs.TryGetValue(jobId, out var state)
        && string.Equals(state.SystemId, systemId, StringComparison.Ordinal)
            ? state
            : null;

    /// <summary>Get the cancellation token associated with a registered job.</summary>
    public CancellationToken GetCancellationToken(string jobId) =>
        _jobs.TryGetValue(jobId, out var state)
            ? state.CancellationToken
            : throw new KeyNotFoundException($"Import job '{jobId}' was not registered.");

    /// <summary>Transition a queued job to processing unless cancellation was requested.</summary>
    public bool TryStart(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state)) return false;

        lock (state.SyncRoot)
        {
            if (state.CancelRequested || state.Status == ImportJobStatus.Cancelled) return false;
            state.Status = ImportJobStatus.Processing;
            return true;
        }
    }

    /// <summary>Complete a job only when no cancellation request won the race.</summary>
    public bool TryComplete(string jobId, int processedCount, int totalCount)
    {
        if (!_jobs.TryGetValue(jobId, out var state)) return false;

        lock (state.SyncRoot)
        {
            if (state.CancelRequested || state.Status == ImportJobStatus.Cancelled) return false;
            state.Status = ImportJobStatus.Completed;
            state.ProcessedCount = processedCount;
            state.TotalCount = totalCount;
            return true;
        }
    }

    /// <summary>Update job state in-place (caller mutates the returned reference).</summary>
    public ImportJobState? Update(string jobId, Action<ImportJobState> mutate)
    {
        if (!_jobs.TryGetValue(jobId, out var state)) return null;
        lock (state.SyncRoot)
        {
            mutate(state);
        }
        return state;
    }

    /// <summary>Request cancellation of a job.</summary>
    public bool RequestCancel(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state)) return false;

        return RequestCancel(state);
    }

    /// <summary>Request cancellation of a job owned by the specified system.</summary>
    public bool RequestCancel(string systemId, string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state)
            || !string.Equals(state.SystemId, systemId, StringComparison.Ordinal)) return false;

        return RequestCancel(state);
    }

    private static bool RequestCancel(ImportJobState state)
    {
        lock (state.SyncRoot)
        {
            if (state.Status is ImportJobStatus.Completed or ImportJobStatus.Failed or ImportJobStatus.Cancelled)
                return false;

            state.CancelRequested = true;
            state.Status = ImportJobStatus.Cancelled;
        }

        state.Cancel();
        return true;
    }

    /// <summary>Remove a job that could not be queued.</summary>
    public bool Remove(string jobId) => _jobs.TryRemove(jobId, out _);
}
