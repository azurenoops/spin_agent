namespace Ato.Copilot.Core.Configuration;

/// <summary>
/// Organization-level feature settings.
/// Bound from appsettings.json "Features" section.
/// </summary>
public class FeatureOptions
{
    /// <summary>
    /// Enable Entra ID discovery for Person components in the org-wide Component Library.
    /// Default: false. When disabled, the discovery button is hidden.
    /// </summary>
    public bool EntraIdDiscoveryEnabled { get; set; } = false;
}
