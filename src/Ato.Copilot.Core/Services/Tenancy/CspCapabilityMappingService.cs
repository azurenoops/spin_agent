using System.Globalization;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Feature 048 T204 — wraps the existing
/// <see cref="ICapabilityMappingService"/> (Features 045 / 008) so that a
/// single <see cref="CspInheritedComponent"/> produces a normalized
/// <see cref="CapabilityMappingResult"/> per FR-101 / FR-102.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper applies the <c>Csp:Inheritance:MappingConfidenceThreshold</c>
/// (default <c>0.6</c>) and collapses the AI candidates into <strong>one
/// capability per component</strong> — never one per control. Multiple
/// candidate controls flow into a single <see cref="CspInheritedCapability"/>
/// row's <see cref="CspInheritedCapability.MappedNistControlIds"/> list.
/// </para>
/// <para>
/// FR-110 reuse-first guarantee: this wrapper composes the existing AI mapper
/// — it does not introduce a parallel mapping algorithm. The concrete
/// <see cref="ICapabilityMappingService"/> implementation is owned upstream
/// (T218 created the interface; T206 wires the concrete) and is the single
/// point of contact with <c>IChatClient</c>.
/// </para>
/// </remarks>
public sealed class CspCapabilityMappingService : ICspCapabilityMappingService
{
    /// <summary>
    /// Reason text written to <see cref="CspInheritedCapability.MappingFailureReason"/>
    /// when the AI returns zero candidates.
    /// </summary>
    internal const string ReasonNoCandidates = "AI returned no candidate controls";

    /// <summary>
    /// Format string for the below-threshold reason. Score is formatted with
    /// <see cref="CultureInfo.InvariantCulture"/> so the persisted text is
    /// stable across server locales (e.g. always <c>0.42</c>, never <c>0,42</c>).
    /// </summary>
    internal const string ReasonBelowThresholdFormat = "Confidence below threshold ({0:F2})";

    private readonly ICapabilityMappingService _aiMapper;
    private readonly ILogger<CspCapabilityMappingService> _logger;

    public CspCapabilityMappingService(
        ICapabilityMappingService aiMapper,
        ILogger<CspCapabilityMappingService> logger)
    {
        _aiMapper = aiMapper ?? throw new ArgumentNullException(nameof(aiMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CapabilityMappingResult> MapAsync(
        CspInheritedComponent component,
        double confidenceThreshold,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        IReadOnlyList<CapabilityControlMatch> matches;
        try
        {
            matches = await _aiMapper.MapAsync(
                new CapabilityMappingInput(
                    CapabilityName: component.Name,
                    Description: component.Description,
                    Provider: null,
                    CspProfileName: null),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-driven cancellation propagates; not an AI-availability
            // signal.
            throw;
        }
        catch (Exception ex)
        {
            // FR-102: AI gateway is unreachable / quota exceeded / transport
            // failure. The upload SHOULD still succeed (the caller persists
            // the component itself); this result signals "no capabilities
            // were auto-mapped" so the wizard can surface the banner.
            _logger.LogWarning(
                ex,
                "AI capability mapping failed for component {ComponentId} ({ComponentName}); "
                + "marking aiMappingAvailable=false",
                component.Id,
                component.Name);

            return new CapabilityMappingResult(
                Mapped: Array.Empty<CspInheritedCapability>(),
                NeedsReview: Array.Empty<CspInheritedCapability>(),
                AiMappingAvailable: false,
                AiMappingFailureReason: ex.Message);
        }

        // ── AI returned an empty match list ─────────────────────────────
        if (matches.Count == 0)
        {
            var capability = NewCapability(
                component,
                mappedControlIds: new List<string>(),
                confidence: null,
                status: CspInheritedCapabilityStatus.NeedsReview,
                failureReason: ReasonNoCandidates);

            return new CapabilityMappingResult(
                Mapped: Array.Empty<CspInheritedCapability>(),
                NeedsReview: new[] { capability },
                AiMappingAvailable: true,
                AiMappingFailureReason: null);
        }

        // ── AI returned at least one match: collapse into ONE capability ─
        var maxConfidence = matches.Max(m => m.ConfidenceScore);
        var controlIds = matches.Select(m => m.ControlId).ToList();

        if (maxConfidence >= confidenceThreshold)
        {
            var mapped = NewCapability(
                component,
                mappedControlIds: controlIds,
                confidence: maxConfidence,
                status: CspInheritedCapabilityStatus.Mapped,
                failureReason: null);

            return new CapabilityMappingResult(
                Mapped: new[] { mapped },
                NeedsReview: Array.Empty<CspInheritedCapability>(),
                AiMappingAvailable: true,
                AiMappingFailureReason: null);
        }

        var needsReview = NewCapability(
            component,
            mappedControlIds: controlIds,
            confidence: maxConfidence,
            status: CspInheritedCapabilityStatus.NeedsReview,
            failureReason: string.Format(
                CultureInfo.InvariantCulture,
                ReasonBelowThresholdFormat,
                maxConfidence));

        return new CapabilityMappingResult(
            Mapped: Array.Empty<CspInheritedCapability>(),
            NeedsReview: new[] { needsReview },
            AiMappingAvailable: true,
            AiMappingFailureReason: null);
    }

    private static CspInheritedCapability NewCapability(
        CspInheritedComponent component,
        List<string> mappedControlIds,
        double? confidence,
        CspInheritedCapabilityStatus status,
        string? failureReason)
        => new()
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = component.Id,
            Name = component.Name,
            Description = component.Description,
            MappedNistControlIds = mappedControlIds,
            MappingConfidence = confidence,
            Status = status,
            MappingFailureReason = failureReason,
            MappedBy = MappedBy.AI,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "system",
        };
}
