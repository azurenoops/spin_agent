using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

internal static class NarrativeReferencePublicationPayloadBuilder
{
    public static (string Payload, string Revision) Build(
        Guid id, Guid? previousId, Guid referenceKey, int version, string scope, string scopeId,
        string sourceSha256, IReadOnlyList<NarrativeReferencePassage> passages, string? previousPassagesJson)
    {
        var previous = previousPassagesJson is null ? [] :
            JsonSerializer.Deserialize<List<NarrativeReferencePassage>>(previousPassagesJson)
                ?? throw new InvalidDataException("Published reference mappings are invalid.");
        var combined = passages.Concat(previous).ToArray();
        if (combined.Any(item => string.IsNullOrWhiteSpace(item.ControlId) || item.NarrativeType is not ("Policy" or "Technical")))
            throw new InvalidDataException("Published reference mappings are invalid.");
        var targets = combined.Select(item => new NarrativeReferenceImpactTarget(item.ControlId!.Trim().ToUpperInvariant(), item.NarrativeType!))
            .Distinct().OrderBy(item => item.ControlId, StringComparer.Ordinal).ThenBy(item => item.NarrativeType, StringComparer.Ordinal).ToArray();
        var payload = JsonSerializer.Serialize(new NarrativeReferencePublicationPayload(
            id, previousId, referenceKey, version, scope, scopeId, sourceSha256, targets));
        return (payload, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
    }
}
