using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ────────────────────────────────────────────────────────────────────────────
// T038: CategorizeSystemTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_categorize_system — Perform FIPS 199 security categorization.
/// RBAC: Compliance.Administrator, Compliance.PlatformEngineer, ISSM
/// </summary>
public class CategorizeSystemTool : BaseTool
{
    private readonly ICategorizationService _categorizationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public CategorizeSystemTool(
        ICategorizationService categorizationService,
        ILogger<CategorizeSystemTool> logger) : base(logger)
    {
        _categorizationService = categorizationService;
    }

    public override string Name => "compliance_categorize_system";

    public override string Description =>
        "Perform or update FIPS 199 / SP 800-60 security categorization for a registered system. " +
        "Provide information types with C/I/A impact levels. Returns computed high-water mark, DoD IL, and NIST baseline.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["information_types"] = new() { Name = "information_types", Description = "Array of info types with sp800_60_id, name, confidentiality_impact, integrity_impact, availability_impact (Low|Moderate|High)", Type = "array", Required = true },
        ["is_national_security_system"] = new() { Name = "is_national_security_system", Description = "Whether the system is designated NSS (affects IL derivation)", Type = "boolean", Required = false },
        ["justification"] = new() { Name = "justification", Description = "Overall categorization rationale", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var justification = GetArg<string>(arguments, "justification");
        var isNss = GetArg<bool?>(arguments, "is_national_security_system") ?? false;

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        // Parse information_types from the argument
        var infoTypesRaw = GetArg<object>(arguments, "information_types");
        if (infoTypesRaw == null)
            return Error("INVALID_INPUT", "The 'information_types' parameter is required.");

        List<InformationTypeInput> infoTypes;
        try
        {
            infoTypes = ParseInformationTypes(infoTypesRaw);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("compliance_categorize_system: parse failed: {Error}", ex.Message);
            return Error("INVALID_INPUT", $"Failed to parse information_types: {ex.Message}");
        }

        Logger.LogInformation("compliance_categorize_system: parsed {Count} info types for system {SystemId}", infoTypes.Count, systemId);
        foreach (var it in infoTypes)
            Logger.LogInformation("  info_type: sp800_60_id={Id}, name={Name}, C={C}, I={I}, A={A}",
                it.Sp80060Id, it.Name, it.ConfidentialityImpact, it.IntegrityImpact, it.AvailabilityImpact);

        if (infoTypes.Count == 0)
            return Error("INVALID_INPUT", "At least one information type is required.");

        try
        {
            var result = await _categorizationService.CategorizeSystemAsync(
                systemId, infoTypes, "mcp-user", isNss, justification, cancellationToken);

            sw.Stop();
            var response = JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatCategorization(result),
                metadata = Meta(sw)
            }, JsonOpts);
            Logger.LogInformation("compliance_categorize_system: returning success response ({Len} chars)", response.Length);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning("compliance_categorize_system: InvalidOperationException: {Error}", ex.Message);
            return Error("CATEGORIZATION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_categorize_system failed for '{SystemId}'", systemId);
            return Error("CATEGORIZATION_FAILED", ex.Message);
        }
    }

    private static List<InformationTypeInput> ParseInformationTypes(object raw)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Handle JsonElement (from MCP JSON deserialization)
        if (raw is JsonElement jsonElement)
        {
            var rawText = jsonElement.GetRawText();

            // Strategy 1: Try direct deserialization of the entire element as List<InformationTypeInput>
            try
            {
                var directList = JsonSerializer.Deserialize<List<InformationTypeInput>>(rawText, opts);
                if (directList != null && directList.Count > 0 && directList.Any(x => !string.IsNullOrWhiteSpace(x.Sp80060Id)))
                    return directList;
            }
            catch { /* fall through */ }

            // Strategy 2: If it's a string, it might be a JSON-encoded array or object
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var str = jsonElement.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<InformationTypeInput>>(str, opts);
                        if (list != null && list.Count > 0 && list.Any(x => !string.IsNullOrWhiteSpace(x.Sp80060Id)))
                            return list;
                    }
                    catch { /* fall through */ }

                    // Try as a single object
                    try
                    {
                        var single = JsonSerializer.Deserialize<InformationTypeInput>(str, opts);
                        if (single != null && !string.IsNullOrWhiteSpace(single.Sp80060Id))
                            return [single];
                    }
                    catch { /* fall through */ }

                    // Try parsing as JsonDocument and extract
                    try
                    {
                        var inner = JsonDocument.Parse(str).RootElement;
                        if (inner.ValueKind == JsonValueKind.Array)
                            return ParseJsonArray(inner, opts);
                        if (inner.ValueKind == JsonValueKind.Object)
                            return ParseJsonArray(JsonDocument.Parse($"[{str}]").RootElement, opts);
                    }
                    catch { /* fall through */ }
                }
                throw new InvalidOperationException("information_types string could not be parsed as information type objects.");
            }

            // Strategy 3: Single object (not an array)
            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    var single = JsonSerializer.Deserialize<InformationTypeInput>(rawText, opts);
                    if (single != null && !string.IsNullOrWhiteSpace(single.Sp80060Id))
                        return [single];
                }
                catch { /* fall through */ }

                return [ParseSingleObject(jsonElement)];
            }

            // Strategy 4: Array — parse element by element with fallbacks
            if (jsonElement.ValueKind == JsonValueKind.Array)
                return ParseJsonArray(jsonElement, opts);

            throw new InvalidOperationException("information_types must be an array of info type objects.");
        }

        // Handle pre-deserialized List<InformationTypeInput>
        if (raw is IEnumerable<InformationTypeInput> typed)
            return typed.ToList();

        // Handle generic list/collection
        if (raw is System.Collections.IEnumerable enumerable)
        {
            var json = JsonSerializer.Serialize(raw);
            return JsonSerializer.Deserialize<List<InformationTypeInput>>(json, opts) ?? [];
        }

        throw new InvalidOperationException("information_types must be an array of info type objects.");
    }

    // Known property names for InformationTypeInput (used to detect flattened key-value arrays)
    private static readonly HashSet<string> KnownInfoTypeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "sp800_60_id", "sp80060id", "sp80060_id", "id",
        "name", "info_type_name",
        "category",
        "confidentiality_impact", "confidentialityimpact", "confidentiality",
        "integrity_impact", "integrityimpact", "integrity",
        "availability_impact", "availabilityimpact", "availability",
        "uses_provisional", "usesprovisional",
        "adjustment_justification", "adjustmentjustification"
    };

    /// <summary>Parse a JSON array of information type objects into InformationTypeInput list.</summary>
    private static List<InformationTypeInput> ParseJsonArray(JsonElement arrayElement, JsonSerializerOptions opts)
    {
        var result = new List<InformationTypeInput>();

        // Collect all elements to check if this is a flat key-value string array
        var elements = arrayElement.EnumerateArray().ToList();

        // Check if ALL elements are strings (suggesting a flattened key-value pattern)
        if (elements.Count > 0 && elements.All(e => e.ValueKind == JsonValueKind.String))
        {
            var strings = elements.Select(e => e.GetString() ?? "").ToList();

            // Count how many elements match known property names
            var keyMatchCount = strings.Count(s => KnownInfoTypeKeys.Contains(s));

            // If at least 3 known keys are present, this is a flattened key-value array
            if (keyMatchCount >= 3)
            {
                var reconstructed = ReconstructFromFlatKeyValueArray(strings);
                if (reconstructed != null)
                    return [reconstructed];
            }
        }

        foreach (var item in elements)
        {
            // If an array item is itself a string (JSON-encoded object), try to parse it
            if (item.ValueKind == JsonValueKind.String)
            {
                var itemStr = item.GetString();
                if (!string.IsNullOrWhiteSpace(itemStr))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<InformationTypeInput>(itemStr, opts);
                        if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Sp80060Id))
                        {
                            result.Add(parsed);
                            continue;
                        }
                    }
                    catch { /* not valid JSON, skip */ }
                }
                continue;
            }

            // Object items: try case-insensitive deserialization first
            if (item.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<InformationTypeInput>(item.GetRawText(), opts);
                    if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Sp80060Id))
                    {
                        result.Add(parsed);
                        continue;
                    }
                }
                catch { /* fall through to manual extraction */ }

                // Fallback: manual property extraction with name variations
                result.Add(ParseSingleObject(item));
            }
        }
        return result;
    }

    /// <summary>Reconstruct an InformationTypeInput from a flat array of alternating key-value strings.</summary>
    private static InformationTypeInput? ReconstructFromFlatKeyValueArray(List<string> strings)
    {
        // Build a dictionary from alternating key-value pairs
        // Skip elements that don't look like keys (junk like ":{")
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < strings.Count; i++)
        {
            var current = strings[i].Trim();

            // If this element matches a known key, the next non-key element is its value
            if (KnownInfoTypeKeys.Contains(current) && i + 1 < strings.Count)
            {
                var nextVal = strings[i + 1].Trim();
                // If the next element is ALSO a known key, this key has no value — skip it
                if (!KnownInfoTypeKeys.Contains(nextVal))
                {
                    dict[NormalizeKey(current)] = nextVal;
                    i++; // skip the value element
                }
                // else: key with no value, leave it empty
            }
            // else: junk element like ":{", skip it
        }

        if (dict.Count == 0)
            return null;

        return new InformationTypeInput
        {
            Sp80060Id = dict.GetValueOrDefault("sp800_60_id", ""),
            Name = dict.GetValueOrDefault("name", ""),
            Category = dict.GetValueOrDefault("category"),
            ConfidentialityImpact = dict.GetValueOrDefault("confidentiality_impact", "Low"),
            IntegrityImpact = dict.GetValueOrDefault("integrity_impact", "Low"),
            AvailabilityImpact = dict.GetValueOrDefault("availability_impact", "Low"),
            AdjustmentJustification = dict.GetValueOrDefault("adjustment_justification")
        };
    }

    /// <summary>Normalize property name variants to canonical snake_case form.</summary>
    private static string NormalizeKey(string key) => key.ToLowerInvariant() switch
    {
        "sp800_60_id" or "sp80060id" or "sp80060_id" or "id" => "sp800_60_id",
        "name" or "info_type_name" => "name",
        "category" => "category",
        "confidentiality_impact" or "confidentialityimpact" or "confidentiality" => "confidentiality_impact",
        "integrity_impact" or "integrityimpact" or "integrity" => "integrity_impact",
        "availability_impact" or "availabilityimpact" or "availability" => "availability_impact",
        "uses_provisional" or "usesprovisional" => "uses_provisional",
        "adjustment_justification" or "adjustmentjustification" => "adjustment_justification",
        _ => key.ToLowerInvariant()
    };

    /// <summary>Manually extract InformationTypeInput from a JsonElement object using various property name conventions.</summary>
    private static InformationTypeInput ParseSingleObject(JsonElement item) => new()
    {
        Sp80060Id = GetJsonProp(item, "sp800_60_id", "sp80060Id", "sp80060_id", "Sp80060Id", "SP800_60_ID", "id") ?? "",
        Name = GetJsonProp(item, "name", "Name", "info_type_name", "infoTypeName") ?? "",
        Category = GetJsonProp(item, "category", "Category"),
        ConfidentialityImpact = GetJsonProp(item, "confidentiality_impact", "confidentialityImpact", "ConfidentialityImpact", "confidentiality") ?? "Low",
        IntegrityImpact = GetJsonProp(item, "integrity_impact", "integrityImpact", "IntegrityImpact", "integrity") ?? "Low",
        AvailabilityImpact = GetJsonProp(item, "availability_impact", "availabilityImpact", "AvailabilityImpact", "availability") ?? "Low",
        UsesProvisional = item.TryGetProperty("uses_provisional", out var up) ? up.GetBoolean()
            : item.TryGetProperty("usesProvisional", out up) ? up.GetBoolean() : true,
        AdjustmentJustification = GetJsonProp(item, "adjustment_justification", "adjustmentJustification", "AdjustmentJustification")
    };

    /// <summary>Try multiple property name variations and return the first match.</summary>
    private static string? GetJsonProp(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
                return prop.GetString();
        }
        return null;
    }

    private static object FormatCategorization(SecurityCategorization sc) => new
    {
        id = sc.Id,
        system_id = sc.RegisteredSystemId,
        system_name = sc.RegisteredSystem?.Name,
        confidentiality_impact = sc.ConfidentialityImpact.ToString(),
        integrity_impact = sc.IntegrityImpact.ToString(),
        availability_impact = sc.AvailabilityImpact.ToString(),
        overall_categorization = sc.OverallCategorization.ToString(),
        fips_199_notation = sc.FormalNotation,
        dod_impact_level = sc.DoDImpactLevel,
        nist_baseline = sc.NistBaseline,
        is_national_security_system = sc.IsNationalSecuritySystem,
        information_type_count = sc.InformationTypes.Count,
        information_types = sc.InformationTypes.Select(it => new
        {
            id = it.Id,
            sp800_60_id = it.Sp80060Id,
            name = it.Name,
            category = it.Category,
            confidentiality = it.ConfidentialityImpact.ToString(),
            integrity = it.IntegrityImpact.ToString(),
            availability = it.AvailabilityImpact.ToString(),
            uses_provisional = it.UsesProvisionalImpactLevels,
            adjustment_justification = it.AdjustmentJustification
        }).ToList(),
        justification = sc.Justification,
        categorized_by = sc.CategorizedBy,
        categorized_at = sc.CategorizedAt.ToString("O")
    };

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        execution_time_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// T039: GetCategorizationTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_get_categorization — Retrieve security categorization for a system.
/// </summary>
public class GetCategorizationTool : BaseTool
{
    private readonly ICategorizationService _categorizationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GetCategorizationTool(
        ICategorizationService categorizationService,
        ILogger<GetCategorizationTool> logger) : base(logger)
    {
        _categorizationService = categorizationService;
    }

    public override string Name => "compliance_get_categorization";

    public override string Description =>
        "Retrieve the FIPS 199 security categorization for a registered system, including all information types and computed fields.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var categorization = await _categorizationService.GetCategorizationAsync(
                systemId, cancellationToken);

            sw.Stop();

            if (categorization == null)
            {
                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    data = (object?)null,
                    message = $"No categorization found for system '{systemId}'.",
                    metadata = Meta(sw)
                }, JsonOpts);
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    id = categorization.Id,
                    system_id = categorization.RegisteredSystemId,
                    system_name = categorization.RegisteredSystem?.Name,
                    confidentiality_impact = categorization.ConfidentialityImpact.ToString(),
                    integrity_impact = categorization.IntegrityImpact.ToString(),
                    availability_impact = categorization.AvailabilityImpact.ToString(),
                    overall_categorization = categorization.OverallCategorization.ToString(),
                    fips_199_notation = categorization.FormalNotation,
                    dod_impact_level = categorization.DoDImpactLevel,
                    nist_baseline = categorization.NistBaseline,
                    is_national_security_system = categorization.IsNationalSecuritySystem,
                    information_type_count = categorization.InformationTypes.Count,
                    information_types = categorization.InformationTypes.Select(it => new
                    {
                        id = it.Id,
                        sp800_60_id = it.Sp80060Id,
                        name = it.Name,
                        category = it.Category,
                        confidentiality = it.ConfidentialityImpact.ToString(),
                        integrity = it.IntegrityImpact.ToString(),
                        availability = it.AvailabilityImpact.ToString(),
                        uses_provisional = it.UsesProvisionalImpactLevels,
                        adjustment_justification = it.AdjustmentJustification
                    }).ToList(),
                    justification = categorization.Justification,
                    categorized_by = categorization.CategorizedBy,
                    categorized_at = categorization.CategorizedAt.ToString("O")
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_get_categorization failed for '{SystemId}'", systemId);
            return Error("RETRIEVAL_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        execution_time_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}

// ────────────────────────────────────────────────────────────────────────────
// T040: SuggestInfoTypesTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_suggest_info_types — AI-assisted SP 800-60 info type suggestions.
/// </summary>
public class SuggestInfoTypesTool : BaseTool
{
    private readonly ICategorizationService _categorizationService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SuggestInfoTypesTool(
        ICategorizationService categorizationService,
        ILogger<SuggestInfoTypesTool> logger) : base(logger)
    {
        _categorizationService = categorizationService;
    }

    public override string Name => "compliance_suggest_info_types";

    public override string Description =>
        "Suggest SP 800-60 information types based on system description and type. Returns ranked list with confidence scores.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["description"] = new() { Name = "description", Description = "Additional context for better suggestions", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var description = GetArg<string>(arguments, "description");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var suggestions = await _categorizationService.SuggestInfoTypesAsync(
                systemId, description, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = systemId,
                    suggestion_count = suggestions.Count,
                    suggestions = suggestions.Select(s => new
                    {
                        sp800_60_id = s.Sp80060Id,
                        name = s.Name,
                        category = s.Category,
                        confidence = Math.Round(s.Confidence, 2),
                        rationale = s.Rationale,
                        default_confidentiality = s.DefaultConfidentialityImpact,
                        default_integrity = s.DefaultIntegrityImpact,
                        default_availability = s.DefaultAvailabilityImpact
                    }).ToList()
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("SUGGESTION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_suggest_info_types failed for '{SystemId}'", systemId);
            return Error("SUGGESTION_FAILED", ex.Message);
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { status = "error", errorCode = code, message }, JsonOpts);

    private object Meta(Stopwatch sw) => new
    {
        tool = Name,
        execution_time_ms = sw.ElapsedMilliseconds,
        timestamp = DateTime.UtcNow.ToString("O")
    };
}
