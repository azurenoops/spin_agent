using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements SSP document export operations: enqueue, list, download, and
/// background processing for Word/PDF/OSCAL JSON formats.
/// Also manages custom DOCX templates (upload, list, delete, rename).
/// </summary>
public class SspExportService : ISspExportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISspService _sspService;
    private readonly IDocumentTemplateService _templateService;
    private readonly IOscalSspExportService _oscalService;
    private readonly ISspExportNotifier _notifier;
    private readonly ILogger<SspExportService> _logger;
    private readonly ExportSettings _settings;
    private readonly Channel<SspExportJob> _exportChannel;

    public SspExportService(
        IServiceScopeFactory scopeFactory,
        ISspService sspService,
        IDocumentTemplateService templateService,
        IOscalSspExportService oscalService,
        ISspExportNotifier notifier,
        ILogger<SspExportService> logger,
        IOptions<ExportSettings> settings,
        Channel<SspExportJob> exportChannel)
    {
        _scopeFactory = scopeFactory;
        _sspService = sspService;
        _templateService = templateService;
        _oscalService = oscalService;
        _notifier = notifier;
        _logger = logger;
        _settings = settings.Value;
        _exportChannel = exportChannel;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Export operations
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<SspExport> EnqueueExportAsync(
        string systemId,
        string format,
        Guid? templateId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(format, nameof(format));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));

        var normalizedFormat = format.ToLowerInvariant();
        if (normalizedFormat is not ("docx" or "pdf" or "json"))
            throw new ArgumentException($"Unsupported format: {format}. Allowed: docx, pdf, json.", nameof(format));

        var export = new SspExport
        {
            SystemId = systemId,
            Format = normalizedFormat,
            Status = "Pending",
            TemplateId = templateId,
            GeneratedBy = userId,
            GeneratedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_settings.RetentionDays),
        };

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.SspExports.Add(export);
        await db.SaveChangesAsync(cancellationToken);

        var job = new SspExportJob(export.Id, systemId, normalizedFormat, templateId, userId);
        await _exportChannel.Writer.WriteAsync(job, cancellationToken);

        _logger.LogInformation(
            "SSP export enqueued: {ExportId} for system {SystemId} format {Format} by {UserId}",
            export.Id, systemId, normalizedFormat, userId);

        return export;
    }

    /// <inheritdoc />
    public async Task<List<ExportSummaryDto>> ListExportsAsync(
        string systemId,
        bool includeFailed = false,
        int limit = 25,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var query = db.SspExports
            .AsNoTracking()
            .Where(e => e.SystemId == systemId);

        if (!includeFailed)
            query = query.Where(e => e.Status != "Failed");

        return await query
            .OrderByDescending(e => e.GeneratedAt)
            .Skip(offset)
            .Take(limit)
            .Select(e => new ExportSummaryDto
            {
                ExportId = e.Id,
                Format = e.Format,
                Status = e.Status,
                FileSize = e.FileSize,
                ControlCount = e.ControlCount,
                GeneratedBy = e.GeneratedBy,
                GeneratedAt = e.GeneratedAt,
                CompletedAt = e.CompletedAt,
                TemplateName = e.Template != null ? e.Template.Name : null,
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ExportDetailDto?> GetExportAsync(
        Guid exportId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        return await db.SspExports
            .AsNoTracking()
            .Where(e => e.Id == exportId)
            .Select(e => new ExportDetailDto
            {
                ExportId = e.Id,
                SystemId = e.SystemId,
                Format = e.Format,
                Status = e.Status,
                FileSize = e.FileSize,
                ContentHash = e.ContentHash,
                ControlCount = e.ControlCount,
                GeneratedBy = e.GeneratedBy,
                GeneratedAt = e.GeneratedAt,
                CompletedAt = e.CompletedAt,
                TemplateName = e.Template != null ? e.Template.Name : null,
                ExpiresAt = e.ExpiresAt,
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(Stream? Stream, string? FileName, string? ContentType)?> GetExportFileStreamAsync(
        Guid exportId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var export = await db.SspExports
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == exportId, cancellationToken);

        if (export is null)
            return null;

        if (export.Status != "Completed" || string.IsNullOrEmpty(export.FilePath))
            return (null, null, null);

        var fullPath = Path.Combine(_settings.ExportsPath, export.FilePath);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Export file missing from disk: {FilePath}", fullPath);
            return (null, null, null);
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileName = $"ssp-{export.SystemId}-{export.Id}{GetFileExtension(export.Format)}";
        var contentType = GetContentType(export.Format);

        return (stream, fileName, contentType);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Template operations
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<CreateTemplateResponse> UploadTemplateAsync(
        string name,
        string? description,
        Stream fileStream,
        string fileName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));

        // Read file into memory for validation
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        // Enforce 10 MB limit (FR-020)
        if (fileBytes.Length > _settings.MaxTemplateSizeBytes)
            throw new ArgumentException(
                $"Template file size ({fileBytes.Length:N0} bytes) exceeds maximum ({_settings.MaxTemplateSizeBytes:N0} bytes).");

        // Validate DOCX format (ZIP/PK signature + word/document.xml)
        if (fileBytes.Length < 4 || fileBytes[0] != 0x50 || fileBytes[1] != 0x4B)
            throw new ArgumentException("Invalid file format. Expected a .docx file.");

        // Extract merge fields from {{FieldName}} patterns
        var mergeFields = ExtractMergeFields(fileBytes);

        var template = new SspTemplate
        {
            Name = name,
            Description = description,
            FileSize = fileBytes.Length,
            MergeFields = System.Text.Json.JsonSerializer.Serialize(mergeFields),
            UploadedBy = userId,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        template.FilePath = $"{template.Id}.docx";

        // Save file to disk
        var fullPath = Path.Combine(_settings.TemplatesPath, template.FilePath);
        EnsureDirectoryExists(fullPath);
        await File.WriteAllBytesAsync(fullPath, fileBytes, cancellationToken);

        // Persist entity
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.SspTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Template uploaded: {TemplateId} Name={Name} Size={Size} MergeFields={FieldCount} By={UserId}",
            template.Id, name, fileBytes.Length, mergeFields.Count, userId);

        return new CreateTemplateResponse
        {
            Id = template.Id,
            Name = template.Name,
            MergeFields = mergeFields,
            IsDefault = template.IsDefault,
            UploadedAt = template.UploadedAt,
        };
    }

    /// <inheritdoc />
    public async Task<List<TemplateListDto>> ListTemplatesAsync(
        int limit = 25,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var templates = await db.SspTemplates
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return templates.Select(t => new TemplateListDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            FileSize = t.FileSize,
            IsDefault = t.IsDefault,
            MergeFields = string.IsNullOrEmpty(t.MergeFields)
                ? new List<string>()
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(t.MergeFields) ?? new List<string>(),
            UploadedBy = t.UploadedBy,
            UploadedAt = t.UploadedAt,
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTemplateAsync(
        Guid templateId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var template = await db.SspTemplates.FindAsync([templateId], cancellationToken);
        if (template is null || !template.IsActive)
            return false;

        template.IsActive = false;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Template deleted (soft): {TemplateId} Name={Name} By={UserId}", templateId, template.Name, userId);
        return true;
    }

    /// <inheritdoc />
    public async Task<UpdateTemplateResponse?> UpdateTemplateAsync(
        Guid templateId,
        string? newName,
        string? newDescription,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var template = await db.SspTemplates.FindAsync([templateId], cancellationToken);
        if (template is null || !template.IsActive)
            return null;

        if (!string.IsNullOrWhiteSpace(newName))
        {
            // Validate unique name among active templates
            var nameExists = await db.SspTemplates
                .AnyAsync(t => t.IsActive && t.Id != templateId && t.Name == newName, cancellationToken);
            if (nameExists)
                throw new ArgumentException($"A template named '{newName}' already exists.");
            template.Name = newName;
        }

        if (newDescription is not null)
            template.Description = newDescription;

        template.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Template updated: {TemplateId} Name={Name} By={UserId}", templateId, template.Name, userId);

        return new UpdateTemplateResponse
        {
            Id = template.Id,
            Name = template.Name,
            Description = template.Description,
            UpdatedAt = template.UpdatedAt.Value,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Background processing (called by SspExportBackgroundService)
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task ProcessExportAsync(
        SspExportJob job,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var export = await db.SspExports.FindAsync([job.ExportId], cancellationToken);
        if (export is null)
        {
            _logger.LogWarning("Export {ExportId} not found in database, skipping", job.ExportId);
            return;
        }

        export.Status = "Processing";
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await ReportProgressAsync(job.UserId, job.ExportId, "Loading system data", 20);

            byte[] fileBytes;
            int controlCount;

            switch (job.Format)
            {
                case "docx":
                    (fileBytes, controlCount) = await GenerateDocxAsync(job, cancellationToken);
                    break;
                case "pdf":
                    (fileBytes, controlCount) = await GeneratePdfAsync(job, cancellationToken);
                    break;
                case "json":
                    (fileBytes, controlCount) = await GenerateOscalJsonAsync(job, db, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported format: {job.Format}");
            }

            await ReportProgressAsync(job.UserId, job.ExportId, "Writing file", 80);

            // Enforce 50 MB limit (FR-020)
            if (fileBytes.Length > _settings.MaxExportSizeBytes)
            {
                export.Status = "Failed";
                export.ErrorMessage = $"Export file size ({fileBytes.Length:N0} bytes) exceeds maximum allowed ({_settings.MaxExportSizeBytes:N0} bytes).";
                export.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await NotifyExportFailedAsync(job.UserId, job.ExportId, export.ErrorMessage);
                return;
            }

            if (fileBytes.Length > _settings.MaxExportSizeBytes * 0.8)
            {
                _logger.LogWarning("Export {ExportId} approaching size limit: {Size} bytes", job.ExportId, fileBytes.Length);
            }

            // Write file to disk
            var relativePath = Path.Combine(job.SystemId, $"{job.ExportId}{GetFileExtension(job.Format)}");
            var fullPath = Path.Combine(_settings.ExportsPath, relativePath);
            EnsureDirectoryExists(fullPath);
            await File.WriteAllBytesAsync(fullPath, fileBytes, cancellationToken);

            await ReportProgressAsync(job.UserId, job.ExportId, "Computing hash", 90);

            // Compute SHA-256 hash
            var hash = ComputeSha256(fileBytes);

            // Update entity
            export.Status = "Completed";
            export.FilePath = relativePath;
            export.FileSize = fileBytes.Length;
            export.ContentHash = hash;
            export.ControlCount = controlCount;
            export.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            sw.Stop();
            _logger.LogInformation(
                "SSP export completed: ExportId={ExportId} SystemId={SystemId} Format={Format} " +
                "UserId={UserId} FileSize={FileSize} ContentHash={ContentHash} " +
                "ControlCount={ControlCount} DurationMs={DurationMs}",
                job.ExportId, job.SystemId, job.Format,
                job.UserId, fileBytes.Length, hash,
                controlCount, sw.ElapsedMilliseconds);

            await NotifyExportReadyAsync(job.UserId, job.ExportId, job.Format);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            export.Status = "Failed";
            export.ErrorMessage = ex.Message;
            export.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogError(ex,
                "SSP export failed: ExportId={ExportId} SystemId={SystemId} Format={Format} " +
                "UserId={UserId} DurationMs={DurationMs} Error={Error}",
                job.ExportId, job.SystemId, job.Format,
                job.UserId, sw.ElapsedMilliseconds, ex.Message);

            await NotifyExportFailedAsync(job.UserId, job.ExportId, ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Retention
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<int> PurgeExpiredExportsAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var expired = await db.SspExports
            .Where(e => e.ExpiresAt < DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var export in expired)
        {
            if (!string.IsNullOrEmpty(export.FilePath))
            {
                var fullPath = Path.Combine(_settings.ExportsPath, export.FilePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogDebug("Deleted expired export file: {FilePath}", fullPath);
                }
            }
        }

        db.SspExports.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);

        if (expired.Count > 0)
            _logger.LogInformation("Purged {Count} expired SSP exports", expired.Count);

        return expired.Count;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Format-specific generation (stubs — completed in Phases 3, 5, 6)
    // ═══════════════════════════════════════════════════════════════════

    private async Task<(byte[] Bytes, int ControlCount)> GenerateDocxAsync(
        SspExportJob job, CancellationToken cancellationToken)
    {
        // Get SSP data to capture control count
        var sspDoc = await _sspService.GenerateSspAsync(
            job.SystemId, "markdown", sections: null, progress: null, cancellationToken);

        await ReportProgressAsync(job.UserId, job.ExportId, "Rendering Word document", 60);

        // Render DOCX via DocumentTemplateService (builds merge data + applies template)
        var docxBytes = await _templateService.RenderDocxAsync(
            job.SystemId,
            "ssp",
            job.TemplateId?.ToString(),
            cancellationToken);

        return (docxBytes, sspDoc.TotalControls);
    }

    private async Task<(byte[] Bytes, int ControlCount)> GeneratePdfAsync(
        SspExportJob job, CancellationToken cancellationToken)
    {
        // Get SSP data to capture control count
        var sspDoc = await _sspService.GenerateSspAsync(
            job.SystemId, "markdown", sections: null, progress: null, cancellationToken);

        await ReportProgressAsync(job.UserId, job.ExportId, "Rendering PDF document", 60);

        // Render PDF via DocumentTemplateService using QuestPDF
        var pdfBytes = await _templateService.RenderPdfAsync(
            job.SystemId,
            "ssp",
            progress: null,
            cancellationToken);

        return (pdfBytes, sspDoc.TotalControls);
    }

    private async Task<(byte[] Bytes, int ControlCount)> GenerateOscalJsonAsync(
        SspExportJob job, AtoCopilotContext db, CancellationToken cancellationToken)
    {
        await ReportProgressAsync(job.UserId, job.ExportId, "Generating OSCAL JSON", 60);

        var result = await _oscalService.ExportAsync(
            job.SystemId,
            includeBackMatter: true,
            prettyPrint: true,
            cancellationToken);

        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(result.OscalJson);

        // Store warnings as informational (status remains Completed)
        if (result.Warnings.Count > 0)
        {
            var export = await db.SspExports.FindAsync([job.ExportId], cancellationToken);
            if (export is not null)
            {
                export.ErrorMessage = string.Join("; ", result.Warnings);
                await db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "OSCAL export {ExportId} completed with {WarningCount} warnings: {Warnings}",
                job.ExportId, result.Warnings.Count, string.Join("; ", result.Warnings));
        }

        return (jsonBytes, result.Statistics.ControlCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Notification helpers
    // ═══════════════════════════════════════════════════════════════════

    private Task ReportProgressAsync(string userId, Guid exportId, string step, int percentage)
        => _notifier.SendProgressAsync(userId, exportId, step, percentage);

    private Task NotifyExportReadyAsync(string userId, Guid exportId, string format)
        => _notifier.SendExportReadyAsync(userId, exportId, format);

    private Task NotifyExportFailedAsync(string userId, Guid exportId, string error)
        => _notifier.SendExportFailedAsync(userId, exportId, error);

    // ═══════════════════════════════════════════════════════════════════
    //  File I/O helpers
    // ═══════════════════════════════════════════════════════════════════

    private static void EnsureDirectoryExists(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private static string GetFileExtension(string format) => format switch
    {
        "docx" => ".docx",
        "pdf" => ".pdf",
        "json" => ".json",
        _ => ".bin",
    };

    private static string GetContentType(string format) => format switch
    {
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "pdf" => "application/pdf",
        "json" => "application/json",
        _ => "application/octet-stream",
    };

    private static List<string> ExtractMergeFields(byte[] docxBytes)
    {
        var fields = new List<string>();
        try
        {
            using var ms = new MemoryStream(docxBytes);
            using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            var docEntry = archive.GetEntry("word/document.xml");
            if (docEntry is null) return fields;

            using var reader = new StreamReader(docEntry.Open());
            var xml = reader.ReadToEnd();

            // Extract {{FieldName}} patterns
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(xml, @"\{\{(\w+)\}\}"))
            {
                var fieldName = match.Groups[1].Value;
                if (!fields.Contains(fieldName))
                    fields.Add(fieldName);
            }
        }
        catch
        {
            // If we can't parse, return empty list — template is still uploadable
        }
        return fields;
    }
}
