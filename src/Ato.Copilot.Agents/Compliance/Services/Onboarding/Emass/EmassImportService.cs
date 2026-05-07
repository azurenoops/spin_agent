using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass;

/// <summary>
/// Wizard Step-3 orchestration service for eMASS bulk imports (FR-030..FR-038).
/// Persists the upload via <see cref="IFileStorageProvider"/>, queues background
/// parse / commit jobs through <see cref="IWizardJobRunner"/>, and links per-system
/// dependencies for the cascade-replace flow (FR-094 / research §R6).
/// </summary>
public class EmassImportService : IEmassImportService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IFileStorageProvider _storage;
    private readonly IWizardJobRunner _jobRunner;
    private readonly IWizardAuditService _audit;
    private readonly ILogger<EmassImportService> _logger;

    public EmassImportService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IFileStorageProvider storage,
        IWizardJobRunner jobRunner,
        IWizardAuditService audit,
        ILogger<EmassImportService> logger)
    {
        _contextFactory = contextFactory;
        _storage = storage;
        _jobRunner = jobRunner;
        _audit = audit;
        _logger = logger;
    }

    public async Task<(EmassImportSession Session, Guid ParseJobId)> StartParseAsync(
        Guid tenantId,
        string originalFileName,
        string contentType,
        Stream content,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Buffer the upload to compute hash + persist (the source stream is one-shot).
        await using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        // SHA-256 (used as ContentChecksumSha256 / source version tag).
        string sha;
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = await sha256.ComputeHashAsync(buffered, ct);
            sha = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        buffered.Position = 0;

        var sessionId = Guid.NewGuid();
        var safeFile = SanitizeFileName(originalFileName);
        var key = WizardStorageKeys.EmassImport(tenantId, sessionId, safeFile);
        await _storage.SaveAsync(key, buffered, contentType ?? "application/octet-stream", ct);

        var ext = Path.GetExtension(safeFile).ToLowerInvariant();
        var format = ext == ".zip" ? EmassImportFormat.PackageZip : EmassImportFormat.Xlsx;

        var session = new EmassImportSession
        {
            Id = sessionId,
            TenantId = tenantId,
            OriginalFileName = safeFile,
            StorageBlobKey = key,
            FileSizeBytes = buffered.Length,
            ContentChecksumSha256 = sha,
            Format = format,
            Status = EmassImportStatus.Uploaded,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };

        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            db.EmassImportSessions.Add(session);
            await db.SaveChangesAsync(ct);
        }

        var parseJob = await _jobRunner.EnqueueAsync(
            WizardJobType.EmassParse,
            tenantId,
            actorUserId,
            new EmassParseJobPayload(sessionId),
            ct);

        await using (var db = await _contextFactory.CreateDbContextAsync(ct))
        {
            var s = await db.EmassImportSessions.FirstAsync(x => x.Id == sessionId, ct);
            s.ParseJobId = parseJob.Id;
            s.Status = EmassImportStatus.Parsing;
            s.UpdatedAt = DateTimeOffset.UtcNow;
            s.UpdatedBy = actorUserId;
            await db.SaveChangesAsync(ct);
        }

        // Reflect the post-enqueue state on the returned object for callers.
        session.ParseJobId = parseJob.Id;
        session.Status = EmassImportStatus.Parsing;

        await _audit.RecordAsync(
            tenantId,
            actorUserId,
            WizardAuditAction.EmassUploaded,
            resourceType: nameof(EmassImportSession),
            resourceId: sessionId,
            beforeJson: null,
            afterJson: JsonSerializer.Serialize(new
            {
                session.OriginalFileName,
                session.FileSizeBytes,
                session.ContentChecksumSha256,
                Format = session.Format.ToString(),
            }, JsonOpts),
            effectsJson: null,
            correlationId,
            ct);

        _logger.LogInformation(
            "eMASS upload {SessionId} ({Bytes} bytes, {Format}) queued parse job {ParseJobId}",
            sessionId, session.FileSizeBytes, session.Format, parseJob.Id);

        return (session, parseJob.Id);
    }

    public async Task<EmassParseResult?> GetPreviewAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var session = await db.EmassImportSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct);
        if (session is null || string.IsNullOrEmpty(session.Preview))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EmassParseResult>(session.Preview, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize preview for session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<Guid> CommitAsync(
        Guid tenantId,
        Guid sessionId,
        IReadOnlyList<EmassCommitInstruction> instructions,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var session = await db.EmassImportSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct);
        if (session is null)
        {
            throw new InvalidOperationException(
                $"eMASS session {sessionId} not found for tenant {tenantId}.");
        }
        if (session.Status != EmassImportStatus.Parsed)
        {
            throw new InvalidOperationException(
                $"Cannot commit session {sessionId} — current status is {session.Status}.");
        }

        var commitJob = await _jobRunner.EnqueueAsync(
            WizardJobType.EmassCommit,
            tenantId,
            actorUserId,
            new EmassCommitJobPayload(sessionId, instructions.ToArray()),
            ct);

        session.CommitJobId = commitJob.Id;
        session.Status = EmassImportStatus.Importing;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId,
            actorUserId,
            WizardAuditAction.EmassCommitted,
            resourceType: nameof(EmassImportSession),
            resourceId: sessionId,
            beforeJson: null,
            afterJson: JsonSerializer.Serialize(new
            {
                CommitJobId = commitJob.Id,
                Instructions = instructions.Select(i => new { i.SystemIdentifier, Decision = i.Decision.ToString() }).ToArray(),
            }, JsonOpts),
            effectsJson: null,
            correlationId,
            ct);

        return commitJob.Id;
    }

    public async Task<EmassImportLog?> GetLogAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var session = await db.EmassImportSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct);
        if (session is null || session.CommitJobId is null)
        {
            return null;
        }

        var job = await db.WizardJobStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == session.CommitJobId, ct);
        if (job is null || string.IsNullOrEmpty(job.Result))
        {
            return new EmassImportLog(sessionId, session.Status, Array.Empty<EmassImportLogEntry>());
        }

        try
        {
            var entries = JsonSerializer.Deserialize<EmassImportLogEntry[]>(job.Result, JsonOpts)
                          ?? Array.Empty<EmassImportLogEntry>();
            return new EmassImportLog(sessionId, session.Status, entries);
        }
        catch (JsonException)
        {
            return new EmassImportLog(sessionId, session.Status, Array.Empty<EmassImportLogEntry>());
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "upload.bin";
        var n = Path.GetFileName(name);
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(n.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "upload.bin" : clean;
    }
}

/// <summary>Payload for the <see cref="WizardJobType.EmassParse"/> job.</summary>
public sealed record EmassParseJobPayload(Guid SessionId);

/// <summary>Payload for the <see cref="WizardJobType.EmassCommit"/> job.</summary>
public sealed record EmassCommitJobPayload(
    Guid SessionId,
    EmassCommitInstruction[] Instructions);
