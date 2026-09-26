using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

/// <summary>Shared transaction, ownership and evidence validation for provider aggregates.</summary>
public sealed class ProviderAuthorizationStore(
    IDbContextFactory<AtoCopilotContext> factory, ITenantContext tenant, ILogger<ProviderAuthorizationStore> logger)
{
    public IDbContextFactory<AtoCopilotContext> Factory => factory;
    public ITenantContext Tenant => tenant;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidDataException("Stored provider authorization data is invalid.");
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public void Authorize()
    {
        if (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue)
        {
            logger.LogWarning("ProviderAuthorization.AccessDenied in non-provider or support context");
            throw new UnauthorizedAccessException("Use an ordinary provider administrator workspace, not support impersonation.");
        }
    }

    public async Task<Guid> ProviderAsync(AtoCopilotContext db, CancellationToken ct)
    {
        Authorize();
        return await db.CspProfiles.Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Complete provider identity before managing offerings.");
    }

    public async Task<ProviderOffering> OfferingAsync(AtoCopilotContext db, Guid id, CancellationToken ct)
    {
        var provider = await ProviderAsync(db, ct);
        return await db.Set<ProviderOffering>().SingleOrDefaultAsync(x => x.Id == id && x.ProviderId == provider, ct)
            ?? throw new KeyNotFoundException("Offering was not found in the current provider.");
    }

    public static void Expected(ProviderOwnedRow row, long revision)
    {
        if (revision < 1 || row.Revision != revision)
            throw new DbUpdateConcurrencyException("The expected revision is stale. Reload and review the current version.");
    }

    public static string Text(string? value, string field, int max, bool required = true)
    {
        value = value?.Trim() ?? "";
        if ((required && value.Length == 0) || value.Length > max)
            throw new ArgumentException($"{field} must contain {(required ? "1" : "0")}-{max} characters.");
        return value;
    }

    public static void Bounded<T>(IReadOnlyList<T>? values, string field, int min = 0)
    {
        if (values is null || values.Count < min || values.Count > 100 || values.Any(x => x is null))
            throw new ArgumentException($"{field} requires {min}-100 non-null items.");
    }

    public static DateOnly? Date(string? value)
    {
        if (value is null) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("Dates must be source-stated YYYY-MM-DD values or null.");
        return date;
    }

    public static ProviderAzureScope Normalize(ProviderAzureScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Cloud is not ("AzureCloud" or "AzureUSGovernment") || scope.DirectoryTenantId == Guid.Empty
            || scope.SubscriptionId == Guid.Empty || string.IsNullOrWhiteSpace(scope.ResourceId)
            || scope.ResourceId.Length > 2048 || scope.ResourceId.Contains('?') || scope.ResourceId.Contains('#')
            || scope.ResourceId.Contains('%') || scope.ResourceId.Contains('\\'))
            throw new ArgumentException("Specify a supported cloud, directory, subscription and Azure resource scope.");
        var segments = scope.ResourceId.Trim().TrimEnd('/').Split('/');
        if (segments.Length < 3 || segments[0] != "" || !segments[1].Equals("subscriptions", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[2], out var subscription) || subscription != scope.SubscriptionId
            || segments.Skip(1).Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".."))
            throw new ArgumentException("The resource scope must be rooted in its exact subscription.");
        if (segments.Length != 3 && (segments.Length < 5 || !segments[3].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Use a subscription, resource group or resource scope.");
        if (segments.Length > 5 && (segments.Length < 9 || !segments[5].Equals("providers", StringComparison.OrdinalIgnoreCase)
            || (segments.Length - 7) % 2 != 0))
            throw new ArgumentException("The resource scope has an invalid provider resource path.");
        return scope with { ResourceId = string.Join('/', segments).ToLowerInvariant() };
    }

    public static bool Contains(ProviderAzureScope parent, ProviderAzureScope child)
    {
        parent = Normalize(parent); child = Normalize(child);
        return parent.Cloud == child.Cloud && parent.DirectoryTenantId == child.DirectoryTenantId
            && parent.SubscriptionId == child.SubscriptionId
            && (parent.ResourceId == child.ResourceId || child.ResourceId.StartsWith(parent.ResourceId + "/", StringComparison.Ordinal));
    }

    public async Task CitationsAsync(AtoCopilotContext db, Guid providerId, IReadOnlyList<ProviderCitation> citations,
        CancellationToken ct, bool required = false)
    {
        Bounded(citations, "citations", required ? 1 : 0);
        foreach (var citation in citations)
        {
            Text(citation.Quote, "citation quote", 8000);
            var source = await (from entry in db.CspPackageEntries
                                join package in db.CspPackages on entry.PackageId equals package.Id
                                where package.ProviderId == providerId && package.Id == citation.PackageId
                                    && entry.Id == citation.ArtifactId
                                select entry).SingleOrDefaultAsync(ct);
            if (source is null || source.Status == "Excluded" || source.ArchivePath != citation.ArchivePath
                || !Read<CspPackageSourceSegment[]>(source.SegmentsJson)
                    .Any(x => x.Locator == citation.Locator && x.Text.Contains(citation.Quote, StringComparison.Ordinal)))
                throw new ArgumentException("A citation does not match retained, non-excluded source evidence in this provider.");
        }
    }

    public async Task CandidateAsync(AtoCopilotContext db, Guid providerId, ProviderSourceCandidateRef? reference,
        string kind, CancellationToken ct)
    {
        if (reference is null) return;
        var candidate = await (from c in db.CspPackageCandidates
                               join p in db.CspPackages on c.PackageId equals p.Id
                               where p.ProviderId == providerId && p.Id == reference.PackageId && c.Id == reference.CandidateId
                               select c).SingleOrDefaultAsync(ct);
        if (candidate is null || candidate.Type != kind || candidate.Revision != reference.Revision
            || candidate.ReviewState != "Reviewed" || candidate.ReviewedBy is null)
            throw new DbUpdateConcurrencyException("Use an explicitly reviewed, current source claim of the correct kind.");
    }

    public static void Audit(AtoCopilotContext db, ProviderOffering offering, Guid target, string action, string actor, object? detail = null)
    {
        db.Set<ProviderAuthorizationAudit>().Add(new()
        {
            ProviderId = offering.ProviderId, OfferingId = offering.Id, TargetId = target,
            Action = Text(action, "action", 100), CreatedBy = Text(actor, "actor", 254),
            Revision = offering.Revision, DetailJson = Json(detail)
        });
    }

    public async Task<T> WriteAsync<T>(Guid? offeringId, string operation, string? key, object intent,
        string actor, Func<AtoCopilotContext, Guid, ProviderOffering?, Task<T>> change, CancellationToken ct)
    {
        Authorize();
        Text(actor, "actor", 254);
        if (key is not null) Text(key, "Idempotency-Key", 100);
        var hash = Hash(Json(intent));
        await using var strategyDb = await factory.CreateDbContextAsync(ct);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var provider = await ProviderAsync(db, ct);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            if (key is not null)
            {
                var replay = await db.Set<ProviderAuthorizationOperation>().SingleOrDefaultAsync(
                    x => x.ProviderId == provider && x.OperationScope == operation && x.IdempotencyKey == key, ct);
                if (replay is not null)
                {
                    if (replay.IntentHash != hash)
                        throw new DbUpdateConcurrencyException("The idempotency key identifies a different request intent.");
                    return Read<T>(replay.ResponseJson);
                }
            }
            try
            {
                var offering = offeringId.HasValue ? await OfferingAsync(db, offeringId.Value, ct) : null;
                var result = await change(db, provider, offering);
                offering ??= db.Set<ProviderOffering>().Local.Single(x => db.Entry(x).State == EntityState.Added);
                Audit(db, offering, offering.Id, operation, actor, new { RequestHash = hash });
                if (key is not null)
                    db.Set<ProviderAuthorizationOperation>().Add(new()
                    {
                        ProviderId = provider, OfferingId = offering.Id, OperationScope = operation,
                        IdempotencyKey = key, IntentHash = hash, ResponseJson = Json(result), CreatedBy = actor
                    });
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return result;
            }
            catch (DbUpdateException error) when (key is not null)
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                await using var winnerDb = await factory.CreateDbContextAsync(ct);
                var winner = await winnerDb.Set<ProviderAuthorizationOperation>().AsNoTracking().SingleOrDefaultAsync(
                    x => x.ProviderId == provider && x.OperationScope == operation && x.IdempotencyKey == key, ct);
                if (winner is null) throw;
                if (winner.IntentHash != hash)
                    throw new DbUpdateConcurrencyException("The idempotency key identifies a different request intent.", error);
                logger.LogWarning(error, "ProviderAuthorization.ConcurrentOperationRecovered operation={Operation}", operation);
                return Read<T>(winner.ResponseJson);
            }
        });
    }

    public static async Task<PagedResult<TOut>> PageAsync<TIn, TOut>(IQueryable<TIn> query, int page, int size,
        Func<TIn, TOut> project, CancellationToken ct) where TIn : class
    {
        if (page < 1 || size is < 1 or > 100 || page > int.MaxValue / size)
            throw new ArgumentException("Use page >=1 and pageSize between1 and100.");
        var count = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(rows.Select(project).ToArray(), page, size, count);
    }
}
