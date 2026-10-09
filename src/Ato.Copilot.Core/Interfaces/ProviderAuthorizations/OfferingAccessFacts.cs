namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>
/// What-if estimate of declared use vs the recorded offering authorization.
/// Does not issue IATT or ATO, allocate Azure, associate hosting, or adopt inheritance.
/// </summary>
public static class OfferingAccessFacts
{
    public static ScopeFitResponse Estimate(
        string authPath, string environmentLane, string impactLevel, string dataTypes, string interconnect)
    {
        var path = string.IsNullOrWhiteSpace(authPath) ? "IATT" : authPath.Trim();
        var env = string.IsNullOrWhiteSpace(environmentLane) ? "Dev" : environmentLane.Trim();
        var il = string.IsNullOrWhiteSpace(impactLevel) ? "IL4" : impactLevel.Trim();
        var data = dataTypes ?? "";
        var extra = string.IsNullOrWhiteSpace(interconnect) ? "None" : interconnect.Trim();
        var declared = $"{path} · {env} · {il} · {data} · {extra}";

        if (il.Contains("IL5", StringComparison.OrdinalIgnoreCase)
            || data.Contains("IL5", StringComparison.OrdinalIgnoreCase))
            return Fit(ScopeFitFacts.OutOfScope, declared, "Declared IL5 exceeds offering IL4.");
        if (path.Equals("IATT", StringComparison.OrdinalIgnoreCase) && env.Equals("Prod", StringComparison.OrdinalIgnoreCase))
            return Fit(ScopeFitFacts.OutOfScope, declared, "IATT declared in Prod. Testing in production is out of authorized scope.");
        if (path.Equals("IATT", StringComparison.OrdinalIgnoreCase) && env.Equals("Test", StringComparison.OrdinalIgnoreCase))
            return Fit(ScopeFitFacts.Exception, declared,
                "IATT declared in Test. Recorded test scope is Dev. Moving IATT to Test because Dev is locked is hosting drift, not closer to ATO.");
        if (path.Equals("ATO", StringComparison.OrdinalIgnoreCase) && !env.Equals("Prod", StringComparison.OrdinalIgnoreCase))
            return Fit(ScopeFitFacts.Exception, declared,
                $"ATO declared in {env}. Operational authorization is recorded for Prod unless that lane is the operational system.");
        if (!extra.Equals("None", StringComparison.OrdinalIgnoreCase))
            return Fit(ScopeFitFacts.Exception, declared, "Extra interconnection is outside the published boundary.");
        return Fit(ScopeFitFacts.InScope, declared, $"Declared {path} in {env} is inside IL4 and the published CRM.");
    }

    private static ScopeFitResponse Fit(string result, string declared, string rationale) =>
        new(result, declared, "", "", null, null, rationale);
}
