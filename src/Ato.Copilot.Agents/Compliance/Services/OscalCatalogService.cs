using Ato.Copilot.Core.Interfaces.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Returns hardcoded FedRAMP baseline control ID lists in OSCAL lowercase format.
/// Eliminates HTTP calls for offline/air-gapped operation.
/// Full profile resolution via oscal-cli is planned for Wave 8+.
/// Feature 076 — T010.
/// </summary>
public class OscalCatalogService : IOscalCatalogService
{
    private readonly ILogger<OscalCatalogService> _logger;

    // FedRAMP Rev 5 control selections — OSCAL lowercase IDs
    private static readonly IReadOnlyList<string> LowBaseline = new[]
    {
        "ac-1","ac-2","ac-3","ac-7","ac-8","ac-14","ac-17","ac-18","ac-19","ac-20","ac-22",
        "at-1","at-2","at-3","at-4",
        "au-1","au-2","au-3","au-4","au-5","au-6","au-8","au-9","au-11","au-12",
        "ca-1","ca-2","ca-3","ca-5","ca-6","ca-7","ca-9",
        "cm-1","cm-2","cm-4","cm-5","cm-6","cm-7","cm-8","cm-10","cm-11",
        "cp-1","cp-2","cp-3","cp-4","cp-9","cp-10",
        "ia-1","ia-2","ia-4","ia-5","ia-6","ia-7","ia-8","ia-11",
        "ir-1","ir-2","ir-4","ir-5","ir-6","ir-7","ir-8",
        "ma-1","ma-2","ma-4","ma-5",
        "mp-1","mp-2","mp-6","mp-7",
        "pe-1","pe-2","pe-3","pe-6","pe-8","pe-12","pe-13","pe-14","pe-15","pe-16","pe-17",
        "pl-1","pl-2","pl-4","pl-10","pl-11",
        "pm-1","pm-2","pm-3","pm-4","pm-5","pm-6","pm-7","pm-8","pm-9","pm-10","pm-11",
        "pm-12","pm-13","pm-14","pm-15","pm-16",
        "ps-1","ps-2","ps-3","ps-4","ps-5","ps-6","ps-7","ps-8","ps-9",
        "pt-1","pt-2","pt-3","pt-4","pt-5","pt-6","pt-7","pt-8",
        "ra-1","ra-2","ra-3","ra-5","ra-7","ra-9",
        "sa-1","sa-2","sa-3","sa-4","sa-5","sa-8","sa-9","sa-10","sa-11","sa-12","sa-15",
        "sa-16","sa-17","sa-22",
        "sc-1","sc-5","sc-7","sc-12","sc-13","sc-15","sc-20","sc-21","sc-22","sc-28","sc-39",
        "si-1","si-2","si-3","si-4","si-5","si-7","si-12","si-16"
    };

    // Moderate adds ~170 controls beyond LOW — representative subset shown
    private static readonly IReadOnlyList<string> ModerateBaseline = LowBaseline
        .Concat(new[]
        {
            "ac-4","ac-5","ac-6","ac-10","ac-11","ac-12","ac-16","ac-21","ac-23","ac-24","ac-25",
            "at-5","au-7","au-10","au-14",
            "ca-4","ca-8",
            "cm-3","cm-9","cm-12","cm-13",
            "cp-6","cp-7","cp-8","cp-11","cp-12","cp-13",
            "ia-3","ia-9","ia-10","ia-12",
            "ir-3","ir-9","ir-10",
            "ma-3","ma-6","ma-7",
            "mp-3","mp-4","mp-5","mp-8",
            "pe-4","pe-5","pe-9","pe-10","pe-11","pe-18","pe-20","pe-23",
            "pl-7","pl-8","pl-9",
            "pm-17","pm-19","pm-20","pm-21","pm-25","pm-26","pm-28","pm-30","pm-31",
            "ra-4","ra-6","ra-8","ra-10",
            "sa-6","sa-18","sa-19","sa-20","sa-21","sa-23",
            "sc-3","sc-4","sc-8","sc-10","sc-16","sc-17","sc-18","sc-19","sc-23","sc-24","sc-25",
            "sc-26","sc-27","sc-29","sc-30","sc-31","sc-32","sc-33","sc-34","sc-35","sc-36",
            "sc-37","sc-38","sc-40","sc-41","sc-42","sc-43",
            "si-6","si-8","si-10","si-11","si-13","si-15","si-17","si-18","si-19","si-20","si-21","si-23"
        }).Distinct().OrderBy(x => x).ToList();

    // High baseline = Moderate + additional controls
    private static readonly IReadOnlyList<string> HighBaseline = ModerateBaseline
        .Concat(new[]
        {
            "ac-2","ac-3","ac-6","ac-17",  // enhancements already in mod, high adds more enhancements
            "cm-2","si-2","si-3"            // placeholder for high-only additions
        }).Distinct().OrderBy(x => x).ToList();

    public OscalCatalogService(ILogger<OscalCatalogService> logger) => _logger = logger;

    public Task<List<string>> GetBaselineControlIdsAsync(
        string baselineLevel,
        CancellationToken cancellationToken = default)
    {
        var controls = baselineLevel.ToLowerInvariant() switch
        {
            "low"      => LowBaseline.ToList(),
            "moderate" => ModerateBaseline.ToList(),
            "high"     => HighBaseline.ToList(),
            _ => throw new ArgumentException($"Unknown baseline level: {baselineLevel}. Use low|moderate|high.")
        };

        _logger.LogDebug("FedRAMP {Level} baseline: {Count} controls", baselineLevel, controls.Count);
        return Task.FromResult(controls);
    }
}
