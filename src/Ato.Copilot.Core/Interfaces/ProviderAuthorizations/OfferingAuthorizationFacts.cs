namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>
/// Independent recorded-authorization facts for an offering. Not an ATO decision,
/// composite health score, or Mission Owner subscription telemetry.
/// </summary>
public sealed record OfferingAuthorizationFacts(string Validity, string PackageReadiness, string MonitoringHealth)
{
    public static OfferingAuthorizationFacts Project(
        IReadOnlyList<string> recordedStandings,
        bool packageIncomplete,
        string? monitoringHealth,
        bool hasRecordedConditions = false)
    {
        return new(
            ValidityFrom(recordedStandings, hasRecordedConditions),
            packageIncomplete ? "Incomplete" : "Ready",
            monitoringHealth ?? "Unavailable");
    }

    public static string MonitoringFromSourceHealth(IReadOnlyList<string> sourceHealth)
    {
        if (sourceHealth.Count == 0) return "NotRecorded";
        return sourceHealth.All(x => x == "Available") ? "Current" : "Stale";
    }

    private static string ValidityFrom(IReadOnlyList<string> standings, bool hasRecordedConditions)
    {
        if (standings.Count == 0) return "NotRecorded";
        if (standings.Any(x => x == "CurrentAsRecorded"))
            return hasRecordedConditions ? "Conditional" : "Effective";
        if (standings.Any(x => x == "Expired")) return "Expired";
        if (standings.Any(x => x == "Withdrawn")) return "Withdrawn";
        return "Undetermined";
    }
}
