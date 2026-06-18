using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Bidirectional transform bridge: SPIN Agent OSCAL structures &lt;-&gt; eMASS API v3.22 JSON.
/// Does NOT call eMASS directly — returns payloads for the caller to POST/PUT.
/// Feature 076 — T014.
/// </summary>
public class EmassBridgeService : IEmassBridgeService
{
    private const int EmassNarrativeMaxChars = 2000;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmassBridgeService> _logger;

    public EmassBridgeService(IServiceScopeFactory scopeFactory, ILogger<EmassBridgeService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<EmassBridgeResult> OscalToEmassAsync(
        string systemId,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var implementations = await db.ControlImplementations
            .AsNoTracking()
            .Where(ci => ci.RegisteredSystemId == systemId)
            .OrderBy(ci => ci.ControlId)
            .ToListAsync(cancellationToken);

        var controls = new List<EmassControlPayload>();
        var truncated = 0;

        foreach (var impl in implementations)
        {
            var narrative = impl.Narrative ?? string.Empty;
            var isTruncated = false;

            if (narrative.Length > EmassNarrativeMaxChars)
            {
                narrative = narrative[..(EmassNarrativeMaxChars - 12)] + " [truncated]";
                isTruncated = true;
                truncated++;
            }

            controls.Add(new EmassControlPayload
            {
                Acronym              = ToEmassControlId(impl.ControlId),
                ImplementationStatus = MapStatusToEmass(impl.ImplementationStatus),
                ControlDesignation   = "System-Specific", // ControlDesignation not yet on entity — defaults per spec
                ImplementationNarrative = narrative,
                IsTruncated          = isTruncated
            });
        }

        _logger.LogInformation("eMASS bridge ({DryRun}): {Count} controls, {Truncated} truncated for {SystemId}",
            dryRun ? "DRY-RUN" : "LIVE", controls.Count, truncated, systemId);

        return new EmassBridgeResult
        {
            SystemId = systemId,
            IsDryRun = dryRun,
            Controls = controls,
            TruncatedNarratives = truncated
        };
    }

    public async Task<int> EmassToOscalAsync(
        string systemId,
        List<EmassControlDto> emassControls,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var existing = await db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .ToDictionaryAsync(ci => ci.ControlId, cancellationToken);

        var upserted = 0;

        foreach (var dto in emassControls)
        {
            var controlId = FromEmassControlId(dto.Acronym);

            if (existing.TryGetValue(controlId, out var ci))
            {
                ci.Narrative = dto.ImplementationNarrative ?? ci.Narrative;
                ci.ImplementationStatus = MapEmassStatusToOscal(dto.ImplementationStatus);
                ci.ModifiedAt = DateTime.UtcNow;
            }
            else
            {
                db.ControlImplementations.Add(new ControlImplementation
                {
                    Id = Guid.NewGuid().ToString(),
                    RegisteredSystemId = systemId,
                    ControlId = controlId,
                    Narrative = dto.ImplementationNarrative ?? string.Empty,
                    ImplementationStatus = MapEmassStatusToOscal(dto.ImplementationStatus),
                    AuthoredBy = "emass-import",
                    AuthoredAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                });
            }
            upserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return upserted;
    }

    // eMASS uses uppercase dotted: "AC-1", "AC-2(1)" style
    internal static string ToEmassControlId(string oscalId)
    {
        var upper = oscalId.ToUpperInvariant();
        // OSCAL ac-2.1 -> eMASS AC-2.1 (dots preserved, just uppercase)
        return upper;
    }

    internal static string FromEmassControlId(string emassId)
        => emassId.ToLowerInvariant();

    internal static string MapStatusToEmass(ImplementationStatus status) => status switch
    {
        ImplementationStatus.Implemented         => "Implemented",
        ImplementationStatus.PartiallyImplemented => "Planned",
        ImplementationStatus.Planned             => "Planned",
        ImplementationStatus.NotApplicable       => "Not Applicable",
        _ => "Planned"
    };

    internal static ImplementationStatus MapEmassStatusToOscal(string? emassStatus) =>
        emassStatus?.ToLowerInvariant() switch
        {
            "implemented"     => ImplementationStatus.Implemented,
            "not applicable"  => ImplementationStatus.NotApplicable,
            "inherited"       => ImplementationStatus.Implemented,
            _ => ImplementationStatus.Planned
        };
}
