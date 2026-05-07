using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass.Handlers;

/// <summary>
/// Background-job handler for <see cref="WizardJobType.EmassCommit"/>. Applies the
/// operator-supplied per-system commit instructions (Skip / Merge / Overwrite),
/// creates or merges <see cref="RegisteredSystem"/> rows, links the resulting systems
/// to the originating <see cref="EmassImportSession"/> via
/// <see cref="IWizardArtifactDependencyService"/>, and writes a per-system log to
/// <see cref="WizardJobStatus.Result"/>. Partial failures are preserved (FR-031 / SC-007).
/// </summary>
public class EmassCommitJobHandler : IWizardJobHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IWizardArtifactDependencyService _dependencies;
    private readonly IWizardProgressNotifier _notifier;
    private readonly ILogger<EmassCommitJobHandler> _logger;

    public EmassCommitJobHandler(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IWizardArtifactDependencyService dependencies,
        IWizardProgressNotifier notifier,
        ILogger<EmassCommitJobHandler> logger)
    {
        _contextFactory = contextFactory;
        _dependencies = dependencies;
        _notifier = notifier;
        _logger = logger;
    }

    public WizardJobType JobType => WizardJobType.EmassCommit;

    public async Task ExecuteAsync(WizardJobEnvelope envelope, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<EmassCommitJobPayload>(envelope.PayloadJson, JsonOpts)
                      ?? throw new InvalidOperationException("EmassCommitJobPayload missing.");
        var sessionId = payload.SessionId;
        var instructionsByIdent = payload.Instructions
            .GroupBy(i => i.SystemIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Decision, StringComparer.OrdinalIgnoreCase);

        await PublishAsync(envelope, WizardJobState.InProgress, percent: 10, message: "Loading preview");

        EmassImportSession session;
        EmassParseResult? preview;
        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            session = await db.EmassImportSessions.FirstAsync(s => s.Id == sessionId, ct);
            preview = string.IsNullOrEmpty(session.Preview)
                ? null
                : JsonSerializer.Deserialize<EmassParseResult>(session.Preview, JsonOpts);
        }

        if (preview is null)
        {
            throw new InvalidOperationException(
                $"eMASS session {sessionId} has no parsed preview to commit.");
        }

        var log = new List<EmassImportLogEntry>();
        var total = preview.Systems.Count;
        var processed = 0;

        foreach (var sys in preview.Systems)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            var decision = instructionsByIdent.TryGetValue(sys.SystemIdentifier ?? string.Empty, out var d)
                ? d
                : EmassCommitDecision.Skip;

            if (sys.MalformedReason is not null && decision != EmassCommitDecision.Skip)
            {
                // Even if operator wanted to merge, malformed rows are skipped (FR-031).
                log.Add(new EmassImportLogEntry(
                    sys.SystemIdentifier ?? string.Empty,
                    sys.SystemName,
                    "Skipped",
                    RegisteredSystemId: null,
                    Reason: sys.MalformedReason));
                await ReportProgressAsync(envelope, processed, total);
                continue;
            }

            if (decision == EmassCommitDecision.Skip)
            {
                log.Add(new EmassImportLogEntry(
                    sys.SystemIdentifier ?? string.Empty,
                    sys.SystemName,
                    "Skipped",
                    RegisteredSystemId: null,
                    Reason: "Operator selected Skip."));
                await ReportProgressAsync(envelope, processed, total);
                continue;
            }

            try
            {
                var registeredId = await ApplyDecisionAsync(session.TenantId, sys, decision, ct);
                await _dependencies.LinkAsync(
                    session.TenantId,
                    ArtifactSourceKind.EmassImportSession,
                    session.Id,
                    session.ContentChecksumSha256,
                    ArtifactDependentKind.RegisteredSystem,
                    Guid.TryParse(registeredId, out var rg) ? rg : Guid.Empty,
                    ct);

                log.Add(new EmassImportLogEntry(
                    sys.SystemIdentifier ?? string.Empty,
                    sys.SystemName,
                    decision == EmassCommitDecision.Overwrite ? "Overwritten" : "Merged",
                    RegisteredSystemId: registeredId,
                    Reason: null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to commit system {SystemId} in session {SessionId}",
                    sys.SystemIdentifier, sessionId);
                log.Add(new EmassImportLogEntry(
                    sys.SystemIdentifier ?? string.Empty,
                    sys.SystemName,
                    "Failed",
                    RegisteredSystemId: null,
                    Reason: ex.Message));
            }

            await ReportProgressAsync(envelope, processed, total);
        }

        var resultJson = JsonSerializer.Serialize(log.ToArray(), JsonOpts);

        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            var s = await db.EmassImportSessions.FirstAsync(x => x.Id == sessionId, ct);
            s.Status = log.Any(e => e.Outcome == "Failed") && log.All(e => e.Outcome != "Merged" && e.Outcome != "Overwritten")
                ? EmassImportStatus.Failed
                : EmassImportStatus.Imported;
            s.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var job = await db.WizardJobStatuses.FirstAsync(j => j.Id == envelope.JobId, ct);
            job.Status = WizardJobState.Succeeded;
            job.Percent = 100;
            job.Message = $"Imported {log.Count(e => e.Outcome is "Merged" or "Overwritten")} of {total} systems";
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.Result = resultJson;
            await db.SaveChangesAsync(ct);
        }

        await PublishAsync(envelope, WizardJobState.Succeeded, percent: 100,
            message: $"Committed {log.Count(e => e.Outcome is "Merged" or "Overwritten")} systems");

        _logger.LogInformation(
            "EmassCommit job {JobId} processed {Total} systems ({Imported} imported, {Skipped} skipped, {Failed} failed)",
            envelope.JobId,
            total,
            log.Count(e => e.Outcome is "Merged" or "Overwritten"),
            log.Count(e => e.Outcome == "Skipped"),
            log.Count(e => e.Outcome == "Failed"));
    }

    /// <summary>
    /// Apply a per-system commit decision. Returns the resolved
    /// <see cref="RegisteredSystem.Id"/>.
    /// </summary>
    private async Task<string> ApplyDecisionAsync(
        Guid tenantId,
        EmassParsedSystem parsed,
        EmassCommitDecision decision,
        CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        // Match by Acronym = system_identifier OR Name (case-insensitive).
        var existing = await db.RegisteredSystems
            .FirstOrDefaultAsync(rs =>
                (rs.Acronym != null && rs.Acronym == parsed.SystemIdentifier) ||
                rs.Name == parsed.SystemName,
                ct);

        if (existing is null)
        {
            // Create new — defaults are intentionally minimal; operator can refine later.
            var sys = new RegisteredSystem
            {
                Name = parsed.SystemName,
                Acronym = string.IsNullOrWhiteSpace(parsed.SystemIdentifier) ? null : parsed.SystemIdentifier,
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionSupport,
                HostingEnvironment = "Imported",
                CreatedBy = "wizard:emass-import",
            };
            db.RegisteredSystems.Add(sys);
            await db.SaveChangesAsync(ct);
            return sys.Id;
        }

        if (decision == EmassCommitDecision.Overwrite)
        {
            existing.Name = parsed.SystemName;
            existing.Acronym = string.IsNullOrWhiteSpace(parsed.SystemIdentifier) ? existing.Acronym : parsed.SystemIdentifier;
            existing.ModifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        // Merge keeps existing fields untouched.
        return existing.Id;
    }

    private Task ReportProgressAsync(WizardJobEnvelope envelope, int processed, int total)
    {
        var pct = total <= 0 ? 100 : 20 + (processed * 70 / total);
        return PublishAsync(envelope, WizardJobState.InProgress, pct, $"Committed {processed}/{total}");
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
