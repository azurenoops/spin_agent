using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for Security Assessment Plan (SAP) generation, customization, finalization, and retrieval.
/// Produces the mandatory RMF Step 4 deliverable by assembling control baselines, OSCAL assessment
/// objectives, STIG mappings, evidence data, and SCA-provided inputs into a structured SAP document.
/// </summary>
/// <remarks>Feature 018.</remarks>
public interface ISapService
{
    /// <summary>Read the latest saved SAP, including all content and children, without preferring finalized history.</summary>
    Task<SecurityAssessmentPlan?> GetWorkingSapAsync(string systemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a Security Assessment Plan for a registered system.
    /// Auto-populates from baseline, OSCAL objectives, STIG mappings, and evidence data.
    /// Accepts SCA overrides for schedule, team, scope, and per-control assessment methods.
    /// </summary>
    /// <param name="input">SAP generation parameters including system ID and optional overrides.</param>
    /// <param name="generatedBy">Identity of the user generating the SAP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated SAP document with content and metadata.</returns>
    /// <exception cref="InvalidOperationException">System not found or baseline not selected.</exception>
    Task<SapDocument> GenerateSapAsync(
        SapGenerationInput input,
        string generatedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a Draft SAP's schedule, scope, team, methods, or rules of engagement.
    /// Only Draft SAPs can be updated — Finalized SAPs are immutable.
    /// </summary>
    /// <param name="input">Update parameters including SAP ID and fields to modify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated SAP document with re-rendered content.</returns>
    /// <exception cref="InvalidOperationException">SAP not found or already finalized.</exception>
    Task<SapDocument> UpdateSapAsync(
        SapUpdateInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalize a Draft SAP — locks it with SHA-256 content hash.
    /// Finalized SAPs cannot be modified or re-finalized.
    /// </summary>
    /// <param name="sapId">SAP ID to finalize.</param>
    /// <param name="finalizedBy">Identity of the finalizing user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Finalized SAP document with content hash.</returns>
    /// <exception cref="InvalidOperationException">SAP not found or already finalized.</exception>
    Task<SapDocument> FinalizeSapAsync(
        string sapId,
        string finalizedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    /// <summary>Finalize only the exact reviewed content and revision.</summary>
    Task<SapDocument> FinalizeSapAsync(
        string sapId, string finalizedBy, CancellationToken cancellationToken,
        string? expectedContentHash, long? expectedRevision);

    /// <summary>
    /// Retrieve a specific SAP by ID or the latest SAP for a system.
    /// If both sapId and systemId are provided, sapId takes precedence.
    /// For system lookups, prefers the working Draft over finalized history.
    /// </summary>
    /// <param name="sapId">Specific SAP ID (takes precedence).</param>
    /// <param name="systemId">System ID — returns latest SAP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SAP document with content and metadata.</returns>
    /// <exception cref="InvalidOperationException">No SAP found for criteria.</exception>
    Task<SapDocument> GetSapAsync(
        string? sapId = null,
        string? systemId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all SAPs for a system with status, dates, and scope summary.
    /// Returns Draft + Finalized history ordered by GeneratedAt descending.
    /// </summary>
    /// <param name="systemId">System ID to list SAPs for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of SAP documents (content omitted for list results).</returns>
    Task<List<SapDocument>> ListSapsAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate SAP completeness — checks scope coverage, methods, team, and schedule.
    /// Returns advisory warnings; does not block finalization.
    /// </summary>
    /// <param name="sapId">SAP ID to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with completeness flag and warnings.</returns>
    Task<SapValidationResult> ValidateSapAsync(
        string sapId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get SAP status summary for RMF lifecycle queries.
    /// Returns latest SAP state (None/Draft/Finalized), scope coverage, and readiness indicator.
    /// </summary>
    /// <param name="systemId">System ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SAP document with status metadata, or null if no SAP exists.</returns>
    Task<SapDocument?> GetSapStatusAsync(
        string systemId,
        CancellationToken cancellationToken = default);
}
