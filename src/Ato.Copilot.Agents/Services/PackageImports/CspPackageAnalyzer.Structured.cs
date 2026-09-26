using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Ato.Copilot.Core.Interfaces.PackageImports;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private void ExtractJson(AnalysisSession session, Entry entry)
    {
        using var document = JsonDocument.Parse(ReadText(entry, session.Cancellation), new JsonDocumentOptions { MaxDepth = _limits.MaxStructuredDepth });
        ValidateJsonProperties(document.RootElement, session.Cancellation);
        var explicitInventory = false;
        WalkJson(session, entry, document.RootElement, "$", null, ref explicitInventory);
        entry.Process(complete: IsEmptyRecordCollection(document.RootElement)
            || explicitInventory && (JsonInventoryOnly(document.RootElement) || IsSupportedOscalStructure(document.RootElement)));
    }

    private void WalkJson(AnalysisSession session, Entry entry, JsonElement element, string path,
        CspPackageCandidateKind? declaredKind, ref bool explicitInventory, CspPackageCandidateDraft? owner = null)
    {
        session.Cancellation.ThrowIfCancellationRequested();
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in element.EnumerateArray())
                WalkJson(session, entry, child, $"{path}[{index++}]", declaredKind, ref explicitInventory, owner);
            if (element.GetArrayLength() == 0)
                session.Segment(entry, path, element.GetRawText());
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            session.Segment(entry, path, element.GetRawText());
            return;
        }
        var kind = declaredKind ?? ParseKind(StringProperty(element, "kind"));
        if (element.TryGetProperty("kind", out _))
        {
            var explicitKind = ParseKind(StringProperty(element, "kind"));
            if (explicitKind is null)
                throw new UnsupportedContentException("UNSUPPORTED_DECLARATION_KIND",
                    "An explicit declaration kind is unsupported. Supply a recognized declaration or review this source.");
            if (declaredKind is not null && explicitKind != declaredKind)
                throw new InvalidDataException("The declaration kind contradicts its enclosing collection.");
        }
        if (kind is not null)
        {
            var segment = session.Segment(entry, path, element.GetRawText());
            if (IsClaimKind(kind.Value))
            {
                ExtractJsonClaim(session, segment, kind.Value, element);
                explicitInventory = true;
                return;
            }
            var properties = JsonRecordFields(element);
            if (kind == CspPackageCandidateKind.AuthorizationReference)
                NormalizeAuthorizationJsonFields(element, properties);
            var record = EmitRecord(session, segment, kind.Value, properties)
                ?? throw new InvalidDataException("Structured declaration has no explicit name, title or control identifier.");
            explicitInventory = true;
            foreach (var property in element.EnumerateObject().Where(property =>
                property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object))
                WalkJsonProperty(session, entry, property, path, ref explicitInventory, record ?? owner);
            return;
        }
        if (!element.EnumerateObject().Any())
            session.Segment(entry, path, element.GetRawText());
        foreach (var property in element.EnumerateObject())
            WalkJsonProperty(session, entry, property, path, ref explicitInventory, owner);
    }

    private void WalkJsonProperty(AnalysisSession session, Entry entry, JsonProperty property, string path,
        ref bool explicitInventory, CspPackageCandidateDraft? owner)
    {
        var escaped = property.Name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        var childPath = $"{path}/{escaped}";
        if (property.Name == "implemented-requirements")
        {
            ExtractOscalRequirements(session, entry, property.Value, childPath, owner);
            explicitInventory = true;
            return;
        }
        var childKind = ParseKind(property.Name);
        if (childKind is not null && property.Value.ValueKind == JsonValueKind.Array)
            explicitInventory = true;
        WalkJson(session, entry, property.Value, childPath, childKind, ref explicitInventory, owner);
    }

    private static bool JsonInventoryOnly(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (ParseKind(StringProperty(root, "kind")) is { } direct && IsClaimKind(direct))
            return true;
        if (ParseKind(StringProperty(root, "kind")) == CspPackageCandidateKind.AuthorizationReference)
            return AuthorizationInventoryOnly(root);
        return root.EnumerateObject().All(property =>
            ParseKind(property.Name) == CspPackageCandidateKind.AuthorizationReference
            ? AuthorizationInventoryOnly(property.Value)
            : ParseKind(property.Name) is { } collectionKind && property.Value.ValueKind == JsonValueKind.Array
            && property.Value.EnumerateArray().All(record =>
                IsClaimKind(collectionKind) ||
                record.ValueKind == JsonValueKind.Object && !string.IsNullOrWhiteSpace(
                    StringProperty(record, "name") ?? StringProperty(record, "title"))
                && record.EnumerateObject().All(field => StructuredFields.Contains(field.Name)
                    || ParseKind(field.Name) == CspPackageCandidateKind.AuthorizationReference
                    && AuthorizationInventoryOnly(field.Value))));
    }

    private static readonly HashSet<string> StructuredFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "title", "description", "id", "uuid", "kind", "type", "componentType",
        "controlId", "control-id", "controlIds", "mappedNistControlIds", "responsibility",
        "componentIds", "contributorIds", "componentId", "component-uuid", "capabilityId"
    };

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void ValidateJsonProperties(JsonElement element, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateJsonProperties(item, cancellation);
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new InvalidDataException("Duplicate JSON properties are ambiguous.");
            ValidateJsonProperties(property.Value, cancellation);
        }
    }

    private static string JsonField(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join('\n', value.EnumerateArray().Select(JsonField)),
        _ => value.GetRawText()
    };

    private void ExtractXml(AnalysisSession session, Entry entry)
    {
        var document = ReadXml(session, entry);
        var recordElements = document.Descendants().Where(element => element.Name.LocalName.ToLowerInvariant()
            is "component" or "capability" or "mapping" or "controlmapping" or "responsibility" or "authorizationreference"
                or "authorizationdecisionclaim" or "boundaryclaim" or "assessmentfinding" or "poamitem").ToArray();
        foreach (var element in recordElements)
        {
            var segment = session.Segment(entry, XmlLocator(element), element.ToString(SaveOptions.DisableFormatting));
            var explicitKind = element.Attribute("kind")?.Value ?? element.Element("kind")?.Value;
            if (explicitKind is not null && ParseKind(explicitKind) != ParseKind(element.Name.LocalName))
            {
                entry.Set(CspPackageEntryStatus.Unsupported, "UNSUPPORTED_DECLARATION_KIND",
                    "An explicit XML declaration kind is unsupported or conflicts with its element. Review this source.");
                continue;
            }
            if (IsClaimKind(ParseKind(element.Name.LocalName)!.Value))
            {
                var record = JsonSerializer.SerializeToElement(XmlClaimObject(element));
                ExtractJsonClaim(session, segment, ParseKind(element.Name.LocalName)!.Value, record);
                continue;
            }
            var fields = element.Attributes().ToDictionary(attribute => attribute.Name.LocalName,
                attribute => attribute.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var field in element.Elements())
            {
                if (ParseKind(element.Name.LocalName) == CspPackageCandidateKind.AuthorizationReference
                    && fields.ContainsKey(field.Name.LocalName))
                    throw new InvalidDataException("Duplicate authorization reference XML fields are ambiguous.");
                fields[field.Name.LocalName] = field.Value;
            }
            EmitRecord(session, segment, ParseKind(element.Name.LocalName)!.Value, fields);
        }
        foreach (var element in document.Descendants().Where(element => !element.HasElements
            && !element.AncestorsAndSelf().Any(recordElements.Contains)))
            session.Segment(entry, XmlLocator(element), element.Value);
        entry.Process(complete: false);
    }

    private XDocument ReadXml(AnalysisSession session, Entry entry)
    {
        var text = ReadText(entry, session.Cancellation);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = _limits.MaxExtractedCharacters,
            MaxCharactersFromEntities = _limits.MaxExtractedCharacters
        };
        using (var reader = XmlReader.Create(new StringReader(text), settings))
            while (reader.Read())
            {
                session.Cancellation.ThrowIfCancellationRequested();
                if (reader.Depth > _limits.MaxStructuredDepth)
                    throw new BudgetExceededException("STRUCTURED_DEPTH_LIMIT", "XML nesting exceeds the configured analysis depth.");
            }
        using var validated = XmlReader.Create(new StringReader(text), settings);
        return XDocument.Load(validated, LoadOptions.PreserveWhitespace);
    }

    private static string XmlLocator(XElement element) => "/" + string.Join('/',
        element.AncestorsAndSelf().Reverse().Select(node =>
            $"{node.Name.LocalName}[{node.ElementsBeforeSelf(node.Name).Count() + 1}]"));

    private string ReadText(Entry entry, CancellationToken cancellation)
    {
        using var stream = new MemoryStream(entry.Content!, writable: false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var buffer = new char[8 * 1024];
        var text = new StringBuilder();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = reader.Read(buffer, 0, buffer.Length);
            if (count == 0) break;
            if (count > _limits.MaxExtractedCharacters - text.Length)
                throw new BudgetExceededException("EXTRACTED_CHARACTER_LIMIT",
                    "Source text exceeds the character budget. Split the source; no truncated successful analysis was returned.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private void ExtractText(AnalysisSession session, Entry entry)
    {
        var text = ReadText(entry, session.Cancellation);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
            session.Segment(entry, $"line:{index + 1}", lines[index]);
        entry.Process(complete: false);
    }

    private void ExtractCsv(AnalysisSession session, Entry entry)
    {
        var rows = CsvRows(ReadText(entry, session.Cancellation), session.Cancellation).ToArray();
        if (rows.Length == 0)
        {
            session.Segment(entry, "row:1", string.Empty);
            entry.Process(complete: false);
            return;
        }
        var headers = rows[0].Values;
        foreach (var (row, index) in rows.Select((row, index) => (row, index)))
        {
            var segment = session.Segment(entry, $"row:{index + 1}", row.Raw);
            if (index == 0) continue;
            EmitTabularRow(session, segment, headers, row.Values);
        }
        entry.Process(complete: false);
    }

    private static void EmitTabularRow(AnalysisSession session, CspPackageSourceSegment segment,
        IReadOnlyList<string> headers, IReadOnlyList<string> cells)
    {
        if (headers.Count != headers.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            for (var index = 0; index < headers.Count && index < cells.Count; index++)
                if (string.Equals(headers[index], "kind", StringComparison.OrdinalIgnoreCase)
                    && ParseKind(cells[index]) == CspPackageCandidateKind.AuthorizationReference)
                    throw new InvalidDataException("Duplicate authorization reference column headers are ambiguous.");
            return;
        }
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count && index < cells.Count; index++)
            fields[headers[index]] = cells[index];
        var kind = ParseKind(Get(fields, "kind"));
        if (Get(fields, "kind") is not null && kind is null)
        {
            session.Entries.Single(entry => entry.Key == segment.EntryKey).Set(CspPackageEntryStatus.Unsupported,
                "UNSUPPORTED_DECLARATION_KIND",
                "An explicit row kind is unsupported. All rows are retained; unsupported declarations cannot become inventory.");
            return;
        }
        if (kind is not null)
            EmitRecord(session, segment, kind.Value, fields);
        else if (fields.ContainsKey("component"))
        {
            fields["name"] = fields["component"];
            EmitRecord(session, segment, CspPackageCandidateKind.Component, fields);
        }
        else if (fields.ContainsKey("capability"))
        {
            fields["name"] = fields["capability"];
            EmitRecord(session, segment, CspPackageCandidateKind.Capability, fields);
        }
    }

    private sealed record CsvRow(string Raw, IReadOnlyList<string> Values);

    private static IEnumerable<CsvRow> CsvRows(string text, CancellationToken cancellation)
    {
        var cells = new List<string>();
        var field = new StringBuilder();
        var start = 0;
        var quoted = false;
        var closed = false;
        for (var index = 0; index < text.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var character = text[index];
            if (quoted)
            {
                if (character != '"') field.Append(character);
                else if (index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; }
                else { quoted = false; closed = true; }
                continue;
            }
            if (character == '"' && field.Length == 0 && !closed) { quoted = true; continue; }
            if (character == '"' || (closed && character is not (',' or '\r' or '\n')))
                throw new InvalidDataException("Malformed CSV quoting.");
            if (character is not (',' or '\r' or '\n')) { field.Append(character); continue; }
            cells.Add(field.ToString());
            field.Clear();
            closed = false;
            if (character == ',') continue;
            yield return new(text[start..index], cells.ToArray());
            cells.Clear();
            if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            start = index + 1;
        }
        if (quoted) throw new InvalidDataException("Unterminated CSV quoted field.");
        if (start < text.Length)
        {
            cells.Add(field.ToString());
            yield return new(text[start..], cells.ToArray());
        }
    }

    private static CspPackageCandidateKind? ParseKind(string? value) => value?.ToLowerInvariant() switch
    {
        "component" or "components" => CspPackageCandidateKind.Component,
        "capability" or "capabilities" => CspPackageCandidateKind.Capability,
        "mapping" or "mappings" or "controlmapping" => CspPackageCandidateKind.ControlMapping,
        "responsibility" or "responsibilities" => CspPackageCandidateKind.Responsibility,
        "authorizationreference" or "authorizationreferences" => CspPackageCandidateKind.AuthorizationReference,
        "authorizationdecisionclaim" or "authorizationdecisionclaims" => CspPackageCandidateKind.AuthorizationDecisionClaim,
        "boundaryclaim" or "boundaryclaims" => CspPackageCandidateKind.BoundaryClaim,
        "assessmentfinding" or "assessmentfindings" => CspPackageCandidateKind.AssessmentFinding,
        "poamitem" or "poamitems" => CspPackageCandidateKind.PoamItem,
        _ => null
    };

    private static string? Get(IReadOnlyDictionary<string, string> fields, params string[] names)
    {
        foreach (var name in names)
            if (fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    private static string[] References(string? value) =>
        value?.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static CspPackageCandidateDraft? EmitRecord(AnalysisSession session, CspPackageSourceSegment segment,
        CspPackageCandidateKind kind, IReadOnlyDictionary<string, string> fields)
    {
        if (IsClaimKind(kind))
            return ExtractTabularClaim(session, segment, kind, fields);
        if (kind == CspPackageCandidateKind.AuthorizationReference)
            return EmitAuthorizationReference(session, segment, fields);
        var control = Get(fields, "controlId", "control-id");
        var responsibility = Get(fields, "responsibility");
        var name = Get(fields, "name", "title")
            ?? (kind == CspPackageCandidateKind.ControlMapping ? control : null)
            ?? (kind == CspPackageCandidateKind.Responsibility ? responsibility : null);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var draft = AddDraft(session, segment, kind, name, Get(fields, "description") ?? string.Empty,
            Get(fields, "id", "uuid"), Get(fields, "type", "componentType"), control, responsibility, []);
        session.SourceDependencies[draft.Key] = References(
            Get(fields, "componentIds", "contributorIds", "componentId", "component-uuid", "capabilityId"));
        if (kind is CspPackageCandidateKind.Component or CspPackageCandidateKind.Capability)
        {
            foreach (var controlId in References(Get(fields, "controlIds", "mappedNistControlIds")))
                AddDraft(session, segment, CspPackageCandidateKind.ControlMapping, controlId, string.Empty,
                    null, null, controlId, null, [draft.Key]);
            if (responsibility is not null)
                AddDraft(session, segment, CspPackageCandidateKind.Responsibility, responsibility, string.Empty,
                    null, null, null, responsibility, [draft.Key]);
        }
        return draft;
    }

    private static CspPackageCandidateDraft AddDraft(AnalysisSession session, CspPackageSourceSegment segment,
        CspPackageCandidateKind kind, string name, string description, string? sourceId,
        string? componentType, string? control, string? responsibility, IReadOnlyList<string> dependencies)
    {
        session.Cancellation.ThrowIfCancellationRequested();
        if (session.Candidates.Count >= session.MaxCandidates)
            throw new BudgetExceededException("CANDIDATE_COUNT_LIMIT",
                "Candidate count exceeds the package budget. Remaining records were not analyzed; split the source.");
        var citation = new CspPackageSourceCitation(segment.Key, segment.EntryKey, segment.ArtifactId,
            segment.ArchivePath, segment.Locator, segment.Text);
        if (citation.Quote.Length == 0 || !segment.Text.Contains(citation.Quote, StringComparison.Ordinal))
            throw new InvalidDataException("Candidate source quote is not present in its segment.");
        var identity = StableKey(segment.Key, kind.ToString(), name);
        var occurrence = session.CandidateOccurrences.GetValueOrDefault(identity);
        session.CandidateOccurrences[identity] = occurrence + 1;
        var draft = new CspPackageCandidateDraft(
            StableKey(identity, occurrence.ToString(CultureInfo.InvariantCulture)),
            kind, name, description, sourceId, componentType, control, responsibility, dependencies, [], [citation]);
        session.Candidates.Add(draft);
        return draft;
    }

    private static void ResolveDependencies(AnalysisSession session)
    {
        var byId = session.Candidates.Where(candidate => candidate.SourceId is not null
                && candidate.Kind is CspPackageCandidateKind.Component or CspPackageCandidateKind.Capability)
            .GroupBy(candidate => candidate.SourceId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        for (var index = 0; index < session.Candidates.Count; index++)
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var candidate = session.Candidates[index];
            if (!session.SourceDependencies.TryGetValue(candidate.Key, out var references)) continue;
            var dependencies = candidate.DependencyKeys.ToList();
            var unresolved = new List<string>();
            foreach (var reference in references)
            {
                if (byId.TryGetValue(reference, out var matches) && matches.Length == 1 && matches[0].Key != candidate.Key)
                    dependencies.Add(matches[0].Key);
                else unresolved.Add(reference);
            }
            session.Candidates[index] = candidate with
            {
                DependencyKeys = dependencies.Distinct(StringComparer.Ordinal).ToArray(),
                UnresolvedDependencies = unresolved.Distinct(StringComparer.Ordinal).ToArray()
            };
        }
    }
}
