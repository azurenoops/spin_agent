using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private static readonly JsonSerializerOptions ClaimJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    private static bool IsClaimKind(CspPackageCandidateKind kind) => kind is
        CspPackageCandidateKind.AuthorizationDecisionClaim or CspPackageCandidateKind.BoundaryClaim
        or CspPackageCandidateKind.AssessmentFinding or CspPackageCandidateKind.PoamItem;

    private static string ClaimSection(CspPackageCandidateKind kind) => kind switch
    {
        CspPackageCandidateKind.AuthorizationDecisionClaim => "authorizationDecision",
        CspPackageCandidateKind.BoundaryClaim => "boundary",
        CspPackageCandidateKind.AssessmentFinding => "assessmentFinding",
        CspPackageCandidateKind.PoamItem => "poamItem",
        _ => throw new InvalidDataException("This candidate kind cannot carry a claim.")
    };

    private static CspPackageClaimFamily ClaimFamily(CspPackageCandidateKind kind) => kind switch
    {
        CspPackageCandidateKind.AuthorizationDecisionClaim => CspPackageClaimFamily.AuthorizationDecision,
        CspPackageCandidateKind.BoundaryClaim => CspPackageClaimFamily.Boundary,
        CspPackageCandidateKind.AssessmentFinding => CspPackageClaimFamily.AssessmentFinding,
        CspPackageCandidateKind.PoamItem => CspPackageClaimFamily.PoamItem,
        _ => CspPackageClaimFamily.Inventory
    };

    private static CspPackageCandidateDraft EmitClaim(AnalysisSession session, CspPackageSourceSegment segment,
        CspPackageCandidateKind kind, string name, string? sourceId, CspPackageClaim claim)
    {
        session.Cancellation.ThrowIfCancellationRequested();
        claim = NormalizeClaimDates(claim);
        var fields = ClaimFields(kind, claim);
        var citations = new List<CspPackageSourceCitation>
        {
            new(segment.Key, segment.EntryKey, segment.ArtifactId, segment.ArchivePath, segment.Locator, segment.Text)
        };
        foreach (var field in fields)
        {
            if (citations.Any(citation => SupportsClaimField(field.Key, field.Value, citation.Quote))) continue;
            var source = session.Segments.FirstOrDefault(source => source.EntryKey == segment.EntryKey
                && SupportsClaimField(field.Key, field.Value, source.Text))
                ?? throw new InvalidDataException("A labeled claim value has no retained source support.");
            if (citations.All(citation => citation.SegmentKey != source.Key))
                citations.Add(new(source.Key, source.EntryKey, source.ArtifactId, source.ArchivePath, source.Locator, source.Text));
        }
        claim = claim with
        {
            FieldSources = BindClaimFields(kind, claim, citations)
        };
        claim = ValidateClaim(kind, claim, citations);
        if (claim.Relationships.Any(relation => relation.TargetEntryId is not null
            && session.Entries.All(entry => entry.Key != relation.TargetEntryId)))
            throw new InvalidDataException("Relationship target entry has not been accounted for.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 2000
            || !citations.Any(citation => citation.Quote.Contains(name, StringComparison.Ordinal))
            || sourceId is not null && !citations.Any(citation => citation.Quote.Contains(sourceId, StringComparison.Ordinal)))
            throw new InvalidDataException("Claim identity must be bounded and explicitly source-supported.");
        var key = StableKey(segment.Key, kind.ToString(), sourceId ?? name, JsonSerializer.Serialize(claim, ClaimJson));
        var retained = session.Candidates.FirstOrDefault(candidate => candidate.Key == key);
        if (retained is not null) return retained;
        if (session.Candidates.Count >= session.MaxCandidates)
            throw new BudgetExceededException("CANDIDATE_COUNT_LIMIT", "Claim proposals exceed the package budget. Split the source; no claim was truncated.");
        var draft = new CspPackageCandidateDraft(key, kind, name, string.Empty, sourceId,
            null, null, null, [], [], citations) { Claim = claim };
        session.Candidates.Add(draft);
        return draft;
    }

    private static CspPackageClaim NormalizeClaimDates(CspPackageClaim claim)
    {
        var qualifications = claim.Qualifications?.ToList() ?? throw new InvalidDataException("Qualifications cannot be null.");
        void Qualify(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                    if (property.Name is not ("subjectKind" or "relationship")) Qualify(property.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) Qualify(item);
            else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text
                && ClaimQualification.IsMatch(text) && !qualifications.Contains(text, StringComparer.Ordinal))
                qualifications.Add(text);
        }
        foreach (var section in new object?[] { claim.AuthorizationDecision, claim.Boundary, claim.AssessmentFinding, claim.PoamItem })
            if (section is not null) Qualify(JsonSerializer.SerializeToElement(section, ClaimJson));
        string? Date(string? value)
        {
            if (value is null) return null;
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return value;
            if (!qualifications.Contains(value, StringComparer.Ordinal)) qualifications.Add(value);
            return null;
        }
        var decision = claim.AuthorizationDecision;
        if (decision is not null)
            decision = decision with { DecisionDate = Date(decision.DecisionDate), ExpirationDate = Date(decision.ExpirationDate) };
        var finding = claim.AssessmentFinding;
        if (finding is not null) finding = finding with { AssessmentDate = Date(finding.AssessmentDate) };
        var poam = claim.PoamItem;
        if (poam is not null)
            poam = poam with { Milestones = (poam.Milestones ?? throw new InvalidDataException("Milestones cannot be null."))
                .Select(milestone => milestone is null ? throw new InvalidDataException("Milestone cannot be null.")
                    : milestone with { DueDate = Date(milestone.DueDate) }).ToArray() };
        return claim with { AuthorizationDecision = decision, AssessmentFinding = finding, PoamItem = poam,
            Qualifications = qualifications };
    }

    private static Dictionary<string, string> ClaimFields(CspPackageCandidateKind kind, CspPackageClaim claim)
    {
        var sections = new object?[] { claim.AuthorizationDecision, claim.Boundary, claim.AssessmentFinding, claim.PoamItem };
        var json = JsonSerializer.SerializeToElement(claim, ClaimJson);
        var section = ClaimSection(kind);
        if (sections.Count(value => value is not null) != 1 || json.GetProperty(section).ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A claim requires exactly its candidate-kind section.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(JsonElement value, string path)
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                if (ClaimCollections.Contains(path.Split('.').Last()))
                    throw new InvalidDataException("Claim collections cannot be null.");
                return;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                if (value.GetArrayLength() > 100)
                    throw new BudgetExceededException("CLAIM_COLLECTION_LIMIT", "Claim collections are limited to 100 items. Split the declaration.");
                var index = 0;
                foreach (var item in value.EnumerateArray()) Walk(item, $"{path}[{index++}]");
                return;
            }
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                    Walk(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}");
                return;
            }
            if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Claim values must be strings.");
            var text = value.GetString()!;
            var maximum = path.EndsWith(".scope", StringComparison.Ordinal) || path.EndsWith(".observation", StringComparison.Ordinal)
                || path.EndsWith(".correctiveAction", StringComparison.Ordinal) ? 8000 : 2000;
            if (text.Length > maximum)
                throw new BudgetExceededException("CLAIM_STRING_LIMIT", $"Claim field exceeds {maximum} characters. Split the declaration.");
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Populated claim fields cannot be empty.");
            if (path == "authorizationDecision.subjectKind" && text == "Unspecified"
                || path == "boundary.relationship" && text == "Undetermined") return;
            result.Add(path, text);
        }
        Walk(json.GetProperty(section), section);
        if (claim.SourceAliases is null || claim.Qualifications is null || claim.Relationships is null
            || claim.FieldSources is null)
            throw new InvalidDataException("Claim collections cannot be null.");
        Walk(json.GetProperty("sourceAliases"), "sourceAliases");
        Walk(json.GetProperty("qualifications"), "qualifications");
        if (claim.Relationships.Count > 100)
            throw new BudgetExceededException("CLAIM_COLLECTION_LIMIT", "Claim relationships are limited to 100 items.");
        for (var index = 0; index < claim.Relationships.Count; index++)
        {
            var relation = claim.Relationships[index] ?? throw new InvalidDataException("Null claim relationship.");
            if (!AllowedClaimRelationship(kind, relation.Kind))
                throw new InvalidDataException("Relationship kind is not valid for this claim.");
            Walk(JsonSerializer.SerializeToElement(relation.TargetSourceId), $"relationships[{index}].targetSourceId");
        }
        if (result.Count == 0 || !result.Keys.Any(key => key.StartsWith(section + ".", StringComparison.Ordinal)))
            throw new InvalidDataException("A claim must contain substantive source fields.");
        if (result.Count > 256)
            throw new BudgetExceededException("CLAIM_FIELD_LIMIT", "Claims are limited to 256 field bindings.");
        return result;
    }

    private static bool AllowedClaimRelationship(CspPackageCandidateKind kind, string relation) => (kind, relation) switch
    {
        (CspPackageCandidateKind.BoundaryClaim, "BoundaryComponent") => true,
        (CspPackageCandidateKind.AssessmentFinding, "FindingComponent" or "FindingCapability") => true,
        (CspPackageCandidateKind.PoamItem, "PoamFinding") => true,
        (CspPackageCandidateKind.AuthorizationDecisionClaim, "DecisionBoundary" or "InheritedAuthorization") => true,
        _ => false
    };

    private static bool SupportsClaimField(string field, string value, string quote)
    {
        if (field == "authorizationDecision.subjectKind")
            return value switch
            {
                "Provider" or "InheritedCloud" or "MissionSystem" => quote.Contains(value, StringComparison.Ordinal),
                _ => false
            };
        if (field == "boundary.relationship")
            return value switch
            {
                "Included" => quote.Contains("includes", StringComparison.OrdinalIgnoreCase)
                    || quote.Contains("included", StringComparison.OrdinalIgnoreCase),
                "Excluded" => quote.Contains("excluded", StringComparison.OrdinalIgnoreCase)
                    || quote.Contains("excludes", StringComparison.OrdinalIgnoreCase)
                    || quote.Contains("outside", StringComparison.OrdinalIgnoreCase),
                "Proposed" => quote.Contains("proposed", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        return quote.Contains(value, StringComparison.Ordinal)
            || quote.Contains(JsonSerializer.Serialize(value, ClaimJson)[1..^1], StringComparison.Ordinal);
    }

    private static IReadOnlyList<CspClaimFieldSource> BindClaimFields(CspPackageCandidateKind kind,
        CspPackageClaim claim, IReadOnlyList<CspPackageSourceCitation> citations) =>
        ClaimFields(kind, claim).Select(field => new CspClaimFieldSource(field.Key,
            citations.Select((citation, index) => (citation, index))
                .Where(item => SupportsClaimField(field.Key, field.Value, item.citation.Quote))
                .Select(item => item.index).Take(1).ToArray())).ToArray();

    private static CspPackageClaim ValidateClaim(CspPackageCandidateKind kind, CspPackageClaim claim,
        IReadOnlyList<CspPackageSourceCitation> citations)
    {
        var fields = ClaimFields(kind, claim);
        if (claim.FieldSources.Count > 256)
            throw new BudgetExceededException("CLAIM_FIELD_LIMIT", "Claims are limited to 256 field bindings.");
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in claim.FieldSources)
        {
            if (binding is null || !bindings.Add(binding.Field) || !fields.TryGetValue(binding.Field, out var value)
                || binding.CitationIndexes is null || binding.CitationIndexes.Count is < 1 or > 32
                || binding.CitationIndexes.Distinct().Count() != binding.CitationIndexes.Count
                || binding.CitationIndexes.Any(index => index < 0 || index >= citations.Count
                    || !SupportsClaimField(binding.Field, value, citations[index].Quote)))
                throw new InvalidDataException("Claim field binding is missing, duplicate, or unsupported by its cited quote.");
        }
        if (!bindings.SetEquals(fields.Keys))
            throw new InvalidDataException("Every substantive claim field must have an exact supporting field binding.");
        foreach (var value in fields.Where(field => field.Key.EndsWith("Date", StringComparison.Ordinal)))
            if (!DateOnly.TryParseExact(value.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidDataException("Claim dates must be strict ISO date-only values; retain ambiguous text as a qualification.");
        if (claim.AuthorizationDecision is { DecisionDate: not null, ExpirationDate: not null } decision
            && string.CompareOrdinal(decision.ExpirationDate, decision.DecisionDate) < 0)
            throw new InvalidDataException("Decision expiration precedes its source-stated decision date.");
        return claim with { Relationships = claim.Relationships.Select(relation => relation with { Resolution = "Unresolved" }).ToArray() };
    }

    private static CspPackageClaim PreserveSemanticQualifications(CspPackageCandidateKind kind, CspPackageClaim claim,
        List<CspPackageSourceCitation> citations, IReadOnlyDictionary<string, CspPackageSourceSegment> sources,
        CancellationToken cancellation)
    {
        var normalized = NormalizeClaimDates(claim);
        var qualifications = normalized.Qualifications.ToList();
        var bindings = claim.FieldSources.ToList();
        foreach (var source in sources.Values.Where(source => citations.Any(citation => citation.SegmentKey == source.Key)).ToArray())
        {
            cancellation.ThrowIfCancellationRequested();
            var document = new LabeledClaimDocument([source], cancellation);
            foreach (var sentence in document.Sentences(0, document.Text.Length))
                if (ClaimQualification.IsMatch(sentence.Value) && !qualifications.Contains(sentence.Value, StringComparer.Ordinal))
                    qualifications.Add(sentence.Value);
        }
        for (var index = claim.Qualifications.Count; index < qualifications.Count; index++)
        {
            var quote = qualifications[index];
            var citationIndex = citations.FindIndex(citation => citation.Quote.Contains(quote, StringComparison.Ordinal));
            if (citationIndex < 0)
            {
                var source = sources.Values.FirstOrDefault(source => source.Text.Contains(quote, StringComparison.Ordinal))
                    ?? throw new InvalidDataException("Qualification lacks source support.");
                citationIndex = citations.Count;
                citations.Add(new(source.Key, source.EntryKey, source.ArtifactId, source.ArchivePath, source.Locator, quote));
            }
            bindings.Add(new($"qualifications[{index}]", [citationIndex]));
        }
        return ValidateClaim(kind, normalized with { Qualifications = qualifications, FieldSources = bindings }, citations);
    }

    private static void ResolveClaimRelationships(AnalysisSession session)
    {
        for (var index = 0; index < session.Candidates.Count; index++)
        {
            session.Cancellation.ThrowIfCancellationRequested();
            var candidate = session.Candidates[index];
            if (candidate.Claim is not { } claim) continue;
            var relations = claim.Relationships.Select(relation =>
            {
                if (relation.TargetEntryId is not null && session.Entries.All(entry => entry.Key != relation.TargetEntryId))
                    throw new InvalidDataException("Claim relationship references an unaccounted package entry.");
                var targetKind = relation.Kind switch
                {
                    "BoundaryComponent" or "FindingComponent" => CspPackageCandidateKind.Component,
                    "FindingCapability" => CspPackageCandidateKind.Capability,
                    "PoamFinding" => CspPackageCandidateKind.AssessmentFinding,
                    "DecisionBoundary" => CspPackageCandidateKind.BoundaryClaim,
                    _ => CspPackageCandidateKind.AuthorizationReference
                };
                var count = session.Candidates.Count(target => target.Key != candidate.Key && target.Kind == targetKind
                    && (target.SourceId == relation.TargetSourceId || target.Claim?.SourceAliases.Contains(relation.TargetSourceId) == true)
                    && (relation.TargetEntryId is null || target.Citations.Any(citation => citation.EntryKey == relation.TargetEntryId)));
                return relation with { Resolution = count switch { 0 => "Unresolved", 1 => "Resolved", _ => "Ambiguous" } };
            }).ToArray();
            session.Candidates[index] = candidate with { Claim = claim with { Relationships = relations } };
        }
    }
}
