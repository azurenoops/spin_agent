using Ato.Copilot.Core.Models.ProviderAuthorizations;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>
/// What-if estimate of declared use vs the recorded offering authorization.
/// Does not issue IATT or ATO, allocate Azure, associate hosting, or adopt inheritance.
/// </summary>
public static class OfferingAccessFacts
{
    public const string CorrectionNone = "None";
    public const string CorrectionRequested = "Requested";
    public const string CorrectionResubmitted = "Resubmitted";

    public static void ApplyCorrectionRequest(ProviderOfferingAccessRequest row, string message)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Declared-use correction requires approved offering access. Access is not revoked.");
        row.CorrectionStatus = CorrectionRequested;
        row.CorrectionMessage = Bound(message, "correction message", 2000);
    }

    public static void ApplyResubmit(ProviderOfferingAccessRequest row, RequestOfferingAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(request);
        if (!row.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resubmitting declared use does not reopen or revoke offering access.");
        if (!row.CorrectionStatus.Equals(CorrectionRequested, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Declared use can be resubmitted only after the CSP requests a correction.");
        var estimate = Estimate(request.AuthPath, request.EnvironmentLane, request.ImpactLevel,
            request.DataTypes, request.Interconnect);
        row.AuthPath = Bound(request.AuthPath, "authorization path", 16);
        row.EnvironmentLane = Bound(request.EnvironmentLane, "environment lane", 16);
        row.ImpactLevel = Bound(request.ImpactLevel, "impact level", 16);
        row.DataTypes = Bound(request.DataTypes, "data types", 256, required: false);
        row.Interconnect = Bound(request.Interconnect, "interconnect", 256, required: false);
        row.EstimateResult = estimate.Result;
        row.EstimateRationale = estimate.Rationale;
        row.CorrectionStatus = CorrectionResubmitted;
    }

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

    private static string Bound(string? value, string field, int max, bool required = true)
    {
        var text = (value ?? "").Trim();
        if (required && text.Length == 0)
            throw new ArgumentException($"{field} is required.");
        if (text.Length > max)
            throw new ArgumentException($"{field} exceeds {max} characters.");
        return text;
    }
}
