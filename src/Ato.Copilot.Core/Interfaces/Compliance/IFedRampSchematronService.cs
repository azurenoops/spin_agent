namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Advisory-only FedRAMP Schematron validation for OSCAL documents.
/// Never blocks export; reports violations as informational guidance.
/// Feature 076 — T007.
/// </summary>
public interface IFedRampSchematronService
{
    /// <summary>
    /// Validate <paramref name="oscalJson"/> against FedRAMP business rules.
    /// Always returns a result — never throws on violations.
    /// </summary>
    Task<FedRampSchematronResult> ValidateAsync(
        string oscalJson,
        string documentType,
        CancellationToken cancellationToken = default);
}

/// <summary>FedRAMP Schematron advisory validation result.</summary>
public record FedRampSchematronResult
{
    public bool IsCompliant { get; init; }
    public bool AdvisoryOnly { get; init; } = true;
    public string DocumentType { get; init; } = string.Empty;
    public List<SchematronViolation> Violations { get; init; } = new();
}

/// <summary>A single FedRAMP Schematron rule violation.</summary>
public record SchematronViolation
{
    public string Severity { get; init; } = "medium"; // high | medium | low
    public string Path { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? RuleId { get; init; }
}