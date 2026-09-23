using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

/// <summary>Retries durable provider-to-customer review delivery, without model generation or human impersonation.</summary>
public sealed class CspResponsibilityFanoutWorker(
    IServiceScopeFactory scopes, ITenantContextAccessor accessor, ILogger<CspResponsibilityFanoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Provider responsibility fanout failed; durable source and review work will be retried");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One bounded-lifetime pass; each organization gets a separate context and transaction scope.</summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ResponsibilityDeliveryClaim> claims;
        await using (var directoryScope = scopes.CreateAsyncScope())
        {
            var directory = directoryScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            await CapabilityResponsibilityRouting.ExpandAsync(directory, ct);
            claims = await CapabilityResponsibilityRouting.ClaimAsync(directory, ct);
        }
        List<Exception> failures = [];
        foreach (var claim in claims)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (claim.TenantId == Guid.Empty)
                {
                    await using var missingScope = scopes.CreateAsyncScope();
                    await CapabilityResponsibilityRouting.RetryAsync(missingScope.ServiceProvider.GetRequiredService<AtoCopilotContext>(),
                        claim, "MissingTenant", ct);
                    logger.LogWarning("Responsibility delivery {DeliveryId} has no customer routing identity; ownership repair is required", claim.Id);
                    continue;
                }
                await using var scope = scopes.CreateAsyncScope();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>() as TenantContext
                    ?? throw new InvalidOperationException("Provider fanout requires the production scoped TenantContext.");
                tenant.TenantId = claim.TenantId;
                using var tenantScope = accessor.Push(tenant);
                await scope.ServiceProvider.GetRequiredService<CspResponsibilityFanoutService>().ProcessAsync(claim, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Responsibility delivery {DeliveryId} failed in tenant {TenantId}; other targets continue", claim.Id, claim.TenantId);
                await using var retryScope = scopes.CreateAsyncScope();
                try
                {
                    await CapabilityResponsibilityRouting.RetryAsync(retryScope.ServiceProvider.GetRequiredService<AtoCopilotContext>(),
                        claim, "DeliveryFailed", ct);
                }
                catch (DbUpdateConcurrencyException leaseLost)
                {
                    logger.LogWarning(leaseLost, "Responsibility delivery {DeliveryId} was claimed by another worker", claim.Id);
                }
                failures.Add(exception);
            }
        }
        if (failures.Count > 0)
            throw new AggregateException("One or more tenant responsibility deliveries remain pending.", failures);
    }
}
