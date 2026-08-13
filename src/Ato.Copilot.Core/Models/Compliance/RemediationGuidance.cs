namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// AI-generated remediation guidance.
/// </summary>
public class RemediationGuidance
{
    /// <summary>Finding this guidance is for.</summary>
    public string FindingId { get; set; } = string.Empty;

    /// <summary>Natural-language explanation.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>Technical implementation plan.</summary>
    public string TechnicalPlan { get; set; } = string.Empty;

    /// <summary>AI confidence (0.0–1.0).</summary>
    public double? ConfidenceScore { get; set; }

    /// <summary>Reference links.</summary>
    public List<string> References { get; set; } = new();

    /// <summary>When guidance was generated.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
