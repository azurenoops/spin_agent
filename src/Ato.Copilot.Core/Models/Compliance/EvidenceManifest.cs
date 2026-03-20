using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Structured listing of all evidence artifacts included in a package.
/// Serialized as evidence-manifest.json within the authorization package ZIP.
/// </summary>
public class EvidenceManifest
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("systemId")]
    public string SystemId { get; set; } = string.Empty;

    [JsonPropertyName("totalArtifacts")]
    public int TotalArtifacts { get; set; }

    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("embeddingMode")]
    public string EmbeddingMode { get; set; } = "embedded";

    [JsonPropertyName("artifacts")]
    public List<EvidenceManifestEntry> Artifacts { get; set; } = new();
}

/// <summary>
/// Individual evidence artifact entry within the manifest.
/// Maps an evidence file to its linked control, category, and file metadata.
/// </summary>
public class EvidenceManifestEntry
{
    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("controlId")]
    public string ControlId { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("collectionMethod")]
    public string CollectionMethod { get; set; } = string.Empty;

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("uploadedAt")]
    public DateTimeOffset? UploadedAt { get; set; }

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }
}
