using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Default <see cref="IOrgControlOverrideService"/> implementation. Pulls
/// per-call DbContexts from <see cref="IDbContextFactory{TContext}"/> so the
/// service can stay scoped without leaking the context across requests.
/// All reads use the EF query filter installed for
/// <see cref="OrgControlOverride"/> (auto-filters by
/// <see cref="ITenantContext.EffectiveTenantId"/>); inserts are stamped by
/// the <c>TenantStampingSaveChangesInterceptor</c>.
/// </summary>
public sealed class OrgControlOverrideService : IOrgControlOverrideService
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly ITenantContext _tenant;
    private readonly ILogger<OrgControlOverrideService> _logger;

    public OrgControlOverrideService(
        IDbContextFactory<AtoCopilotContext> factory,
        ITenantContext tenant,
        ILogger<OrgControlOverrideService> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrgControlOverride>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.OrgControlOverrides
            .AsNoTracking()
            .OrderBy(o => o.ControlId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrgControlOverride?> GetAsync(
        string controlId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeControlId(controlId);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.OrgControlOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ControlId == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrgControlOverride?> UpsertAsync(
        string controlId,
        ControlImplementationStatus? implementationStatus,
        ControlInheritanceApplicability? inheritanceApplicability,
        string? justification,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeControlId(controlId);
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Actor is required.", nameof(actor));
        }

        // If both override fields are null, treat as delete-when-exists.
        // Keeps the API surface tiny — the UI doesn't need a separate
        // "reset to default" call.
        if (implementationStatus is null && inheritanceApplicability is null)
        {
            await DeleteAsync(normalized, actor, cancellationToken);
            return null;
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new ArgumentException(
                "Justification is required when an override field is set.",
                nameof(justification));
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.OrgControlOverrides
            .FirstOrDefaultAsync(o => o.ControlId == normalized, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            var row = new OrgControlOverride
            {
                Id = Guid.NewGuid(),
                // TenantId is intentionally left at Guid.Empty — the
                // TenantStampingSaveChangesInterceptor stamps it on insert
                // from ITenantContext.EffectiveTenantId. This avoids a hard
                // dependency on the accessor here and keeps the service
                // testable without a full middleware fixture.
                ControlId = normalized,
                ImplementationStatus = implementationStatus,
                InheritanceApplicability = inheritanceApplicability,
                Justification = justification.Trim(),
                CreatedAt = now,
                CreatedBy = actor,
                UpdatedAt = now,
                UpdatedBy = actor,
            };
            db.OrgControlOverrides.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Org control override created for tenant {TenantId} control {ControlId} by {Actor}",
                _tenant.EffectiveTenantId, normalized, actor);
            return row;
        }

        existing.ImplementationStatus = implementationStatus;
        existing.InheritanceApplicability = inheritanceApplicability;
        existing.Justification = justification.Trim();
        existing.UpdatedAt = now;
        existing.UpdatedBy = actor;
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Org control override updated for tenant {TenantId} control {ControlId} by {Actor}",
            _tenant.EffectiveTenantId, normalized, actor);
        return existing;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string controlId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeControlId(controlId);
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Actor is required.", nameof(actor));
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await db.OrgControlOverrides
            .FirstOrDefaultAsync(o => o.ControlId == normalized, cancellationToken);
        if (row is null) return false;

        db.OrgControlOverrides.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Org control override deleted for tenant {TenantId} control {ControlId} by {Actor}",
            _tenant.EffectiveTenantId, normalized, actor);
        return true;
    }

    private static string NormalizeControlId(string controlId)
    {
        if (string.IsNullOrWhiteSpace(controlId))
        {
            throw new ArgumentException("ControlId is required.", nameof(controlId));
        }
        var trimmed = controlId.Trim().ToUpperInvariant();
        if (trimmed.Length > 20)
        {
            throw new ArgumentException("ControlId exceeds 20 characters.", nameof(controlId));
        }
        return trimmed;
    }
}
