using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass.Handlers;

/// <summary>
/// Background-job handler for <see cref="WizardJobType.EmassParse"/>. Loads the upload
/// from <see cref="IFileStorageProvider"/>, parses it via <see cref="IEmassImportParser"/>,
/// writes a JSON preview to the <see cref="EmassImportSession"/>, and emits progress
/// events through <see cref="IWizardProgressNotifier"/> (FR-064 / FR-065).
/// </summary>
public class EmassParseJobHandler : IWizardJobHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IFileStorageProvider _storage;
    private readonly IEmassImportParser _parser;
    private readonly IWizardProgressNotifier _notifier;
    private readonly ILogger<EmassParseJobHandler> _logger;

    public EmassParseJobHandler(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IFileStorageProvider storage,
        IEmassImportParser parser,
        IWizardProgressNotifier notifier,
        ILogger<EmassParseJobHandler> logger)
    {
        _contextFactory = contextFactory;
        _storage = storage;
        _parser = parser;
        _notifier = notifier;
        _logger = logger;
    }

    public WizardJobType JobType => WizardJobType.EmassParse;

    public async Task ExecuteAsync(WizardJobEnvelope envelope, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<EmassParseJobPayload>(envelope.PayloadJson, JsonOpts)
                      ?? throw new InvalidOperationException("EmassParseJobPayload missing.");
        var sessionId = payload.SessionId;

        await PublishAsync(envelope, WizardJobState.InProgress, percent: 10, message: "Loading upload");

        EmassImportSession session;
        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            session = await db.EmassImportSessions.FirstAsync(s => s.Id == sessionId, ct);
        }

        await using var content = await _storage.GetAsync(session.StorageBlobKey, ct)
                                  ?? throw new InvalidOperationException(
                                      $"Storage key {session.StorageBlobKey} not found.");

        await PublishAsync(envelope, WizardJobState.InProgress, percent: 40, message: "Parsing systems");

        var result = await _parser.ParseAsync(content, session.OriginalFileName, ct);
        var previewJson = JsonSerializer.Serialize(result, JsonOpts);

        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            var s = await db.EmassImportSessions.FirstAsync(x => x.Id == sessionId, ct);
            s.Preview = previewJson;
            s.Status = EmassImportStatus.Parsed;
            s.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var job = await db.WizardJobStatuses.FirstAsync(j => j.Id == envelope.JobId, ct);
            job.Status = WizardJobState.Succeeded;
            job.Percent = 100;
            job.Message = $"Parsed {result.Systems.Count} systems";
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.Result = previewJson;
            await db.SaveChangesAsync(ct);
        }

        await PublishAsync(envelope, WizardJobState.Succeeded, percent: 100,
            message: $"Parsed {result.Systems.Count} systems");

        _logger.LogInformation(
            "EmassParse job {JobId} succeeded ({SystemCount} systems)",
            envelope.JobId, result.Systems.Count);
    }

    private Task PublishAsync(
        WizardJobEnvelope envelope,
        WizardJobState state,
        int percent,
        string message)
        => _notifier.PublishAsync(
            new WizardJobStatusEvent(
                envelope.JobId,
                envelope.TenantId,
                envelope.JobType,
                state,
                percent,
                message,
                ErrorCode: null,
                Suggestion: null,
                Timestamp: DateTimeOffset.UtcNow));
}
