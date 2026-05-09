namespace Ato.Copilot.Core.Configuration.Tenancy;

/// <summary>
/// Configuration POCO bound from <c>Csp:Inheritance:*</c> for Feature 048 US9 /
/// US10. Wired in <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// by T206.
/// </summary>
public sealed class CspInheritedOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Csp:Inheritance";

    /// <summary>
    /// AI-mapping confidence threshold in <c>[0.0, 1.0]</c>. Capabilities whose
    /// max confidence falls below this value are persisted with
    /// <c>Status = NeedsReview</c>. Default <c>0.6</c>.
    /// </summary>
    public double MappingConfidenceThreshold { get; set; } = 0.6;

    /// <summary>
    /// Per-file upload size cap in bytes. Mirrors the OpenAPI contract
    /// (<c>50 * 1024 * 1024</c>). The endpoint layer enforces this; the value
    /// is centralized here so dashboard and server agree.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 50L * 1024L * 1024L;
}
