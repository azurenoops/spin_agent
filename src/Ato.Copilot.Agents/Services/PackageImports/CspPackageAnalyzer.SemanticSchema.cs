using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using Microsoft.Extensions.AI;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private ChatResponseFormat SemanticResponseFormat(IReadOnlyList<CspPackageSourceSegment> batch)
    {
        static object Text(int maximum = 2000, bool nullable = true) =>
            new { type = nullable ? new[] { "string", "null" } : ["string"], minLength = 1, maxLength = maximum };
        static object Choice(params string[] values) => new { type = "string", @enum = values };
        static object Array(object items, int maximum = 100) => new { type = "array", items, maxItems = maximum };
        static object Shape(params (string Name, object Schema)[] fields) => new
        {
            type = "object", additionalProperties = false,
            properties = fields.ToDictionary(field => field.Name, field => field.Schema),
            required = fields.Select(field => field.Name).ToArray()
        };
        var segmentKey = Choice(SemanticSourceAliases(batch).Keys.ToArray());
        var citations = Array(Shape(("segmentKey", segmentKey)), _limits.MaxSemanticSegmentsPerCall);
        object Candidate(string[] kinds, int nameLimit, params (string Name, object Schema)[] fields) =>
            Shape([("kind", Choice(kinds)), ("name", Text(nameLimit, false)), ("description", Text()),
                ("sourceId", Text(256)), ("citations", citations), .. fields]);
        object Claim(string kind, string section, object payload, params string[] relationships) =>
            Candidate([kind], 2000, ("claim", Shape(
                (section, payload),
                ("relationships", Array(Shape(("kind", Choice(relationships)),
                    ("targetSourceId", Text(2000, false))))),
                ("sourceAliases", Array(Text(2000, false))),
                ("qualifications", Array(Text(2000, false))))));
        var dependencies = Array(Text(256, false), 128);
        var candidates = new object[]
        {
            Candidate(["Component", "Capability"], 256, ("componentType", Text(100)),
                ("dependencySourceIds", dependencies)),
            Candidate(["ControlMapping"], 256, ("controlId", Text(50, false)),
                ("responsibility", Text(50)), ("dependencySourceIds", dependencies)),
            Candidate(["Responsibility"], 256, ("responsibility", Text(50, false)),
                ("dependencySourceIds", dependencies)),
            Candidate(["AuthorizationReference"], 256, ("authorizationReference", Shape(
                ("reference", Text(2000, false)), ("issuer", Text(500)),
                ("issuedAt", Text(33)), ("expiresAt", Text(33))))),
            Claim("AuthorizationDecisionClaim", "authorizationDecision", Shape(
                ("subjectKind", Choice("Provider", "InheritedCloud", "MissionSystem", "Unspecified")),
                ("subject", Text()), ("reference", Text()), ("authority", Text()), ("decisionType", Text()),
                ("statusAsStated", Text()), ("decisionDate", Text(10)), ("expirationDate", Text(10)),
                ("scope", Text(8000)), ("conditions", Array(Text(2000, false))),
                ("exclusions", Array(Text(2000, false)))), "DecisionBoundary", "InheritedAuthorization"),
            Claim("BoundaryClaim", "boundary", Shape(
                ("subject", Text()), ("relationship", Choice("Included", "Excluded", "Proposed", "Undetermined")),
                ("scope", Text(8000)), ("environment", Text()), ("resourceIds", Array(Text(2000, false))),
                ("responsibilities", Array(Text(2000, false))), ("decisionReference", Text())), "BoundaryComponent"),
            Claim("AssessmentFinding", "assessmentFinding", Shape(
                ("sourceFindingId", Text()), ("observation", Text(8000)), ("severityAsStated", Text()),
                ("statusAsStated", Text()), ("assessor", Text()), ("assessmentDate", Text(10)),
                ("controlIds", Array(Text(2000, false))), ("evidenceReferences", Array(Text(2000, false)))),
                "FindingComponent", "FindingCapability"),
            Claim("PoamItem", "poamItem", Shape(
                ("sourcePoamId", Text()), ("correctiveAction", Text(8000)), ("ownerAsStated", Text()),
                ("statusAsStated", Text()), ("milestones", Array(Shape(("description", Text()), ("dueDate", Text(10))))),
                ("requiredClosureEvidence", Array(Text(2000, false))),
                ("submittedEvidenceReferences", Array(Text(2000, false)))), "PoamFinding")
        };
        var schema = Shape(
            ("analyzedSegmentKeys", Array(segmentKey, batch.Count)),
            ("candidates", Array(new { anyOf = candidates }, _limits.MaxCandidates)),
            ("familyCoverage", Array(Shape(("family", Choice(Enum.GetNames<CspPackageClaimFamily>())),
                ("status", Choice("Analyzed", "NoDeclarations"))), Enum.GetValues<CspPackageClaimFamily>().Length)));
        return ChatResponseFormat.ForJsonSchema(JsonSerializer.SerializeToElement(schema), "package_analysis");
    }
}
