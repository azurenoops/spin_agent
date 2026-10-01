using System.Data;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.PackageImports;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Tenancy;

public sealed partial class ProviderSetupService
{
    public async Task<object> PrepareUploadAsync(PrepareProviderUpload request, string key, ProviderSetupActor actor, CancellationToken ct)
    {
        Authorize();
        ArgumentNullException.ThrowIfNull(request.Intent);
        var input = request.Intent;
        if (input.IntentId == Guid.Empty || key != input.IntentId.ToString("D") || input.SchemaVersion != 1
            || input.EntryPoint is not ("Onboarding" or "ActivePortal")
            || input.AssociationMode is not ("Unassociated" or "ExactBoundary")
            || string.IsNullOrWhiteSpace(input.PackageName) || input.PackageName.Length > 256
            || input.Files is null || input.Files.Count is < 1 or > 1000)
            throw new ArgumentException("Invalid upload intent.");
        if (input.Files.Where((file, ordinal) => file.Ordinal != ordinal || file.ByteLength < 0
                || file.ByteLength > 50L * 1024 * 1024 || string.IsNullOrWhiteSpace(file.FileName)
                || file.FileName.Length > 512 || file.MediaType is null || file.MediaType.Length > 256
                || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)).Any()
            || input.Files.Sum(file => file.ByteLength) > 50L * 1024 * 1024)
            throw new ArgumentException("Invalid or oversized source manifest.");
        if ((input.AssociationMode == "ExactBoundary") != (input.Context is not null))
            throw new ArgumentException("Exact boundary context is required only for associated intake.");
        RequireHandling(handling.Value, input);
        var normalized = input with { Files = input.Files.Select(file => file with { Sha256 = file.Sha256.ToUpperInvariant() }).ToArray() };
        var serialized = Json(normalized);
        var intentMaterial = JsonNode.Parse(serialized)!.AsObject();
        intentMaterial.Remove("intentId");
        var intentHash = Hash(intentMaterial.ToJsonString(JsonOptions));
        await using var strategy = await factory.CreateDbContextAsync(ct);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var profile = await ProfileAsync(db, ct) ?? throw new ArgumentException("Provider identity is required.");
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var existing = await db.Set<CspPackageUploadIntent>().SingleOrDefaultAsync(x =>
                x.ProviderId == profile.Id && x.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                if (existing.IntentHash != intentHash) throw new DbUpdateConcurrencyException("UPLOAD_INTENT_MISMATCH: Retain the original exact upload request.");
                return await IntentStateAsync(db, existing, ct);
            }
            ProviderSetupDraft? draft = null;
            if (input.EntryPoint == "ActivePortal")
            {
                if (profile.OnboardingState != OnboardingState.Active)
                    throw new ArgumentException("Finish provider setup before active portal intake.");
            }
            else
            {
                draft = await db.Set<ProviderSetupDraft>().SingleOrDefaultAsync(x => x.ProviderId == profile.Id, ct)
                    ?? throw new ArgumentException("Save the provider setup draft first.");
                if (draft.Revision != request.ExpectedSetupRevision) throw new DbUpdateConcurrencyException("Setup draft changed; reload before uploading.");
                if (await db.Set<CspPackageUploadIntent>().CountAsync(x => x.ProviderId == profile.Id && x.DraftId == draft.Id, ct) >= 25)
                    throw new ArgumentException("Setup has 25 retained source requests. Continue additional source work through active portal intake after completing setup.");
            }
            if (input.OfferingHintId.HasValue && !await db.Set<ProviderOffering>().AnyAsync(x =>
                x.ProviderId == profile.Id && x.Id == input.OfferingHintId, ct))
                throw new KeyNotFoundException("Offering hint was not found in this provider.");
            if (input.Context is { } context)
            {
                var offering = await db.Set<ProviderOffering>().SingleOrDefaultAsync(x => x.ProviderId == profile.Id && x.Id == context.OfferingId, ct)
                    ?? throw new KeyNotFoundException("Offering was not found.");
                ProviderAuthorizationStore.Expected(offering, context.ExpectedOfferingRevision);
                await ProviderAuthorizationService.RequireBoundaryAsync(db, offering, context.BoundaryRevisionId, ct);
            }
            var intent = new CspPackageUploadIntent { Id = input.IntentId, ProviderId = profile.Id, DraftId = draft?.Id, EntryPoint = input.EntryPoint,
                OfferingHintId = input.OfferingHintId,
                IdempotencyKey = key, IntentHash = intentHash, IntentJson = serialized, CreatedBy = actor.ObjectId };
            db.Add(intent);
            if (draft is not null)
            {
                var fields = JsonNode.Parse(draft.DraftJson)!.AsObject();
                var source = fields["sources"]!.AsObject();
                source["choice"] = "Intents";
                source["selection"] = null;
                source["intentIds"] ??= new JsonArray();
                source["intentIds"]!.AsArray().Add(input.IntentId.ToString("D"));
                fields["currentScreen"] = "p-uncertain";
                draft.DraftJson = fields.ToJsonString();
                draft.Revision++;
                draft.UpdatedAt = DateTimeOffset.UtcNow;
                draft.UpdatedBy = actor.ObjectId;
            }
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return await IntentStateAsync(db, intent, ct);
        });
    }

    public async Task<object> ListUploadIntentsAsync(string entryPoint, int page, int pageSize, Guid? offeringHintId, CancellationToken ct)
    {
        if (entryPoint != "ActivePortal" || page < 1 || page > 1000000 || pageSize is < 1 or > 100)
            throw new ArgumentException("Use ActivePortal and a bounded positive page/pageSize.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await ProfileAsync(db, ct) ?? throw new KeyNotFoundException("Provider not found.");
        var query = db.Set<CspPackageUploadIntent>().Where(x => x.ProviderId == profile.Id && x.EntryPoint == entryPoint
            && !db.CspPackages.Any(p => p.ProviderId == profile.Id && p.IdempotencyKey == x.IdempotencyKey));
        if (offeringHintId.HasValue)
            query = query.Where(x => x.OfferingHintId == offeringHintId);
        var total = await query.CountAsync(ct);
        var selected = await query.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = new List<object>();
        foreach (var row in selected) items.Add(await IntentStateAsync(db, row, ct));
        return new { items, page, pageSize, total };
    }

    public async Task<object> UploadIntentAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await ProfileAsync(db, ct) ?? throw new KeyNotFoundException("Provider not found.");
        var intent = await db.Set<CspPackageUploadIntent>().SingleOrDefaultAsync(x => x.ProviderId == profile.Id && x.Id == id, ct)
            ?? throw new KeyNotFoundException("Upload intent was not found.");
        return await IntentStateAsync(db, intent, ct);
    }

    public async Task RequireUploadPermissionAsync(Guid id, string key, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await ProfileAsync(db, ct) ?? throw new KeyNotFoundException("Provider not found.");
        var intent = await db.Set<CspPackageUploadIntent>().SingleOrDefaultAsync(x =>
            x.Id == id && x.ProviderId == profile.Id && x.IdempotencyKey == key, ct)
            ?? throw new KeyNotFoundException("Exact upload intent was not found.");
        RequireHandling(handling.Value, Read<ProviderUploadIntentInput>(intent.IntentJson));
    }

    public async Task RequireIngressAsync(string? intentHeader, string key, CancellationToken ct)
    {
        Authorize();
        if (!string.IsNullOrWhiteSpace(intentHeader))
        {
            if (!Guid.TryParse(intentHeader, out var id)) throw new ArgumentException("Invalid upload intent identity.");
            await RequireUploadPermissionAsync(id, key, ct);
            return;
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await ProfileAsync(db, ct);
        if (!string.IsNullOrWhiteSpace(handling.Value.PolicyId)
            || profile is not null && await db.Set<ProviderSetupDraft>().AnyAsync(x => x.ProviderId == profile.Id, ct))
            throw new ArgumentException("HANDLING_DECLARATION_REQUIRED: Continue source upload from provider setup with a retained intent and explicit content declaration.");
    }

    public async Task<object> ReconcileReceiptAsync(ReconcileProviderReceipt request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RequestKey) || request.RequestKey.Length > 100 || request.IntentHash?.Length != 64)
            throw new ArgumentException("Supply the retained request key and exact intent hash.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await ProfileAsync(db, ct) ?? throw new KeyNotFoundException("Provider not found.");
        var intent = await db.Set<CspPackageUploadIntent>().SingleOrDefaultAsync(x =>
            x.ProviderId == profile.Id && x.IdempotencyKey == request.RequestKey, ct);
        if (intent is not null)
        {
            if (!string.Equals(intent.IntentHash, request.IntentHash, StringComparison.OrdinalIgnoreCase))
                throw new DbUpdateConcurrencyException("UPLOAD_INTENT_MISMATCH: Use the saved upload intent.");
            return await ReconciliationAsync(db, intent, ct);
        }
        var package = await db.CspPackages.SingleOrDefaultAsync(x => x.ProviderId == profile.Id && x.IdempotencyKey == request.RequestKey, ct);
        if (package is not null && !string.Equals(package.ContentHash, request.IntentHash, StringComparison.OrdinalIgnoreCase))
            throw new DbUpdateConcurrencyException("UPLOAD_INTENT_MISMATCH: Legacy content identity differs.");
        return new { outcome = package is null ? "NotObserved" : "Confirmed", observedAt = DateTimeOffset.UtcNow,
            intentHash = request.IntentHash, receipt = package is null ? null : await CspPackageService.StatusAsync(db, package, ct),
            nextAction = package is null ? "ReselectSameFiles" : "OpenReceipt" };
    }

    private static async Task<object> IntentStateAsync(AtoCopilotContext db, CspPackageUploadIntent intent, CancellationToken ct)
    {
        var package = await db.CspPackages.SingleOrDefaultAsync(x => x.ProviderId == intent.ProviderId && x.IdempotencyKey == intent.IdempotencyKey, ct);
        return new { intentId = intent.Id, intent.IntentHash, intent.Revision, savedAt = intent.CreatedAt,
            input = Read<ProviderUploadIntentInput>(intent.IntentJson),
            receipt = package is null ? null : await CspPackageService.StatusAsync(db, package, ct),
            reconciliation = await ReconciliationAsync(db, intent, ct) };
    }

    private static async Task<object> ReconciliationAsync(AtoCopilotContext db, CspPackageUploadIntent intent, CancellationToken ct)
    {
        var package = await db.CspPackages.SingleOrDefaultAsync(x => x.ProviderId == intent.ProviderId && x.IdempotencyKey == intent.IdempotencyKey, ct);
        if (package is not null && package.UploadIntentId != intent.Id)
            throw new DbUpdateConcurrencyException("UPLOAD_INTENT_MISMATCH: Receipt is bound to another request.");
        return new { outcome = package is null ? "NotObserved" : "Confirmed", observedAt = DateTimeOffset.UtcNow,
            intentId = intent.Id, intent.IntentHash, receipt = package is null ? null : await CspPackageService.StatusAsync(db, package, ct),
            nextAction = package is null ? "ReselectSameFiles" : "OpenReceipt" };
    }
}
