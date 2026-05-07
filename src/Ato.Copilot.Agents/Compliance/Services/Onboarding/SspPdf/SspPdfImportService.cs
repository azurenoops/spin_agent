using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf;

/// <summary>
/// Orchestrates the SSP PDF batch import flow (FR-040..FR-046):
/// upload → enqueue per-PDF extraction job → user reviews + corrects fields →
/// commit creates a <see cref="RegisteredSystem"/> with PDF-source provenance.
/// </summary>
public sealed class SspPdfImportService : ISspPdfImportService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IFileStorageProvider _storage;
    private readonly IWizardJobRunner _jobRunner;
    private readonly IWizardAuditService _audit;
    private readonly IWizardArtifactDependencyService _dependencies;
    private readonly ILogger<SspPdfImportService> _logger;

    public SspPdfImportService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IFileStorageProvider storage,
        IWizardJobRunner jobRunner,
        IWizardAuditService audit,
        IWizardArtifactDependencyService dependencies,
        ILogger<SspPdfImportService> logger)
    {
        _contextFactory = contextFactory;
        _storage = storage;
        _jobRunner = jobRunner;
        _audit = audit;
        _dependencies = dependencies;
        _logger = logger;
    }

    public async Task<SspPdfBatchUploadResult> StartBatchAsync(
        Guid tenantId,
        IReadOnlyList<(string fileName, string contentType, Stream content)> files,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        var batchId = Guid.NewGuid();
        var entries = new List<SspPdfBatchEntry>(files.Count);

        foreach (var (fileName, _, content) in files)
        {
            var sessionId = Guid.NewGuid();
            await using var buffered = new MemoryStream();
            await content.CopyToAsync(buffered, ct);
            buffered.Position = 0;
            var sha = ComputeSha256(buffered);
            buffered.Position = 0;

            var safeFile = SanitizeFileName(fileName);
            var key = WizardStorageKeys.SspPdfImport(tenantId, sessionId, safeFile);
            await _storage.SaveAsync(key, buffered, "application/pdf", ct);

            var session = new SspPdfImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                BatchId = batchId,
                OriginalFileName = safeFile,
                StorageBlobKey = key,
                FileSizeBytes = buffered.Length,
                ContentChecksumSha256 = sha,
                Status = SspPdfStatus.Uploaded,
                CreatedBy = actorUserId,
                UpdatedBy = actorUserId,
            };

            await using (var db = await _contextFactory.CreateDbContextAsync(ct))
            {
                db.SspPdfImportSessions.Add(session);
                await db.SaveChangesAsync(ct);
            }

            var job = await _jobRunner.EnqueueAsync(
                WizardJobType.SspPdfExtract,
                tenantId,
                actorUserId,
                new SspPdfExtractJobPayload(sessionId),
                ct);

            await using (var db = await _contextFactory.CreateDbContextAsync(ct))
            {
                var s = await db.SspPdfImportSessions.FirstAsync(x => x.Id == sessionId, ct);
                s.ExtractJobId = job.Id;
                s.Status = SspPdfStatus.Extracting;
                s.UpdatedAt = DateTimeOffset.UtcNow;
                s.UpdatedBy = actorUserId;
                await db.SaveChangesAsync(ct);
            }

            await _audit.RecordAsync(
                tenantId, actorUserId, WizardAuditAction.SspPdfUploaded,
                resourceType: nameof(SspPdfImportSession),
                resourceId: sessionId,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new
                {
                    session.OriginalFileName,
                    session.FileSizeBytes,
                    session.ContentChecksumSha256,
                    BatchId = batchId,
                }, JsonOpts),
                effectsJson: null,
                correlationId,
                ct);

            entries.Add(new SspPdfBatchEntry(sessionId, job.Id, safeFile));
        }

        _logger.LogInformation(
            "SSP-PDF batch {BatchId} accepted {Count} files (tenant {TenantId})",
            batchId, entries.Count, tenantId);

        return new SspPdfBatchUploadResult(batchId, entries);
    }

    public async Task<IReadOnlyList<SspPdfSessionSummary>> GetBatchSummaryAsync(
        Guid tenantId, Guid batchId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.SspPdfImportSessions
            .Where(s => s.TenantId == tenantId && s.BatchId == batchId)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new SspPdfSessionSummary(
                s.Id, s.OriginalFileName, s.Status, s.RejectReason, s.ExtractJobId, s.CreatedSystemId))
            .ToListAsync(ct);
    }

    public async Task<SspPdfExtractionResult?> GetExtractionAsync(
        Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var session = await db.SspPdfImportSessions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == sessionId, ct);
        if (session?.ExtractionResult is null) return null;
        return JsonSerializer.Deserialize<SspPdfExtractionResult>(session.ExtractionResult, JsonOpts);
    }

    public async Task UpdateCorrectionsAsync(
        Guid tenantId,
        Guid sessionId,
        IReadOnlyList<SspPdfFieldCorrection> corrections,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var session = await db.SspPdfImportSessions
            .FirstAsync(s => s.TenantId == tenantId && s.Id == sessionId, ct);
        session.UserCorrections = JsonSerializer.Serialize(corrections, JsonOpts);
        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.SspPdfExtracted,
            resourceType: nameof(SspPdfImportSession),
            resourceId: sessionId,
            beforeJson: null,
            afterJson: session.UserCorrections,
            effectsJson: null,
            correlationId,
            ct);
    }

    public async Task<Guid> CommitToSystemAsync(
        Guid tenantId, Guid sessionId, Guid actorUserId, Guid correlationId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var session = await db.SspPdfImportSessions
            .FirstAsync(s => s.TenantId == tenantId && s.Id == sessionId, ct);

        if (session.Status != SspPdfStatus.Extracted)
            throw new InvalidOperationException($"Session {sessionId} cannot be committed (Status={session.Status}).");

        var extraction = session.ExtractionResult is null
            ? null
            : JsonSerializer.Deserialize<SspPdfExtractionResult>(session.ExtractionResult, JsonOpts);
        var corrections = session.UserCorrections is null
            ? Array.Empty<SspPdfFieldCorrection>()
            : JsonSerializer.Deserialize<SspPdfFieldCorrection[]>(session.UserCorrections, JsonOpts) ?? Array.Empty<SspPdfFieldCorrection>();

        var resolved = ResolveFields(extraction?.Fields, corrections);
        var systemName = resolved.GetValueOrDefault("system_name") ?? "SSP PDF Imported System";
        var systemIdentifier = resolved.GetValueOrDefault("system_identifier");

        var registered = new RegisteredSystem
        {
            Name = systemName,
            Acronym = systemIdentifier?.Length is > 0 and <= 20 ? systemIdentifier : null,
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Imported (SSP PDF)",
            CreatedBy = $"wizard:ssp-pdf-import:{session.OriginalFileName}",
        };

        db.RegisteredSystems.Add(registered);
        session.CreatedSystemId = Guid.TryParse(registered.Id, out var rg) ? rg : Guid.Empty;
        session.Status = SspPdfStatus.Imported;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        // Best-effort dependency link.
        if (Guid.TryParse(registered.Id, out var dependentId))
        {
            await _dependencies.LinkAsync(
                tenantId,
                ArtifactSourceKind.SspPdfImportSession,
                sessionId,
                session.ContentChecksumSha256,
                ArtifactDependentKind.RegisteredSystem,
                dependentId,
                ct);
        }

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.SspPdfImported,
            resourceType: nameof(SspPdfImportSession),
            resourceId: sessionId,
            beforeJson: null,
            afterJson: JsonSerializer.Serialize(new
            {
                RegisteredSystemId = registered.Id,
                Source = $"SSP PDF ({session.OriginalFileName})",
            }, JsonOpts),
            effectsJson: null,
            correlationId,
            ct);

        return session.CreatedSystemId.Value;
    }

    private static Dictionary<string, string> ResolveFields(
        IReadOnlyList<SspPdfField>? extracted,
        IReadOnlyList<SspPdfFieldCorrection> corrections)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (extracted is not null)
        {
            foreach (var f in extracted)
            {
                if (!string.IsNullOrWhiteSpace(f.Value)) dict[f.Name] = f.Value!;
            }
        }
        foreach (var c in corrections)
        {
            if (!string.IsNullOrWhiteSpace(c.Value)) dict[c.FieldName] = c.Value!;
        }
        return dict;
    }

    private static string ComputeSha256(Stream content)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(content);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SanitizeFileName(string raw)
    {
        var trimmed = string.IsNullOrWhiteSpace(raw) ? "upload.pdf" : raw.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(c, '_');
        return trimmed;
    }
}

/// <summary>Background-job payload for a per-PDF extraction.</summary>
public record SspPdfExtractJobPayload(Guid SessionId);
