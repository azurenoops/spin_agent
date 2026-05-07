using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Templates.Validators;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Templates;

/// <summary>
/// Step 6 service: org-template upload, list, replace, mark-default, delete.
/// Enforces FR-085 default-uniqueness, FR-088 size cap, FR-081 format
/// pairing, FR-094 cascade-flag-stale on replace, FR-096 deletion guards.
/// </summary>
public sealed class OrganizationTemplateService : IOrganizationTemplateService
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly IFileStorageProvider _storage;
    private readonly IWizardAuditService _audit;
    private readonly IWizardArtifactDependencyService _dependencies;
    private readonly IOptions<OnboardingOptions> _options;
    private readonly ILogger<OrganizationTemplateService> _log;
    private readonly Dictionary<TemplateType, IOrganizationTemplateValidator> _validators;

    public OrganizationTemplateService(
        IDbContextFactory<AtoCopilotContext> factory,
        IFileStorageProvider storage,
        IWizardAuditService audit,
        IWizardArtifactDependencyService dependencies,
        IOptions<OnboardingOptions> options,
        ILogger<OrganizationTemplateService> log)
    {
        _factory = factory;
        _storage = storage;
        _audit = audit;
        _dependencies = dependencies;
        _options = options;
        _log = log;
        _validators = new Dictionary<TemplateType, IOrganizationTemplateValidator>
        {
            [TemplateType.Ssp] = new DocxTemplateValidator(new[]
            {
                "{{system_name}}", "{{system_id}}", "{{baseline}}", "{{controls}}",
            }),
            [TemplateType.Sar] = new DocxTemplateValidator(new[]
            {
                "{{system_name}}", "{{assessment_date}}", "{{findings}}",
            }),
            [TemplateType.Sap] = new DocxTemplateValidator(new[]
            {
                "{{system_name}}", "{{assessment_scope}}", "{{methodology}}",
            }),
            [TemplateType.Crm] = new XlsxTemplateValidator(new[]
            {
                "Control ID", "Title", "Responsibility",
            }),
            [TemplateType.HwSwInventory] = new XlsxTemplateValidator(new[]
            {
                "Asset Name", "Type", "Owner",
            }),
        };
    }

    private static TemplateFileFormat FormatFor(TemplateType t)
        => t == TemplateType.Crm || t == TemplateType.HwSwInventory
            ? TemplateFileFormat.Xlsx
            : TemplateFileFormat.Docx;

    public async Task<IReadOnlyList<OrganizationDocumentTemplate>> ListAsync(
        Guid tenantId, TemplateType? templateType = null,
        bool includeDeleted = false, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.OrganizationDocumentTemplates.AsQueryable()
            .Where(t => t.TenantId == tenantId);
        if (!includeDeleted) q = q.Where(t => t.DeletedAt == null);
        if (templateType.HasValue) q = q.Where(t => t.TemplateType == templateType.Value);
        return await q.OrderByDescending(t => t.UpdatedAt).ToListAsync(ct);
    }

    public async Task<OrganizationDocumentTemplate?> GetAsync(
        Guid tenantId, Guid templateId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.OrganizationDocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == templateId, ct);
    }

    public async Task<TemplateUploadResult> UploadAsync(
        Guid tenantId, Guid actorUserId, TemplateType templateType,
        string label, string version, string originalFileName,
        Stream content, long lengthBytes, bool isDefault,
        CancellationToken ct = default)
    {
        var maxBytes = _options.Value.Limits.MaxDocumentTemplateBytes;
        if (lengthBytes > maxBytes)
            throw new InvalidOperationException(WizardErrorCodes.TemplateTooLarge);

        var expectedFormat = FormatFor(templateType);
        var ext = (Path.GetExtension(originalFileName) ?? string.Empty).TrimStart('.').ToLowerInvariant();
        var actualFormat = ext switch
        {
            "docx" => TemplateFileFormat.Docx,
            "xlsx" => TemplateFileFormat.Xlsx,
            _ => (TemplateFileFormat?)null,
        };
        if (actualFormat is null || actualFormat != expectedFormat)
            throw new InvalidOperationException(WizardErrorCodes.TemplateWrongFormat);

        // Buffer once: we read for hash + validator + persistence.
        await using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        ms.Position = 0;
        var checksum = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();

        ms.Position = 0;
        var validator = _validators[templateType];
        var validation = await validator.ValidateAsync(ms, originalFileName, ct);

        var template = new OrganizationDocumentTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TemplateType = templateType,
            Label = label,
            Version = version,
            OriginalFileName = originalFileName,
            FileFormat = expectedFormat,
            FileSizeBytes = lengthBytes,
            ContentChecksumSha256 = checksum,
            IsDefault = false, // promote after row exists
            ValidationStatus = validation.IsCompliant
                ? TemplateValidationStatus.Compliant
                : TemplateValidationStatus.FlaggedNonCompliant,
            ValidationWarnings = validation.Warnings.Count == 0
                ? null
                : JsonSerializer.Serialize(validation.Warnings),
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        template.StorageBlobKey = WizardStorageKeys.Template(tenantId, template.Id, originalFileName);

        ms.Position = 0;
        await _storage.SaveAsync(template.StorageBlobKey, ms, GetMime(expectedFormat), ct);

        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            db.OrganizationDocumentTemplates.Add(template);
            await db.SaveChangesAsync(ct);
        }

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.TemplateUploaded,
            $"Template/{template.Id:D}", null, null,
            JsonSerializer.Serialize(new
            {
                template.Id,
                template.TemplateType,
                template.Label,
                template.Version,
                template.ValidationStatus,
            }),
            null, Guid.NewGuid(), ct);

        if (isDefault)
        {
            await MarkDefaultAsync(tenantId, template.Id, actorUserId, ct);
        }

        return new TemplateUploadResult(template, validation.Warnings);
    }

    public async Task<OrganizationDocumentTemplate> PatchMetadataAsync(
        Guid tenantId, Guid templateId, Guid actorUserId,
        string? label, string? version, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.OrganizationDocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == templateId, ct)
            ?? throw new KeyNotFoundException("Template not found.");
        if (!string.IsNullOrWhiteSpace(label)) entity.Label = label!;
        if (!string.IsNullOrWhiteSpace(version)) entity.Version = version!;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.TemplateRenamed,
            $"Template/{templateId:D}", null, null,
            JsonSerializer.Serialize(new { entity.Label, entity.Version }),
            null, Guid.NewGuid(), ct);
        return entity;
    }

    public async Task DeleteAsync(
        Guid tenantId, Guid templateId, Guid actorUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.OrganizationDocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == templateId, ct)
            ?? throw new KeyNotFoundException("Template not found.");
        if (entity.IsDefault)
            throw new InvalidOperationException(WizardErrorCodes.TemplateDefaultProtected);

        entity.Status = TemplateStatus.Deleted;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = entity.DeletedAt.Value;
        entity.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.TemplateDeleted,
            $"Template/{templateId:D}", null, null, null,
            null, Guid.NewGuid(), ct);
    }

    public async Task<Stream?> DownloadAsync(
        Guid tenantId, Guid templateId, CancellationToken ct = default)
    {
        var entity = await GetAsync(tenantId, templateId, ct);
        if (entity is null) return null;
        return await _storage.GetAsync(entity.StorageBlobKey, ct);
    }

    public async Task<TemplateReplaceResult> ReplaceFileAsync(
        Guid tenantId, Guid templateId, Guid actorUserId,
        string originalFileName, Stream content, long lengthBytes,
        string? version, CancellationToken ct = default)
    {
        var maxBytes = _options.Value.Limits.MaxDocumentTemplateBytes;
        if (lengthBytes > maxBytes)
            throw new InvalidOperationException(WizardErrorCodes.TemplateTooLarge);

        await using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        ms.Position = 0;
        var checksum = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();

        OrganizationDocumentTemplate entity;
        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            entity = await db.OrganizationDocumentTemplates
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == templateId, ct)
                ?? throw new KeyNotFoundException("Template not found.");

            ms.Position = 0;
            var validator = _validators[entity.TemplateType];
            var validation = await validator.ValidateAsync(ms, originalFileName, ct);

            entity.OriginalFileName = originalFileName;
            entity.FileSizeBytes = lengthBytes;
            entity.ContentChecksumSha256 = checksum;
            entity.StorageBlobKey = WizardStorageKeys.Template(tenantId, entity.Id, originalFileName);
            if (!string.IsNullOrWhiteSpace(version)) entity.Version = version!;
            entity.ValidationStatus = validation.IsCompliant
                ? TemplateValidationStatus.Compliant
                : TemplateValidationStatus.FlaggedNonCompliant;
            entity.ValidationWarnings = validation.Warnings.Count == 0
                ? null
                : JsonSerializer.Serialize(validation.Warnings);
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedBy = actorUserId;

            ms.Position = 0;
            await _storage.SaveAsync(entity.StorageBlobKey, ms, GetMime(entity.FileFormat), ct);
            await db.SaveChangesAsync(ct);
        }

        var dependentsFlagged = await _dependencies.FlagDependentsStaleAsync(
            tenantId, ArtifactSourceKind.Template, entity.Id,
            $"Template '{entity.Label}' was replaced ({checksum[..8]}…)", ct);

        var dependents = await _dependencies.ListBySourceAsync(
            tenantId, ArtifactSourceKind.Template, entity.Id, page: 1, pageSize: 100, ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.TemplateReplaced,
            $"Template/{entity.Id:D}", null, null,
            JsonSerializer.Serialize(new
            {
                entity.Version,
                entity.ValidationStatus,
                dependentsFlagged,
            }),
            null, Guid.NewGuid(), ct);

        return new TemplateReplaceResult(
            entity, dependentsFlagged,
            dependents.Select(d => d.Id).ToList());
    }

    public async Task<OrganizationDocumentTemplate> MarkDefaultAsync(
        Guid tenantId, Guid templateId, Guid actorUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var supportsTx = db.Database.IsRelational();
        await using var tx = supportsTx
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        var target = await db.OrganizationDocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == templateId, ct)
            ?? throw new KeyNotFoundException("Template not found.");

        var prevDefaults = await db.OrganizationDocumentTemplates
            .Where(t => t.TenantId == tenantId
                     && t.TemplateType == target.TemplateType
                     && t.IsDefault
                     && t.Id != target.Id)
            .ToListAsync(ct);
        foreach (var p in prevDefaults)
        {
            p.IsDefault = false;
            p.UpdatedAt = DateTimeOffset.UtcNow;
            p.UpdatedBy = actorUserId;
        }
        target.IsDefault = true;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        target.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.TemplateMarkedDefault,
            $"Template/{templateId:D}", null, null,
            JsonSerializer.Serialize(new { target.TemplateType, target.Label }),
            null, Guid.NewGuid(), ct);

        return target;
    }

    public async Task ClearDefaultAsync(
        Guid tenantId, Guid templateId, Guid actorUserId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.OrganizationDocumentTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == templateId, ct)
            ?? throw new KeyNotFoundException("Template not found.");
        if (!entity.IsDefault) return;
        entity.IsDefault = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = actorUserId;
        await db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            tenantId, actorUserId, WizardAuditAction.TemplateMarkedDefault,
            $"Template/{templateId:D}/clear", null, null, null,
            null, Guid.NewGuid(), ct);
    }

    private static string GetMime(TemplateFileFormat fmt)
        => fmt == TemplateFileFormat.Docx
            ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
