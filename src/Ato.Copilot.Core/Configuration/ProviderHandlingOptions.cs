namespace Ato.Copilot.Core.Configuration;

/// <summary>Operator-provided policy; profile defaults and offering labels never grant data handling.</summary>
public sealed class ProviderHandlingOptions
{
    public string? PolicyId { get; set; }
    public string? Version { get; set; }
    public string? EnvironmentLabel { get; set; }
    public string? ApprovalReference { get; set; }
    public string[] AllowedClassifications { get; set; } = [];
    public string[] AllowedMarkings { get; set; } = [];
    public bool SyntheticOnly { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public bool UploadsEnabled { get; set; }
    public bool AnalysisEnabled { get; set; }
}
