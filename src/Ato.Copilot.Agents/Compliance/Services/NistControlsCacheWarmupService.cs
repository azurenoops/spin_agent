using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that pre-loads the NIST SP 800-53 Rev 5 catalog into <c>IMemoryCache</c>
/// at application startup and refreshes it periodically at 90% of the configured TTL.
/// After each successful cache warmup, syncs controls to the NistControls SQL table
/// so that FK relationships from ComplianceFindings are valid.
/// </summary>
public sealed class NistControlsCacheWarmupService : BackgroundService
{
    private readonly INistControlsService _nistControlsService;
    private readonly IDbContextFactory<AtoCopilotContext>? _dbFactory;
    private readonly IOptions<NistControlsOptions> _options;
    private readonly ILogger<NistControlsCacheWarmupService> _logger;
    private readonly ComplianceValidationService? _validationService;

    /// <summary>Initializes a new instance of the <see cref="NistControlsCacheWarmupService"/> class.</summary>
    /// <param name="nistControlsService">NIST controls service to warm up.</param>
    /// <param name="options">NIST controls configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="dbFactory">Optional EF Core context factory for SQL sync.</param>
    /// <param name="validationService">Optional validation service for post-warmup checks.</param>
    public NistControlsCacheWarmupService(
        INistControlsService nistControlsService,
        IOptions<NistControlsOptions> options,
        ILogger<NistControlsCacheWarmupService> logger,
        IDbContextFactory<AtoCopilotContext>? dbFactory = null,
        ComplianceValidationService? validationService = null)
    {
        _nistControlsService = nistControlsService;
        _options = options;
        _logger = logger;
        _dbFactory = dbFactory;
        _validationService = validationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var warmupDelay = TimeSpan.FromSeconds(_options.Value.WarmupDelaySeconds);
        _logger.LogInformation("NIST Controls cache warmup scheduled after {DelaySeconds}s initial delay", _options.Value.WarmupDelaySeconds);

        await Task.Delay(warmupDelay, stoppingToken);

        // Calculate refresh interval at 90% of cache TTL
        var refreshInterval = TimeSpan.FromHours(_options.Value.CacheDurationHours * 0.9);
        using var timer = new PeriodicTimer(refreshInterval);

        // Initial warmup
        await WarmupCacheAsync(stoppingToken);

        // Periodic refresh loop
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await WarmupCacheAsync(stoppingToken);
        }
    }

    internal async Task WarmupCacheAsync(CancellationToken stoppingToken)
    {
        const int maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(_options.Value.WarmupRetryDelaySeconds);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Starting NIST catalog cache warmup (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);

                var catalog = await _nistControlsService.GetCatalogAsync(stoppingToken);
                if (catalog is null)
                {
                    _logger.LogWarning("NIST catalog warmup returned null on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelay, stoppingToken);
                        continue;
                    }
                    break;
                }

                var version = await _nistControlsService.GetVersionAsync(stoppingToken);
                _logger.LogInformation(
                    "NIST catalog cache warmup succeeded: version {Version}, {GroupCount} control families",
                    version,
                    catalog.Groups.Count);

                // Run validation if the service is registered
                if (_validationService is not null)
                {
                    try
                    {
                        await _validationService.ValidateControlMappingsAsync(stoppingToken);
                        _logger.LogInformation("Post-warmup control mapping validation completed");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Post-warmup validation failed (non-fatal)");
                    }
                }

                // Sync controls to SQL so FK relationships from Findings are valid
                await SyncControlsToDatabaseAsync(stoppingToken);

                return; // Success — exit retry loop
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("NIST catalog cache warmup cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NIST catalog cache warmup failed on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelay, stoppingToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"NIST catalog cache warmup failed after {maxRetries} attempts; startup cannot continue without a valid catalog.");
    }

    /// <summary>
    /// Syncs cached NIST controls to the NistControls SQL table using upsert logic.
    /// Base controls are inserted first, then enhancements (which have ParentControlId FK).
    /// Non-fatal — failures are logged but do not block warmup.
    /// </summary>
    private async Task SyncControlsToDatabaseAsync(CancellationToken stoppingToken)
    {
        if (_dbFactory is null)
        {
            _logger.LogDebug("No IDbContextFactory registered — skipping NistControls DB sync");
            return;
        }

        try
        {
            var controls = await _nistControlsService.GetAllControlsAsync(stoppingToken);
            if (controls.Count == 0)
            {
                _logger.LogWarning("No NIST controls available for DB sync");
                return;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);

            var existingIdsList = await db.NistControls
                .Select(c => c.Id)
                .ToListAsync(stoppingToken);
            var existingIds = new HashSet<string>(existingIdsList, StringComparer.OrdinalIgnoreCase);

            // Separate base controls and enhancements to respect FK ordering
            var baseControls = controls.Where(c => !c.IsEnhancement).ToList();
            var enhancements = controls.Where(c => c.IsEnhancement).ToList();

            var added = 0;
            var updated = 0;

            // Upsert base controls first
            foreach (var control in baseControls)
            {
                if (existingIds.Contains(control.Id))
                {
                    var existing = await db.NistControls.FindAsync([control.Id], stoppingToken);
                    if (existing is not null)
                    {
                        existing.Family = control.Family;
                        existing.Title = control.Title;
                        existing.Description = control.Description;
                        existing.ImpactLevel = control.ImpactLevel;
                        existing.Baselines = control.Baselines;
                        existing.AzureImplementation = control.AzureImplementation;
                        existing.AzurePolicyDefinitionIds = control.AzurePolicyDefinitionIds;
                        existing.Enhancements = control.Enhancements;
                        existing.FedRampParameters = control.FedRampParameters;
                        updated++;
                    }
                }
                else
                {
                    // Detach navigation to prevent EF from cascading
                    var entity = CloneForInsert(control);
                    db.NistControls.Add(entity);
                    added++;
                }
            }

            // Upsert enhancements (ParentControlId FK is now satisfied)
            foreach (var enhancement in enhancements)
            {
                if (existingIds.Contains(enhancement.Id))
                {
                    var existing = await db.NistControls.FindAsync([enhancement.Id], stoppingToken);
                    if (existing is not null)
                    {
                        existing.Family = enhancement.Family;
                        existing.Title = enhancement.Title;
                        existing.Description = enhancement.Description;
                        existing.ImpactLevel = enhancement.ImpactLevel;
                        existing.Baselines = enhancement.Baselines;
                        existing.AzureImplementation = enhancement.AzureImplementation;
                        existing.AzurePolicyDefinitionIds = enhancement.AzurePolicyDefinitionIds;
                        existing.Enhancements = enhancement.Enhancements;
                        existing.FedRampParameters = enhancement.FedRampParameters;
                        existing.ParentControlId = enhancement.ParentControlId;
                        updated++;
                    }
                }
                else
                {
                    var entity = CloneForInsert(enhancement);
                    db.NistControls.Add(entity);
                    added++;
                }
            }

            await db.SaveChangesAsync(stoppingToken);

            _logger.LogInformation(
                "NistControls DB sync completed: {Added} added, {Updated} updated, {Total} total in catalog",
                added, updated, controls.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NistControls DB sync failed (non-fatal — FK normalization will handle missing controls)");
        }
    }

    /// <summary>Creates a shallow copy without navigation properties to avoid EF tracking issues.</summary>
    private static NistControl CloneForInsert(NistControl source) => new()
    {
        Id = source.Id,
        Family = source.Family,
        Title = source.Title,
        Description = source.Description,
        ImpactLevel = source.ImpactLevel,
        Baselines = new List<string>(source.Baselines),
        AzureImplementation = source.AzureImplementation,
        AzurePolicyDefinitionIds = new List<string>(source.AzurePolicyDefinitionIds),
        Enhancements = new List<string>(source.Enhancements),
        FedRampParameters = source.FedRampParameters,
        ParentControlId = source.ParentControlId,
        IsEnhancement = source.IsEnhancement,
        ControlEnhancements = new List<NistControl>() // Empty — no nav tracking
    };
}
