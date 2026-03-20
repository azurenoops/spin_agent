using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Loads and caches NIST 800-53 baseline and CNSSI 1253 overlay reference data
/// from embedded JSON resources.
/// </summary>
public interface IReferenceDataService
{
    /// <summary>
    /// Get the list of control IDs for a given baseline level.
    /// </summary>
    /// <param name="baselineLevel">Low, Moderate, or High.</param>
    /// <returns>List of NIST 800-53 control IDs.</returns>
    IReadOnlyList<string> GetBaselineControlIds(string baselineLevel);

    /// <summary>
    /// Get CNSSI 1253 overlay entries for a specific DoD Impact Level.
    /// </summary>
    /// <param name="impactLevel">IL2, IL4, IL5, or IL6.</param>
    /// <returns>List of overlay entries with control IDs and enhancements.</returns>
    IReadOnlyList<OverlayEntry> GetOverlayEntries(string impactLevel);

    /// <summary>
    /// Get the list of control IDs for a FedRAMP baseline level.
    /// </summary>
    /// <param name="level">li-saas, low, moderate, or high.</param>
    /// <returns>List of FedRAMP-specific control IDs.</returns>
    IReadOnlyList<string> GetFedRampBaselineControlIds(string level);
}

/// <summary>
/// A single CNSSI 1253 overlay entry.
/// </summary>
public class OverlayEntry
{
    public string ControlId { get; set; } = string.Empty;
    public string Il { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
    public List<string> Enhancements { get; set; } = new();
    public string? Notes { get; set; }
}

public class ReferenceDataService : IReferenceDataService
{
    private readonly ILogger<ReferenceDataService> _logger;
    private readonly Lazy<Dictionary<string, List<string>>> _baselines;
    private readonly Lazy<Dictionary<string, List<string>>> _fedrampBaselines;
    private readonly Lazy<List<OverlayEntry>> _overlays;
    private readonly ConcurrentDictionary<string, IReadOnlyList<OverlayEntry>> _overlayCache = new();

    public ReferenceDataService(ILogger<ReferenceDataService> logger)
    {
        _logger = logger;
        _baselines = new Lazy<Dictionary<string, List<string>>>(LoadBaselines);
        _fedrampBaselines = new Lazy<Dictionary<string, List<string>>>(LoadFedRampBaselines);
        _overlays = new Lazy<List<OverlayEntry>>(LoadOverlays);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetBaselineControlIds(string baselineLevel)
    {
        var key = baselineLevel.ToLowerInvariant();
        if (_baselines.Value.TryGetValue(key, out var controls))
            return controls;

        _logger.LogWarning("Unknown baseline level '{Level}'. Returning empty list.", baselineLevel);
        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyList<OverlayEntry> GetOverlayEntries(string impactLevel)
    {
        return _overlayCache.GetOrAdd(impactLevel.ToUpperInvariant(), il =>
        {
            var entries = _overlays.Value
                .Where(o => o.Il.Equals(il, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogDebug("Loaded {Count} overlay entries for {IL}.", entries.Count, il);
            return entries;
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetFedRampBaselineControlIds(string level)
    {
        var key = level.ToLowerInvariant();
        if (_fedrampBaselines.Value.TryGetValue(key, out var controls))
            return controls;

        _logger.LogWarning("Unknown FedRAMP baseline level '{Level}'. Returning empty list.", level);
        return Array.Empty<string>();
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private Dictionary<string, List<string>> LoadBaselines()
    {
        var json = ReadEmbeddedResource("nist-800-53-baselines.json");
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, List<string>>();

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            // Skip metadata properties ($schema, version, source)
            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            var controls = new List<string>();
            foreach (var item in property.Value.EnumerateArray())
            {
                controls.Add(item.GetString() ?? string.Empty);
            }
            result[property.Name] = controls;
        }

        _logger.LogInformation(
            "Loaded NIST 800-53 baselines: Low={Low}, Moderate={Moderate}, High={High}",
            result.GetValueOrDefault("low")?.Count ?? 0,
            result.GetValueOrDefault("moderate")?.Count ?? 0,
            result.GetValueOrDefault("high")?.Count ?? 0);

        return result;
    }

    private Dictionary<string, List<string>> LoadFedRampBaselines()
    {
        var json = ReadEmbeddedResource("fedramp-baselines.json");
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, List<string>>();

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            var controls = new List<string>();
            foreach (var item in property.Value.EnumerateArray())
            {
                controls.Add(item.GetString() ?? string.Empty);
            }
            result[property.Name] = controls;
        }

        _logger.LogInformation(
            "Loaded FedRAMP baselines: Li-SaaS={LiSaas}, Low={Low}, Moderate={Moderate}, High={High}",
            result.GetValueOrDefault("li-saas")?.Count ?? 0,
            result.GetValueOrDefault("low")?.Count ?? 0,
            result.GetValueOrDefault("moderate")?.Count ?? 0,
            result.GetValueOrDefault("high")?.Count ?? 0);

        return result;
    }

    private List<OverlayEntry> LoadOverlays()
    {
        var json = ReadEmbeddedResource("cnssi-1253-overlays.json");
        var doc = JsonDocument.Parse(json);
        var overlays = new List<OverlayEntry>();

        if (doc.RootElement.TryGetProperty("overlays", out var overlaysArray))
        {
            foreach (var item in overlaysArray.EnumerateArray())
            {
                var entry = new OverlayEntry
                {
                    ControlId = item.GetProperty("controlId").GetString() ?? string.Empty,
                    Il = item.GetProperty("il").GetString() ?? string.Empty,
                    Notes = item.TryGetProperty("notes", out var notes) ? notes.GetString() : null,
                };

                if (item.TryGetProperty("parameters", out var parameters) &&
                    parameters.ValueKind == JsonValueKind.Object)
                {
                    foreach (var param in parameters.EnumerateObject())
                    {
                        entry.Parameters[param.Name] = param.Value.ValueKind == JsonValueKind.String
                            ? param.Value.GetString() ?? string.Empty
                            : param.Value.GetRawText();
                    }
                }

                if (item.TryGetProperty("enhancements", out var enhancements) &&
                    enhancements.ValueKind == JsonValueKind.Array)
                {
                    foreach (var enh in enhancements.EnumerateArray())
                    {
                        var val = enh.GetString();
                        if (!string.IsNullOrEmpty(val))
                            entry.Enhancements.Add(val);
                    }
                }

                overlays.Add(entry);
            }
        }

        _logger.LogInformation("Loaded {Count} CNSSI 1253 overlay entries.", overlays.Count);
        return overlays;
    }

    private static string ReadEmbeddedResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException(
                $"Embedded resource '{fileName}' not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
