using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class SspExportService
{
    public async Task<DocumentPreviewDto> CreatePreviewAsync(string systemId, string userId,
        CancellationToken cancellationToken = default, string? idempotencyKey = null, bool approvedSources = false)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var requestScope = await SnapshotRequestScopeAsync(db, systemId, userId, approvedSources ? "ApprovedPreview" : "Preview", idempotencyKey, cancellationToken);
        var prior = await FindSnapshotRequestAsync(db, requestScope, cancellationToken);
        if (prior != null) return await PreviewResponseAsync(prior, cancellationToken);
        var generated = approvedSources
            ? await _oscalService.PreviewApprovedAsync(systemId, true, true, cancellationToken)
            : await _oscalService.PreviewAsync(systemId, true, true, cancellationToken);
        generated = await AddResponsibilitiesAsync(systemId, generated, cancellationToken);
        generated = await AddProviderEvidenceAsync(systemId, generated, cancellationToken);
        var bytes = Encoding.UTF8.GetBytes(generated.OscalJson);
        if (bytes.Length > _settings.MaxExportSizeBytes)
            throw new InvalidOperationException("Preview exceeds the configured export size limit.");
        var gaps = generated.BuildPreviewSourceGaps();
        var snapshot = new SspExport
        {
            SystemId = systemId, Format = "json", Status = "Preview", GeneratedBy = userId,
            CompletedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(_settings.RetentionDays),
            ContentHash = ComputeSha256(bytes), FileSize = bytes.Length, ControlCount = generated.Statistics.ControlCount,
            RequestScopeKey = requestScope,
            SourceTenantId = db.TenantFilterEffectiveId == Guid.Empty ? null : db.TenantFilterEffectiveId,
            RequestedPersonId = db.WorkspacePersonId,
            SourceManifestJson = JsonSerializer.Serialize(generated.SourceManifest), SourceGapsJson = JsonSerializer.Serialize(gaps)
        };
        snapshot.FilePath = Path.Combine(systemId, $"{snapshot.Id}.json");
        var path = Path.Combine(_settings.ExportsPath, snapshot.FilePath);
        try
        {
            try
            {
                EnsureDirectoryExists(path);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw PreviewStorageUnavailable(ex);
            }
            db.Add(snapshot);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (requestScope != null)
        {
            DeletePreviewFile(path);
            db.Entry(snapshot).State = EntityState.Detached;
            var winner = await FindSnapshotRequestAsync(db, requestScope, cancellationToken);
            if (winner == null) throw;
            return await PreviewResponseAsync(winner, cancellationToken);
        }
        catch
        {
            DeletePreviewFile(path);
            throw;
        }
        return new(systemId, "json", "application/json", generated.OscalJson, snapshot.ContentHash, snapshot.GeneratedAt, gaps)
        {
            PreviewId = snapshot.Id, SourceManifest = generated.SourceManifest,
            SourceState = approvedSources ? "ApprovedSources" : "CurrentWorkingData"
        };
    }

    public async Task<SspExport> EnqueueFromPreviewAsync(string systemId, Guid previewId, string userId,
        CancellationToken cancellationToken = default, string? idempotencyKey = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var requestScope = await SnapshotRequestScopeAsync(db, systemId, userId, "ExportPreview", idempotencyKey, cancellationToken);
        var prior = await FindSnapshotRequestAsync(db, requestScope, cancellationToken);
        if (prior != null) return ReplayExport(prior, previewId);
        var preview = await RequirePreviewAsync(db, systemId, previewId, cancellationToken);
        await RequireCurrentDesignAsync(scope.ServiceProvider, preview.SourceManifestJson, systemId, cancellationToken);
        await ValidateCurrentEvidenceAsync(preview.SourceManifestJson, systemId, cancellationToken);
        await ReadPreviewBytesAsync(preview, cancellationToken);
        var export = new SspExport
        {
            SystemId = systemId, Format = "json", Status = "Pending", GeneratedBy = userId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_settings.RetentionDays), SourcePreviewId = preview.Id,
            SourceManifestJson = preview.SourceManifestJson, SourceGapsJson = preview.SourceGapsJson,
            RequestScopeKey = requestScope,
            SourceTenantId = db.TenantFilterEffectiveId == Guid.Empty ? null : db.TenantFilterEffectiveId,
            RequestedPersonId = db.WorkspacePersonId
        };
        db.Add(export);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) when (requestScope != null)
        {
            db.Entry(export).State = EntityState.Detached;
            var winner = await FindSnapshotRequestAsync(db, requestScope, cancellationToken);
            if (winner == null) throw;
            return ReplayExport(winner, previewId);
        }
        // The durable request is committed; client cancellation must not strand it before queue handoff.
        await _exportChannel.Writer.WriteAsync(new SspExportJob(export.Id, systemId, "json", null, userId), CancellationToken.None);
        return export;
    }

    private static SspExport ReplayExport(SspExport prior, Guid previewId)
    {
        if (prior.SourcePreviewId != previewId)
            throw new DbUpdateConcurrencyException("Idempotency-Key already identifies a different retained preview export.");
        return prior;
    }

    private static Task<SspExport?> FindSnapshotRequestAsync(AtoCopilotContext db, string? requestScope, CancellationToken ct) =>
        requestScope == null ? Task.FromResult<SspExport?>(null)
            : db.SspExports.AsNoTracking().SingleOrDefaultAsync(x => x.RequestScopeKey == requestScope, ct);

    private static async Task<string?> SnapshotRequestScopeAsync(AtoCopilotContext db, string systemId, string actor,
        string operation, string? key, CancellationToken ct)
    {
        if (key == null) return null;
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new ArgumentException("Idempotency-Key must contain 1-100 characters.");
        var tenant = await db.RegisteredSystems.AsNoTracking().Where(s => s.Id == systemId && s.IsActive)
            .Select(s => (Guid?)s.TenantId).SingleOrDefaultAsync(ct)
            ?? throw new ArgumentException("System not found in this workspace.");
        return ApprovedProfileDocumentData.Hash(JsonSerializer.Serialize(new
        {
            Tenant = tenant, System = systemId, Actor = actor, Person = db.WorkspacePersonId, Operation = operation, Key = key
        }));
    }

    private async Task<DocumentPreviewDto> PreviewResponseAsync(SspExport snapshot, CancellationToken ct)
    {
        if (snapshot.Status != "Preview" || snapshot.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new DbUpdateConcurrencyException("The original preview has expired or is unavailable. Use a new key only for an explicit new preview.");
        await ValidateCurrentEvidenceAsync(snapshot.SourceManifestJson, snapshot.SystemId, ct);
        var bytes = await ReadPreviewBytesAsync(snapshot, ct);
        return new(snapshot.SystemId, "json", "application/json", Encoding.UTF8.GetString(bytes),
            snapshot.ContentHash!, snapshot.GeneratedAt,
            JsonSerializer.Deserialize<DocumentSourceGapDto[]>(snapshot.SourceGapsJson ?? "[]") ?? [])
        {
            PreviewId = snapshot.Id,
            SourceManifest = snapshot.SourceManifestJson == null ? null : JsonSerializer.Deserialize<DocumentSourceManifest>(snapshot.SourceManifestJson),
            SourceState = snapshot.SourceManifestJson != null &&
                JsonSerializer.Deserialize<DocumentSourceManifest>(snapshot.SourceManifestJson)?.HasWorkingProfileSources == false
                ? "ApprovedSources" : "CurrentWorkingData"
        };
    }

    private static async Task<SspExport> RequirePreviewAsync(AtoCopilotContext db, string systemId, Guid previewId, CancellationToken ct)
    {
        var preview = await db.SspExports.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == previewId && x.SystemId == systemId && x.Format == "json" && x.Status == "Preview", ct)
            ?? throw new ArgumentException("Preview not found for this system.");
        if (preview.ExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("Preview has expired; generate a new preview.");
        var manifest = preview.SourceManifestJson is null ? null : JsonSerializer.Deserialize<DocumentSourceManifest>(preview.SourceManifestJson);
        if (manifest?.HasWorkingProfileSources == true)
            throw new ArgumentException("Working profile previews are review-only and cannot be promoted to final exports. Generate from approved sources instead.");
        var gaps = JsonSerializer.Deserialize<DocumentSourceGapDto[]>(preview.SourceGapsJson ?? "[]") ?? [];
        if (gaps.Any(x => x.Code != "OSCAL_SOURCE_WARNING"))
            throw new ArgumentException("Preview has unresolved source/approval gaps; review source metadata and generate a new preview.");
        return preview;
    }

    private static async Task RequireCurrentDesignAsync(IServiceProvider services, string? manifestJson, string systemId, CancellationToken ct)
    {
        var manifest = manifestJson == null ? null : JsonSerializer.Deserialize<DocumentSourceManifest>(manifestJson);
        if (manifest?.Design == null) return;
        var design = await SystemDesignDocumentData.LoadAsync(services, systemId, ct)
            ?? throw new InvalidOperationException("DESIGN_APPROVAL_UNVERIFIED: Retained design source is unavailable.");
        if (design.Source != manifest.Design || manifest.DesignArtifacts == null ||
            !design.ArtifactSources.SequenceEqual(manifest.DesignArtifacts))
            throw new InvalidOperationException("DESIGN_SOURCE_CHANGED: Create a fresh approved preview after reviewing changed design sources or artifacts.");
    }

    private async Task<byte[]> ReadPreviewBytesAsync(SspExport preview, CancellationToken ct)
    {
        if (preview.FilePath == null) throw new InvalidOperationException("Retained preview content is unavailable.");
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(Path.Combine(_settings.ExportsPath, preview.FilePath), ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw PreviewStorageUnavailable(ex);
        }
        if (ComputeSha256(bytes) != preview.ContentHash) throw new InvalidOperationException("Retained preview content hash mismatch.");
        return bytes;
    }

    private void DeletePreviewFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw PreviewStorageUnavailable(ex);
        }
    }

    private IOException PreviewStorageUnavailable(UnauthorizedAccessException exception)
    {
        _logger.LogError(exception, "Retained preview storage is inaccessible under {ExportDirectory}", _settings.ExportsPath);
        return new IOException("Retained preview storage is unavailable. Check ExportSettings:DataPath and storage permissions.", exception);
    }
}
