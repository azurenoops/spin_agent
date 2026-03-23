using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Tools;

// ────────────────────────────────────────────────────────────────────────────
// T048: SelectBaselineTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_select_baseline — Select NIST 800-53 baseline from FIPS 199 categorization.
/// RBAC: Compliance.Administrator, ISSM
/// </summary>
public class SelectBaselineTool : BaseTool
{
    private readonly IBaselineService _baselineService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SelectBaselineTool(
        IBaselineService baselineService,
        ILogger<SelectBaselineTool> logger) : base(logger)
    {
        _baselineService = baselineService;
    }

    public override string Name => "compliance_select_baseline";

    public override string Description =>
        "Select the NIST 800-53 control baseline for a system based on its FIPS 199 categorization. " +
        "Optionally applies a CNSSI 1253 overlay matching the DoD Impact Level. " +
        "Prerequisite: System must have a security categorization.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["apply_overlay"] = new() { Name = "apply_overlay", Description = "Whether to apply the CNSSI 1253 overlay (default: true)", Type = "boolean", Required = false },
        ["overlay_name"] = new() { Name = "overlay_name", Description = "Override overlay name (e.g., 'CNSSI 1253 IL5')", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var applyOverlay = GetArg<bool?>(arguments, "apply_overlay") ?? true;
        var overlayName = GetArg<string>(arguments, "overlay_name");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var baseline = await _baselineService.SelectBaselineAsync(
                systemId, applyOverlay, overlayName, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = FormatBaseline(baseline),
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("BASELINE_SELECTION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_select_baseline failed for '{SystemId}'", systemId);
            return Error("BASELINE_SELECTION_FAILED", ex.Message);
        }
    }

    private static object FormatBaseline(ControlBaseline b) => new
    {
        id = b.Id,
        system_id = b.RegisteredSystemId,
        baseline_level = b.BaselineLevel,
        overlay_applied = b.OverlayApplied,
        total_controls = b.TotalControls,
        customer_controls = b.CustomerControls,
        inherited_controls = b.InheritedControls,
        shared_controls = b.SharedControls,
        tailored_out_controls = b.TailoredOutControls,
        tailored_in_controls = b.TailoredInControls,
        control_ids = b.ControlIds,
        created_by = b.CreatedBy,
        created_at = b.CreatedAt.ToString("O")
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
// T049: TailorBaselineTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_tailor_baseline — Add or remove controls from a baseline.
/// RBAC: Compliance.Administrator, ISSM
/// </summary>
public class TailorBaselineTool : BaseTool
{
    private readonly IBaselineService _baselineService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TailorBaselineTool(
        IBaselineService baselineService,
        ILogger<TailorBaselineTool> logger) : base(logger)
    {
        _baselineService = baselineService;
    }

    public override string Name => "compliance_tailor_baseline";

    public override string Description =>
        "Tailor the NIST 800-53 baseline by adding or removing controls with documented rationale. " +
        "Warning is issued if removing overlay-required controls.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["tailoring_actions"] = new() { Name = "tailoring_actions", Description = "Array of {control_id, action ('Added'|'Removed'), rationale}", Type = "array", Required = true }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        var actionsRaw = GetArg<object>(arguments, "tailoring_actions");

        // DEBUG: capture all arguments
        try { File.AppendAllText("/tmp/tailor_debug.txt",
            "\n\n--- ALL ARGS ---\n" + string.Join("\n", arguments.Select(kv =>
                kv.Key + " = " + (kv.Value is JsonElement je ? je.GetRawText() : kv.Value?.ToString() ?? "null")))); } catch { }

        if (actionsRaw == null)
            return Error("INVALID_INPUT", "The 'tailoring_actions' parameter is required.");

        List<TailoringInput> tailoringActions;
        try
        {
            tailoringActions = ParseTailoringActions(actionsRaw);
        }
        catch (Exception ex)
        {
            return Error("INVALID_INPUT", $"Failed to parse tailoring_actions: {ex.Message}");
        }

        if (tailoringActions.Count == 0)
            return Error("INVALID_INPUT", "At least one tailoring action is required.");

        try
        {
            var result = await _baselineService.TailorBaselineAsync(
                systemId, tailoringActions, "mcp-user", cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    baseline_level = result.Baseline.BaselineLevel,
                    total_controls = result.Baseline.TotalControls,
                    tailored_in = result.Baseline.TailoredInControls,
                    tailored_out = result.Baseline.TailoredOutControls,
                    accepted_count = result.Accepted.Count,
                    rejected_count = result.Rejected.Count,
                    accepted = result.Accepted.Select(a => new
                    {
                        control_id = a.ControlId,
                        action = a.Action,
                        accepted = a.Accepted,
                        reason = a.Reason
                    }),
                    rejected = result.Rejected.Select(r => new
                    {
                        control_id = r.ControlId,
                        action = r.Action,
                        accepted = r.Accepted,
                        reason = r.Reason
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("TAILORING_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_tailor_baseline failed for '{SystemId}'", systemId);
            return Error("TAILORING_FAILED", ex.Message);
        }
    }

    private static readonly JsonSerializerOptions CaseInsensitiveOpts = new() { PropertyNameCaseInsensitive = true };

    // Known property names for tailoring actions
    private static readonly HashSet<string> TailoringKeys = new(StringComparer.OrdinalIgnoreCase)
    { "control_id", "controlid", "action", "rationale" };

    private static List<TailoringInput> ParseTailoringActions(object raw)
    {
        if (raw is JsonElement jsonElement)
        {
            var rawText = jsonElement.GetRawText();

            // DEBUG: capture raw value for diagnosis
            try { File.WriteAllText("/tmp/tailor_debug.txt",
                "type=" + raw.GetType().FullName +
                "\nkind=" + jsonElement.ValueKind +
                "\nlength=" + rawText.Length +
                "\nvalue=" + rawText); } catch { }

            // Strategy 1: Direct deserialization of entire element
            try
            {
                var list = JsonSerializer.Deserialize<List<TailoringInput>>(rawText, CaseInsensitiveOpts);
                if (list != null && list.Count > 0 && list.Any(x => !string.IsNullOrWhiteSpace(x.ControlId)))
                    return list;
            }
            catch { /* fall through */ }

            // Strategy 2: String-encoded JSON
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var str = jsonElement.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<TailoringInput>>(str, CaseInsensitiveOpts);
                        if (list != null && list.Count > 0) return list;
                    }
                    catch { /* fall through */ }

                    try
                    {
                        var single = JsonSerializer.Deserialize<TailoringInput>(str, CaseInsensitiveOpts);
                        if (single != null && !string.IsNullOrWhiteSpace(single.ControlId)) return [single];
                    }
                    catch { /* fall through */ }
                }
            }

            // Strategy 3: Single object
            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                try
                {
                    var single = JsonSerializer.Deserialize<TailoringInput>(rawText, CaseInsensitiveOpts);
                    if (single != null && !string.IsNullOrWhiteSpace(single.ControlId)) return [single];
                }
                catch { /* fall through */ }
                return [ParseSingleTailoring(jsonElement)];
            }

            // Strategy 4: Array — parse element by element
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                var elements = jsonElement.EnumerateArray().ToList();

                // Check for flat key-value string array
                if (elements.Count > 0 && elements.All(e => e.ValueKind == JsonValueKind.String))
                {
                    var strings = elements.Select(e => e.GetString() ?? "").ToList();
                    var keyCount = strings.Count(s => TailoringKeys.Contains(s));
                    if (keyCount >= 2)
                    {
                        var reconstructed = ReconstructTailoring(strings);
                        if (reconstructed != null) return [reconstructed];
                    }
                }

                var result = new List<TailoringInput>();
                foreach (var item in elements)
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        try
                        {
                            var parsed = JsonSerializer.Deserialize<TailoringInput>(item.GetRawText(), CaseInsensitiveOpts);
                            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.ControlId))
                            { result.Add(parsed); continue; }
                        }
                        catch { /* fall through */ }
                        result.Add(ParseSingleTailoring(item));
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            try
                            {
                                var parsed = JsonSerializer.Deserialize<TailoringInput>(s, CaseInsensitiveOpts);
                                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.ControlId))
                                    result.Add(parsed);
                            }
                            catch { /* skip invalid strings */ }
                        }
                    }
                }
                return result;
            }
        }

        if (raw is IEnumerable<TailoringInput> typed)
            return typed.ToList();

        var json = JsonSerializer.Serialize(raw);
        return JsonSerializer.Deserialize<List<TailoringInput>>(json, CaseInsensitiveOpts) ?? [];
    }

    private static TailoringInput ParseSingleTailoring(JsonElement item)
    {
        string? Get(params string[] names) { foreach (var n in names) if (item.TryGetProperty(n, out var v)) return v.GetString(); return null; }
        return new TailoringInput
        {
            ControlId = Get("control_id", "controlId", "ControlId") ?? "",
            Action = Get("action", "Action") ?? "",
            Rationale = Get("rationale", "Rationale") ?? ""
        };
    }

    private static TailoringInput? ReconstructTailoring(List<string> strings)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < strings.Count; i++)
        {
            var key = strings[i].Trim();
            if (TailoringKeys.Contains(key) && i + 1 < strings.Count)
            {
                var val = strings[i + 1].Trim();
                if (!TailoringKeys.Contains(val)) { dict[key.ToLowerInvariant()] = val; i++; }
            }
        }
        if (dict.Count == 0) return null;
        return new TailoringInput
        {
            ControlId = dict.GetValueOrDefault("control_id", dict.GetValueOrDefault("controlid", "")),
            Action = dict.GetValueOrDefault("action", ""),
            Rationale = dict.GetValueOrDefault("rationale", "")
        };
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
// T050: SetInheritanceTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_set_inheritance — Set control inheritance designations.
/// RBAC: Compliance.Administrator, ISSM
/// </summary>
public class SetInheritanceTool : BaseTool
{
    private readonly IBaselineService _baselineService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions CaseInsensitiveOpts = new() { PropertyNameCaseInsensitive = true };

    public SetInheritanceTool(
        IBaselineService baselineService,
        ILogger<SetInheritanceTool> logger) : base(logger)
    {
        _baselineService = baselineService;
    }

    public override string Name => "compliance_set_inheritance";

    public override string Description =>
        "Set control inheritance type (Inherited/Shared/Customer) for controls in the baseline. " +
        "Tracks provider and customer responsibility for shared controls.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["inheritance_mappings"] = new() { Name = "inheritance_mappings", Description = "Array of {control_id, inheritance_type ('Inherited'|'Shared'|'Customer'), provider?, customer_responsibility?} OR a simple array of control ID strings (e.g. [\"AC-1\",\"AC-2\"])", Type = "array", Required = true },
        ["inheritance_type"] = new() { Name = "inheritance_type", Description = "Default inheritance type when inheritance_mappings is a simple control ID array: 'Inherited', 'Shared', or 'Customer'. Defaults to 'Inherited'.", Type = "string", Required = false },
        ["provider"] = new() { Name = "provider", Description = "Default provider name when inheritance_mappings is a simple control ID array (e.g. 'Azure Government FedRAMP High')", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        var mappingsRaw = GetArg<object>(arguments, "inheritance_mappings");

        // DEBUG: capture all arguments
        try { File.WriteAllText("/tmp/inherit_debug.txt",
            "--- ALL ARGS ---\n" + string.Join("\n", arguments.Select(kv =>
                kv.Key + " = " + (kv.Value is JsonElement je ? je.GetRawText() : kv.Value?.ToString() ?? "null")))); } catch { }

        if (mappingsRaw == null)
            return Error("INVALID_INPUT", "The 'inheritance_mappings' parameter is required.");

        var defaultType = GetArg<string>(arguments, "inheritance_type") ?? "Inherited";
        var defaultProvider = GetArg<string>(arguments, "provider");

        List<InheritanceInput> mappings;
        try
        {
            mappings = ParseInheritanceMappings(mappingsRaw, defaultType, defaultProvider);
            // DEBUG: capture parsed result
            try { File.AppendAllText("/tmp/inherit_debug.txt",
                "\n\n--- PARSED ---\ncount=" + mappings.Count +
                "\nitems=" + string.Join("; ", mappings.Select(m =>
                    $"[{m.ControlId}|{m.InheritanceType}|{m.Provider}]"))); } catch { }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText("/tmp/inherit_debug.txt", "\n\n--- EXCEPTION ---\n" + ex); } catch { }
            return Error("INVALID_INPUT", $"Failed to parse inheritance_mappings: {ex.Message}");
        }

        if (mappings.Count == 0)
            return Error("INVALID_INPUT", "At least one inheritance mapping is required.");

        try
        {
            var result = await _baselineService.SetInheritanceAsync(
                systemId, mappings, "mcp-user", cancellationToken: cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    controls_updated = result.ControlsUpdated,
                    inherited_count = result.InheritedCount,
                    shared_count = result.SharedCount,
                    customer_count = result.CustomerCount,
                    skipped_controls = result.SkippedControls,
                    baseline_total_controls = result.Baseline.TotalControls
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("INHERITANCE_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_set_inheritance failed for '{SystemId}'", systemId);
            return Error("INHERITANCE_FAILED", ex.Message);
        }
    }

    // Known property names for inheritance mappings
    private static readonly HashSet<string> InheritanceKeys = new(StringComparer.OrdinalIgnoreCase)
    { "control_id", "controlid", "inheritance_type", "inheritancetype", "provider", "customer_responsibility", "customerresponsibility" };

    private static List<InheritanceInput> ParseInheritanceMappings(object raw, string defaultType = "Inherited", string? defaultProvider = null)
    {
        if (raw is JsonElement jsonElement)
        {
            var rawText = jsonElement.GetRawText();

            // DEBUG: capture raw value
            try { File.AppendAllText("/tmp/inherit_debug.txt",
                "\n\n--- PARSE ---\ntype=" + raw.GetType().FullName +
                "\nkind=" + jsonElement.ValueKind +
                "\nlength=" + rawText.Length +
                "\nvalue=" + rawText); } catch { }

            // Strategy 1: Direct deserialization
            try
            {
                var list = JsonSerializer.Deserialize<List<InheritanceInput>>(rawText, CaseInsensitiveOpts);
                if (list != null && list.Count > 0 && list.Any(x => !string.IsNullOrWhiteSpace(x.ControlId)))
                    return list;
            }
            catch { /* fall through */ }

            // Strategy 2: String-encoded JSON
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var str = jsonElement.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    try { var list = JsonSerializer.Deserialize<List<InheritanceInput>>(str, CaseInsensitiveOpts); if (list != null && list.Count > 0) return list; } catch { }
                    try { var single = JsonSerializer.Deserialize<InheritanceInput>(str, CaseInsensitiveOpts); if (single != null && !string.IsNullOrWhiteSpace(single.ControlId)) return [single]; } catch { }
                }
            }

            // Strategy 3: Single object
            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                try { var single = JsonSerializer.Deserialize<InheritanceInput>(rawText, CaseInsensitiveOpts); if (single != null && !string.IsNullOrWhiteSpace(single.ControlId)) return [single]; } catch { }
                return [ParseSingleInheritance(jsonElement)];
            }

            // Strategy 4: Array
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                var elements = jsonElement.EnumerateArray().ToList();

                // Check for flat key-value string array
                if (elements.Count > 0 && elements.All(e => e.ValueKind == JsonValueKind.String))
                {
                    var strings = elements.Select(e => e.GetString() ?? "").ToList();
                    var keyCount = strings.Count(s => InheritanceKeys.Contains(s));
                    if (keyCount >= 2)
                    {
                        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < strings.Count; i++)
                        {
                            var key = strings[i].Trim();
                            if (InheritanceKeys.Contains(key) && i + 1 < strings.Count)
                            {
                                var val = strings[i + 1].Trim();
                                if (!InheritanceKeys.Contains(val)) { dict[key.ToLowerInvariant()] = val; i++; }
                            }
                        }
                        if (dict.Count > 0)
                            return [new InheritanceInput
                            {
                                ControlId = dict.GetValueOrDefault("control_id", dict.GetValueOrDefault("controlid", "")),
                                InheritanceType = dict.GetValueOrDefault("inheritance_type", dict.GetValueOrDefault("inheritancetype", "")),
                                Provider = dict.GetValueOrDefault("provider"),
                                CustomerResponsibility = dict.GetValueOrDefault("customer_responsibility", dict.GetValueOrDefault("customerresponsibility"))
                            }];
                    }

                    // Flat array of control ID strings (e.g. ["AC-1", "AC-2", "AC-3"])
                    // Convert each to an InheritanceInput using defaults
                    if (strings.Any(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Z]{2}-\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                    {
                        return strings
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => new InheritanceInput
                            {
                                ControlId = s.Trim(),
                                InheritanceType = defaultType,
                                Provider = defaultProvider
                            })
                            .ToList();
                    }
                }

                var result = new List<InheritanceInput>();
                foreach (var item in elements)
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        try { var parsed = JsonSerializer.Deserialize<InheritanceInput>(item.GetRawText(), CaseInsensitiveOpts); if (parsed != null && !string.IsNullOrWhiteSpace(parsed.ControlId)) { result.Add(parsed); continue; } } catch { }
                        result.Add(ParseSingleInheritance(item));
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            try { var parsed = JsonSerializer.Deserialize<InheritanceInput>(s, CaseInsensitiveOpts); if (parsed != null && !string.IsNullOrWhiteSpace(parsed.ControlId)) { result.Add(parsed); continue; } } catch { }
                            // Treat as bare control ID string
                            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Z]{2}-\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            {
                                result.Add(new InheritanceInput
                                {
                                    ControlId = s.Trim(),
                                    InheritanceType = defaultType,
                                    Provider = defaultProvider
                                });
                            }
                        }
                    }
                }
                return result;
            }
        }

        if (raw is IEnumerable<InheritanceInput> typed)
            return typed.ToList();

        var json = JsonSerializer.Serialize(raw);
        return JsonSerializer.Deserialize<List<InheritanceInput>>(json, CaseInsensitiveOpts) ?? [];
    }

    private static InheritanceInput ParseSingleInheritance(JsonElement item)
    {
        string? Get(params string[] names) { foreach (var n in names) if (item.TryGetProperty(n, out var v)) return v.GetString(); return null; }
        return new InheritanceInput
        {
            ControlId = Get("control_id", "controlId", "ControlId") ?? "",
            InheritanceType = Get("inheritance_type", "inheritanceType", "InheritanceType") ?? "",
            Provider = Get("provider", "Provider"),
            CustomerResponsibility = Get("customer_responsibility", "customerResponsibility", "CustomerResponsibility")
        };
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
// T051: GetBaselineTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_get_baseline — Retrieve baseline with optional details and family filter.
/// </summary>
public class GetBaselineTool : BaseTool
{
    private readonly IBaselineService _baselineService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GetBaselineTool(
        IBaselineService baselineService,
        ILogger<GetBaselineTool> logger) : base(logger)
    {
        _baselineService = baselineService;
    }

    public override string Name => "compliance_get_baseline";

    public override string Description =>
        "Retrieve the NIST 800-53 control baseline for a system. " +
        "Optionally includes tailoring and inheritance details, and supports family filtering.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["system_id"] = new() { Name = "system_id", Description = "System GUID, name, or acronym", Type = "string", Required = true },
        ["include_details"] = new() { Name = "include_details", Description = "Include tailoring and inheritance records (default: false)", Type = "boolean", Required = false },
        ["family_filter"] = new() { Name = "family_filter", Description = "Filter by control family prefix (e.g., 'AC', 'SI')", Type = "string", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var systemId = GetArg<string>(arguments, "system_id");
        var includeDetails = GetArg<bool?>(arguments, "include_details") ?? false;
        var familyFilter = GetArg<string>(arguments, "family_filter");

        if (string.IsNullOrWhiteSpace(systemId))
            return Error("INVALID_INPUT", "The 'system_id' parameter is required.");

        try
        {
            var baseline = await _baselineService.GetBaselineAsync(
                systemId, includeDetails, familyFilter, cancellationToken);

            sw.Stop();

            if (baseline == null)
            {
                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    data = (object?)null,
                    message = $"No baseline found for system '{systemId}'.",
                    metadata = Meta(sw)
                }, JsonOpts);
            }

            var data = new Dictionary<string, object?>
            {
                ["id"] = baseline.Id,
                ["system_id"] = baseline.RegisteredSystemId,
                ["baseline_level"] = baseline.BaselineLevel,
                ["overlay_applied"] = baseline.OverlayApplied,
                ["total_controls"] = baseline.TotalControls,
                ["customer_controls"] = baseline.CustomerControls,
                ["inherited_controls"] = baseline.InheritedControls,
                ["shared_controls"] = baseline.SharedControls,
                ["tailored_out_controls"] = baseline.TailoredOutControls,
                ["tailored_in_controls"] = baseline.TailoredInControls,
                ["control_count"] = baseline.ControlIds.Count,
                ["control_ids"] = baseline.ControlIds,
                ["created_by"] = baseline.CreatedBy,
                ["created_at"] = baseline.CreatedAt.ToString("O"),
                ["modified_at"] = baseline.ModifiedAt?.ToString("O")
            };

            if (includeDetails)
            {
                data["tailorings"] = baseline.Tailorings.Select(t => new
                {
                    id = t.Id,
                    control_id = t.ControlId,
                    action = t.Action.ToString(),
                    rationale = t.Rationale,
                    is_overlay_required = t.IsOverlayRequired,
                    tailored_by = t.TailoredBy,
                    tailored_at = t.TailoredAt.ToString("O")
                }).ToList();

                data["inheritances"] = baseline.Inheritances.Select(i => new
                {
                    id = i.Id,
                    control_id = i.ControlId,
                    inheritance_type = i.InheritanceType.ToString(),
                    provider = i.Provider,
                    customer_responsibility = i.CustomerResponsibility,
                    set_by = i.SetBy,
                    set_at = i.SetAt.ToString("O")
                }).ToList();
            }

            if (!string.IsNullOrWhiteSpace(familyFilter))
            {
                data["family_filter"] = familyFilter;
            }

            return JsonSerializer.Serialize(new
            {
                status = "success",
                data,
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_get_baseline failed for '{SystemId}'", systemId);
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
// T052: GenerateCrmTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_generate_crm — Generate a Customer Responsibility Matrix.
/// </summary>
public class GenerateCrmTool : BaseTool
{
    private readonly IBaselineService _baselineService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public GenerateCrmTool(
        IBaselineService baselineService,
        ILogger<GenerateCrmTool> logger) : base(logger)
    {
        _baselineService = baselineService;
    }

    public override string Name => "compliance_generate_crm";

    public override string Description =>
        "Generate a Customer Responsibility Matrix (CRM) from the system's baseline inheritance data. " +
        "Shows inherited/shared/customer breakdowns grouped by control family with STIG applicability.";

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
            var crm = await _baselineService.GenerateCrmAsync(systemId, cancellationToken);

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    system_id = crm.SystemId,
                    system_name = crm.SystemName,
                    baseline_level = crm.BaselineLevel,
                    total_controls = crm.TotalControls,
                    inherited_controls = crm.InheritedControls,
                    shared_controls = crm.SharedControls,
                    customer_controls = crm.CustomerControls,
                    undesignated_controls = crm.UndesignatedControls,
                    inheritance_percentage = crm.InheritancePercentage,
                    family_groups = crm.FamilyGroups.Select(fg => new
                    {
                        family = fg.Family,
                        family_name = fg.FamilyName,
                        control_count = fg.Controls.Count,
                        controls = fg.Controls.Select(c => new
                        {
                            control_id = c.ControlId,
                            inheritance_type = c.InheritanceType,
                            provider = c.Provider,
                            customer_responsibility = c.CustomerResponsibility
                        })
                    })
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (InvalidOperationException ex)
        {
            return Error("CRM_GENERATION_FAILED", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_generate_crm failed for '{SystemId}'", systemId);
            return Error("CRM_GENERATION_FAILED", ex.Message);
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
// T062: ShowStigMappingTool
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MCP tool: compliance_show_stig_mapping — Show DISA STIG rules mapped to a NIST control via CCI chain.
/// Returns STIG Rule IDs, benchmark names, CAT levels, and CCI references.
/// RBAC: Compliance.User, ISSM, Engineer
/// </summary>
public class ShowStigMappingTool : BaseTool
{
    private readonly IStigKnowledgeService _stigKnowledgeService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public ShowStigMappingTool(
        IStigKnowledgeService stigKnowledgeService,
        ILogger<ShowStigMappingTool> logger) : base(logger)
    {
        _stigKnowledgeService = stigKnowledgeService;
    }

    public override string Name => "compliance_show_stig_mapping";

    public override string Description =>
        "Show DISA STIG rules mapped to a NIST 800-53 control via the CCI (Control Correlation Identifier) chain. " +
        "Returns STIG Rule IDs, benchmark names, CAT severity levels, check/fix text, and CCI references. " +
        "Useful for understanding which technical checks implement a given NIST control.";

    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["control_id"] = new() { Name = "control_id", Description = "NIST 800-53 control ID (e.g., 'AC-2', 'AU-3', 'SC-8')", Type = "string", Required = true },
        ["severity"] = new() { Name = "severity", Description = "Optional severity filter: 'High' (CAT I), 'Medium' (CAT II), or 'Low' (CAT III)", Type = "string", Required = false },
        ["max_results"] = new() { Name = "max_results", Description = "Maximum number of STIG rules to return (default: 25)", Type = "integer", Required = false }
    };

    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var controlId = GetArg<string>(arguments, "control_id");
        var severityStr = GetArg<string>(arguments, "severity");
        var maxResults = GetArg<int?>(arguments, "max_results") ?? 25;

        if (string.IsNullOrWhiteSpace(controlId))
            return Error("INVALID_INPUT", "The 'control_id' parameter is required.");

        // Parse optional severity filter
        StigSeverity? severity = null;
        if (!string.IsNullOrWhiteSpace(severityStr))
        {
            if (Enum.TryParse<StigSeverity>(severityStr, ignoreCase: true, out var parsed))
                severity = parsed;
            else
                return Error("INVALID_INPUT", $"Invalid severity '{severityStr}'. Use 'High', 'Medium', or 'Low'.");
        }

        try
        {
            // Get STIG controls via CCI chain
            var stigControls = await _stigKnowledgeService.GetStigsByCciChainAsync(
                controlId, severity, cancellationToken);

            // Get CCI mappings for this control
            var cciMappings = await _stigKnowledgeService.GetCciMappingsAsync(
                controlId, cancellationToken);

            var limitedControls = stigControls.Take(maxResults).ToList();

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                status = "success",
                data = new
                {
                    control_id = controlId,
                    total_stig_rules = stigControls.Count,
                    returned_rules = limitedControls.Count,
                    severity_filter = severityStr,
                    cci_count = cciMappings.Count,
                    cat_summary = new
                    {
                        cat_i_high = stigControls.Count(c => c.Severity == StigSeverity.High),
                        cat_ii_medium = stigControls.Count(c => c.Severity == StigSeverity.Medium),
                        cat_iii_low = stigControls.Count(c => c.Severity == StigSeverity.Low)
                    },
                    benchmarks = stigControls
                        .Where(c => c.BenchmarkId != null)
                        .GroupBy(c => c.BenchmarkId)
                        .Select(g => new { benchmark = g.Key, rule_count = g.Count() })
                        .OrderByDescending(b => b.rule_count)
                        .ToList(),
                    stig_rules = limitedControls.Select(c => new
                    {
                        stig_id = c.StigId,
                        rule_id = c.RuleId,
                        title = c.Title,
                        severity = c.Severity.ToString(),
                        category = $"CAT {(c.Severity == StigSeverity.High ? "I" : c.Severity == StigSeverity.Medium ? "II" : "III")}",
                        benchmark_id = c.BenchmarkId,
                        stig_version = c.StigVersion,
                        stig_family = c.StigFamily,
                        nist_controls = c.NistControls,
                        cci_refs = c.CciRefs,
                        check_text = c.CheckText,
                        fix_text = c.FixText,
                        responsibility = c.Responsibility,
                        service_type = c.ServiceType,
                        weight = c.Weight,
                        release_date = c.ReleaseDate?.ToString("yyyy-MM-dd")
                    }).ToList(),
                    cci_mappings = cciMappings.Take(10).Select(m => new
                    {
                        cci_id = m.CciId,
                        nist_control_id = m.NistControlId,
                        definition = m.Definition,
                        status = m.Status
                    }).ToList()
                },
                metadata = Meta(sw)
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "compliance_show_stig_mapping failed for '{ControlId}'", controlId);
            return Error("STIG_MAPPING_FAILED", ex.Message);
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
