using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using Ato.Copilot.Agents.Compliance.Models;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Imports OSCAL catalogs and profiles from official GitHub repos into the
/// ComplianceFrameworks / FrameworkControls / FrameworkBaselines database tables.
/// </summary>
public interface IFrameworkImportService
{
    /// <summary>Import all registered frameworks and their baselines.</summary>
    Task<FrameworkImportResult> ImportAllAsync(CancellationToken ct = default);

    /// <summary>Import or refresh a single framework by its identifier.</summary>
    Task<int> ImportFrameworkAsync(string identifier, CancellationToken ct = default);
}

public record FrameworkImportResult(int FrameworksImported, int TotalControls, int TotalBaselines, List<string> Errors);

/// <summary>
/// Seed definitions for known compliance frameworks and their OSCAL source URLs.
/// </summary>
public static class FrameworkSeedData
{
    public static readonly FrameworkSeed[] Frameworks =
    [
        new("NIST-800-53-R5", "NIST 800-53 Rev. 5", "5.1.1", "NIST",
            "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_catalog.json",
            new BaselineSeed[]
            {
                new("Low", "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_LOW-baseline_profile.json"),
                new("Moderate", "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_MODERATE-baseline_profile.json"),
                new("High", "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_HIGH-baseline_profile.json"),
            }),
        new("NIST-800-53-R4", "NIST 800-53 Rev. 4", "4.0", "NIST",
            "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev4/json/NIST_SP-800-53_rev4_catalog.json",
            new BaselineSeed[]
            {
                new("Low", "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev4/json/NIST_SP-800-53_rev4_LOW-baseline_profile.json"),
                new("Moderate", "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev4/json/NIST_SP-800-53_rev4_MODERATE-baseline_profile.json"),
                new("High", "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev4/json/NIST_SP-800-53_rev4_HIGH-baseline_profile.json"),
            }),
        new("FEDRAMP-R5", "FedRAMP Rev. 5", "5.0", "GSA/FedRAMP",
            // FedRAMP uses NIST catalog but has its own baselines
            "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_catalog.json",
            new BaselineSeed[]
            {
                new("Li-SaaS", "https://raw.githubusercontent.com/GSA/fedramp-automation/master/dist/content/rev5/baselines/json/FedRAMP_rev5_LI-SaaS-baseline_profile.json"),
                new("Low", "https://raw.githubusercontent.com/GSA/fedramp-automation/master/dist/content/rev5/baselines/json/FedRAMP_rev5_LOW-baseline_profile.json"),
                new("Moderate", "https://raw.githubusercontent.com/GSA/fedramp-automation/master/dist/content/rev5/baselines/json/FedRAMP_rev5_MODERATE-baseline_profile.json"),
                new("High", "https://raw.githubusercontent.com/GSA/fedramp-automation/master/dist/content/rev5/baselines/json/FedRAMP_rev5_HIGH-baseline_profile.json"),
            }),
        new("NIST-800-171-R3", "NIST 800-171 Rev. 3", "3.0", "NIST",
            "https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-171/rev3/json/NIST_SP800-171_rev3_catalog.json",
            Array.Empty<BaselineSeed>()),
    ];
}

public record FrameworkSeed(string Identifier, string Name, string Version, string Publisher, string CatalogUrl, BaselineSeed[] Baselines);
public record BaselineSeed(string Level, string SourceUrl);

// ─── OSCAL Profile Deserialization Models ────────────────────────────────────
// These are minimal types needed to parse OSCAL baseline profile JSON documents.

sealed record OscalProfileRoot
{
    [JsonPropertyName("profile")]
    public OscalProfile? Profile { get; init; }
}

sealed record OscalProfile
{
    [JsonPropertyName("imports")]
    public List<OscalImport>? Imports { get; init; }

    [JsonPropertyName("modify")]
    public OscalModify? Modify { get; init; }
}

sealed record OscalImport
{
    [JsonPropertyName("include-controls")]
    public List<OscalIncludeControls>? IncludeControls { get; init; }
}

sealed record OscalIncludeControls
{
    [JsonPropertyName("with-ids")]
    public List<string>? WithIds { get; init; }
}

sealed record OscalModify
{
    [JsonPropertyName("set-parameters")]
    public List<OscalSetParameter>? SetParameters { get; init; }
}

sealed record OscalSetParameter
{
    [JsonPropertyName("param-id")]
    public string? ParamId { get; init; }

    [JsonPropertyName("values")]
    public List<string>? Values { get; init; }
}

// ─── Implementation ──────────────────────────────────────────────────────────

public class FrameworkImportService : IFrameworkImportService
{
    private const string EmbeddedNistRev5CatalogResource = "Ato.Copilot.Agents.Compliance.Resources.NIST_SP-800-53_rev5_catalog.json";
    private const string EmbeddedNistBaselinesResource = "Ato.Copilot.Agents.Compliance.Resources.nist-800-53-baselines.json";
    private const string EmbeddedFedRampBaselinesResource = "Ato.Copilot.Agents.Compliance.Resources.fedramp-baselines.json";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FrameworkImportService> _logger;

    public FrameworkImportService(
        IServiceScopeFactory scopeFactory,
        HttpClient httpClient,
        ILogger<FrameworkImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<FrameworkImportResult> ImportAllAsync(CancellationToken ct = default)
    {
        var errors = new List<string>();
        int fwkCount = 0, totalControls = 0, totalBaselines = 0;

        foreach (var seed in FrameworkSeedData.Frameworks)
        {
            try
            {
                var controlCount = await ImportFrameworkAsync(seed.Identifier, ct);
                fwkCount++;
                totalControls += controlCount;
                totalBaselines += seed.Baselines.Length;
                _logger.LogInformation("Imported {Identifier}: {Count} controls, {Baselines} baselines",
                    seed.Identifier, controlCount, seed.Baselines.Length);
            }
            catch (Exception ex)
            {
                var msg = $"Failed to import {seed.Identifier}: {ex.Message}";
                _logger.LogError(ex, "Failed to import framework {Identifier}", seed.Identifier);
                errors.Add(msg);
            }
        }

        return new FrameworkImportResult(fwkCount, totalControls, totalBaselines, errors);
    }

    public async Task<int> ImportFrameworkAsync(string identifier, CancellationToken ct = default)
    {
        var seed = FrameworkSeedData.Frameworks.FirstOrDefault(f =>
            f.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
        if (seed is null)
            throw new ArgumentException($"Unknown framework identifier: {identifier}");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Upsert framework row
        var framework = await db.ComplianceFrameworks
            .FirstOrDefaultAsync(f => f.Identifier == seed.Identifier, ct);

        if (framework is null)
        {
            framework = new ComplianceFramework
            {
                Identifier = seed.Identifier,
                Name = seed.Name,
                Version = seed.Version,
                Publisher = seed.Publisher,
                CatalogUrl = seed.CatalogUrl,
                OscalModelType = "catalog",
            };
            db.ComplianceFrameworks.Add(framework);
            await db.SaveChangesAsync(ct);
        }

        var catalog = await LoadCatalogAsync(seed, ct);

        // Delete existing controls for this framework (cascade also removes baseline entries via FK)
        await db.FrameworkControls.Where(c => c.FrameworkId == framework.Id).ExecuteDeleteAsync(ct);

        // Parse controls from OSCAL catalog
        var controls = new List<FrameworkControl>();
        int sortOrder = 0;

        foreach (var group in catalog.Groups)
        {
            var familyUpper = group.Id.ToUpperInvariant();

            foreach (var oscalCtl in group.Controls)
            {
                var label = oscalCtl.Props?.FirstOrDefault(p => p.Name == "label")?.Value;
                var displayId = (label ?? oscalCtl.Id).ToUpperInvariant().Replace(".", "(").TrimEnd(')');
                // Normalize: OSCAL uses "ac-2.1" (Rev5) but we display "AC-2(1)"
                displayId = NormalizeControlId(oscalCtl.Id);

                var isWithdrawn = oscalCtl.Props?.Any(p => p.Name == "status" && p.Value == "withdrawn") == true;
                var withdrawnTo = isWithdrawn
                    ? oscalCtl.Links?.FirstOrDefault(l => l.Rel == "incorporated-into")?.Href?.TrimStart('#')
                    : null;

                controls.Add(new FrameworkControl
                {
                    FrameworkId = framework.Id,
                    ControlId = displayId,
                    Family = familyUpper,
                    Title = oscalCtl.Title,
                    Description = ExtractStatementProse(oscalCtl),
                    IsEnhancement = false,
                    SortOrder = sortOrder++,
                    Withdrawn = isWithdrawn,
                    WithdrawnTo = withdrawnTo != null ? NormalizeControlId(withdrawnTo) : null,
                });

                // Process enhancements
                if (oscalCtl.Controls is { Count: > 0 })
                {
                    foreach (var enh in oscalCtl.Controls)
                    {
                        var enhDisplayId = NormalizeControlId(enh.Id);
                        var enhWithdrawn = enh.Props?.Any(p => p.Name == "status" && p.Value == "withdrawn") == true;

                        controls.Add(new FrameworkControl
                        {
                            FrameworkId = framework.Id,
                            ControlId = enhDisplayId,
                            Family = familyUpper,
                            Title = enh.Title,
                            Description = ExtractStatementProse(enh),
                            ParentControlId = displayId,
                            IsEnhancement = true,
                            SortOrder = sortOrder++,
                            Withdrawn = enhWithdrawn,
                            WithdrawnTo = enhWithdrawn
                                ? enh.Links?.FirstOrDefault(l => l.Rel == "incorporated-into")?.Href?.TrimStart('#') is { } inc
                                    ? NormalizeControlId(inc) : null
                                : null,
                        });
                    }
                }
            }
        }

        db.FrameworkControls.AddRange(controls);
        framework.ControlCount = controls.Count;
        framework.ImportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Imported {Count} controls for {Id}", controls.Count, seed.Identifier);

        // Import baselines
        await ImportBaselinesAsync(db, framework, seed, ct);

        return controls.Count;
    }

    private async Task ImportBaselinesAsync(
        AtoCopilotContext db,
        ComplianceFramework framework,
        FrameworkSeed seed,
        CancellationToken ct)
    {
        foreach (var baselineSeed in seed.Baselines)
        {
            try
            {
                // Upsert baseline row
                var baseline = await db.FrameworkBaselines
                    .FirstOrDefaultAsync(b => b.FrameworkId == framework.Id && b.Level == baselineSeed.Level, ct);

                if (baseline is null)
                {
                    baseline = new FrameworkBaseline
                    {
                        FrameworkId = framework.Id,
                        Level = baselineSeed.Level,
                        SourceUrl = baselineSeed.SourceUrl,
                    };
                    db.FrameworkBaselines.Add(baseline);
                    await db.SaveChangesAsync(ct);
                }

                // Delete existing baseline control entries
                await db.BaselineControlEntries.Where(e => e.BaselineId == baseline.Id).ExecuteDeleteAsync(ct);

                // Fetch and parse OSCAL profile
                _logger.LogInformation("Fetching baseline {Level} for {Id} from {Url}",
                    baselineSeed.Level, seed.Identifier, baselineSeed.SourceUrl);

                var controlIds = await LoadBaselineControlIdsAsync(seed, baselineSeed, ct);

                // Build baseline control entries (normalize to uppercase display IDs)
                var entries = controlIds
                    .Select(id => NormalizeControlId(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(id => new BaselineControlEntry
                    {
                        BaselineId = baseline.Id,
                        ControlId = id,
                    })
                    .ToList();

                db.BaselineControlEntries.AddRange(entries);
                baseline.ControlCount = entries.Count;
                baseline.ImportedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation("Imported {Count} controls for baseline {Level} / {Id}",
                    entries.Count, baselineSeed.Level, seed.Identifier);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import baseline {Level} for {Id}",
                    baselineSeed.Level, seed.Identifier);
            }
        }
    }

    private async Task<NistCatalog> LoadCatalogAsync(FrameworkSeed seed, CancellationToken ct)
    {
        _logger.LogInformation("Fetching catalog for {Id} from {Url}", seed.Identifier, seed.CatalogUrl);
        var onlineCatalog = await TryFetchCatalogAsync(seed.CatalogUrl, ct);
        if (onlineCatalog is not null)
        {
            return onlineCatalog;
        }

        if (SupportsEmbeddedCatalog(seed.Identifier))
        {
            _logger.LogWarning("Falling back to embedded catalog for {Id}", seed.Identifier);
            var embeddedCatalog = await LoadEmbeddedCatalogAsync(EmbeddedNistRev5CatalogResource, ct);
            if (embeddedCatalog is not null)
            {
                return embeddedCatalog;
            }
        }

        throw new InvalidOperationException($"Unable to load OSCAL catalog for {seed.Identifier}");
    }

    private async Task<List<string>> LoadBaselineControlIdsAsync(
        FrameworkSeed seed,
        BaselineSeed baselineSeed,
        CancellationToken ct)
    {
        var onlineIds = await TryFetchBaselineControlIdsAsync(seed, baselineSeed, ct);
        if (onlineIds.Count > 0)
        {
            return onlineIds;
        }

        var embeddedIds = await LoadEmbeddedBaselineControlIdsAsync(seed.Identifier, baselineSeed.Level, ct);
        if (embeddedIds.Count > 0)
        {
            _logger.LogWarning(
                "Falling back to embedded baseline controls for {Id} {Level}",
                seed.Identifier,
                baselineSeed.Level);
            return embeddedIds;
        }

        return onlineIds;
    }

    private async Task<NistCatalog?> TryFetchCatalogAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(90));

            using var stream = await _httpClient.GetStreamAsync(url, cts.Token);
            var root = await JsonSerializer.DeserializeAsync<NistCatalogRoot>(stream, cancellationToken: cts.Token);
            return root?.Catalog;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Catalog fetch timed out for {Url}", url);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Catalog fetch failed for {Url}", url);
            return null;
        }
    }

    private async Task<NistCatalog?> LoadEmbeddedCatalogAsync(string resourceName, CancellationToken ct)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            await using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogWarning("Embedded catalog resource not found: {Resource}", resourceName);
                return null;
            }

            var root = await JsonSerializer.DeserializeAsync<NistCatalogRoot>(stream, cancellationToken: ct);
            return root?.Catalog;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedded catalog load failed for {Resource}", resourceName);
            return null;
        }
    }

    private async Task<List<string>> TryFetchBaselineControlIdsAsync(
        FrameworkSeed seed,
        BaselineSeed baselineSeed,
        CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            await using var stream = await _httpClient.GetStreamAsync(baselineSeed.SourceUrl, cts.Token);
            var profileRoot = await JsonSerializer.DeserializeAsync<OscalProfileRoot>(stream, cancellationToken: cts.Token);
            return ExtractProfileControlIds(profileRoot);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Baseline fetch timed out for {Id} {Level}", seed.Identifier, baselineSeed.Level);
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Baseline fetch failed for {Id} {Level}", seed.Identifier, baselineSeed.Level);
            return new List<string>();
        }
    }

    private async Task<List<string>> LoadEmbeddedBaselineControlIdsAsync(
        string frameworkIdentifier,
        string baselineLevel,
        CancellationToken ct)
    {
        var resourceName = frameworkIdentifier.Equals("FEDRAMP-R5", StringComparison.OrdinalIgnoreCase)
            ? EmbeddedFedRampBaselinesResource
            : frameworkIdentifier.Equals("NIST-800-53-R5", StringComparison.OrdinalIgnoreCase)
                ? EmbeddedNistBaselinesResource
                : null;

        if (resourceName is null)
        {
            return new List<string>();
        }

        var key = baselineLevel.Trim().ToLowerInvariant();

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            await using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                _logger.LogWarning("Embedded baseline resource not found: {Resource}", resourceName);
                return new List<string>();
            }

            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty(key, out var baselineElement) || baselineElement.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            var ids = new List<string>();
            foreach (var item in baselineElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var id = item.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedded baseline load failed for {Framework} {Level}", frameworkIdentifier, baselineLevel);
            return new List<string>();
        }
    }

    private static bool SupportsEmbeddedCatalog(string identifier)
    {
        return identifier.Equals("NIST-800-53-R5", StringComparison.OrdinalIgnoreCase)
            || identifier.Equals("FEDRAMP-R5", StringComparison.OrdinalIgnoreCase);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalize OSCAL control IDs to standard display format:
    /// "ac-2" → "AC-2", "ac-2.1" → "AC-2(1)", "ac-02.01" → "AC-2(1)"
    /// </summary>
    internal static string NormalizeControlId(string oscalId)
    {
        if (string.IsNullOrEmpty(oscalId)) return oscalId;

        var id = oscalId.Trim().ToUpperInvariant();

        // OSCAL Rev5 uses dot notation for enhancements: "ac-2.1" → "AC-2(1)"
        var dotIdx = id.IndexOf('.');
        if (dotIdx > 0 && dotIdx < id.Length - 1)
        {
            var basePart = id[..dotIdx];
            var enhPart = id[(dotIdx + 1)..];
            // Strip leading zeros: "02" → "2"
            if (int.TryParse(enhPart, out var enhNum))
                enhPart = enhNum.ToString();
            return $"{basePart}({enhPart})";
        }

        // Already in parenthetical format or a base control
        return id;
    }

    private static string? ExtractStatementProse(OscalControl ctl)
    {
        if (ctl.Parts is null) return null;
        var statement = ctl.Parts.FirstOrDefault(p => p.Name == "statement");
        if (statement is null) return null;

        var parts = new List<string>();
        CollectProse(statement, parts);
        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    private static void CollectProse(ControlPart part, List<string> collector)
    {
        if (!string.IsNullOrWhiteSpace(part.Prose))
            collector.Add(part.Prose);
        if (part.Parts is not null)
        {
            foreach (var sub in part.Parts)
                CollectProse(sub, collector);
        }
    }

    private static List<string> ExtractProfileControlIds(OscalProfileRoot? root)
    {
        var ids = new List<string>();
        if (root?.Profile?.Imports is null) return ids;

        foreach (var import in root.Profile.Imports)
        {
            if (import.IncludeControls is null) continue;
            foreach (var include in import.IncludeControls)
            {
                if (include.WithIds is not null)
                    ids.AddRange(include.WithIds);
            }
        }

        return ids;
    }

}
