using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ato.Copilot.Core.Interfaces.PackageImports;

namespace Ato.Copilot.Agents.Services.PackageImports;

public sealed partial class CspPackageAnalyzer
{
    private static readonly HashSet<string> AuthorizationFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "kind", "name", "title", "description", "id", "uuid",
        "reference", "issuer", "issuedAt", "expiresAt"
    };

    private static void NormalizeAuthorizationJsonFields(JsonElement element, Dictionary<string, string> fields)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!AuthorizationFields.Contains(property.Name)) continue;
            if (property.Value.ValueKind == JsonValueKind.Null)
                fields.Remove(property.Name);
            else if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Authorization reference field '{property.Name}' must be a string or null.");
        }
    }

    private static bool AuthorizationInventoryOnly(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().All(AuthorizationInventoryOnly),
        JsonValueKind.Object => element.EnumerateObject().Any(property =>
                string.Equals(property.Name, "reference", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            && element.EnumerateObject().All(property => AuthorizationFields.Contains(property.Name)),
        _ => false
    };

    private static CspPackageCandidateDraft EmitAuthorizationReference(AnalysisSession session,
        CspPackageSourceSegment segment, IReadOnlyDictionary<string, string> fields)
    {
        if (Get(fields, "kind") is { } kind && ParseKind(kind) != CspPackageCandidateKind.AuthorizationReference)
            throw new InvalidDataException("Conflicting authorization reference kind requires source correction.");
        var reference = Get(fields, "reference");
        if (reference is null || reference.Length > 2000)
            throw new InvalidDataException("Authorization reference requires a stated reference of 1-2000 characters.");
        if (fields.TryGetValue("issuer", out var statedIssuer) && statedIssuer.Length > 500)
            throw new InvalidDataException("Authorization reference issuer exceeds 500 characters.");
        var issuer = Get(fields, "issuer");
        var issuedAt = AuthorizationDate(fields, "issuedAt");
        var expiresAt = AuthorizationDate(fields, "expiresAt");
        if (issuedAt.HasValue && expiresAt.HasValue && expiresAt.Value < issuedAt.Value)
            throw new InvalidDataException("Authorization reference expiresAt cannot be earlier than issuedAt.");
        var draft = AddDraft(session, segment, CspPackageCandidateKind.AuthorizationReference,
            Get(fields, "name", "title") ?? reference, Get(fields, "description") ?? string.Empty,
            Get(fields, "id", "uuid"), null, null, null, []) with
        {
            AuthorizationReference = new(reference, issuer, issuedAt, expiresAt)
        };
        session.Candidates[^1] = draft;
        return draft;
    }

    private static DateTimeOffset? AuthorizationDate(IReadOnlyDictionary<string, string> fields, string name)
    {
        var value = Get(fields, name);
        if (value is null) return null;
        const string isoDate = @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}(?:T[0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,7})?)?(?:Z|[+-][0-9]{2}:[0-9]{2}))?\z";
        if (value.Length > 33
            || !Regex.IsMatch(value, isoDate, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(100))
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            throw new InvalidDataException($"Authorization reference {name} must be an ISO-8601 date or date/time with a stated offset.");
        return date;
    }
}
