using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private static Dictionary<string, string> JsonRecordFields(JsonElement element)
    {
        var fields = element.EnumerateObject()
            .ToDictionary(property => property.Name, property => JsonField(property.Value), StringComparer.OrdinalIgnoreCase);
        var responsibility = OscalResponsibility(element);
        if (responsibility is not null)
        {
            if (Get(fields, "responsibility") is { } direct && direct != responsibility)
                throw new InvalidDataException("Conflicting explicit responsibilities.");
            fields["responsibility"] = responsibility;
        }
        if (element.TryGetProperty("incorporates-components", out var contributors))
        {
            RequireArray(contributors, "incorporates-components");
            var references = References(Get(fields, "componentIds", "contributorIds")).ToList();
            foreach (var contributor in contributors.EnumerateArray())
                references.Add(RequiredString(contributor, "component-uuid"));
            fields["componentIds"] = string.Join('\n', references.Distinct(StringComparer.Ordinal));
        }
        return fields;
    }

    private void ExtractOscalRequirements(AnalysisSession session, Entry entry, JsonElement requirements,
        string path, CspPackageCandidateDraft? owner)
    {
        RequireArray(requirements, "implemented-requirements");
        var index = 0;
        foreach (var requirement in requirements.EnumerateArray())
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var controlId = RequiredString(requirement, "control-id");
            var segment = session.Segment(entry, $"{path}[{index++}]", requirement.GetRawText());
            var contributed = ExtractOscalContributions(session, entry, requirement, segment.Locator, controlId, segment);
            if (!contributed)
                EmitOscalMapping(session, segment, segment, requirement, controlId, owner);
        }
        if (index == 0) session.Segment(entry, path, requirements.GetRawText());
    }

    private bool ExtractOscalContributions(AnalysisSession session, Entry entry, JsonElement node, string path,
        string controlId, CspPackageSourceSegment requirement)
    {
        var found = false;
        foreach (var property in node.EnumerateObject())
        {
            session.Cancellation.ThrowIfCancellationRequested();
            if (property.Name == "by-components")
            {
                RequireArray(property.Value, property.Name);
                var index = 0;
                foreach (var contribution in property.Value.EnumerateArray())
                {
                    _ = RequiredString(contribution, "component-uuid");
                    var segment = session.Segment(entry, $"{path}/by-components[{index++}]", contribution.GetRawText());
                    EmitOscalMapping(session, segment, requirement, contribution, controlId, null);
                    found = true;
                }
            }
            else if (property.Name == "statements")
            {
                RequireArray(property.Value, property.Name);
                var index = 0;
                foreach (var statement in property.Value.EnumerateArray())
                {
                    if (statement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("OSCAL statement must be an object.");
                    found |= ExtractOscalContributions(session, entry, statement, $"{path}/statements[{index++}]", controlId, requirement);
                }
            }
        }
        return found;
    }

    private static void EmitOscalMapping(AnalysisSession session, CspPackageSourceSegment segment,
        CspPackageSourceSegment requirement, JsonElement record, string controlId, CspPackageCandidateDraft? owner)
    {
        var responsibility = OscalResponsibility(record);
        var mapping = AddDraft(session, segment, CspPackageCandidateKind.ControlMapping, controlId,
            StringProperty(record, "description") ?? string.Empty, StringProperty(record, "uuid"),
            null, controlId, responsibility, owner is null ? [] : [owner.Key]);
        if (segment.Key != requirement.Key)
            mapping = AddSupportingCitation(session, mapping, requirement);
        if (StringProperty(record, "component-uuid") is { } componentId)
            session.SourceDependencies[mapping.Key] = [componentId];
        if (owner is not null)
            foreach (var citation in owner.Citations)
                mapping = AddSupportingCitation(session, mapping,
                    session.Segments.Single(source => source.Key == citation.SegmentKey));
        if (responsibility is not null)
        {
            var duty = AddDraft(session, segment, CspPackageCandidateKind.Responsibility, responsibility,
                StringProperty(record, "description") ?? string.Empty, null, null, controlId, responsibility, [mapping.Key]);
            if (segment.Key != requirement.Key)
                AddSupportingCitation(session, duty, requirement);
        }
    }

    private static CspPackageCandidateDraft AddSupportingCitation(AnalysisSession session,
        CspPackageCandidateDraft candidate, CspPackageSourceSegment segment)
    {
        if (candidate.Citations.Any(citation => citation.SegmentKey == segment.Key)) return candidate;
        var citation = new CspPackageSourceCitation(segment.Key, segment.EntryKey, segment.ArtifactId,
            segment.ArchivePath, segment.Locator, segment.Text);
        var updated = candidate with { Citations = [.. candidate.Citations, citation] };
        var index = session.Candidates.FindIndex(draft => draft.Key == candidate.Key);
        session.Candidates[index] = updated;
        return updated;
    }

    private static string? OscalResponsibility(JsonElement element)
    {
        if (!element.TryGetProperty("props", out var properties)) return null;
        RequireArray(properties, "props");
        var responsibilities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties.EnumerateArray())
        {
            if (property.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("OSCAL property must be an object.");
            if (StringProperty(property, "name") == "responsibility")
                responsibilities.Add(RequiredString(property, "value"));
        }
        if (responsibilities.Count > 1)
            throw new InvalidDataException("Multiple conflicting responsibility values require source correction.");
        return responsibilities.SingleOrDefault();
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(StringProperty(element, name)))
            throw new InvalidDataException($"Structured record requires a nonempty string '{name}'.");
        return StringProperty(element, name)!;
    }

    private static void RequireArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Structured property '{name}' must be an array.");
    }

    private static bool IsEmptyRecordCollection(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.GetArrayLength() == 0;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.EnumerateObject().Any()) return true;
        return root.EnumerateObject().Any(property => property.Value.ValueKind == JsonValueKind.Array)
            && root.EnumerateObject().All(property => ParseKind(property.Name) is not null
                && property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 0);
    }

    private static bool IsSupportedOscalStructure(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.EnumerateObject().Any(property => property.Name is "system-security-plan" or "component-definition")
        && KnownOscalFields(root);

    private static bool KnownOscalFields(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.EnumerateArray().All(KnownOscalFields),
        JsonValueKind.Object => value.EnumerateObject().All(property =>
            (StructuredFields.Contains(property.Name) || OscalFields.Contains(property.Name)) && KnownOscalFields(property.Value)),
        _ => true
    };

    private static readonly HashSet<string> OscalFields = new(StringComparer.Ordinal)
    {
        "system-security-plan", "component-definition", "metadata", "version", "oscal-version",
        "system-implementation", "components", "capabilities", "status", "state", "control-implementation",
        "control-implementations", "implemented-requirements", "by-components", "statements", "statement-id",
        "incorporates-components", "props", "value", "ns", "class", "remarks", "source",
        "links", "href", "rel", "media-type", "text", "published", "last-modified"
    };
}
