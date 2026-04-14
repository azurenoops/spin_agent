using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.Models;
using Ato.Copilot.Agents.Observability;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// NIST 800-53 Rev 5 controls catalog service with IMemoryCache-backed loading:
/// 1. Online fetch from usnistgov/oscal-content GitHub repo with Polly resilience
/// 2. Embedded resource fallback for air-gapped/offline environments
/// 3. IMemoryCache with configurable TTL and CacheItemPriority.High
/// Tracks LastSyncedAt and CatalogSource for observability.
/// </summary>
public class NistControlsService : INistControlsService
{
    private const string CatalogCacheKey = "NistControls:Catalog";
    private const string ControlsCacheKey = "NistControls:Controls";
    private const string EmbeddedResourceName = "Ato.Copilot.Agents.Compliance.Resources.NIST_SP-800-53_rev5_catalog.json";

    private static readonly ActivitySource ActivitySource = new("Ato.Copilot.NistControls");

    private readonly ILogger<NistControlsService> _logger;
    private readonly IMemoryCache _cache;
    private readonly NistControlsOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Lazy<Task<NistCatalog?>> _lazyCatalog;

    private DateTime? _lastSyncedAt;
    private string _catalogSource = "none";

    /// <summary>UTC timestamp of the last successful catalog load.</summary>
    public DateTime? LastSyncedAt => _lastSyncedAt;

    /// <summary>Source of the currently loaded catalog: "online", "embedded", or "none".</summary>
    public string CatalogSource => _catalogSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="NistControlsService"/> class.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public NistControlsService(
        ILogger<NistControlsService> logger,
        IMemoryCache cache,
        IOptions<NistControlsOptions> options,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _cache = cache;
        _options = options.Value;
        _httpClient = httpClient;
        _configuration = configuration;
        _lazyCatalog = new Lazy<Task<NistCatalog?>>(
            () => LoadAndCacheCatalogAsync(CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Backward-compatible constructor for existing consumers and tests.
    /// </summary>
    public NistControlsService(
        ILogger<NistControlsService> logger,
        IConfiguration configuration,
        HttpClient httpClient)
        : this(
            logger,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new NistControlsOptions()),
            httpClient,
            configuration)
    {
    }

    // ─── Interface Methods ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<NistCatalog?> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = ActivitySource.StartActivity("GetCatalog");
        var sw = Stopwatch.StartNew();

        try
        {
            if (_cache.TryGetValue(CatalogCacheKey, out NistCatalog? cached) && cached is not null)
            {
                activity?.SetTag("cache.hit", true);
                activity?.SetTag("control.count", cached.Groups.Sum(g => g.Controls.Count));
                ComplianceMetricsService.RecordApiCall("GetCatalog", success: true);
                sw.Stop();
                ComplianceMetricsService.RecordDuration("GetCatalog", sw.Elapsed.TotalSeconds);
                return cached;
            }

            activity?.SetTag("cache.hit", false);
            var catalog = await _lazyCatalog.Value;

            activity?.SetTag("success", catalog is not null);
            activity?.SetTag("fallback.used", _catalogSource == "embedded");
            if (catalog is not null)
                activity?.SetTag("control.count", catalog.Groups.Sum(g => g.Controls.Count));

            ComplianceMetricsService.RecordApiCall("GetCatalog", success: catalog is not null);
            sw.Stop();
            ComplianceMetricsService.RecordDuration("GetCatalog", sw.Elapsed.TotalSeconds);

            return catalog;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", ex.Message);
            activity?.SetTag("success", false);
            ComplianceMetricsService.RecordApiCall("GetCatalog", success: false);
            sw.Stop();
            ComplianceMetricsService.RecordDuration("GetCatalog", sw.Elapsed.TotalSeconds);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        return catalog?.Metadata.Version ?? "Unknown";
    }

    /// <inheritdoc />
    public async Task<NistControl?> GetControlAsync(string controlId, CancellationToken cancellationToken = default)
    {
        var controls = await GetControlsAsync(cancellationToken);
        return controls.FirstOrDefault(c =>
            string.Equals(c.Id, controlId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<List<NistControl>> GetControlFamilyAsync(
        string familyId,
        bool includeControls = true,
        CancellationToken cancellationToken = default)
    {
        var controls = await GetControlsAsync(cancellationToken);

        var familyUpper = familyId.ToUpperInvariant();
        var familyControls = controls
            .Where(c => string.Equals(c.Family, familyUpper, StringComparison.OrdinalIgnoreCase)
                        && !c.IsEnhancement)
            .ToList();

        if (!includeControls)
        {
            return familyControls.Select(c => new NistControl
            {
                Id = c.Id,
                Family = c.Family,
                Title = c.Title,
                ImpactLevel = c.ImpactLevel,
                Baselines = c.Baselines,
                IsEnhancement = c.IsEnhancement
            }).ToList();
        }

        return familyControls;
    }

    /// <inheritdoc />
    public async Task<List<NistControl>> SearchControlsAsync(
        string query,
        string? controlFamily = null,
        string? impactLevel = null,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var controls = await GetControlsAsync(cancellationToken);

        var queryLower = query.ToLowerInvariant();

        var results = controls.AsEnumerable();

        if (!string.IsNullOrEmpty(controlFamily))
            results = results.Where(c =>
                string.Equals(c.Family, controlFamily, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(impactLevel))
            results = results.Where(c =>
                c.Baselines.Any(b => string.Equals(b, impactLevel, StringComparison.OrdinalIgnoreCase)));

        results = results.Where(c =>
            c.Id.Contains(queryLower, StringComparison.OrdinalIgnoreCase) ||
            c.Title.Contains(queryLower, StringComparison.OrdinalIgnoreCase) ||
            c.Description.Contains(queryLower, StringComparison.OrdinalIgnoreCase));

        return results.Take(maxResults).ToList();
    }

    /// <inheritdoc />
    public async Task<ControlEnhancement?> GetControlEnhancementAsync(string controlId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(controlId);

        var catalog = await GetCatalogAsync(cancellationToken);
        if (catalog is null) return null;

        var oscalControl = FindOscalControl(catalog, controlId);
        if (oscalControl is null) return null;

        var statement = ExtractPartProse(oscalControl.Parts, "statement");
        var guidance = ExtractPartProse(oscalControl.Parts, "guidance");
        var objectives = ExtractObjectives(oscalControl.Parts);

        return new ControlEnhancement(
            Id: oscalControl.Id.ToUpperInvariant(),
            Title: oscalControl.Title,
            Statement: statement,
            Guidance: guidance,
            Objectives: objectives,
            LastUpdated: DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async Task<bool> ValidateControlIdAsync(string controlId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(controlId);

        var catalog = await GetCatalogAsync(cancellationToken);
        if (catalog is null) return false;

        return FindOscalControl(catalog, controlId) is not null;
    }

    /// <summary>
    /// Returns the status of the catalog including source, sync time, and control count.
    /// </summary>
    public async Task<CatalogStatus> GetCatalogStatusAsync(CancellationToken cancellationToken = default)
    {
        var controls = await GetControlsAsync(cancellationToken);

        return new CatalogStatus
        {
            Source = _catalogSource,
            LastSyncedAt = _lastSyncedAt,
            TotalControls = controls.Count,
            Families = controls.Select(c => c.Family).Distinct().Count(),
            IsLoaded = _cache.TryGetValue(CatalogCacheKey, out _)
        };
    }

    // ─── Cache Loading ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<List<NistControl>> GetAllControlsAsync(CancellationToken cancellationToken = default)
        => await GetControlsAsync(cancellationToken);

    /// <summary>
    /// Gets the backward-compatible NistControl list from cache, loading if needed.
    /// </summary>
    private async Task<List<NistControl>> GetControlsAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(ControlsCacheKey, out List<NistControl>? controls) && controls is not null)
            return controls;

        // Trigger lazy catalog load (which also caches controls)
        await _lazyCatalog.Value;

        if (_cache.TryGetValue(ControlsCacheKey, out controls) && controls is not null)
            return controls;

        return new List<NistControl>();
    }

    /// <summary>
    /// Loads the catalog using a SemaphoreSlim to prevent concurrent loads,
    /// then caches both the typed catalog and backward-compatible control list.
    /// </summary>
    private async Task<NistCatalog?> LoadAndCacheCatalogAsync(CancellationToken cancellationToken)
    {
        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(CatalogCacheKey, out NistCatalog? cached))
                return cached;

            var catalog = await LoadCatalogFromSourcesAsync(cancellationToken);
            if (catalog is null) return null;

            // Cache with configured TTL and high priority
            var absoluteExpiration = TimeSpan.FromHours(_options.CacheDurationHours);
            var slidingExpiration = TimeSpan.FromHours(_options.CacheDurationHours * 0.25);

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absoluteExpiration,
                SlidingExpiration = slidingExpiration,
                Priority = CacheItemPriority.High,
                Size = 1
            };

            _cache.Set(CatalogCacheKey, catalog, cacheOptions);

            // Also cache backward-compatible NistControl list
            var controls = BuildNistControlList(catalog);
            _cache.Set(ControlsCacheKey, controls, cacheOptions);

            _logger.LogInformation(
                "NIST catalog cached: version {Version}, {GroupCount} groups, {ControlCount} controls, source={Source}, TTL={Hours}h",
                catalog.Metadata.Version,
                catalog.Groups.Count,
                controls.Count,
                _catalogSource,
                _options.CacheDurationHours);

            return catalog;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>Loads catalog from remote URL then falls back to embedded resource.</summary>
    private async Task<NistCatalog?> LoadCatalogFromSourcesAsync(CancellationToken cancellationToken)
    {
        // Try online fetch first
        var catalog = await TryLoadFromOnlineAsync(cancellationToken);
        if (catalog is not null) return catalog;

        // Fallback to embedded resource
        if (_options.EnableOfflineFallback)
        {
            catalog = await LoadFromEmbeddedResourceAsync(cancellationToken);
            if (catalog is not null) return catalog;
        }

        _logger.LogError("All NIST catalog sources failed. No catalog available");
        return null;
    }

    /// <summary>Try fetching catalog from remote URL with typed deserialization.</summary>
    private async Task<NistCatalog?> TryLoadFromOnlineAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            _logger.LogInformation("Fetching NIST catalog from {Url}", _options.BaseUrl);
            using var stream = await _httpClient.GetStreamAsync(_options.BaseUrl, cts.Token);
            var root = await JsonSerializer.DeserializeAsync<NistCatalogRoot>(stream, cancellationToken: cts.Token);

            if (root?.Catalog is null)
            {
                _logger.LogWarning("Online NIST catalog deserialized as null");
                return null;
            }

            _lastSyncedAt = DateTime.UtcNow;
            _catalogSource = "online";

            _logger.LogInformation("NIST catalog loaded from online ({GroupCount} groups)", root.Catalog.Groups.Count);
            return root.Catalog;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("NIST catalog online fetch timed out after {Timeout}s", _options.TimeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch NIST catalog from online");
            return null;
        }
    }

    /// <summary>Load from embedded OSCAL catalog resource (air-gapped fallback).</summary>
    private async Task<NistCatalog?> LoadFromEmbeddedResourceAsync(CancellationToken ct)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null)
            {
                _logger.LogError("Embedded NIST catalog resource not found: {Resource}", EmbeddedResourceName);
                return null;
            }

            var root = await JsonSerializer.DeserializeAsync<NistCatalogRoot>(stream, cancellationToken: ct);
            if (root?.Catalog is null)
            {
                _logger.LogWarning("Embedded NIST catalog deserialized as null");
                return null;
            }

            _lastSyncedAt = DateTime.UtcNow;
            _catalogSource = "embedded";

            _logger.LogInformation("NIST catalog loaded from embedded resource ({GroupCount} groups)", root.Catalog.Groups.Count);
            return root.Catalog;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load embedded NIST catalog");
            return null;
        }
    }

    // ─── Backward-Compatible NistControl Entity Building ─────────────────────

    /// <summary>
    /// Builds the backward-compatible <see cref="NistControl"/> EF entity list from the typed catalog.
    /// </summary>
    private List<NistControl> BuildNistControlList(NistCatalog catalog)
    {
        var controls = new List<NistControl>();

        foreach (var group in catalog.Groups)
        {
            var familyUpper = group.Id.ToUpperInvariant();

            foreach (var oscalControl in group.Controls)
            {
                var parsed = ConvertToNistControl(oscalControl, familyUpper, isEnhancement: false, parentId: null);
                controls.Add(parsed);

                if (oscalControl.Controls is { Count: > 0 })
                {
                    foreach (var enhancement in oscalControl.Controls)
                    {
                        var enhParsed = ConvertToNistControl(enhancement, familyUpper, isEnhancement: true, parentId: parsed.Id);
                        parsed.ControlEnhancements.Add(enhParsed);
                        controls.Add(enhParsed);
                    }
                }
            }
        }

        _logger.LogDebug("Built {Count} NistControl entities from typed catalog", controls.Count);
        return controls;
    }

    /// <summary>Convert a typed OscalControl to a backward-compatible NistControl EF entity.</summary>
    private static NistControl ConvertToNistControl(OscalControl oscal, string family, bool isEnhancement, string? parentId)
    {
        var description = ExtractPartProse(oscal.Parts, "statement");

        return new NistControl
        {
            Id = oscal.Id,
            Family = family,
            Title = oscal.Title,
            Description = description,
            IsEnhancement = isEnhancement,
            ParentControlId = parentId,
            Baselines = ExtractBaselinesFromProps(oscal.Props),
            FedRampParameters = ExtractFedRampParamsFromTyped(oscal.Params)
        };
    }

    /// <summary>Extract baselines from typed ControlProperty props.</summary>
    private static List<string> ExtractBaselinesFromProps(List<ControlProperty>? props)
    {
        var baselines = new List<string>();

        if (props is null) return new List<string> { "Low", "Moderate", "High" };

        foreach (var prop in props)
        {
            if (prop.Name == "label") continue;
            if (prop.Class?.Contains("baseline") == true || prop.Name.Contains("baseline"))
            {
                if (!string.IsNullOrEmpty(prop.Value) && !baselines.Contains(prop.Value))
                    baselines.Add(prop.Value);
            }
        }

        if (baselines.Count == 0)
            baselines.AddRange(["Low", "Moderate", "High"]);

        return baselines;
    }

    /// <summary>Extract FedRAMP params from typed ControlParam list.</summary>
    private static string? ExtractFedRampParamsFromTyped(List<ControlParam>? parameters)
    {
        if (parameters is null or { Count: 0 }) return null;

        var paramList = parameters
            .Where(p => p.Label is not null)
            .Select(p => $"{p.Id}: {p.Label}")
            .ToList();

        return paramList.Count > 0 ? string.Join("; ", paramList) : null;
    }

    // ─── OSCAL Typed Model Helpers ───────────────────────────────────────────

    /// <summary>Find an OSCAL control by ID (case-insensitive) across all groups and nested enhancements.</summary>
    private static OscalControl? FindOscalControl(NistCatalog catalog, string controlId)
    {
        foreach (var group in catalog.Groups)
        {
            foreach (var control in group.Controls)
            {
                if (string.Equals(control.Id, controlId, StringComparison.OrdinalIgnoreCase))
                    return control;

                if (control.Controls is { Count: > 0 })
                {
                    foreach (var enhancement in control.Controls)
                    {
                        if (string.Equals(enhancement.Id, controlId, StringComparison.OrdinalIgnoreCase))
                            return enhancement;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>Extract concatenated prose from parts matching the given name (e.g., "statement", "guidance").</summary>
    private static string ExtractPartProse(List<ControlPart>? parts, string partName)
    {
        if (parts is null) return string.Empty;

        var matchingPart = parts.FirstOrDefault(p =>
            string.Equals(p.Name, partName, StringComparison.OrdinalIgnoreCase));

        if (matchingPart is null) return string.Empty;

        return CollectProse(matchingPart);
    }

    /// <summary>Recursively collect all prose text from a part and its sub-parts.</summary>
    private static string CollectProse(ControlPart part)
    {
        var prose = part.Prose ?? string.Empty;

        if (part.Parts is { Count: > 0 })
        {
            var subProse = string.Join(" ", part.Parts.Select(CollectProse).Where(p => !string.IsNullOrEmpty(p)));
            if (!string.IsNullOrEmpty(subProse))
            {
                prose = string.IsNullOrEmpty(prose) ? subProse : $"{prose} {subProse}";
            }
        }

        return prose;
    }

    /// <summary>Extract assessment objective prose strings from parts named "assessment-objective".</summary>
    private static List<string> ExtractObjectives(List<ControlPart>? parts)
    {
        if (parts is null) return new List<string>();

        var objectives = new List<string>();
        var objPart = parts.FirstOrDefault(p =>
            string.Equals(p.Name, "assessment-objective", StringComparison.OrdinalIgnoreCase));

        if (objPart is null) return objectives;

        CollectObjectiveProse(objPart, objectives);
        return objectives;
    }

    /// <summary>Recursively collects all objective prose from nested parts.</summary>
    private static void CollectObjectiveProse(ControlPart part, List<string> objectives)
    {
        if (!string.IsNullOrEmpty(part.Prose))
        {
            objectives.Add(part.Prose);
        }

        if (part.Parts is { Count: > 0 })
        {
            foreach (var subPart in part.Parts)
            {
                CollectObjectiveProse(subPart, objectives);
            }
        }
    }
}

/// <summary>
/// Status information for the NIST controls catalog.
/// </summary>
public class CatalogStatus
{
    /// <summary>Source of the loaded catalog (online, cache, embedded).</summary>
    public string Source { get; set; } = "none";

    /// <summary>UTC timestamp of last successful sync.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>Total number of controls loaded.</summary>
    public int TotalControls { get; set; }

    /// <summary>Number of distinct control families.</summary>
    public int Families { get; set; }

    /// <summary>Whether the catalog is loaded.</summary>
    public bool IsLoaded { get; set; }
}
