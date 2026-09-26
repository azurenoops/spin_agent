using System.Text.Json;
using System.Xml.Linq;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private static readonly HashSet<string> ClaimCollections = new(StringComparer.Ordinal)
    {
        "conditions", "exclusions", "resourceIds", "responsibilities", "controlIds", "evidenceReferences",
        "milestones", "requiredClosureEvidence", "submittedEvidenceReferences", "qualifications", "sourceAliases",
        "relationships", "fieldSources", "citationIndexes"
    };

    private static CspPackageCandidateDraft ExtractJsonClaim(AnalysisSession session, CspPackageSourceSegment segment,
        CspPackageCandidateKind kind, JsonElement record)
    {
        var hasClaim = record.TryGetProperty("claim", out var payload);
        if (hasClaim)
            SemanticProperties(record, ["kind", "id", "uuid", "name", "title", "claim"]);
        else
        {
            var section = record.EnumerateObject().Where(property => property.Name is not ("kind" or "id" or "uuid" or "name" or "title"
                or "relationships" or "sourceAliases" or "qualifications" or "fieldSources"))
                .ToDictionary(property => property.Name, property => property.Value);
            var wrapper = new Dictionary<string, object?> { [ClaimSection(kind)] = section };
            foreach (var property in record.EnumerateObject().Where(property =>
                property.Name is "relationships" or "sourceAliases" or "qualifications" or "fieldSources"))
                wrapper[property.Name] = property.Value;
            payload = JsonSerializer.SerializeToElement(wrapper);
        }
        ValidateJsonProperties(payload, session.Cancellation);
        var claim = payload.Deserialize<CspPackageClaim>(ClaimJson) ?? throw new InvalidDataException("Claim is null.");
        var sourceId = StringProperty(record, "id") ?? StringProperty(record, "uuid")
            ?? claim.AssessmentFinding?.SourceFindingId ?? claim.PoamItem?.SourcePoamId ?? claim.AuthorizationDecision?.Reference;
        var name = StringProperty(record, "name") ?? StringProperty(record, "title") ?? sourceId
            ?? claim.Boundary?.Subject ?? claim.Boundary?.Scope
            ?? throw new InvalidDataException("A structured claim requires an explicit identity or subject.");
        return EmitClaim(session, segment, kind, name, sourceId, claim);
    }

    private static CspPackageCandidateDraft ExtractTabularClaim(AnalysisSession session, CspPackageSourceSegment segment,
        CspPackageCandidateKind kind, IReadOnlyDictionary<string, string> fields)
    {
        var record = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (key == "claim" || ClaimCollections.Contains(key))
            {
                using var parsed = JsonDocument.Parse(value);
                record[key] = parsed.RootElement.Clone();
            }
            else record[key] = value;
        }
        return ExtractJsonClaim(session, segment, kind, JsonSerializer.SerializeToElement(record));
    }

    private static object XmlClaimObject(XElement element)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes())
            if (!fields.TryAdd(attribute.Name.LocalName, attribute.Value))
                throw new InvalidDataException("Duplicate claim attributes.");
        foreach (var child in element.Elements())
        {
            var key = child.Name.LocalName;
            object? value = ClaimCollections.Contains(key)
                ? child.Elements().Select(item => item.HasElements ? XmlClaimObject(item) : (object)item.Value).ToArray()
                : child.HasElements ? XmlClaimObject(child) : child.Value;
            if (!fields.TryAdd(key, value)) throw new InvalidDataException("Duplicate claim XML fields.");
        }
        return fields;
    }
}
