using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed class EmassExchangeConflictException(string message) : Exception(message);

public sealed class EmassExchangeService(
    AtoCopilotContext db, ITenantContext tenant, ISystemWorkspaceAccessService access)
{
    private static readonly HashSet<string> Outcomes =
        ["TransferRecorded", "ReceiptRecorded", "ImportAccepted", "ImportRejected", "PartialImport"];

    private IQueryable<EmassExchangeRecord> Records(string systemId) =>
        db.Set<EmassExchangeRecord>().Where(x => x.TenantId == tenant.EffectiveTenantId && x.RegisteredSystemId == systemId);

    private async Task<bool> RequireAccessAsync(string systemId, bool write, CancellationToken ct)
    {
        if (tenant.EffectiveTenantId == Guid.Empty || !await db.RegisteredSystems.AsNoTracking()
                .AnyAsync(x => x.Id == systemId && x.TenantId == tenant.EffectiveTenantId, ct))
            throw new KeyNotFoundException("System is not accessible.");
        var permissions = await access.GetAccessAsync(
            tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
        if (!permissions.Permissions.CanRead) throw new KeyNotFoundException("System is not accessible.");
        // Feature 071's workflow writer contract is ISSM/ISSO, not assessor or AO.
        var canWrite = !tenant.IsCspAdmin && permissions.Roles.Any(x => x is "Issm" or "Isso" or "Administrator");
        if (write && !canWrite) throw new UnauthorizedAccessException("An assigned eMASS workflow writer is required.");
        return canWrite;
    }

    public async Task<EmassExchangeHistory> GetHistoryAsync(string systemId, CancellationToken ct = default)
    {
        var canWrite = await RequireAccessAsync(systemId, false, ct);
        var records = await Records(systemId).AsNoTracking().OrderByDescending(x => x.Version).ToListAsync(ct);
        return new(records.FirstOrDefault()?.Version ?? 0, canWrite, records.Select(Map).ToArray());
    }

    public async Task<IReadOnlyList<EmassExchangeExport>> GetExportsAsync(string systemId, CancellationToken ct = default)
    {
        await RequireAccessAsync(systemId, false, ct);
        var packages = await db.AuthorizationPackages.AsNoTracking().Where(x =>
            x.TenantId == tenant.EffectiveTenantId && x.RegisteredSystemId == systemId
            && x.Status == PackageStatus.Completed && x.CompletedAt != null
            && x.ContentHash != null && x.ContentHash.Trim() != "").ToListAsync(ct);
        return packages.OrderByDescending(x => x.GeneratedAt)
            .Select(x => new EmassExchangeExport(x.Id, x.ContentHash!, x.GeneratedAt, x.Purpose.ToString())).ToArray();
    }

    public async Task<EmassExchangeDto> RecordAsync(
        string systemId, RecordEmassExchangeRequest request, string actor, CancellationToken ct = default)
    {
        await RequireAccessAsync(systemId, true, ct);
        Required(request.PackageId, 36, "Package ID");
        Required(request.PackageHash, 128, "Package hash");
        Required(request.ReceivingWorkflow, 200, "Receiving workflow");
        Required(request.ExternalReference, 500, "External reference");
        Required(request.IdempotencyKey, 100, "Idempotency key");
        Required(actor, 200, "Authenticated actor");
        if (request.Notes is null || request.Notes.Length > 4000)
            throw new ArgumentException("Notes must be at most 4000 characters.");
        if (request.Outcome is null || !Outcomes.Contains(request.Outcome))
            throw new ArgumentException("Unsupported manual exchange outcome.");
        if (request.ExpectedVersion < 0 || request.OccurredAt == default || request.OccurredAt > DateTimeOffset.UtcNow)
            throw new ArgumentException("A nonfuture event time and nonnegative history version are required.");
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { request, actor }))));
        var replay = await ReplayAsync(systemId, request.IdempotencyKey, requestHash, ct);
        if (replay is not null) return replay;
        var version = await Records(systemId).Select(x => (long?)x.Version).MaxAsync(ct) ?? 0;
        if (version != request.ExpectedVersion)
            throw new EmassExchangeConflictException("Exchange history changed. Reload and review before recording.");
        var package = await db.AuthorizationPackages.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == request.PackageId && x.TenantId == tenant.EffectiveTenantId && x.RegisteredSystemId == systemId
            && x.Status == PackageStatus.Completed && x.CompletedAt != null, ct);
        if (package is null || string.IsNullOrWhiteSpace(package.ContentHash)
            || package.ContentHash != request.PackageHash || package.GeneratedAt != request.ExportGeneratedAt)
            throw new ArgumentException("The retained completed package, hash and export version must match this system.");
        if (request.OccurredAt < package.CompletedAt)
            throw new ArgumentException("The exchange event cannot precede package completion.");
        if (request.SupersedesId is not null)
        {
            Required(request.SupersedesId, 36, "Correction predecessor");
            Required(request.Notes, 4000, "Correction reason");
            if (!await Records(systemId).AnyAsync(x => x.Id == request.SupersedesId && x.PackageId == request.PackageId
                    && x.PackageHash == request.PackageHash && x.ExportGeneratedAt == request.ExportGeneratedAt, ct))
                throw new ArgumentException("The correction must reference this system's history for the same package.");
            if (await Records(systemId).AnyAsync(x => x.SupersedesId == request.SupersedesId, ct))
                throw new EmassExchangeConflictException("That observation already has a correction. Correct the latest entry.");
        }
        var record = new EmassExchangeRecord
        {
            TenantId = tenant.EffectiveTenantId, RegisteredSystemId = systemId, Version = checked(version + 1),
            PackageId = package.Id, PackageHash = package.ContentHash, ExportGeneratedAt = package.GeneratedAt,
            Outcome = request.Outcome, ReceivingWorkflow = request.ReceivingWorkflow, ExternalReference = request.ExternalReference,
            OccurredAt = request.OccurredAt, RecordedAt = DateTimeOffset.UtcNow, RecordedBy = actor,
            Notes = request.Notes, SupersedesId = request.SupersedesId,
            IdempotencyKey = request.IdempotencyKey, RequestHash = requestHash,
        };
        db.Set<EmassExchangeRecord>().Add(record);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            replay = await ReplayAsync(systemId, request.IdempotencyKey, requestHash, ct);
            if (replay is not null) return replay;
            if (await Records(systemId).AnyAsync(x => x.Version == record.Version, ct))
                throw new EmassExchangeConflictException("Exchange history changed. Reload and review before recording.");
            throw;
        }
        return Map(record);
    }

    private async Task<EmassExchangeDto?> ReplayAsync(string systemId, string key, string hash, CancellationToken ct)
    {
        var previous = await Records(systemId).AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (previous is null) return null;
        if (previous.RequestHash != hash)
            throw new EmassExchangeConflictException("The idempotency key was already used for a different observation.");
        return Map(previous);
    }

    private static void Required(string? value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException($"{name} is required and must be at most {maxLength} characters.");
    }

    private static EmassExchangeDto Map(EmassExchangeRecord x) => new(
        x.Id, x.Version, x.PackageId, x.PackageHash, x.ExportGeneratedAt,
        x.Outcome, x.ReceivingWorkflow, x.ExternalReference, x.OccurredAt,
        x.RecordedAt, x.RecordedBy, x.Notes, x.SupersedesId);
}
