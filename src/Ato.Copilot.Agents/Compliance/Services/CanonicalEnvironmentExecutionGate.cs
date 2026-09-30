using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Prevents subscription-wide collectors from widening canonical exact-resource authority.</summary>
public static class CanonicalEnvironmentExecutionGate
{
    /// <summary>The existing collector cannot enforce exact resources.</summary>
    public const string ScopeUnsupported = "ASSESSMENT_SCOPE_UNSUPPORTED";
    /// <summary>Current environment authority denies collection.</summary>
    public const string Ineligible = "ASSESSMENT_ENVIRONMENT_INELIGIBLE";
    /// <summary>A legacy mutation must use the canonical environment workflow.</summary>
    public const string SharedSourceRequired = "ASSESSMENT_SHARED_ENVIRONMENT_REQUIRED";
    /// <summary>Safe explanation of collector capability limits.</summary>
    public const string ScopeMessage =
        "The current Azure collectors cannot enforce this environment's exact resource scope. No broader collection was run.";
    /// <summary>Return path and supported alternative without granting broader access.</summary>
    public const string Suggestion =
        "Open Connected environments in System definition → Environment to review the shared source. Import scoped assessment results until scoped Azure collection is supported.";

    /// <summary>A retained canonical revision must never fall back to a legacy AzureProfile.</summary>
    public static bool IsCanonical(ResolvedSystemEnvironmentScopes scopes) =>
        scopes.Version > 0 || scopes.Sources.Count > 0;

    /// <summary>Returns an explicit unsupported or ineligible result without invoking any Azure source.</summary>
    public static AssessmentEnvironmentException Failure(ResolvedSystemEnvironmentScopes scopes) =>
        scopes.Sources.Count == 0 || scopes.Sources.Any(x => !x.Eligible)
            ? new(Ineligible, "An attached environment is unavailable, withdrawn, expired, or awaiting scope review.", Suggestion)
            : new(ScopeUnsupported, ScopeMessage, Suggestion);

    /// <summary>Legacy subscription selection is not reviewed monitoring authority.</summary>
    public static AssessmentEnvironmentException ReconciliationRequired() =>
        new(Ineligible, "Monitoring requires a reconciled environment with reviewed exact resource scope.", Suggestion);
}
