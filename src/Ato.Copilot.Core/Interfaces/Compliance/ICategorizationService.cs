using System.Text.Json.Serialization;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for FIPS 199 / SP 800-60 security categorization of registered systems.
/// Provides information type management, high-water-mark computation, and DoD IL derivation.
/// </summary>
public interface ICategorizationService
{
    /// <summary>
    /// Perform or update FIPS 199 security categorization for a registered system.
    /// Updates the current <see cref="SecurityCategorization"/> projection and appends an immutable history version.
    /// Computes the C/I/A high-water mark automatically.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID (GUID string).</param>
    /// <param name="informationTypes">Information types with C/I/A impact levels.</param>
    /// <param name="categorizedBy">Identity of the user performing categorization.</param>
    /// <param name="isNationalSecuritySystem">Whether the system is designated NSS (affects IL derivation).</param>
    /// <param name="justification">Optional overall categorization rationale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created/updated SecurityCategorization with computed fields.</returns>
    /// <exception cref="InvalidOperationException">Thrown when system not found or no information types provided.</exception>
    Task<SecurityCategorization> CategorizeSystemAsync(
        string systemId,
        IEnumerable<InformationTypeInput> informationTypes,
        string categorizedBy,
        bool isNationalSecuritySystem = false,
        string? justification = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the security categorization for a system, including information types.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The categorization if found; null otherwise.</returns>
    Task<SecurityCategorization?> GetCategorizationAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve immutable categorization decisions, newest version first.
    /// </summary>
    Task<IReadOnlyList<CategorizationHistoryEntry>> GetCategorizationHistoryAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggest SP 800-60 information types based on system description and type.
    /// Returns a ranked list with confidence scores using heuristic matching.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="additionalContext">Extra context for better suggestions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked list of suggested information types.</returns>
    Task<IReadOnlyList<SuggestedInformationType>> SuggestInfoTypesAsync(
        string systemId,
        string? additionalContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compute the FIPS 199 high-water mark from supplied information types
    /// without persisting. Useful for "what-if" analysis.
    /// </summary>
    /// <param name="informationTypes">Collection of information types to evaluate.</param>
    /// <param name="isNationalSecuritySystem">NSS flag for IL derivation.</param>
    /// <returns>Computed categorization summary.</returns>
    CategorizationSummary ComputeHighWaterMark(
        IEnumerable<InformationTypeInput> informationTypes,
        bool isNationalSecuritySystem = false);
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>
/// Input DTO for specifying an information type with C/I/A impact levels.
/// </summary>
public class InformationTypeInput
{
    /// <summary>SP 800-60 identifier (e.g., "D.1.1").</summary>
    [JsonPropertyName("sp800_60_id")]
    public string Sp80060Id { get; set; } = string.Empty;

    /// <summary>Information type name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>SP 800-60 category.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Confidentiality impact.</summary>
    [JsonPropertyName("confidentiality_impact")]
    public string ConfidentialityImpact { get; set; } = "Low";

    /// <summary>Integrity impact.</summary>
    [JsonPropertyName("integrity_impact")]
    public string IntegrityImpact { get; set; } = "Low";

    /// <summary>Availability impact.</summary>
    [JsonPropertyName("availability_impact")]
    public string AvailabilityImpact { get; set; } = "Low";

    /// <summary>Whether values match SP 800-60 provisional defaults.</summary>
    [JsonPropertyName("uses_provisional")]
    public bool UsesProvisional { get; set; } = true;

    /// <summary>Required if UsesProvisional is false — justification for adjustment.</summary>
    [JsonPropertyName("adjustment_justification")]
    public string? AdjustmentJustification { get; set; }
}

/// <summary>
/// AI/heuristic-suggested information type with confidence score.
/// </summary>
public class SuggestedInformationType
{
    /// <summary>SP 800-60 identifier.</summary>
    public string Sp80060Id { get; set; } = string.Empty;

    /// <summary>Information type name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SP 800-60 category.</summary>
    public string? Category { get; set; }

    /// <summary>Confidence score (0.0–1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Reason for the suggestion.</summary>
    public string? Rationale { get; set; }

    /// <summary>Default confidentiality impact per SP 800-60 Vol. 2.</summary>
    public string DefaultConfidentialityImpact { get; set; } = "Low";

    /// <summary>Default integrity impact per SP 800-60 Vol. 2.</summary>
    public string DefaultIntegrityImpact { get; set; } = "Low";

    /// <summary>Default availability impact per SP 800-60 Vol. 2.</summary>
    public string DefaultAvailabilityImpact { get; set; } = "Low";
}

/// <summary>
/// Summary of a high-water mark computation, returned by <see cref="ICategorizationService.ComputeHighWaterMark"/>.
/// </summary>
public class CategorizationSummary
{
    /// <summary>Maximum confidentiality impact across all information types.</summary>
    public ImpactValue ConfidentialityImpact { get; set; }

    /// <summary>Maximum integrity impact.</summary>
    public ImpactValue IntegrityImpact { get; set; }

    /// <summary>Maximum availability impact.</summary>
    public ImpactValue AvailabilityImpact { get; set; }

    /// <summary>Overall categorization (high-water mark of C/I/A).</summary>
    public ImpactValue OverallCategorization { get; set; }

    /// <summary>Derived DoD Impact Level (IL2/IL4/IL5/IL6).</summary>
    public string DoDImpactLevel { get; set; } = string.Empty;

    /// <summary>Derived NIST baseline level (Low/Moderate/High).</summary>
    public string NistBaseline { get; set; } = string.Empty;

    /// <summary>Formal FIPS 199 notation string.</summary>
    public string FormalNotation { get; set; } = string.Empty;

    /// <summary>Number of information types evaluated.</summary>
    public int InformationTypeCount { get; set; }
}
