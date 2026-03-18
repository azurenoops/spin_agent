using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Background service that consumes SSP export jobs from a Channel and processes them sequentially.
/// Each job dispatches to the appropriate format generator (docx, pdf, json) in SspExportService.
/// </summary>
public class SspExportBackgroundService : BackgroundService
{
    private readonly Channel<SspExportJob> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SspExportBackgroundService> _logger;

    public SspExportBackgroundService(
        Channel<SspExportJob> channel,
        IServiceScopeFactory scopeFactory,
        ILogger<SspExportBackgroundService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SspExportBackgroundService started, listening for export jobs");

        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    _logger.LogInformation(
                        "Processing export job: {ExportId} system={SystemId} format={Format}",
                        job.ExportId, job.SystemId, job.Format);

                    using var scope = _scopeFactory.CreateScope();
                    var exportService = scope.ServiceProvider.GetRequiredService<ISspExportService>();
                    await exportService.ProcessExportAsync(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Log but don't crash — continue processing next job
                    _logger.LogError(ex,
                        "Unhandled error processing export {ExportId}. Job will not be retried.",
                        job.ExportId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }

        _logger.LogInformation("SspExportBackgroundService stopped");
    }
}
