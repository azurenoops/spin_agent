using System.Text.Json;
using System.Text.Json.Serialization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Feature 050 (FR-004 / FR-005 / FR-014 / FR-015 / FR-016) — append-only
/// writer + paginated reader for <see cref="CapabilityHistoryEvent"/> rows.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppendAsync"/> deliberately does not call
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>. The caller
/// (<c>CspInheritedComponentService</c> or the Remap pipeline) owns the
/// enclosing transaction so the audit row and the state change commit
/// atomically. Failing to call <c>SaveChangesAsync</c> here is the
/// contract — not a bug.
/// </para>
/// <para>
/// <see cref="ListAsync"/> filters by <c>TenantId</c> first (matches the
/// composite index <c>(TenantId, CapabilityId, OccurredAt DESC)</c>) and
/// orders by <c>OccurredAt DESC, Id DESC</c> for stable pagination.
/// </para>
/// </remarks>
public sealed class CapabilityHistoryService : ICapabilityHistoryService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<CapabilityHistoryService> _logger;

    /// <summary>
    /// JSON serializer options used for the <c>MetadataJson</c> column:
    /// camelCase property names and null suppression so callers pass plain
    /// anonymous objects (e.g. <c>new { fromComponentId, toComponentId }</c>)
    /// and the resulting JSON matches the wire contract in
    /// <c>contracts/http-api.md</c>.
    /// </summary>
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CapabilityHistoryService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<CapabilityHistoryService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<CapabilityHistoryEvent> AppendAsync(
        AtoCopilotContext db,
        Guid capabilityId,
        Guid tenantId,
        CapabilityHistoryEventType eventType,
        string actorOid,
        string summary,
        object? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorOid);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (actorOid.Length > 254)
        {
            throw new ArgumentException(
                "actorOid exceeds 254 characters.", nameof(actorOid));
        }
        if (summary.Length > 500)
        {
            throw new ArgumentException(
                "summary exceeds 500 characters.", nameof(summary));
        }

        return AppendCoreAsync(
            db, capabilityId, tenantId, eventType, actorOid, summary, metadata, ct);
    }

    private async Task<CapabilityHistoryEvent> AppendCoreAsync(
        AtoCopilotContext db,
        Guid capabilityId,
        Guid tenantId,
        CapabilityHistoryEventType eventType,
        string actorOid,
        string summary,
        object? metadata,
        CancellationToken ct)
    {
        var evt = new CapabilityHistoryEvent
        {
            Id = Guid.NewGuid(),
            CapabilityId = capabilityId,
            TenantId = tenantId,
            EventType = eventType,
            ActorOid = actorOid,
            OccurredAt = DateTimeOffset.UtcNow,
            Summary = summary,
            MetadataJson = metadata is null
                ? null
                : JsonSerializer.Serialize(metadata, s_json),
        };

        await db.CapabilityHistoryEvents.AddAsync(evt, ct).ConfigureAwait(false);
        // Intentionally no SaveChangesAsync — caller's transaction owns it.
        return evt;
    }

    /// <inheritdoc />
    public async Task<CapabilityHistoryPage> ListAsync(
        Guid capabilityId,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var p = Math.Max(1, page);
        var ps = Math.Clamp(pageSize, 1, 200);

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // TenantId first → matches the leading column of the composite index
        // IX_CapabilityHistoryEvents_Tenant_Capability_Occurred and enforces
        // FR-013 tenant isolation at the storage layer.
        var query = db.CapabilityHistoryEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.CapabilityId == capabilityId);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        // SQLite does not support `ORDER BY` on `DateTimeOffset` server-side
        // (EF Core SqlServer does). To keep dual-provider parity, materialize
        // the tenant+capability slice first, then order + page in memory.
        // History rows per capability are bounded (typical < 20, ceiling
        // < 1,000 per data-model.md § 1.9), so the in-memory pass is cheap.
        var raw = await query.ToListAsync(ct).ConfigureAwait(false);
        var items = raw
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToList();

        return new CapabilityHistoryPage(items, p, ps, total);
    }
}
