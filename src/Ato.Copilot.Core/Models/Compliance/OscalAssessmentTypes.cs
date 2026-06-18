using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Models.Compliance;

// ─── OSCAL Assessment Results (SAR) types — Feature 076 T003 ──────────────

/// <summary>Root wrapper for OSCAL assessment-results JSON.</summary>
public sealed record OscalAssessmentResultsRoot
{
    [JsonPropertyName("assessment-results")]
    public OscalAssessmentResults AssessmentResults { get; init; } = new();
}

/// <summary>OSCAL 1.1.2 assessment-results document.</summary>
public sealed record OscalAssessmentResults
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("metadata")]
    public OscalDocumentMetadata Metadata { get; init; } = new();

    [JsonPropertyName("import-ap")]
    public OscalHref? ImportAp { get; init; }

    [JsonPropertyName("results")]
    public List<OscalAssessmentResult> Results { get; init; } = new();

    [JsonPropertyName("back-matter")]
    public OscalBackMatterSection? BackMatter { get; init; }
}

/// <summary>Single assessment result period.</summary>
public sealed record OscalAssessmentResult
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("start")]
    public string Start { get; init; } = string.Empty;

    [JsonPropertyName("end")]
    public string? End { get; init; }

    [JsonPropertyName("reviewed-controls")]
    public OscalReviewedControls? ReviewedControls { get; init; }

    [JsonPropertyName("observations")]
    public List<OscalObservation>? Observations { get; init; }

    [JsonPropertyName("risks")]
    public List<OscalRisk>? Risks { get; init; }

    [JsonPropertyName("findings")]
    public List<OscalFinding>? Findings { get; init; }
}

/// <summary>OSCAL observation — evidence of a condition observed.</summary>
public sealed record OscalObservation
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("methods")]
    public List<string> Methods { get; init; } = new(); // INTERVIEW, TEST, EXAMINE

    [JsonPropertyName("types")]
    public List<string>? Types { get; init; } // finding, historic, etc.

    [JsonPropertyName("subjects")]
    public List<OscalSubjectReference>? Subjects { get; init; }

    [JsonPropertyName("relevant-evidence")]
    public List<OscalRelevantEvidence>? RelevantEvidence { get; init; }

    [JsonPropertyName("collected")]
    public string? Collected { get; init; }
}

/// <summary>OSCAL risk entry with characterization, likelihood, and impact.</summary>
public sealed record OscalRisk
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("statement")]
    public string Statement { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "open"; // open|investigating|remediating|closed

    [JsonPropertyName("characterizations")]
    public List<OscalRiskCharacterization>? Characterizations { get; init; }

    [JsonPropertyName("remediations")]
    public List<OscalRemediation>? Remediations { get; init; }

    [JsonPropertyName("related-observations")]
    public List<OscalUuidRef>? RelatedObservations { get; init; }
}

/// <summary>Risk characterization with likelihood and impact facets.</summary>
public sealed record OscalRiskCharacterization
{
    [JsonPropertyName("origin")]
    public List<OscalOriginActor>? Origin { get; init; }

    [JsonPropertyName("facets")]
    public List<OscalRiskFacet> Facets { get; init; } = new();
}

/// <summary>Single risk facet (likelihood or impact).</summary>
public sealed record OscalRiskFacet
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty; // likelihood | impact

    [JsonPropertyName("system")]
    public string System { get; init; } = "https://fedramp.gov/ns/oscal";

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty; // low|moderate|high
}

/// <summary>OSCAL finding — conclusion about control satisfaction.</summary>
public sealed record OscalFinding
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("target")]
    public OscalFindingTarget Target { get; init; } = new();

    [JsonPropertyName("related-observations")]
    public List<OscalUuidRef>? RelatedObservations { get; init; }

    [JsonPropertyName("related-risks")]
    public List<OscalUuidRef>? RelatedRisks { get; init; }
}

/// <summary>Finding target — what was evaluated and its satisfaction status.</summary>
public sealed record OscalFindingTarget
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "statement-id"; // statement-id | objective-id

    [JsonPropertyName("target-id")]
    public string TargetId { get; init; } = string.Empty; // e.g. "ac-1_smt.a"

    [JsonPropertyName("status")]
    public OscalFindingStatus Status { get; init; } = new();
}

/// <summary>Finding status — satisfied or not-satisfied with optional reason.</summary>
public sealed record OscalFindingStatus
{
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty; // satisfied | not-satisfied

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; init; }
}

// ─── OSCAL POA&M types — Feature 076 T003 ─────────────────────────────────

/// <summary>Root wrapper for OSCAL plan-of-action-and-milestones JSON.</summary>
public sealed record OscalPoamRoot
{
    [JsonPropertyName("plan-of-action-and-milestones")]
    public OscalPoam PlanOfActionAndMilestones { get; init; } = new();
}

/// <summary>OSCAL 1.1.2 plan-of-action-and-milestones document.</summary>
public sealed record OscalPoam
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("metadata")]
    public OscalDocumentMetadata Metadata { get; init; } = new();

    [JsonPropertyName("import-ssp")]
    public OscalHref? ImportSsp { get; init; }

    [JsonPropertyName("system-id")]
    public OscalSystemId? SystemId { get; init; }

    [JsonPropertyName("local-definitions")]
    public OscalPoamLocalDefinitions? LocalDefinitions { get; init; }

    [JsonPropertyName("observations")]
    public List<OscalObservation>? Observations { get; init; }

    [JsonPropertyName("risks")]
    public List<OscalRisk>? Risks { get; init; }

    [JsonPropertyName("findings")]
    public List<OscalFinding>? Findings { get; init; }

    [JsonPropertyName("poam-items")]
    public List<OscalPoamItem> PoamItems { get; init; } = new();

    [JsonPropertyName("back-matter")]
    public OscalBackMatterSection? BackMatter { get; init; }
}

/// <summary>Individual POA&M item.</summary>
public sealed record OscalPoamItem
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("props")]
    public List<OscalProp>? Props { get; init; }

    [JsonPropertyName("related-findings")]
    public List<OscalUuidRef>? RelatedFindings { get; init; } // OSCAL 1.1.0+ required assembly

    [JsonPropertyName("related-observations")]
    public List<OscalUuidRef>? RelatedObservations { get; init; }

    [JsonPropertyName("related-risks")]
    public List<OscalUuidRef>? RelatedRisks { get; init; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; init; }
}

/// <summary>POA&M local definitions for scanning tools and assessment assets.</summary>
public sealed record OscalPoamLocalDefinitions
{
    [JsonPropertyName("components")]
    public List<OscalComponent>? Components { get; init; }

    [JsonPropertyName("inventory-items")]
    public List<OscalInventoryItem>? InventoryItems { get; init; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; init; }
}

// ─── Shared supporting types ──────────────────────────────────────────────

/// <summary>A single OSCAL property with optional namespace.</summary>
public sealed record OscalProp
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("ns")]
    public string? Ns { get; init; }

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("class")]
    public string? Class { get; init; }

    [JsonPropertyName("group")]
    public string? Group { get; init; } // OSCAL 1.1.0+ group attribute
}

/// <summary>UUID reference (used for related-observations, related-risks, related-findings).</summary>
public sealed record OscalUuidRef
{
    [JsonPropertyName("observation-uuid")]
    public string? ObservationUuid { get; init; }

    [JsonPropertyName("risk-uuid")]
    public string? RiskUuid { get; init; }

    [JsonPropertyName("finding-uuid")]
    public string? FindingUuid { get; init; }
}

/// <summary>Simple href wrapper.</summary>
public sealed record OscalHref
{
    [JsonPropertyName("href")]
    public string Href { get; init; } = string.Empty;
}

/// <summary>System identifier with type.</summary>
public sealed record OscalSystemId
{
    [JsonPropertyName("identifier-type")]
    public string? IdentifierType { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}

/// <summary>Shared document metadata across all OSCAL models.</summary>
public sealed record OscalDocumentMetadata
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("last-modified")]
    public string LastModified { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    [JsonPropertyName("oscal-version")]
    public string OscalVersion { get; init; } = "1.1.2";

    [JsonPropertyName("roles")]
    public List<object>? Roles { get; init; }

    [JsonPropertyName("parties")]
    public List<object>? Parties { get; init; }

    [JsonPropertyName("responsible-parties")]
    public List<object>? ResponsibleParties { get; init; }
}

/// <summary>Back-matter section with resources.</summary>
public sealed record OscalBackMatterSection
{
    [JsonPropertyName("resources")]
    public List<OscalBackMatterResource> Resources { get; init; } = new();
}

/// <summary>Back-matter resource with rlinks and optional hash.</summary>
public sealed record OscalBackMatterResource
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("props")]
    public List<OscalProp>? Props { get; init; }

    [JsonPropertyName("rlinks")]
    public List<OscalResourceLink>? Rlinks { get; init; }
}

/// <summary>Resource link with optional SHA-256 hash.</summary>
public sealed record OscalResourceLink
{
    [JsonPropertyName("href")]
    public string Href { get; init; } = string.Empty;

    [JsonPropertyName("media-type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("hashes")]
    public List<OscalHash>? Hashes { get; init; }
}

/// <summary>Cryptographic hash for evidence integrity.</summary>
public sealed record OscalHash
{
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = "SHA-256";

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;
}

/// <summary>Reviewed-controls selection.</summary>
public sealed record OscalReviewedControls
{
    [JsonPropertyName("control-selections")]
    public List<OscalControlSelection> ControlSelections { get; init; } = new();
}

/// <summary>Control selection filter.</summary>
public sealed record OscalControlSelection
{
    [JsonPropertyName("include-controls")]
    public List<OscalControlWithIds>? IncludeControls { get; init; }

    [JsonPropertyName("include-all")]
    public object? IncludeAll { get; init; } // {} to include all
}

/// <summary>Explicit list of control IDs.</summary>
public sealed record OscalControlWithIds
{
    [JsonPropertyName("with-ids")]
    public List<string> WithIds { get; init; } = new();
}

/// <summary>Subject reference for observations.</summary>
public sealed record OscalSubjectReference
{
    [JsonPropertyName("subject-uuid")]
    public string SubjectUuid { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "component";
}

/// <summary>Relevant evidence link for observations.</summary>
public sealed record OscalRelevantEvidence
{
    [JsonPropertyName("href")]
    public string? Href { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>Risk remediation entry.</summary>
public sealed record OscalRemediation
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("lifecycle")]
    public string Lifecycle { get; init; } = "recommendation"; // recommendation|planned

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("tasks")]
    public List<OscalRemediationTask>? Tasks { get; init; }
}

/// <summary>Remediation task (milestone).</summary>
public sealed record OscalRemediationTask
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "milestone";

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("timing")]
    public OscalTaskTiming? Timing { get; init; }
}

/// <summary>Task timing with on-date.</summary>
public sealed record OscalTaskTiming
{
    [JsonPropertyName("on-date")]
    public OscalOnDate? OnDate { get; init; }
}

/// <summary>On-date value for task timing.</summary>
public sealed record OscalOnDate
{
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;
}

/// <summary>Origin actor for observations and risks.</summary>
public sealed record OscalOriginActor
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "tool"; // tool|party|assessment-platform

    [JsonPropertyName("actor-uuid")]
    public string ActorUuid { get; init; } = string.Empty;
}

/// <summary>OSCAL component in system-implementation or local-definitions.</summary>
public sealed record OscalComponent
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "software";

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public OscalComponentStatus Status { get; init; } = new();

    [JsonPropertyName("props")]
    public List<OscalProp>? Props { get; init; }
}

/// <summary>Component operational status.</summary>
public sealed record OscalComponentStatus
{
    [JsonPropertyName("state")]
    public string State { get; init; } = "operational";
}

/// <summary>Inventory item in system-implementation.</summary>
public sealed record OscalInventoryItem
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("props")]
    public List<OscalProp>? Props { get; init; }

    [JsonPropertyName("implemented-components")]
    public List<OscalImplementedComponent>? ImplementedComponents { get; init; }
}

/// <summary>Component reference within an inventory item.</summary>
public sealed record OscalImplementedComponent
{
    [JsonPropertyName("component-uuid")]
    public string ComponentUuid { get; init; } = string.Empty;
}
