using System.Text.Json.Serialization;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for NIST 800-53 control baseline selection, CNSSI 1253 overlay application,
/// control tailoring, inheritance tracking, and CRM generation.
/// </summary>
public interface IBaselineService
{
    /// <summary>
    /// Select the NIST 800-53 baseline for a system based on its FIPS 199 categorization.
    /// Optionally applies a CNSSI 1253 overlay matching the DoD IL.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="applyOverlay">Whether to apply the CNSSI 1253 overlay (default: true).</param>
    /// <param name="overlayName">Override overlay name (e.g., "CNSSI 1253 IL5").</param>
    /// <param name="selectedBy">Identity of the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created ControlBaseline with full control list.</returns>
    /// <exception cref="InvalidOperationException">System not found or no categorization exists.</exception>
    Task<ControlBaseline> SelectBaselineAsync(
        string systemId,
        bool applyOverlay = true,
        string? overlayName = null,
        string selectedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tailor the baseline by adding or removing controls with documented rationale.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="tailoringActions">Array of add/remove actions.</param>
    /// <param name="tailoredBy">Identity of the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated ControlBaseline with tailoring results.</returns>
    Task<TailoringResult> TailorBaselineAsync(
        string systemId,
        IEnumerable<TailoringInput> tailoringActions,
        string tailoredBy = "mcp-user",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set inheritance type for controls (Inherited / Shared / Customer).
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="inheritanceMappings">Array of control inheritance settings.</param>
    /// <param name="setBy">Identity of the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated inheritance counts.</returns>
    Task<InheritanceResult> SetInheritanceAsync(
        string systemId,
        IEnumerable<InheritanceInput> inheritanceMappings,
        string setBy = "mcp-user",
        InheritanceChangeSource changeSource = InheritanceChangeSource.Manual,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the full control baseline for a system with optional tailoring and inheritance details.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="includeDetails">Include tailoring and inheritance records.</param>
    /// <param name="familyFilter">Filter by control family prefix (e.g., "AC").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ControlBaseline if found; null otherwise.</returns>
    Task<ControlBaseline?> GetBaselineAsync(
        string systemId,
        bool includeDetails = false,
        string? familyFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a Customer Responsibility Matrix (CRM) from the baseline's inheritance data.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CRM summary with inherited/shared/customer breakdowns.</returns>
    Task<CrmResult> GenerateCrmAsync(
        string systemId,
        CancellationToken cancellationToken = default);
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>
/// Input DTO for a tailoring action (add or remove a control).
/// </summary>
public class TailoringInput
{
    /// <summary>NIST control ID (e.g., "AC-2(12)").</summary>
    [JsonPropertyName("control_id")]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Action: "Added" or "Removed".</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>Documented justification for the tailoring.</summary>
    [JsonPropertyName("rationale")]
    public string Rationale { get; set; } = string.Empty;
}

/// <summary>
/// Input DTO for setting control inheritance.
/// </summary>
public class InheritanceInput
{
    /// <summary>NIST control ID.</summary>
    [JsonPropertyName("control_id")]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Inheritance type: "Inherited", "Shared", or "Customer".</summary>
    [JsonPropertyName("inheritance_type")]
    public string InheritanceType { get; set; } = string.Empty;

    /// <summary>CSP name if Inherited or Shared (e.g., "Azure Government (FedRAMP High)").</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>Customer responsibility description if Shared.</summary>
    [JsonPropertyName("customer_responsibility")]
    public string? CustomerResponsibility { get; set; }
}

/// <summary>
/// Result of a tailoring operation.
/// </summary>
public class TailoringResult
{
    /// <summary>Updated baseline.</summary>
    public ControlBaseline Baseline { get; set; } = null!;

    /// <summary>Successfully applied actions.</summary>
    public List<TailoringActionResult> Accepted { get; set; } = new();

    /// <summary>Rejected actions with reasons.</summary>
    public List<TailoringActionResult> Rejected { get; set; } = new();
}

/// <summary>
/// Individual tailoring action result.
/// </summary>
public class TailoringActionResult
{
    /// <summary>Control ID.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Action attempted.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Whether the action was accepted.</summary>
    public bool Accepted { get; set; }

    /// <summary>Reason for rejection if not accepted.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Result of an inheritance operation.
/// </summary>
public class InheritanceResult
{
    /// <summary>Updated baseline.</summary>
    public ControlBaseline Baseline { get; set; } = null!;

    /// <summary>Number of controls set.</summary>
    public int ControlsUpdated { get; set; }

    /// <summary>Total inherited controls.</summary>
    public int InheritedCount { get; set; }

    /// <summary>Total shared controls.</summary>
    public int SharedCount { get; set; }

    /// <summary>Total customer controls.</summary>
    public int CustomerCount { get; set; }

    /// <summary>Controls that were skipped (not in baseline).</summary>
    public List<string> SkippedControls { get; set; } = new();

    /// <summary>Number of narratives whose implementation status was auto-updated based on inheritance type.</summary>
    public int NarrativesAutoUpdated { get; set; }
}

/// <summary>
/// Customer Responsibility Matrix result.
/// </summary>
public class CrmResult
{
    /// <summary>System ID.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>System name.</summary>
    public string SystemName { get; set; } = string.Empty;

    /// <summary>Baseline level.</summary>
    public string BaselineLevel { get; set; } = string.Empty;

    /// <summary>Total controls in baseline.</summary>
    public int TotalControls { get; set; }

    /// <summary>Fully inherited controls.</summary>
    public int InheritedControls { get; set; }

    /// <summary>Shared responsibility controls.</summary>
    public int SharedControls { get; set; }

    /// <summary>Customer-only controls.</summary>
    public int CustomerControls { get; set; }

    /// <summary>Controls without inheritance designation.</summary>
    public int UndesignatedControls { get; set; }

    /// <summary>Inheritance percentage.</summary>
    public double InheritancePercentage { get; set; }

    /// <summary>Detailed entries by control family.</summary>
    public List<CrmFamilyGroup> FamilyGroups { get; set; } = new();
}

/// <summary>
/// CRM entries grouped by NIST control family.
/// </summary>
public class CrmFamilyGroup
{
    /// <summary>Family prefix (e.g., "AC").</summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>Family name.</summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>Controls in this family.</summary>
    public List<CrmEntry> Controls { get; set; } = new();
}

/// <summary>
/// Individual CRM entry for a control.
/// </summary>
public class CrmEntry
{
    /// <summary>NIST control ID.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Inheritance type (Inherited/Shared/Customer/Undesignated).</summary>
    public string InheritanceType { get; set; } = string.Empty;

    /// <summary>Provider name if inherited/shared.</summary>
    public string? Provider { get; set; }

    /// <summary>Customer responsibility if shared.</summary>
    public string? CustomerResponsibility { get; set; }

    /// <summary>Source of the designation (Org Default, System Override, CSP Profile, CRM Import, etc.).</summary>
    public string? DesignationSource { get; set; }
}
