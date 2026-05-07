using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf.Handlers;

/// <summary>
/// Background-job handler for <see cref="WizardJobType.SspPdfExtract"/>. Loads
/// the persisted PDF, runs <see cref="ISspPdfExtractionService"/>, persists the
/// extraction result (or rejection reason) to the session, and emits progress
/// via <see cref="IWizardProgressNotifier"/> (FR-040..FR-046, FR-064/FR-065).
/// </summary>
public class SspPdfExtractJobHandler : IWizardJobHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IFileStorageProvider _storage;
    private readonly ISspPdfExtractionService _extractor;
    private readonly IWizardProgressNotifier _notifier;
    private readonly ILogger<SspPdfExtractJobHandler> _logger;

    public SspPdfExtractJobHandler(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IFileStorageProvider storage,
        ISspPdfExtractionService extractor,
        IWizardProgressNotifier notifier,
        ILogger<SspPdfExtractJobHandler> logger)
    {
        _contextFactory = contextFactory;
        _storage = storage;
        _extractor = extractor;
        _notifier = notifier;
        _logger = logger;
    }

    public WizardJobType JobType => WizardJobType.SspPdfExtract;

    public async Task ExecuteAsync(WizardJobEnvelope envelope, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SspPdfExtractJobPayload>(envelope.PayloadJson, JsonOpts)
                      ?? throw new InvalidOperationException("SspPdfExtractJobPayload missing.");
        var sessionId = payload.SessionId;

        await PublishAsync(envelope, WizardJobState.InProgress, 10, "Loading PDF");

        SspPdfImportSession session;
        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            session = await db.SspPdfImportSessions.FirstAsync(s => s.Id == sessionId, ct);
        }

        await using var pdfStream = await _storage.GetAsync(session.StorageBlobKey, ct)
                                    ?? throw new InvalidOperationException(
                                        $"Storage key {session.StorageBlobKey} not found.");

        await PublishAsync(envelope, WizardJobState.InProgress, 40, "Extracting fields");

        var result = await _extractor.ExtractAsync(pdfStream, session.OriginalFileName, ct);

        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            var s = await db.SspPdfImportSessions.FirstAsync(x => x.Id == sessionId, ct);
            s.ExtractionResult = JsonSerializer.Serialize(result, JsonOpts);
            s.RejectReason = result.RejectReason;
            s.Status = result.IsAccepted ? SspPdfStatus.Extracted : SspPdfStatus.Rejected;
            s.UpdatedAt = DateTimeOffset.UtcNow;

            var job = await db.WizardJobStatuses.FirstAsync(j => j.Id == envelope.JobId, ct);
            job.Status = result.IsAccepted ? WizardJobState.Succeeded : WizardJobState.Failed;
            job.Percent = 100;
            job.Message = result.IsAccepted
                ? $"Extracted {result.Fields.Count} fields from {result.PageCount} pages"
                : result.RejectMessage ?? "PDF rejected";
            job.ErrorCode = result.IsAccepted ? null : MapRejectToErrorCode(result.RejectReason);
            job.Suggestion = result.IsAccepted ? null : result.RejectMessage;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.Result = s.ExtractionResult;
            await db.SaveChangesAsync(ct);
        }

        await PublishAsync(
            envelope,
            result.IsAccepted ? WizardJobState.Succeeded : WizardJobState.Failed,
            100,
            result.IsAccepted
                ? $"Extracted {result.Fields.Count} fields"
                : result.RejectMessage ?? "PDF rejected",
            errorCode: result.IsAccepted ? null : MapRejectToErrorCode(result.RejectReason),
            suggestion: result.IsAccepted ? null : result.RejectMessage);

        _logger.LogInformation(
            "SspPdfExtract job {JobId} {Outcome} (session {SessionId})",
            envelope.JobId, result.IsAccepted ? "succeeded" : "rejected", sessionId);
    }

    private static string? MapRejectToErrorCode(SspPdfRejectReason? reason) => reason switch
    {
        SspPdfRejectReason.PasswordProtected => Ato.Copilot.Core.Onboarding.WizardErrorCodes.SspPdfPasswordProtected,
        SspPdfRejectReason.Encrypted => Ato.Copilot.Core.Onboarding.WizardErrorCodes.SspPdfPasswordProtected,
        SspPdfRejectReason.ImageOnly => Ato.Copilot.Core.Onboarding.WizardErrorCodes.SspPdfNoTextLayer,
        SspPdfRejectReason.UnknownFramework => Ato.Copilot.Core.Onboarding.WizardErrorCodes.SspPdfUnknownFramework,
        SspPdfRejectReason.Unreadable => Ato.Copilot.Core.Onboarding.WizardErrorCodes.SspPdfUnreadable,
        _ => null,
    };

    private Task PublishAsync(
        WizardJobEnvelope envelope,
        WizardJobState state,
        int percent,
        string message,
        string? errorCode = null,
        string? suggestion = null)
        => _notifier.PublishAsync(
            new WizardJobStatusEvent(
                envelope.JobId,
                envelope.TenantId,
                envelope.JobType,
                state,
                percent,
                message,
                errorCode,
                suggestion,
                DateTimeOffset.UtcNow));
}
