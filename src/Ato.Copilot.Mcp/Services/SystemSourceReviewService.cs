using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Services;

/// <summary>Small selected-system source receipts over the existing import tables, storage and parsers.</summary>
public sealed class SystemSourceReviewService(
    IDbContextFactory<AtoCopilotContext> factory, ITenantContext tenant, SystemSetupService setup,
    IFileStorageProvider storage, IEmassImportParser emassParser, ISspPdfExtractionService pdfParser)
{
    public const long MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record StoredPreview(Guid Actor, SystemSourcePreview Preview);

    public async Task<(SystemSourceReceipt Receipt, bool Created)> UploadAsync(
        Guid tenantId, string systemId, string kind, string key, IFormFile file, CancellationToken ct)
    {
        await setup.EnsureManageAsync(tenantId, systemId, ct);
        ValidateKind(kind);
        ValidateKey(key);
        var actor = tenant.PersonId!.Value;
        var fileName = Path.GetFileName(file.FileName);
        if (fileName.Length is < 1 or > 200 || (kind == "emass" ? !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            : !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
            throw Error(415, "UNSUPPORTED_SOURCE_FORMAT", "Selected-system review supports an XLSX containing one system or a digital SSP PDF.");
        if (file.Length is <= 0 or > MaximumBytes)
            throw Error(413, "SOURCE_TOO_LARGE", "Small-file source review accepts up to 4 MiB. Larger/background imports are not enabled here.");
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var requestHash = Hash(new { tenantId, systemId, actor, kind, fileName, sha });
        await using var db = await factory.CreateDbContextAsync(ct);
        var source = await FindKey(db, tenantId, systemId, kind, actor, key, ct);
        var created = source is null;
        if (source is null)
        {
            var id = Guid.NewGuid();
            source = kind == "emass"
                ? new EmassImportSession { Id = id, Format = EmassImportFormat.Xlsx }
                : new SspPdfImportSession { Id = id, BatchId = Guid.NewGuid() };
            source.TenantId = tenantId; source.TargetSystemId = systemId;
            source.RequestKey = key; source.RequestPayloadHash = requestHash; source.RequestActorPersonId = actor;
            source.OriginalFileName = fileName; source.ContentChecksumSha256 = sha; source.FileSizeBytes = bytes.LongLength;
            source.CreatedBy = actor; source.UpdatedBy = actor;
            source.StorageBlobKey = kind == "emass"
                ? WizardStorageKeys.EmassImport(tenantId, id, fileName)
                : WizardStorageKeys.SspPdfImport(tenantId, id, fileName);
            db.Add(source);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                source = await FindKey(db, tenantId, systemId, kind, actor, key, ct);
                if (source is null) throw;
                created = false;
            }
        }
        if (source.RequestPayloadHash != requestHash)
            throw Error(409, "SOURCE_REQUEST_CONFLICT", "This request key already identifies different bytes, filename or target.");
        if (source.ReviewProposalJson is not null)
            return (SystemSourceReadProjection.Receipt(source, kind), false);

        buffer.Position = 0;
        await storage.SaveAsync(source.StorageBlobKey, buffer, kind == "emass"
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf", ct);
        buffer.Position = 0;
        SystemSourceAnalysis analysis;
        if (source is EmassImportSession emass)
        {
            var parsed = await emassParser.ParseAsync(buffer, fileName, ct);
            emass.Preview = JsonSerializer.Serialize(parsed, Json);
            var item = parsed.Systems.Count == 1 && parsed.Systems[0].MalformedReason is null ? parsed.Systems[0] : null;
            analysis = item is null ? new("failed", "A selected-system XLSX must contain exactly one well-formed system. No target was changed.", [])
                : new("parsed", null, IdentityFields(item.SystemName, item.SystemIdentifier));
            emass.Status = item is null ? EmassImportStatus.Failed : EmassImportStatus.Parsed;
        }
        else
        {
            var pdf = (SspPdfImportSession)source;
            var parsed = await pdfParser.ExtractAsync(buffer, fileName, ct);
            pdf.ExtractionResult = JsonSerializer.Serialize(parsed, Json);
            pdf.RejectReason = parsed.RejectReason;
            analysis = !parsed.IsAccepted ? new("failed", parsed.RejectMessage, []) : new("parsed", null,
                IdentityFields(parsed.Fields.FirstOrDefault(x => x.Name == "system_name")?.Value,
                    parsed.Fields.FirstOrDefault(x => x.Name == "system_identifier")?.Value)
                    .Concat(parsed.Fields.Where(x => x.Name is not ("system_name" or "system_identifier"))
                        .Select(x => new SystemSourceField(x.Name, x.Name, x.Value, false))).ToArray());
            pdf.Status = parsed.IsAccepted ? SspPdfStatus.Extracted : SspPdfStatus.Failed;
        }
        source.ReviewProposalJson = JsonSerializer.Serialize(analysis, Json);
        source.ReviewRevision = 1;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            source = await Find(db, tenantId, systemId, kind, source.Id, ct);
        }
        return (SystemSourceReadProjection.Receipt(source, kind), created);
    }

    public async Task<SystemSourceReceipt> GetAsync(Guid tenantId, string systemId, string kind, Guid id, CancellationToken ct)
    {
        await setup.GetAsync(tenantId, systemId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return SystemSourceReadProjection.Receipt(await Find(db, tenantId, systemId, kind, id, ct), kind);
    }

    public async Task<SystemSourceReceipt> RecoverAsync(Guid tenantId, string systemId, string kind, string key, CancellationToken ct)
    {
        await setup.EnsureManageAsync(tenantId, systemId, ct);
        ValidateKind(kind); ValidateKey(key);
        await using var db = await factory.CreateDbContextAsync(ct);
        var source = await FindKey(db, tenantId, systemId, kind, tenant.PersonId!.Value, key, ct) ?? throw Missing();
        return SystemSourceReadProjection.Receipt(source, kind);
    }

    public async Task<(byte[] Bytes, string FileName)> OriginalAsync(Guid tenantId, string systemId, string kind, Guid id, CancellationToken ct)
    {
        await setup.GetAsync(tenantId, systemId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var source = await Find(db, tenantId, systemId, kind, id, ct);
        await using var original = await storage.GetAsync(source.StorageBlobKey, ct)
            ?? throw Error(503, "SOURCE_BYTES_UNAVAILABLE", "The receipt exists but its original bytes are unavailable.");
        await using var buffer = new MemoryStream();
        await original.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.LongLength != source.FileSizeBytes ||
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != source.ContentChecksumSha256)
            throw Error(503, "SOURCE_CONTENT_MISMATCH", "Original source integrity verification failed.");
        return (bytes, source.OriginalFileName);
    }

    public async Task<SystemSourcePreview> PreviewAsync(Guid tenantId, string systemId, string kind, Guid id,
        SystemSourcePreviewRequest request, CancellationToken ct)
    {
        await setup.EnsureManageAsync(tenantId, systemId, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var source = await Find(db, tenantId, systemId, kind, id, ct);
        var analysis = RequireParsed(source, request.ExpectedSourceRevision);
        var system = await System(db, tenantId, systemId, ct);
        var fingerprint = Identity(system);
        var fields = analysis.Fields.Select(f => new SystemSourcePreviewField(f.Field,
            f.Field == "name" ? system.Name : f.Field == "acronym" ? system.Acronym : null, f.ProposedValue, f.Supported)).ToArray();
        var hash = Hash(new { source.Id, source.ContentChecksumSha256, source.ReviewRevision, systemId, fingerprint, fields });
        var preview = new SystemSourcePreview(id, systemId, source.ReviewRevision, source.ContentChecksumSha256, fingerprint, hash, fields);
        source.ReviewSnapshotJson = JsonSerializer.Serialize(new StoredPreview(tenant.PersonId!.Value, preview), Json);
        source.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return preview;
    }

    public async Task<SystemSourceReceipt> ApplyAsync(Guid tenantId, string systemId, string kind, Guid id,
        SystemSourceApplyRequest request, string key, CancellationToken ct)
    {
        await setup.EnsureManageAsync(tenantId, systemId, ct);
        ValidateKey(key);
        if (request.Decisions is null || request.Decisions.Count > 2 ||
            request.Decisions.Select(x => x.Field).Distinct().Count() != request.Decisions.Count ||
            request.Decisions.Any(x => x.Field is not ("name" or "acronym") || x.Decision is not ("keepCurrent" or "applyProposed")))
            throw Error(400, "INVALID_SOURCE_DECISIONS", "Explicitly keep or apply supported identity fields only.");
        var actor = tenant.PersonId!.Value;
        var intentHash = Hash(new { tenantId, systemId, actor, id, kind, request });
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var source = await Find(db, tenantId, systemId, kind, id, ct);
            if (source.ApplyReceiptJson is not null)
            {
                var receipt = JsonSerializer.Deserialize<SystemSourceApplyReceipt>(source.ApplyReceiptJson, Json)!;
                if (receipt.Key != key) throw Error(409, "SOURCE_ALREADY_APPLIED", "This source review was already applied.");
                if (receipt.IntentHash != intentHash || receipt.ActorPersonId != actor)
                    throw Error(409, "SOURCE_REQUEST_CONFLICT", "This key belongs to another source decision.");
                await transaction.CommitAsync(ct);
                return SystemSourceReadProjection.Receipt(source, kind);
            }
            var analysis = RequireParsed(source, request.ExpectedSourceRevision);
            var system = await System(db, tenantId, systemId, ct);
            var snapshot = source.ReviewSnapshotJson is null ? null : JsonSerializer.Deserialize<StoredPreview>(source.ReviewSnapshotJson, Json);
            if (snapshot is null || snapshot.Actor != actor || snapshot.Preview.PreviewHash != request.PreviewHash ||
                snapshot.Preview.IdentityRevision != request.ExpectedSystemRevision || Identity(system) != request.ExpectedSystemRevision)
                throw Error(409, "STALE_IMPORT_REVIEW", "Review this exact source and current target again before applying.");
            if (analysis.Fields.Where(x => x.Supported).Any(field => !request.Decisions.Any(d => d.Field == field.Field)))
                throw Error(400, "INVALID_SOURCE_DECISIONS", "Choose keep or apply for each supported identity field.");
            var name = system.Name;
            var acronym = system.Acronym;
            foreach (var decision in request.Decisions.Where(x => x.Decision == "applyProposed"))
            {
                var field = analysis.Fields.SingleOrDefault(x => x.Field == decision.Field && x.Supported)
                    ?? throw Error(400, "UNSUPPORTED_IMPORT_FIELD", "This extracted value is not supported for application.");
                if (field.Field == "name") name = field.ProposedValue!;
                else acronym = field.ProposedValue;
            }
            if ((name != system.Name || acronym != system.Acronym) &&
                (await db.SspSections.AnyAsync(x => x.RegisteredSystemId == systemId && x.Status == SspSectionStatus.Approved, ct) ||
                 await db.SystemProfileSections.AnyAsync(x => x.RegisteredSystemId == systemId && x.GovernanceStatus == SspSectionStatus.Approved, ct)))
                throw Error(409, "SOURCE_REQUIRES_DOCUMENT_REVIEW", "Approved documentation exists. Stage changes in the governed document workflow; its baseline was not changed.");
            var oldName = system.Name;
            var oldAcronym = system.Acronym;
            var now = DateTime.UtcNow;
            var affected = await db.RegisteredSystems.Where(x => x.TenantId == tenantId && x.Id == systemId && x.IsActive &&
                    x.Name == oldName && x.Acronym == oldAcronym && x.Description == system.Description && x.SetupRevision == system.SetupRevision)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, name).SetProperty(x => x.Acronym, acronym)
                    .SetProperty(x => x.ModifiedAt, now).SetProperty(x => x.SetupRevision, x => x.SetupRevision + 1)
                    .SetProperty(x => x.SetupLastCommandJson, (string?)null), ct);
            if (affected != 1) throw Error(409, "STALE_IMPORT_REVIEW", "The target changed while applying this review.");
            source.ApplyReceiptJson = JsonSerializer.Serialize(new SystemSourceApplyReceipt(key, intentHash, actor, DateTimeOffset.UtcNow,
                systemId, source.ReviewRevision, source.ContentChecksumSha256, oldName, oldAcronym, name, acronym), Json);
            source.UpdatedAt = DateTimeOffset.UtcNow; source.UpdatedBy = actor;
            if (source is EmassImportSession emass) emass.Status = EmassImportStatus.Imported;
            else ((SspPdfImportSession)source).Status = SspPdfStatus.Imported;
            db.DashboardActivities.Add(new() { TenantId = tenantId, RegisteredSystemId = systemId,
                Actor = actor.ToString(), EventType = "SystemSourceIdentityReviewed",
                Summary = "Applied explicit source identity decisions; existing approvals and authorization standing were not changed.",
                RelatedEntityType = kind, RelatedEntityId = id.ToString() });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return SystemSourceReadProjection.Receipt(source, kind);
        });
    }

    private static IReadOnlyList<SystemSourceField> IdentityFields(string? name, string? identifier) =>
    [
        new("name", "system_name", name, !string.IsNullOrWhiteSpace(name) && name.Length <= 200),
        new("acronym", "system_identifier", identifier, !string.IsNullOrWhiteSpace(identifier) && identifier.Length <= 20)
    ];
    private static SystemSourceAnalysis RequireParsed(ISystemSourceSession source, long revision)
    {
        if (source.ApplyReceiptJson is not null) throw Error(409, "SOURCE_ALREADY_APPLIED", "This receipt has an immutable applied review.");
        if (source.ReviewRevision != revision) throw Error(409, "STALE_IMPORT_REVIEW", "Source analysis changed; refresh its review.");
        var analysis = source.ReviewProposalJson is null ? null : JsonSerializer.Deserialize<SystemSourceAnalysis>(source.ReviewProposalJson, Json);
        if (analysis?.State != "parsed") throw Error(409, "SOURCE_NOT_REVIEWABLE", "Source analysis is incomplete or failed.");
        return analysis;
    }
    private static string Identity(RegisteredSystem system) => Hash(new { system.Name, system.Acronym, system.Description, system.SetupRevision });
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));
    private static async Task<RegisteredSystem> System(AtoCopilotContext db, Guid tenantId, string systemId, CancellationToken ct) =>
        await db.RegisteredSystems.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == systemId && x.IsActive, ct)
            ?? throw Error(404, "SYSTEM_NOT_FOUND", "The selected system is unavailable.");
    private static async Task<ISystemSourceSession> Find(AtoCopilotContext db, Guid tenantId, string systemId, string kind, Guid id, CancellationToken ct)
    {
        ValidateKind(kind);
        return kind == "emass"
            ? await db.Set<EmassImportSession>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.TargetSystemId == systemId && x.Id == id, ct) ?? throw Missing()
            : await db.Set<SspPdfImportSession>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.TargetSystemId == systemId && x.Id == id, ct) ?? throw Missing();
    }
    private static async Task<ISystemSourceSession?> FindKey(AtoCopilotContext db, Guid tenantId, string systemId, string kind, Guid actor, string key, CancellationToken ct) =>
        kind == "emass"
            ? await db.Set<EmassImportSession>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.TargetSystemId == systemId && x.RequestActorPersonId == actor && x.RequestKey == key, ct)
            : await db.Set<SspPdfImportSession>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.TargetSystemId == systemId && x.RequestActorPersonId == actor && x.RequestKey == key, ct);
    private static void ValidateKind(string kind)
    {
        if (kind is not ("emass" or "ssp-pdf")) throw Error(400, "INVALID_SOURCE_KIND", "Use emass or ssp-pdf.");
    }
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Any(char.IsControl))
            throw Error(400, "INVALID_SOURCE_KEY", "A bounded, stable Idempotency-Key is required.");
    }
    private static WorkspaceException Missing() => Error(404, "SOURCE_NOT_FOUND", "The source is not associated with this exact system.");
    private static WorkspaceException Error(int status, string code, string message) => new(status, code, message);
}
