using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models;

/// <summary>
/// A cached tool response entry for both in-memory and persistent storage (offline mode).
/// Composite key: SHA256(toolName:sortedParamsJson:subscriptionId).
/// </summary>
[TenantScoped]
public class CachedResponse
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Primary key (EF Core, persistent cache only).</summary>
    public int Id { get; set; }

    /// <summary>Composite cache key: SHA256(tool:params:subscriptionId).</summary>
    [Required]
    [MaxLength(256)]
    public string CacheKey { get; set; } = "";

    /// <summary>Name of the tool that produced the response.</summary>
    [Required]
    [MaxLength(200)]
    public string ToolName { get; set; } = "";

    /// <summary>Serialized JSON of the tool response.</summary>
    [Required]
    public string Response { get; set; } = "";

    /// <summary>Timestamp when entry was cached.</summary>
    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Time-to-live in seconds.</summary>
    [Range(1, 86400)]
    public int TtlSeconds { get; set; } = 900;

    /// <summary>Origin of the data: "online" or "cached".</summary>
    [MaxLength(20)]
    public string Source { get; set; } = "online";

    /// <summary>Number of cache hits since creation.</summary>
    public int HitCount { get; set; }

    /// <summary>Azure subscription scope for per-subscription isolation.</summary>
    [Required]
    [MaxLength(100)]
    public string SubscriptionId { get; set; } = "";

    /// <summary>Whether this cache entry has expired based on TTL.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow > CachedAt.AddSeconds(TtlSeconds);
}

/// <summary>
/// Root configuration object for the Caching appsettings section.
/// </summary>
public class CachingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Caching";

    /// <summary>Maximum cache size in megabytes.</summary>
    [Range(1, 1024)]
    public int SizeLimitMb { get; set; } = 256;

    /// <summary>Default TTL in seconds for cached responses.</summary>
    [Range(1, 86400)]
    public int DefaultTtlSeconds { get; set; } = 900;

    /// <summary>TTL in seconds for control lookup cache entries.</summary>
    [Range(1, 86400)]
    public int ControlLookupTtlSeconds { get; set; } = 3600;

    /// <summary>TTL in seconds for assessment cache entries.</summary>
    [Range(1, 86400)]
    public int AssessmentTtlSeconds { get; set; } = 900;

    /// <summary>Whether to serve stale data while refreshing in the background.</summary>
    public bool EnableStaleWhileRevalidate { get; set; } = true;
}
