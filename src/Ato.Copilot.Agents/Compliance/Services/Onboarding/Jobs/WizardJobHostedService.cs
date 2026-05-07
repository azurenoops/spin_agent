using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;

/// <summary>
/// Marker interface implemented by the per-job-type handlers registered in DI. The
/// hosted service resolves the handler whose <see cref="JobType"/> matches the
/// envelope on the channel and invokes <see cref="ExecuteAsync"/>.
/// </summary>
public interface IWizardJobHandler
{
    /// <summary>The wizard job type this handler processes.</summary>
    WizardJobType JobType { get; }

    /// <summary>Execute the work for an envelope. State transitions / progress events
    /// are the handler's responsibility (the hosted service only handles dispatch
    /// and final-state fallback when an unhandled exception escapes).</summary>
    Task ExecuteAsync(WizardJobEnvelope envelope, CancellationToken ct);
}

/// <summary>
/// Hosted background service that drains the bounded
/// <see cref="WizardJobChannel"/> using configurable concurrency
/// (<c>Onboarding:Jobs:MaxConcurrency</c>). Each envelope is dispatched to the
/// matching <see cref="IWizardJobHandler"/>; unhandled exceptions are caught and
/// translated into a <c>Failed</c> state with the canonical
/// <see cref="Ato.Copilot.Core.Onboarding.WizardErrorCodes.JobFailed"/> code so
/// FR-065 ("retain original artifact, surface error") is honored.
/// </summary>
public class WizardJobHostedService : BackgroundService
{
    private readonly WizardJobChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OnboardingOptions> _options;
    private readonly ILogger<WizardJobHostedService> _logger;

    public WizardJobHostedService(
        WizardJobChannel channel,
        IServiceScopeFactory scopeFactory,
        IOptions<OnboardingOptions> options,
        ILogger<WizardJobHostedService> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = Math.Max(1, _options.Value.Jobs.MaxConcurrency);
        _logger.LogInformation(
            "WizardJobHostedService starting with concurrency {Concurrency}",
            concurrency);

        var workers = Enumerable.Range(0, concurrency)
            .Select(_ => RunWorkerAsync(stoppingToken))
            .ToArray();

        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            WizardJobEnvelope envelope;
            try
            {
                envelope = await _channel.Channel.Reader.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await DispatchAsync(envelope, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception in wizard job {JobId} ({JobType})",
                    envelope.JobId, envelope.JobType);

                await TryMarkFailedAsync(envelope, ex, stoppingToken);
            }
        }
    }

    private async Task DispatchAsync(WizardJobEnvelope envelope, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var handler = scope.ServiceProvider
            .GetServices<IWizardJobHandler>()
            .FirstOrDefault(h => h.JobType == envelope.JobType);

        if (handler is null)
        {
            _logger.LogWarning(
                "No handler registered for wizard job type {JobType} (job {JobId})",
                envelope.JobType, envelope.JobId);
            await TryMarkFailedAsync(envelope, new InvalidOperationException(
                $"No handler registered for {envelope.JobType}"), ct);
            return;
        }

        await MarkRunningAsync(scope, envelope, ct);
        await handler.ExecuteAsync(envelope, ct);
    }

    private static async Task MarkRunningAsync(
        AsyncServiceScope scope,
        WizardJobEnvelope envelope,
        CancellationToken ct)
    {
        var contextFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var status = await db.WizardJobStatuses
            .FirstOrDefaultAsync(s => s.Id == envelope.JobId, ct);
        if (status is null)
            return;

        status.Status = WizardJobState.InProgress;
        status.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var notifier = scope.ServiceProvider.GetRequiredService<IWizardProgressNotifier>();
        await notifier.PublishAsync(
            new WizardJobStatusEvent(
                envelope.JobId,
                envelope.TenantId,
                envelope.JobType,
                WizardJobState.InProgress,
                Percent: 0,
                Message: "Started",
                ErrorCode: null,
                Suggestion: null,
                Timestamp: status.StartedAt!.Value),
            ct);
    }

    private async Task TryMarkFailedAsync(
        WizardJobEnvelope envelope,
        Exception ex,
        CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var contextFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
            await using var db = await contextFactory.CreateDbContextAsync(ct);

            var status = await db.WizardJobStatuses
                .FirstOrDefaultAsync(s => s.Id == envelope.JobId, ct);
            if (status is null)
                return;

            status.Status = WizardJobState.Failed;
            status.FinishedAt = DateTimeOffset.UtcNow;
            status.ErrorCode = Core.Onboarding.WizardErrorCodes.JobFailed;
            status.Message = ex.Message;
            await db.SaveChangesAsync(ct);

            var notifier = scope.ServiceProvider.GetRequiredService<IWizardProgressNotifier>();
            await notifier.PublishAsync(
                new WizardJobStatusEvent(
                    envelope.JobId,
                    envelope.TenantId,
                    envelope.JobType,
                    WizardJobState.Failed,
                    Percent: null,
                    Message: ex.Message,
                    ErrorCode: Core.Onboarding.WizardErrorCodes.JobFailed,
                    Suggestion: "The original artifact was retained. Retry from the wizard.",
                    Timestamp: status.FinishedAt!.Value),
                ct);
        }
        catch (Exception inner)
        {
            _logger.LogError(
                inner,
                "Failed to mark wizard job {JobId} as Failed",
                envelope.JobId);
        }
    }
}
