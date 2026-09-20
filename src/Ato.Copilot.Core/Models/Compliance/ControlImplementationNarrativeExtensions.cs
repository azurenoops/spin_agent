namespace Ato.Copilot.Core.Models.Compliance;

public static class ControlImplementationNarrativeExtensions
{
    public static bool HasCanonicalNarrative(this ControlImplementation implementation) =>
        !string.IsNullOrWhiteSpace(implementation.PolicyNarrative) ||
        !string.IsNullOrWhiteSpace(implementation.TechnicalNarrative);

    public static void SetCombinedNarrative(
        this ControlImplementation implementation,
        string? narrative)
    {
        implementation.Narrative = narrative;
        implementation.TechnicalNarrative = narrative;
        implementation.MigratedFromLegacy = false;
    }
}