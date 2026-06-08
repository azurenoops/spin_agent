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
    public string JobId { get; set; } = string.Empty;
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Queued;
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CancelRequested { get; set; }
}

/// <summary>
/// Singleton registry for in-flight and recently-completed scan import jobs.
/// </summary>
public sealed class ScanImportStatusTracker
{
    private readonly ConcurrentDictionary<string, ImportJobState> _jobs = new(StringComparer.Ordinal);

    /// <summary>Register a new job in Queued state.</summary>
    public ImportJobState Register(string jobId)
    {
        var state = new ImportJobState { JobId = jobId, Status = ImportJobStatus.Queued };
        _jobs[jobId] = state;
        return state;
    }

    /// <summary>Try to get job state; returns null if not found.</summary>
    public ImportJobState? TryGet(string jobId) =>
        _jobs.TryGetValue(jobId, out var s) ? s : null;

    /// <summary>Update job state in-place (caller mutates the returned reference).</summary>
    public ImportJobState? Update(string jobId, Action<ImportJobState> mutate)
    {
        if (!_jobs.TryGetValue(jobId, out var state)) return null;
        mutate(state);
        return state;
    }

    /// <summary>Request cancellation of a job.</summary>
    public bool RequestCancel(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var state)) return false;
        state.CancelRequested = true;
        return true;
    }
}
