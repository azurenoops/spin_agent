using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Top-level NIST SP 800-53 Rev 5 catalog with metadata, 20 control groups, and back matter.
/// </summary>
public sealed record NistCatalog
{
    /// <summary>Original unflattened catalog captured by the authoritative importer.</summary>
    [JsonIgnore]
    public string? SourceJson { get; init; }

    /// <summary>Actual retrieval source, including an explicit embedded fallback identity.</summary>
    [JsonIgnore]
    public string? SourceUri { get; init; }

    /// <summary>Catalog UUID.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>Catalog metadata including title, version, and OSCAL version.</summary>
    [JsonPropertyName("metadata")]
    public CatalogMetadata Metadata { get; init; } = new();

    /// <summary>20 control family groups (AC, AT, AU, CA, CM, CP, IA, IR, MA, MP, PE, PL, PM, PS, PT, RA, SA, SC, SI, SR).</summary>
    [JsonPropertyName("groups")]
    public List<ControlGroup> Groups { get; init; } = new();

    /// <summary>Optional back-matter reference resources.</summary>
    [JsonPropertyName("back-matter")]
    public BackMatter? BackMatter { get; init; }
}

/// <summary>
/// Catalog metadata from the OSCAL <c>metadata</c> object.
/// </summary>
public sealed record CatalogMetadata
{
    /// <summary>Full catalog title (e.g., "NIST Special Publication 800-53 Revision 5: Security and Privacy Controls...").</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>ISO 8601 timestamp of last modification.</summary>
    [JsonPropertyName("last-modified")]
    public string LastModified { get; init; } = string.Empty;

    /// <summary>Catalog version (e.g., "5.2.0").</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>OSCAL schema version (e.g., "1.1.3").</summary>
    [JsonPropertyName("oscal-version")]
    public string OscalVersion { get; init; } = string.Empty;

    /// <summary>Optional additional notes.</summary>
    [JsonPropertyName("remarks")]
    public string? Remarks { get; init; }
}

/// <summary>
/// Represents one NIST control family (e.g., "Access Control").
/// </summary>
public sealed record ControlGroup
{
    /// <summary>Family ID in lowercase (e.g., "ac", "sc").</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Always "family" for control groups.</summary>
    [JsonPropertyName("class")]
    public string? Class { get; init; }

    /// <summary>Family name (e.g., "Access Control").</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Property annotations on the group.</summary>
    [JsonPropertyName("props")]
    public List<ControlProperty>? Props { get; init; }

    /// <summary>Controls belonging to this family.</summary>
    [JsonPropertyName("controls")]
    public List<OscalControl> Controls { get; init; } = new();
}

/// <summary>
/// Individual NIST control from the OSCAL catalog.
/// Distinct from the EF Core <c>NistControl</c> entity — this is the deserialization model.
/// </summary>
public sealed record OscalControl
{
    /// <summary>Control ID in lowercase (e.g., "ac-2", "sc-7").</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Always "SP800-53" for controls.</summary>
    [JsonPropertyName("class")]
    public string? Class { get; init; }

    /// <summary>Control title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Organization-defined parameters for control customization.</summary>
    [JsonPropertyName("params")]
    public List<ControlParam>? Params { get; init; }

    /// <summary>Property annotations (label, sort-id, implementation-level).</summary>
    [JsonPropertyName("props")]
    public List<ControlProperty>? Props { get; init; }

    /// <summary>References to other controls.</summary>
    [JsonPropertyName("links")]
    public List<ControlLink>? Links { get; init; }

    /// <summary>Control parts: statement, guidance, assessment objectives.</summary>
    [JsonPropertyName("parts")]
    public List<ControlPart>? Parts { get; init; }

    /// <summary>Nested control enhancements (e.g., ac-2.1, sc-7.3).</summary>
    [JsonPropertyName("controls")]
    public List<OscalControl>? Controls { get; init; }
}

/// <summary>
/// Name-value property annotation on controls/groups.
/// </summary>
public sealed record ControlProperty
{
    /// <summary>Property name (e.g., "label", "sort-id", "status").</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Property value.</summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    /// <summary>Property class (e.g., "sp800-53a").</summary>
    [JsonPropertyName("class")]
    public string? Class { get; init; }

    /// <summary>Namespace URI.</summary>
    [JsonPropertyName("ns")]
    public string? Ns { get; init; }
}

/// <summary>
/// Recursive part hierarchy carrying control text content (statement, guidance, objectives).
/// </summary>
public sealed record ControlPart
{
    /// <summary>Part ID (e.g., "ac-2_smt", "ac-2_gdn").</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// Part name discriminator: "statement", "guidance", "assessment-objective", "assessment-method", "item".
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Part properties (e.g., method label).</summary>
    [JsonPropertyName("props")]
    public List<ControlProperty>? Props { get; init; }

    /// <summary>Text content of this part.</summary>
    [JsonPropertyName("prose")]
    public string? Prose { get; init; }

    /// <summary>Nested sub-parts (recursive).</summary>
    [JsonPropertyName("parts")]
    public List<ControlPart>? Parts { get; init; }
}

/// <summary>
/// Organization-defined parameter for control customization.
/// </summary>
public sealed record ControlParam
{
    /// <summary>Parameter ID (e.g., "ac-02_odp.01").</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable label.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    /// <summary>Alt-identifier and label properties.</summary>
    [JsonPropertyName("props")]
    public List<ControlProperty>? Props { get; init; }

    /// <summary>Usage guidance for this parameter.</summary>
    [JsonPropertyName("guidelines")]
    public List<ControlGuideline>? Guidelines { get; init; }
}

/// <summary>
/// Guidance text for a parameter.
/// </summary>
public sealed record ControlGuideline
{
    /// <summary>Guideline text content.</summary>
    [JsonPropertyName("prose")]
    public string Prose { get; init; } = string.Empty;
}

/// <summary>
/// Reference link between controls.
/// </summary>
public sealed record ControlLink
{
    /// <summary>Link target (UUID fragment or URL).</summary>
    [JsonPropertyName("href")]
    public string Href { get; init; } = string.Empty;

    /// <summary>Relationship type (e.g., "related").</summary>
    [JsonPropertyName("rel")]
    public string? Rel { get; init; }
}

/// <summary>
/// Container for reference resources in back-matter.
/// </summary>
public sealed record BackMatter
{
    /// <summary>Referenced documents and resources.</summary>
    [JsonPropertyName("resources")]
    public List<BackMatterResource>? Resources { get; init; }
}

/// <summary>
/// A single back-matter resource.
/// </summary>
public sealed record BackMatterResource
{
    /// <summary>Resource UUID.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>Resource title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

/// <summary>
/// Enriched view of a control's statement, guidance, and assessment objectives.
/// Produced by <c>NistControlsService.GetControlEnhancementAsync</c>.
/// </summary>
/// <param name="Id">Control ID (e.g., "AC-2").</param>
/// <param name="Title">Control title.</param>
/// <param name="Statement">Concatenated statement prose from <c>name="statement"</c> parts.</param>
/// <param name="Guidance">Concatenated guidance prose from <c>name="guidance"</c> parts.</param>
/// <param name="Objectives">List of assessment objective prose strings.</param>
/// <param name="LastUpdated">Timestamp of extraction.</param>
public sealed record ControlEnhancement(
    string Id,
    string Title,
    string Statement,
    string Guidance,
    List<string> Objectives,
    DateTime LastUpdated);
