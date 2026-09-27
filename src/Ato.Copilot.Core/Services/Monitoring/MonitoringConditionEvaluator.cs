using System.Text.Json;
using System.Globalization;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services.Monitoring;

public sealed record MonitoringCondition(string Field, string Operator, string Value);

public static class MonitoringConditionEvaluator
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static MonitoringCondition Parse(string json)
    {
        var c = JsonSerializer.Deserialize<MonitoringCondition>(json, Json)
            ?? throw new ArgumentException("A condition is required.");
        if (string.IsNullOrWhiteSpace(c.Field) || c.Field.Length > 200 || c.Value is null || c.Value.Length > 2000 ||
            !(c.Field is "Severity" or "Type" or "ControlId" or "ControlFamily" || c.Field.StartsWith("Change.")) ||
            !(c.Operator is "Equals" or "NotEquals" or "Becomes" or "LessThanOrEqual" or "GreaterThanOrEqual") ||
            (c.Operator == "Becomes" && !c.Field.StartsWith("Change.")))
            throw new ArgumentException("Use Severity, Type, ControlId, ControlFamily or Change.<property> with Equals, NotEquals, Becomes, LessThanOrEqual or GreaterThanOrEqual.");
        return c;
    }

    public static bool Matches(string? json, ComplianceAlert alert)
    {
        if (string.IsNullOrWhiteSpace(json)) return true; // Legacy filter-only rules.
        try
        {
            var c = Parse(json);
            string? before = null;
            string? actual = c.Field switch
            {
                "Severity" => alert.Severity.ToString(),
                "Type" => alert.Type.ToString(),
                "ControlId" => alert.ControlId,
                "ControlFamily" => alert.ControlFamily,
                _ => null
            };
            if (c.Field.StartsWith("Change."))
            {
                using var details = JsonDocument.Parse(alert.ChangeDetails ?? "{}");
                var values = details.RootElement.ValueKind == JsonValueKind.Array
                    ? details.RootElement.EnumerateArray().ToArray() : new[] { details.RootElement };
                foreach (var value in values)
                {
                    if (value.ValueKind != JsonValueKind.Object ||
                        !value.TryGetProperty("property", out var property) ||
                        !string.Equals(property.GetString(), c.Field[7..], StringComparison.OrdinalIgnoreCase)) continue;
                    actual = value.TryGetProperty("newValue", out var n) ? n.ToString() : null;
                    before = value.TryGetProperty("oldValue", out var o) ? o.ToString() : null;
                    break;
                }
                // The existing drift engine retains baseline/current snapshots rather than
                // property events. Evaluate the selected control, never an unrelated row.
                if (actual is null && details.RootElement.ValueKind == JsonValueKind.Object &&
                    details.RootElement.TryGetProperty("baseline", out var baseline) &&
                    details.RootElement.TryGetProperty("current", out var current))
                {
                    before = SnapshotValue(baseline, c.Field[7..], alert.ControlId);
                    actual = SnapshotValue(current, c.Field[7..], alert.ControlId);
                }
            }
            if (actual is null) return false; // Missing data is not a negative match.
            var equals = string.Equals(actual, c.Value, StringComparison.OrdinalIgnoreCase);
            return c.Operator switch
            {
                "Equals" => equals,
                "NotEquals" => !equals,
                "Becomes" => equals && before is not null && !string.Equals(before, actual, StringComparison.OrdinalIgnoreCase),
                "LessThanOrEqual" => decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var lower) &&
                    decimal.TryParse(c.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var ceiling) && lower <= ceiling,
                "GreaterThanOrEqual" => decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var upper) &&
                    decimal.TryParse(c.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var floor) && upper >= floor,
                _ => false
            };
        }
        catch (JsonException) { return false; } // Invalid legacy expressions must never match.
        catch (ArgumentException) { return false; }
    }

    private static string? SnapshotValue(JsonElement snapshot, string field, string? controlId)
    {
        using var parsed = JsonDocument.Parse(snapshot.ValueKind == JsonValueKind.String ? snapshot.GetString()! : snapshot.GetRawText());
        var rows = parsed.RootElement.ValueKind == JsonValueKind.Array
            ? parsed.RootElement.EnumerateArray().ToArray() : new[] { parsed.RootElement };
        var selected = rows.Where(row => row.ValueKind == JsonValueKind.Object &&
            (rows.Length == 1 || (controlId != null && row.TryGetProperty("ControlId", out var id) && id.GetString() == controlId))).ToList();
        if (selected.Count != 1) return null;
        return selected[0].TryGetProperty(field, out var value) ? value.ToString() : null;
    }
}
