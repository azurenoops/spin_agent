using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models;

/// <summary>
/// Configuration for a named endpoint rate limit policy using sliding window algorithm.
/// </summary>
public class RateLimitPolicy
{
    /// <summary>Named policy identifier (e.g., "chat", "stream", "jsonrpc").</summary>
    [Required]
    public string PolicyName { get; set; } = "";

    /// <summary>Route pattern this policy applies to (e.g., "/mcp/chat").</summary>
    [Required]
    public string Endpoint { get; set; } = "";

    /// <summary>Maximum requests permitted per sliding window.</summary>
    [Range(1, 10000)]
    public int PermitLimit { get; set; } = 30;

    /// <summary>Sliding window duration in seconds.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Number of segments in the sliding window.</summary>
    [Range(1, 10)]
    public int SegmentsPerWindow { get; set; } = 2;
}

/// <summary>
/// Root configuration object for the RateLimiting appsettings section.
/// </summary>
public class RateLimitingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Per-endpoint rate limit policies.</summary>
    public List<RateLimitPolicy> Policies { get; set; } =
    [
        new() { PolicyName = "chat", Endpoint = "/mcp/chat", PermitLimit = 30, WindowSeconds = 60, SegmentsPerWindow = 2 },
        new() { PolicyName = "stream", Endpoint = "/mcp/chat/stream", PermitLimit = 10, WindowSeconds = 60, SegmentsPerWindow = 2 },
        new() { PolicyName = "jsonrpc", Endpoint = "/mcp", PermitLimit = 60, WindowSeconds = 60, SegmentsPerWindow = 2 }
    ];

    /// <summary>Endpoints exempt from rate limiting.</summary>
    public List<string> ExemptEndpoints { get; set; } = ["/health", "/mcp/tools"];
}
