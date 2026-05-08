using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Feature 048 (T134, FR-081/FR-082): default <see cref="IGlobalBaselineService"/>
/// implementation. Uses <see cref="IDbContextFactory{TContext}"/> so the service
/// can run as a singleton and so that publish/unpublish operate against a
/// fresh context (the persisted <see cref="GlobalBaseline"/> rows live in the
/// system tenant and are <c>[GlobalReference]</c> — no tenant query filter
/// applies).
/// </summary>
public sealed class GlobalBaselineService : IGlobalBaselineService
{
    private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "ControlNarrative",
        "EvidenceArtifact",
        "OrgInheritanceDefault",
    };

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<GlobalBaselineService> _logger;

    public GlobalBaselineService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ITenantContext tenantContext,
        ILogger<GlobalBaselineService> logger)
    {
        _contextFactory = contextFactory;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GlobalBaseline>> ListAsync(
        string? kind,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.GlobalBaselines.AsNoTracking()
            .Where(b => b.UnpublishedAt == null);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            query = query.Where(b => b.Kind == kind);
        }

        // SQLite cannot translate ORDER BY on DateTimeOffset. Materialize first
        // (page size is bounded ≤ 200) and order client-side. The dataset is
        // small by design — global baselines are a curated reference table.
        var materialized = await query.ToListAsync(cancellationToken);
        return materialized
            .OrderByDescending(b => b.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<GlobalBaseline?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.GlobalBaselines.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GlobalBaseline> PublishAsync(
        string kind,
        Guid sourceId,
        string? title,
        string? notes,
        string actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kind) || !AllowedKinds.Contains(kind))
        {
            throw new ArgumentException(
                $"Unsupported baseline kind '{kind}'. Allowed: {string.Join(", ", AllowedKinds)}.",
                nameof(kind));
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("sourceId must be a non-empty GUID.", nameof(sourceId));
        }

        var sourceTenantId = _tenantContext?.EffectiveTenantId ?? Guid.Empty;

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var baseline = new GlobalBaseline
        {
            Id = Guid.NewGuid(),
            Kind = NormalizeKind(kind),
            SourceId = sourceId,
            SourceTenantId = sourceTenantId,
            Title = title,
            Notes = notes,
            PublishedAt = DateTimeOffset.UtcNow,
            PublishedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
        };

        db.GlobalBaselines.Add(baseline);
        await db.SaveChangesAsync(cancellationToken);

        await EmitAuditAsync(db, baseline, actor, success: true, action: "GlobalBaseline.Publish", cancellationToken);

        _logger.LogInformation(
            "Published GlobalBaseline {Id} of kind {Kind} from tenant {SourceTenantId} by {Actor}",
            baseline.Id, baseline.Kind, baseline.SourceTenantId, baseline.PublishedBy);

        return baseline;
    }

    /// <inheritdoc />
    public async Task<bool> UnpublishAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var baseline = await db.GlobalBaselines.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (baseline is null || baseline.UnpublishedAt is not null)
        {
            return false;
        }

        baseline.UnpublishedAt = DateTimeOffset.UtcNow;
        baseline.UnpublishedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor;
        await db.SaveChangesAsync(cancellationToken);

        await EmitAuditAsync(db, baseline, actor, success: true, action: "GlobalBaseline.Unpublish", cancellationToken);

        _logger.LogInformation(
            "Unpublished GlobalBaseline {Id} of kind {Kind} by {Actor}",
            baseline.Id, baseline.Kind, baseline.UnpublishedBy);

        return true;
    }

    private static string NormalizeKind(string kind) => kind switch
    {
        var k when string.Equals(k, "ControlNarrative", StringComparison.OrdinalIgnoreCase) => "ControlNarrative",
        var k when string.Equals(k, "EvidenceArtifact", StringComparison.OrdinalIgnoreCase) => "EvidenceArtifact",
        var k when string.Equals(k, "OrgInheritanceDefault", StringComparison.OrdinalIgnoreCase) => "OrgInheritanceDefault",
        _ => kind,
    };

    private static async Task EmitAuditAsync(
        AtoCopilotContext db,
        GlobalBaseline baseline,
        string actor,
        bool success,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            db.AuditLogs.Add(new AuditLogEntry
            {
                TenantId = baseline.SourceTenantId,
                ActorTenantId = baseline.SourceTenantId,
                ImpersonatedTenantId = null,
                UserId = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
                UserRole = "CspAdmin",
                Action = action,
                Outcome = success ? AuditOutcome.Success : AuditOutcome.Failure,
                Details = $"Kind={baseline.Kind} SourceId={baseline.SourceId} BaselineId={baseline.Id}",
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Audit emission must not break the publish/unpublish path.
        }
    }
}
