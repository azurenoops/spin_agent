using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Default <see cref="ITenantProvisioningService"/> backed by EF Core.
/// Idempotent on <c>EntraTenantId</c> for create operations; emits audit
/// events through <see cref="ILogger{TCategoryName}"/> until the audit-log
/// model is wired in (T072 / T073).
/// </summary>
/// <remarks>
/// Lifetime: Scoped (consumes a Scoped <see cref="AtoCopilotContext"/>).
/// Operations on the <c>Tenants</c> table itself are exempt from the global
/// query filter because <see cref="Tenant"/> is decorated with
/// <c>[GlobalReference]</c>.
/// </remarks>
public sealed class TenantProvisioningService : ITenantProvisioningService
{
    private readonly AtoCopilotContext _db;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        AtoCopilotContext db,
        ILogger<TenantProvisioningService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(Tenant Tenant, bool Created)> CreateAsync(
        Guid entraTenantId,
        string displayName,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("displayName is required.", nameof(displayName));
        }
        if (displayName.Length > 200)
        {
            throw new ArgumentException("displayName cannot exceed 200 characters.", nameof(displayName));
        }
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("actor is required.", nameof(actor));
        }

        // Idempotent: if a row already exists for this EntraTenantId, return it.
        var existing = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.EntraTenantId == entraTenantId, cancellationToken);
        if (existing is not null)
        {
            return (existing, Created: false);
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            EntraTenantId = entraTenantId,
            DisplayName = displayName.Trim(),
            Status = TenantStatus.Active,
            OnboardingState = OnboardingState.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = actor,
            TimeZone = "UTC",
            DefaultClassificationLevel = ClassificationLevel.Unclassified,
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Provisioned tenant {TenantId} (EntraTenantId={EntraTenantId}, DisplayName={DisplayName}) by {Actor}",
            tenant.Id, entraTenantId, tenant.DisplayName, actor);

        return (tenant, Created: true);
    }

    /// <inheritdoc />
    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Tenant?> GetByEntraTenantIdAsync(Guid entraTenantId, CancellationToken cancellationToken = default) =>
        await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.EntraTenantId == entraTenantId, cancellationToken);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Tenant> Items, int Total)> ListAsync(
        TenantStatus? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        IQueryable<Tenant> q = _db.Tenants.AsNoTracking();
        if (statusFilter.HasValue)
        {
            q = q.Where(t => t.Status == statusFilter.Value);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(t => t.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<Tenant> UpdateStatusAsync(
        Guid id,
        TenantStatus status,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("reason is required.", nameof(reason));
        }
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("actor is required.", nameof(actor));
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {id} not found.");

        if (tenant.Status == status)
        {
            // Idempotent — no-op when the requested state already holds.
            return tenant;
        }

        var prev = tenant.Status;
        tenant.Status = status;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        tenant.UpdatedBy = actor;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Tenant {TenantId} status {Previous} -> {Next} by {Actor} ({Reason})",
            tenant.Id, prev, status, actor, reason);

        return tenant;
    }
}
