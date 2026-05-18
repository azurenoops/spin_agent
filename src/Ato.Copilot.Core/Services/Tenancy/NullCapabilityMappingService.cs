using Ato.Copilot.Core.Interfaces.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Feature 048 T206 + T207 — fallback concrete for
/// <see cref="ICapabilityMappingService"/> covering the FR-102
/// "AI mapper unavailable" path.
/// </summary>
/// <remarks>
/// <para>
/// FR-102 mandates that an unavailable AI mapper still allow uploads to
/// succeed: components are persisted, no capabilities are auto-created,
/// and the wizard surfaces the "AI mapping unavailable" banner. This
/// implementation honours that contract by returning an empty match list,
/// which the wrapper (<see cref="CspCapabilityMappingService"/>) then
/// converts into a single <c>NeedsReview</c> capability per component
/// with reason <c>"AI returned no candidate controls"</c>.
/// </para>
/// <para>
/// FR-110 single-registration invariant — this is the ONE concrete
/// registration of <see cref="ICapabilityMappingService"/> in the DI
/// graph. When the AI-backed mapper from Features 045 / 008 is wired
/// through (planned for the T227 slice), the registration MUST swap
/// this implementation in place rather than adding a second one.
/// The <c>CspInheritanceReuseAuditHealthCheck</c> (T218) enforces this.
/// </para>
/// </remarks>
public sealed class NullCapabilityMappingService : ICapabilityMappingService
{
    private readonly ILogger<NullCapabilityMappingService> _logger;

    public NullCapabilityMappingService(ILogger<NullCapabilityMappingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CapabilityControlMatch>> MapAsync(
        CapabilityMappingInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        _logger.LogDebug(
            "NullCapabilityMappingService returning empty mapping for capability '{Capability}' "
            + "(AI mapper not yet wired — FR-102 fallback path active)",
            input.CapabilityName);
        return Task.FromResult<IReadOnlyList<CapabilityControlMatch>>(
            Array.Empty<CapabilityControlMatch>());
    }
}
