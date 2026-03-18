namespace Ato.Copilot.Mcp.Configuration;

public class EvidenceOptions
{
    public const string SectionName = "Evidence";

    public string StorageProvider { get; set; } = "Local";
    public string AzureBlobConnectionString { get; set; } = string.Empty;
    public string AzureBlobContainerName { get; set; } = "evidence";
    public int RetentionDays { get; set; } = 365;
    public string LocalStoragePath { get; set; } = "/data/evidence";
    public int PurgeIntervalHours { get; set; } = 24;
}
