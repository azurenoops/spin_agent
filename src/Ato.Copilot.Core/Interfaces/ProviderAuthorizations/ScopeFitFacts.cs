namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>
/// Scope-fit result for declared use vs the recorded offering authorization.
/// Not an ATO, not an impact-review disposition, and not Mission Owner SSP body.
/// </summary>
public static class ScopeFitFacts
{
    public const string Unevaluated = "Unevaluated";
    public const string InScope = "InScope";
    public const string Exception = "Exception";
    public const string OutOfScope = "OutOfScope";

    public static readonly string[] Results = [Unevaluated, InScope, Exception, OutOfScope];

    public static string Normalize(string? result) =>
        Results.Contains(result, StringComparer.Ordinal) ? result! : Unevaluated;

    public static string Require(string? result)
    {
        if (Results.Contains(result, StringComparer.Ordinal)) return result!;
        throw new ArgumentException("Scope fit must be Unevaluated, InScope, Exception, or OutOfScope.");
    }
}
