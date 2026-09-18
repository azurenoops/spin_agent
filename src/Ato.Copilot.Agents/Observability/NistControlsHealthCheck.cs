using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Observability;

/// <summary>
/// Health check for the NIST SP 800-53 Rev 5 controls subsystem (per FR-033).
/// Probes <c>GetVersionAsync</c> and <c>ValidateControlIdAsync</c> for 3 test
/// controls (AC-3, SC-13, AU-2). Returns Healthy, Degraded, or Unhealthy with
/// a structured data dictionary for the health endpoint JSON response.
/// </summary>
public class NistControlsHealthCheck : IHealthCheck
{
    /// <summary>Three system-critical controls used as health probes.</summary>
    private static readonly string[] TestControlIds = ["AC-3", "SC-13", "AU-2"];

    private readonly INistControlsService _nistService;
    private readonly IOptions<NistControlsOptions> _options;
    private readonly ILogger<NistControlsHealthCheck> _logger;
    private readonly NistCatalogIntegrityValidator _integrityValidator;

    /// <summary>
    /// Initializes a new instance of <see cref="NistControlsHealthCheck"/>.
    /// </summary>
    /// <param name="nistService">The NIST controls service.</param>
    /// <param name="options">NIST controls configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public NistControlsHealthCheck(
        INistControlsService nistService,
        IOptions<NistControlsOptions> options,
        ILogger<NistControlsHealthCheck> logger,
        NistCatalogIntegrityValidator integrityValidator)
    {
        _nistService = nistService;
        _options = options;
        _logger = logger;
        _integrityValidator = integrityValidator;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Probe version — also triggers catalog load if not already cached
            var version = await _nistService.GetVersionAsync(cancellationToken);

            // Probe 3 test controls
            var validCount = 0;
            foreach (var controlId in TestControlIds)
            {
                if (await _nistService.ValidateControlIdAsync(controlId, cancellationToken))
                    validCount++;
            }

            sw.Stop();

            // Fix #561: distinguish "not yet loaded" (Degraded) from "loaded but invalid" (Unhealthy)
            // Get catalog status to determine if catalog is warming up vs failed
            string catalogSource = "unknown";
            bool isLoaded = false;
            if (_nistService is Ato.Copilot.Agents.Compliance.Services.NistControlsService nistSvc)
            {
                catalogSource = nistSvc.CatalogSource;
                isLoaded = catalogSource != "none";
            }

            var data = new Dictionary<string, object>
            {
                ["version"] = version,
                ["validTestControls"] = $"{validCount}/{TestControlIds.Length}",
                ["responseTimeMs"] = sw.ElapsedMilliseconds,
                ["timestamp"] = DateTime.UtcNow.ToString("O"),
                ["cacheDurationHours"] = _options.Value.CacheDurationHours,
                ["catalogSource"] = catalogSource
            };

            var integrityResult = _integrityValidator.LastResult;
            if (integrityResult is not null)
            {
                data["integrityValid"] = integrityResult.IsValid;
                data["schemaValid"] = integrityResult.SchemaValid;
                data["oscalVersion"] = integrityResult.OscalVersion;
                data["catalogVersion"] = integrityResult.CatalogVersion;
                data["groupCount"] = integrityResult.GroupCount;
                data["baseControlCount"] = integrityResult.BaseControlCount;
                data["enhancementCount"] = integrityResult.EnhancementCount;
                data["totalControlCount"] = integrityResult.TotalControlCount;
                data["integrityViolations"] = string.Join(" | ", integrityResult.Violations);

                if (!integrityResult.IsValid)
                {
                    _logger.LogError(
                        "NIST health check: Unhealthy — catalog integrity validation failed: {Violations}",
                        string.Join(" | ", integrityResult.Violations));
                    return HealthCheckResult.Unhealthy(
                        "NIST catalog failed structural integrity validation",
                        data: data);
                }
            }

            // Not loaded yet (still warming up) → Degraded, not Unhealthy.
            // Guard: only enter this path when the service IS the concrete NistControlsService
            // AND it positively reported CatalogSource == "none".  If the cast failed (e.g.
            // the service is mocked in tests) isLoaded defaults to false, but the type-guard
            // below prevents mis-classifying mocks or other implementations as "warming up".
            if (_nistService is Ato.Copilot.Agents.Compliance.Services.NistControlsService
                && !isLoaded
                && version == "Unknown")
            {
                _logger.LogWarning("NIST health check: Degraded — catalog still loading (warmup in progress)");
                return HealthCheckResult.Degraded(
                    "NIST catalog warming up — retry in a few seconds",
                    data: data);
            }

            if (version == "Unknown" || validCount == 0)
            {
                _logger.LogWarning(
                    "NIST health check: Unhealthy — version={Version}, validControls={Valid}/{Total}",
                    version, validCount, TestControlIds.Length);

                return HealthCheckResult.Unhealthy(
                    $"NIST catalog unavailable or empty (version={version}, {validCount}/{TestControlIds.Length} controls valid)",
                    data: data);
            }

            if (validCount < TestControlIds.Length)
            {
                _logger.LogWarning(
                    "NIST health check: Degraded — version={Version}, validControls={Valid}/{Total}",
                    version, validCount, TestControlIds.Length);

                return HealthCheckResult.Degraded(
                    $"NIST catalog partially available ({validCount}/{TestControlIds.Length} test controls valid)",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"NIST catalog operational (v{version}, {validCount}/{TestControlIds.Length} test controls valid, {sw.ElapsedMilliseconds}ms)",
                data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "NIST health check execution failed");

            return HealthCheckResult.Unhealthy(
                "NIST controls health check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["responseTimeMs"] = sw.ElapsedMilliseconds,
                    ["timestamp"] = DateTime.UtcNow.ToString("O")
                });
        }
    }
}
