using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private sealed record SemanticProposal(CspPackageCandidateDraft Draft, IReadOnlyList<string> SourceReferences);
    private sealed record SemanticBatchResult(
        List<SemanticProposal> Proposals, IReadOnlyList<Ato.Copilot.Core.Models.PackageImports.CspPackageFamilyCoverage> Families);
    private sealed record SemanticCorrection(string Error, string? CandidateKind, string? CandidateName);

    private sealed class SemanticCandidateValidationException(JsonElement candidate, Exception inner)
        : Exception(inner.Message, inner)
    {
        public SemanticCorrection Correction { get; } = new(BoundedValidationMessage(inner.Message),
            CorrectionLabel(candidate, "kind"), CorrectionLabel(candidate, "name"));
    }

    private static string BoundedValidationMessage(string message) => message.Length <= 512 ? message : message[..512];

    private static string? CorrectionLabel(JsonElement candidate, string property) =>
        candidate.ValueKind == JsonValueKind.Object && candidate.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: <= 256 } text ? text : null;

    private SemanticBatchResult ValidateSemanticResponse(AnalysisSession session,
        IReadOnlyList<CspPackageSourceSegment> batch, string response)
    {
        using var document = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        ValidateJsonProperties(root, session.Cancellation);
        SemanticProperties(root, ["analyzedSegmentKeys", "candidates", "familyCoverage"]);
        var analyzed = SemanticStrings(root, "analyzedSegmentKeys", batch.Count, required: true);
        var byKey = SemanticSourceAliases(batch);
        if (analyzed.Count != byKey.Count || !analyzed.ToHashSet(StringComparer.Ordinal).SetEquals(byKey.Keys))
            throw new InvalidDataException("The model must acknowledge every supplied source segment exactly once.");
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The semantic response requires a candidates array.");
        if (candidates.GetArrayLength() > session.MaxCandidates)
            throw new BudgetExceededException("CANDIDATE_COUNT_LIMIT", "Semantic proposals exceed the remaining package candidate budget. No partial batch was accepted.");
        var proposals = new List<SemanticProposal>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates.EnumerateArray())
        {
            session.Cancellation.ThrowIfCancellationRequested();
            SemanticProposal proposal;
            try
            {
                proposal = ParseSemanticCandidate(candidate, byKey, session.Cancellation);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException)
            {
                throw new SemanticCandidateValidationException(candidate, exception);
            }
            if (proposal.Draft.Claim?.Relationships.Any(relation => relation.TargetEntryId is not null
                && session.Entries.All(entry => entry.Key != relation.TargetEntryId)) == true)
                throw new InvalidDataException("Semantic relationship entry has not been accounted for.");
            if (!keys.Add(proposal.Draft.Key))
                throw new InvalidDataException("Duplicate semantic proposals are ambiguous.");
            if (session.Candidates.Any(existing => MatchesRetainedProposal(session, existing, proposal))) continue;
            if (proposals.Count >= session.MaxCandidates - session.Candidates.Count)
                throw new BudgetExceededException("CANDIDATE_COUNT_LIMIT", "Semantic proposals exceed the remaining package candidate budget. No partial batch was accepted.");
            proposals.Add(proposal);
        }
        return new(proposals, ValidateSemanticFamilies(root, proposals, session.AnalysisProfileVersion));
    }

    private static bool MatchesRetainedProposal(AnalysisSession session, CspPackageCandidateDraft existing,
        SemanticProposal proposal)
    {
        var draft = proposal.Draft;
        return existing.Kind == draft.Kind && existing.Name == draft.Name && existing.Description == draft.Description
            && existing.SourceId == draft.SourceId && existing.ComponentType == draft.ComponentType
            && existing.ControlId == draft.ControlId && existing.Responsibility == draft.Responsibility
            && existing.AuthorizationReference == draft.AuthorizationReference
            && JsonSerializer.Serialize(existing.Claim, ClaimJson) == JsonSerializer.Serialize(draft.Claim, ClaimJson)
            && proposal.SourceReferences.ToHashSet(StringComparer.Ordinal).SetEquals(
                session.SourceDependencies.GetValueOrDefault(existing.Key) ?? [])
            && draft.Citations.All(citation => existing.Citations.Any(retained =>
                retained.SegmentKey == citation.SegmentKey && retained.Quote.Contains(citation.Quote, StringComparison.Ordinal)));
    }

    private SemanticProposal ParseSemanticCandidate(JsonElement record,
        IReadOnlyDictionary<string, CspPackageSourceSegment> sources, CancellationToken cancellation)
    {
        SemanticProperties(record, ["kind", "name", "description", "sourceId", "componentType", "controlId",
            "responsibility", "dependencySourceIds", "authorizationReference", "citations", "claim"]);
        var kindText = SemanticString(record, "kind", 40, required: true)!;
        if (!Enum.GetNames<CspPackageCandidateKind>().Contains(kindText, StringComparer.Ordinal))
            throw new InvalidDataException("Unknown semantic candidate kind.");
        var kind = Enum.Parse<CspPackageCandidateKind>(kindText);
        var name = SemanticString(record, "name", IsClaimKind(kind) ? 2000 : 256, required: true)!;
        var description = SemanticString(record, "description", 2000, allowEmpty: true) ?? string.Empty;
        var sourceId = SemanticString(record, "sourceId", 256);
        var componentType = SemanticString(record, "componentType", 100);
        var control = SemanticString(record, "controlId", 50, required: kind == CspPackageCandidateKind.ControlMapping);
        var responsibility = SemanticString(record, "responsibility", 50, required: kind == CspPackageCandidateKind.Responsibility);
        var references = SemanticStrings(record, "dependencySourceIds", 128);
        if (!record.TryGetProperty("citations", out var elements) || elements.ValueKind != JsonValueKind.Array
            || elements.GetArrayLength() is < 1 || elements.GetArrayLength() > _limits.MaxSemanticSegmentsPerCall)
            throw new InvalidDataException("A bounded set of source citations is required.");
        var citations = new List<CspPackageSourceCitation>();
        foreach (var element in elements.EnumerateArray())
        {
            SemanticProperties(element, ["segmentKey", "quote"]);
            var key = SemanticString(element, "segmentKey", 256, required: true)!;
            if (!sources.TryGetValue(key, out var source))
                throw new InvalidDataException("Semantic citation does not match a supplied source.");
            var quote = element.TryGetProperty("quote", out _)
                ? SemanticString(element, "quote", _limits.MaxSemanticInputCharactersPerCall, required: true)!
                : source.Text;
            if (!source.Text.Contains(quote, StringComparison.Ordinal))
                throw new InvalidDataException("Semantic citation does not match a supplied source.");
            if (citations.Any(citation => citation.SegmentKey == source.Key && citation.Quote == quote))
                throw new InvalidDataException("Duplicate semantic citations are not allowed.");
            citations.Add(new(source.Key, source.EntryKey, source.ArtifactId, source.ArchivePath, source.Locator, quote));
        }
        void RequireSupport(string? value, string field = "authorizationReference")
        {
            if (!string.IsNullOrEmpty(value) && !citations.Any(citation => citation.Quote.Contains(value, StringComparison.Ordinal)))
                throw new InvalidDataException($"Semantic field '{field}' is not present in its supporting quotes.");
        }
        foreach (var (field, value) in new[]
        {
            ("name", name), ("description", description), ("sourceId", sourceId),
            ("componentType", componentType), ("controlId", control), ("responsibility", responsibility)
        })
            RequireSupport(value, field);
        foreach (var value in references) RequireSupport(value, "dependencySourceIds");

        if (IsClaimKind(kind))
        {
            if (componentType is not null || control is not null || responsibility is not null || references.Count != 0
                || record.TryGetProperty("authorizationReference", out var forbidden) && forbidden.ValueKind != JsonValueKind.Null
                || !record.TryGetProperty("claim", out var payload))
                throw new InvalidDataException("Claim proposals require a typed payload and cannot carry inventory dependencies.");
            var claim = payload.Deserialize<Ato.Copilot.Core.Models.PackageImports.CspPackageClaim>(ClaimJson)
                ?? throw new InvalidDataException("Null claim payload.");
            if (!payload.TryGetProperty("fieldSources", out _))
                claim = claim with { FieldSources = BindClaimFields(kind, claim, citations) };
            claim = ValidateClaim(kind, claim, citations);
            claim = PreserveSemanticQualifications(kind, claim, citations, sources, cancellation);
            var key = StableKey(citations[0].SegmentKey, kind.ToString(), sourceId ?? name, JsonSerializer.Serialize(claim, ClaimJson));
            var claimDraft = new CspPackageCandidateDraft(key, kind, name, description, sourceId,
                null, null, null, [], [], citations) { Claim = claim };
            return new(claimDraft, []);
        }
        if (record.TryGetProperty("claim", out var unexpectedClaim) && unexpectedClaim.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Inventory candidates cannot carry claim payloads.");
        CspPackageAuthorizationReferenceDraft? authorization = null;
        if (kind == CspPackageCandidateKind.AuthorizationReference)
        {
            if (componentType is not null || control is not null || responsibility is not null || references.Count > 0)
                throw new InvalidDataException("Authorization references cannot carry component claims or dependencies.");
            if (!record.TryGetProperty("authorizationReference", out var metadata))
                throw new InvalidDataException("Authorization reference metadata is required.");
            SemanticProperties(metadata, ["reference", "issuer", "issuedAt", "expiresAt"]);
            var reference = SemanticString(metadata, "reference", 2000, required: true)!;
            var issuer = SemanticString(metadata, "issuer", 500);
            var issuedText = SemanticString(metadata, "issuedAt", 33);
            var expiresText = SemanticString(metadata, "expiresAt", 33);
            foreach (var value in new[] { reference, issuer, issuedText, expiresText }) RequireSupport(value);
            var dates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (issuedText is not null) dates["issuedAt"] = issuedText;
            if (expiresText is not null) dates["expiresAt"] = expiresText;
            var issuedAt = AuthorizationDate(dates, "issuedAt");
            var expiresAt = AuthorizationDate(dates, "expiresAt");
            if (issuedAt.HasValue && expiresAt.HasValue && expiresAt < issuedAt)
                throw new InvalidDataException("Authorization expiry precedes its stated issue date.");
            authorization = new(reference, issuer, issuedAt, expiresAt);
        }
        else if (record.TryGetProperty("authorizationReference", out var metadata) && metadata.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Authorization metadata is only valid for its private candidate kind.");

        var orderedCitations = citations.OrderBy(citation => citation.SegmentKey, StringComparer.Ordinal)
            .ThenBy(citation => citation.Quote, StringComparer.Ordinal).ToArray();
        var orderedReferences = references.Order(StringComparer.Ordinal).ToArray();
        var draft = new CspPackageCandidateDraft(string.Empty, kind, name, description, sourceId,
            componentType, control, responsibility, [], [], orderedCitations) { AuthorizationReference = authorization };
        var stableKey = StableKey("semantic", JsonSerializer.Serialize(draft), JsonSerializer.Serialize(orderedReferences));
        return new(draft with { Key = stableKey }, orderedReferences);
    }

    private static void SemanticProperties(JsonElement element, IReadOnlyCollection<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object
            || element.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal)))
            throw new InvalidDataException("Unexpected semantic response fields.");
    }

    private static string? SemanticString(JsonElement element, string name, int limit,
        bool required = false, bool allowEmpty = false)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) throw new InvalidDataException($"Required semantic field '{name}' is missing.");
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Semantic field '{name}' must be a string.");
        var text = value.GetString()!;
        if (text.Length > limit || !allowEmpty && string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"Semantic field '{name}' exceeds its bounds or is empty.");
        return text;
    }

    private static IReadOnlyList<string> SemanticStrings(JsonElement record, string name, int maximum, bool required = false)
    {
        if (!record.TryGetProperty(name, out var values) || values.ValueKind == JsonValueKind.Null)
        {
            if (required) throw new InvalidDataException($"Required semantic array '{name}' is missing.");
            return [];
        }
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > maximum)
            throw new InvalidDataException($"Semantic array '{name}' exceeds its bounds.");
        var result = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 256 } text
                || string.IsNullOrWhiteSpace(text) || result.Contains(text, StringComparer.Ordinal))
                throw new InvalidDataException($"Semantic array '{name}' contains an invalid or duplicate value.");
            result.Add(text);
        }
        return result;
    }
}
