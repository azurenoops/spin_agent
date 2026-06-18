using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for OSCAL SAR and POA&M type serialization (Feature 076 — T003).
/// Validates that OSCAL 1.1.2 kebab-case JSON roundtrips correctly with the new POCOs.
/// </summary>
public class OscalAssessmentTypesTests
{
    private static readonly JsonSerializerOptions OscalOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    // ─── SAR serialization ──────────────────────────────────────────────────

    [Fact]
    public void OscalAssessmentResultsRoot_SerializesToCorrectTopLevelKey()
    {
        var root = new OscalAssessmentResultsRoot
        {
            AssessmentResults = new OscalAssessmentResults
            {
                Uuid = "00000000-0000-0000-0000-000000000001",
                Metadata = new OscalDocumentMetadata
                {
                    Title = "Test SAR",
                    LastModified = "2026-06-18T00:00:00Z",
                    OscalVersion = "1.1.2"
                }
            }
        };

        var json = JsonSerializer.Serialize(root, OscalOpts);

        json.Should().Contain("\"assessment-results\"");
        json.Should().Contain("\"oscal-version\":\"1.1.2\"");
        json.Should().Contain("\"last-modified\":");
        json.Should().NotContain("\"assessmentResults\"");    // must be kebab-case
        json.Should().NotContain("\"lastModified\"");         // must be kebab-case
    }

    [Fact]
    public void OscalObservation_SerializesMethodsAndTypes()
    {
        var obs = new OscalObservation
        {
            Uuid = "00000000-0000-0000-0000-000000000002",
            Description = "Observed password length policy",
            Methods = ["INTERVIEW", "EXAMINE"],
            Types = ["finding"],
            Collected = "2026-06-01T09:00:00Z"
        };

        var json = JsonSerializer.Serialize(obs, OscalOpts);

        json.Should().Contain("\"methods\":[\"INTERVIEW\",\"EXAMINE\"]");
        json.Should().Contain("\"types\":[\"finding\"]");
        json.Should().Contain("\"collected\":");
    }

    [Fact]
    public void OscalRiskFacet_UsesFedrampNamespace()
    {
        var facet = new OscalRiskFacet
        {
            Name = "likelihood",
            System = "https://fedramp.gov/ns/oscal",
            Value = "moderate"
        };

        var json = JsonSerializer.Serialize(facet, OscalOpts);

        json.Should().Contain("\"system\":\"https://fedramp.gov/ns/oscal\"");
        json.Should().Contain("\"name\":\"likelihood\"");
        json.Should().Contain("\"value\":\"moderate\"");
    }

    [Fact]
    public void OscalFinding_TargetReferencesStatementId()
    {
        var finding = new OscalFinding
        {
            Uuid = "00000000-0000-0000-0000-000000000003",
            Title = "AC-1 Not Satisfied",
            Description = "Policy not reviewed annually",
            Target = new OscalFindingTarget
            {
                Type = "statement-id",
                TargetId = "ac-1_smt.a",
                Status = new OscalFindingStatus { State = "not-satisfied" }
            }
        };

        var json = JsonSerializer.Serialize(finding, OscalOpts);

        json.Should().Contain("\"target-id\":\"ac-1_smt.a\"");
        json.Should().Contain("\"state\":\"not-satisfied\"");
        json.Should().Contain("\"type\":\"statement-id\"");
    }

    // ─── POA&M serialization ─────────────────────────────────────────────────

    [Fact]
    public void OscalPoamRoot_SerializesToCorrectTopLevelKey()
    {
        var root = new OscalPoamRoot
        {
            PlanOfActionAndMilestones = new OscalPoam
            {
                Uuid = "00000000-0000-0000-0000-000000000004",
                Metadata = new OscalDocumentMetadata { Title = "Test POA&M", OscalVersion = "1.1.2", LastModified = "2026-06-18T00:00:00Z" },
                PoamItems = []
            }
        };

        var json = JsonSerializer.Serialize(root, OscalOpts);

        json.Should().Contain("\"plan-of-action-and-milestones\"");
        json.Should().NotContain("\"planOfActionAndMilestones\"");
        json.Should().Contain("\"poam-items\":[]");
    }

    [Fact]
    public void OscalPoamItem_IncludesRelatedFindingsAssembly()
    {
        // OSCAL 1.1.0+ requires related-findings assembly on poam-items
        var item = new OscalPoamItem
        {
            Uuid = "00000000-0000-0000-0000-000000000005",
            Title = "Weak password configuration",
            Description = "Password policy not enforced on legacy server",
            RelatedFindings =
            [
                new OscalUuidRef { FindingUuid = "00000000-0000-0000-0000-000000000006" }
            ],
            RelatedRisks =
            [
                new OscalUuidRef { RiskUuid = "00000000-0000-0000-0000-000000000007" }
            ]
        };

        var json = JsonSerializer.Serialize(item, OscalOpts);

        json.Should().Contain("\"related-findings\"");
        json.Should().Contain("\"finding-uuid\":");
        json.Should().Contain("\"related-risks\"");
        json.Should().Contain("\"risk-uuid\":");
    }

    [Fact]
    public void OscalProp_WithFedRampNamespace_SerializesNsField()
    {
        var prop = new OscalProp
        {
            Name = "vendor-dependency",
            Ns = "https://fedramp.gov/ns/oscal",
            Value = "true"
        };

        var json = JsonSerializer.Serialize(prop, OscalOpts);

        json.Should().Contain("\"ns\":\"https://fedramp.gov/ns/oscal\"");
        json.Should().Contain("\"vendor-dependency\"");
    }

    // ─── Shared types ────────────────────────────────────────────────────────

    [Fact]
    public void OscalHash_SerializesAlgorithmAndValue()
    {
        var hash = new OscalHash
        {
            Algorithm = "SHA-256",
            Value = "a3f5b2c1d4e6f7890abcdef1234567890abcdef1234567890abcdef1234567890"
        };

        var json = JsonSerializer.Serialize(hash, OscalOpts);

        json.Should().Contain("\"algorithm\":\"SHA-256\"");
        json.Should().Contain("\"value\":\"a3f5b2c1");
    }

    [Fact]
    public void OscalDocumentMetadata_OscalVersion_AlwaysEquals_1_1_2()
    {
        var meta = new OscalDocumentMetadata();
        meta.OscalVersion.Should().Be("1.1.2");
    }

    [Fact]
    public void OscalRemediationTask_SerializesToMilestoneType()
    {
        var task = new OscalRemediationTask
        {
            Uuid = "00000000-0000-0000-0000-000000000008",
            Type = "milestone",
            Title = "Patch applied",
            Timing = new OscalTaskTiming
            {
                OnDate = new OscalOnDate { Date = "2026-09-30" }
            }
        };

        var json = JsonSerializer.Serialize(task, OscalOpts);

        json.Should().Contain("\"type\":\"milestone\"");
        json.Should().Contain("\"on-date\"");
        json.Should().Contain("\"date\":\"2026-09-30\"");
    }
}
