namespace Ato.Copilot.Core.Configuration;

/// <summary>
/// Configuration for SSP document export file storage and limits.
/// Bound from the "ExportSettings" section in appsettings.json.
/// </summary>
public class ExportSettings
{
    public const string SectionName = "ExportSettings";

    /// <summary>Base directory for export files and uploaded templates. Defaults to "./data".</summary>
    public string DataPath { get; set; } = "./data";

    /// <summary>Number of days to retain exported files before cleanup. Default: 30.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Maximum export file size in bytes. Default: 50 MB (52,428,800).</summary>
    public long MaxExportSizeBytes { get; set; } = 52_428_800;

    /// <summary>Maximum template file size in bytes. Default: 10 MB (10,485,760).</summary>
    public long MaxTemplateSizeBytes { get; set; } = 10_485_760;

    /// <summary>Computed path to the exports directory.</summary>
    public string ExportsPath => Path.Combine(DataPath, "exports");

    /// <summary>Computed path to the templates directory.</summary>
    public string TemplatesPath => Path.Combine(DataPath, "templates");

    /// <summary>Computed path to the authorization packages directory.</summary>
    public string PackagesPath => Path.Combine(DataPath, "packages");
}
