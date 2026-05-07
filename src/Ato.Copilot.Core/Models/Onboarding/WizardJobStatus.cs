namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Persisted state of a background wizard job (FR-064 / FR-066). Drives both the SignalR
/// progress stream and the <c>GET /api/onboarding/jobs/{jobId}</c> polling fallback.
/// </summary>
public class WizardJobStatus
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Type of work being performed.</summary>
    public WizardJobType JobType { get; set; }

    /// <summary>Lifecycle status.</summary>
    public WizardJobState Status { get; set; } = WizardJobState.Queued;

    /// <summary>Progress percentage (0–100); null when not applicable.</summary>
    public int? Percent { get; set; }

    /// <summary>Last status message (free-form, max 1024 chars).</summary>
    public string? Message { get; set; }

    /// <summary>Last error code from the wizard error catalog (Onboarding/WizardErrorCodes).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Plain-language remediation suggestion (Constitution VII).</summary>
    public string? Suggestion { get; set; }

    /// <summary>JSON: job descriptor (artifact ids, parameters).</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>JSON: result payload (set on <c>Succeeded</c>).</summary>
    public string? Result { get; set; }

    public DateTimeOffset? EnqueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>User who enqueued the job.</summary>
    public Guid EnqueuedBy { get; set; }
}

/// <summary>Type of work performed by a wizard job.</summary>
public enum WizardJobType
{
    EmassParse,
    EmassCommit,
    SspPdfExtract,
    NarrativeSeedIndex,
    TemplateValidation,
    ExportRerender,
    ImportRerender,
}

/// <summary>Lifecycle state of a wizard job.</summary>
public enum WizardJobState
{
    Queued,
    InProgress,
    Succeeded,
    Failed,
    Cancelled,
}
