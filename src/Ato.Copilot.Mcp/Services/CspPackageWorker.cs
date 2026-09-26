using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Services.PackageImports;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Services;

/// <summary>Polls the database ledger; restart recovery does not depend on an in-memory channel.</summary>
public sealed class CspPackageWorker(IServiceScopeFactory scopes, ILogger<CspPackageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processor = new CspPackageProcessor(
                    scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(),
                    scope.ServiceProvider.GetRequiredService<IFileStorageProvider>(),
                    scope.ServiceProvider.GetRequiredService<ICspPackageAnalyzer>(), logger);
                if (await processor.ProcessNextAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "CspPackage.WorkerPollFailed; ledger remains durable for retry");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
