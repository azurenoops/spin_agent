namespace Ato.Copilot.Core.Configuration;

/// <summary>
/// Configuration options for the Feature 047 onboarding wizard.
/// Bound from the <c>Onboarding</c> section of <c>appsettings.json</c>.
/// </summary>
public class OnboardingOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c>.</summary>
    public const string SectionName = "Onboarding";

    /// <summary>Per-artifact size limits enforced by upload endpoints.</summary>
    public OnboardingLimits Limits { get; set; } = new();

    /// <summary>SignalR/polling progress configuration (FR-066).</summary>
    public OnboardingProgressOptions Progress { get; set; } = new();

    /// <summary>Background job runner configuration (research §R7).</summary>
    public OnboardingJobOptions Jobs { get; set; } = new();
}

/// <summary>Per-artifact size limits enforced by onboarding upload endpoints.</summary>
public class OnboardingLimits
{
    /// <summary>Max bytes for the organization-context logo upload (default 5 MB).</summary>
    public long MaxOrganizationContextLogoBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Max bytes for a single narrative seed document (default 25 MB).</summary>
    public long MaxNarrativeSeedBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>Max bytes for a single document template upload (default 10 MB).</summary>
    public long MaxDocumentTemplateBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Max bytes for an eMASS export ZIP/XLSX upload (default 50 MB).</summary>
    public long MaxEmassImportBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Max bytes for a single SSP PDF upload (default 100 MB).</summary>
    public long MaxSspPdfImportBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>Max number of SSP PDFs that can be uploaded in a single batch (default 25).</summary>
    public int MaxSspPdfBatchSize { get; set; } = 25;

    /// <summary>Max number of registered Azure subscriptions per tenant (default 100).</summary>
    public int MaxAzureSubscriptionsPerTenant { get; set; } = 100;
}

/// <summary>SignalR/polling progress configuration (FR-066).</summary>
public class OnboardingProgressOptions
{
    /// <summary>
    /// Polling fallback interval (seconds) when SignalR is unavailable. Clients
    /// SHOULD poll <c>GET /api/onboarding/jobs/{jobId}</c> at this cadence.
    /// </summary>
    public int PollingFallbackSeconds { get; set; } = 10;

    /// <summary>
    /// Threshold (seconds) at which a synchronous request is converted to a
    /// background job and progress notifications switch to SignalR/polling.
    /// </summary>
    public int LongRunningThresholdSeconds { get; set; } = 10;
}

/// <summary>Background job runner configuration (research §R7).</summary>
public class OnboardingJobOptions
{
    /// <summary>Maximum number of wizard jobs processed concurrently per server instance.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Bounded channel capacity for queued wizard jobs.</summary>
    public int QueueCapacity { get; set; } = 256;
}
