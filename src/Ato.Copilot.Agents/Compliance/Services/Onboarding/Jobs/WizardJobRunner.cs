using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;

/// <summary>
/// Internal envelope written to the bounded channel by <see cref="WizardJobRunner"/> and
/// consumed by <see cref="WizardJobHostedService"/>. Carries the persisted job id +
/// the JSON payload (already serialized into the <see cref="WizardJobStatus"/> row).
/// </summary>
public sealed record WizardJobEnvelope(
    Guid JobId,
    Guid TenantId,
    WizardJobType JobType,
    Guid EnqueuedBy,
    string PayloadJson);

/// <summary>
/// Bounded-channel wizard job queue (research §R7). Per-process singleton; the hosted
/// service drains the channel using configurable concurrency. The channel is wrapped
/// to keep direct write access internal to <see cref="WizardJobRunner"/>.
/// </summary>
public sealed class WizardJobChannel
{
    public Channel<WizardJobEnvelope> Channel { get; }

    public WizardJobChannel(IOptions<OnboardingOptions> options)
    {
        var capacity = Math.Max(1, options.Value.Jobs.QueueCapacity);
        Channel = System.Threading.Channels.Channel.CreateBounded<WizardJobEnvelope>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
    }
}

/// <summary>
/// Channel-backed <see cref="IWizardJobRunner"/> implementation. Persists a <c>Queued</c>
/// <see cref="WizardJobStatus"/> row first (so polling/SignalR clients always see the
/// authoritative state), then publishes onto the in-process channel. Concrete handlers
/// are dispatched by <see cref="WizardJobHostedService"/>.
/// </summary>
public class WizardJobRunner : IWizardJobRunner
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly WizardJobChannel _channel;
    private readonly IWizardProgressNotifier _notifier;
    private readonly ILogger<WizardJobRunner> _logger;

    public WizardJobRunner(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        WizardJobChannel channel,
        IWizardProgressNotifier notifier,
        ILogger<WizardJobRunner> logger)
    {
        _contextFactory = contextFactory;
        _channel = channel;
        _notifier = notifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WizardJobStatus> EnqueueAsync<TPayload>(
        WizardJobType jobType,
        Guid tenantId,
        Guid enqueuedBy,
        TPayload payload,
        CancellationToken ct = default)
    {
        var payloadJson = JsonSerializer.Serialize(payload);

        var status = new WizardJobStatus
        {
            TenantId = tenantId,
            JobType = jobType,
            Status = WizardJobState.Queued,
            EnqueuedBy = enqueuedBy,
            EnqueuedAt = DateTimeOffset.UtcNow,
            Payload = payloadJson,
        };

        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            db.WizardJobStatuses.Add(status);
            await db.SaveChangesAsync(ct);
        }

        await _channel.Channel.Writer.WriteAsync(
            new WizardJobEnvelope(status.Id, tenantId, jobType, enqueuedBy, payloadJson),
            ct);

        // Fire the initial Queued event for SignalR subscribers.
        await _notifier.PublishAsync(
            new WizardJobStatusEvent(
                status.Id,
                tenantId,
                jobType,
                WizardJobState.Queued,
                Percent: null,
                Message: "Queued",
                ErrorCode: null,
                Suggestion: null,
                Timestamp: status.EnqueuedAt ?? DateTimeOffset.UtcNow),
            ct);

        _logger.LogInformation(
            "Wizard job {JobId} ({JobType}) enqueued for tenant {TenantId}",
            status.Id, jobType, tenantId);

        return status;
    }
}
