using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Interceptors;

/// <summary>
/// Feature 048 (FR-021): EF Core <see cref="SaveChangesInterceptor"/> that
/// stamps <c>TenantId</c> on inserts, forbids tenant changes on updates, and
/// rejects cross-tenant FK references — except when the referenced entity is
/// <see cref="GlobalReferenceAttribute"/>.
/// </summary>
/// <remarks>
/// Per data-model.md §4:
/// <list type="number">
///   <item><b>Stamp on insert.</b> If <c>TenantId</c> is <see cref="Guid.Empty"/>,
///         set it to <c>ITenantContext.EffectiveTenantId</c>. If it is non-empty
///         and differs from <c>EffectiveTenantId</c> and the actor is NOT CSP-Admin,
///         throw <see cref="TenantConsistencyException"/>.</item>
///   <item><b>Forbid tenant change on update.</b> If <c>TenantId</c> appears in
///         the modified-properties set, throw.</item>
///   <item><b>Validate FK consistency.</b> For every loaded navigation pointing
///         at another <c>[TenantScoped]</c> entity, throw if the referenced row's
///         <c>TenantId</c> differs from this entry's <c>TenantId</c>.</item>
///   <item><b>Soft-validate global references.</b> Allow references to
///         <c>[GlobalReference]</c> rows regardless of <c>TenantId</c>.</item>
/// </list>
/// </remarks>
public sealed class TenantStampingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContextAccessor _accessor;
    private readonly ILogger<TenantStampingSaveChangesInterceptor> _logger;

    public TenantStampingSaveChangesInterceptor(
        ITenantContextAccessor accessor,
        ILogger<TenantStampingSaveChangesInterceptor> logger)
    {
        _accessor = accessor;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            ApplyStampingAndValidation(eventData.Context);
        }
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            ApplyStampingAndValidation(eventData.Context);
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyStampingAndValidation(DbContext context)
    {
        var tenant = _accessor.Current;
        if (tenant is null)
        {
            // Some startup paths (system-tenant bootstrap, schema-additions seed)
            // run before middleware sets a context. Allow these — the entries
            // explicitly set TenantId. We only enforce when a tenant is in scope.
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
            {
                continue;
            }

            var clrType = entry.Entity.GetType();
            var isTenantScoped = clrType.GetCustomAttributes(typeof(TenantScopedAttribute), inherit: false).Length > 0;
            if (!isTenantScoped)
            {
                continue;
            }

            // Some [TenantScoped] entities (e.g. Feature 051 LoginAuditEvent)
            // deliberately use a domain-specific tenant column name
            // (EffectiveTenantId) instead of the conventional TenantId, per
            // their data-model spec. The query-filter installer in
            // AtoCopilotContext.ApplyTenantQueryFilters silently skips these
            // by the same metadata check; the stamping interceptor MUST do
            // the same or every SaveChangesAsync on such an entity throws
            // before the entity's own writer code can run (which sets the
            // domain-specific column explicitly).
            //
            // FK consistency (ValidateFkConsistency) is also skipped because
            // it reads `entry.Property("TenantId").CurrentValue` to compare
            // against parents — without a conventional `TenantId` there's
            // nothing to validate at the generic interceptor layer. The
            // entity's writer remains responsible for FK ownership checks.
            if (entry.Metadata.FindProperty("TenantId") is null)
            {
                // Still run actor-attribution stamping against any conventional
                // columns the entity DOES expose (ActorTenantId /
                // ImpersonatedTenantId), then move on.
                StampActorAttributionIfPresent(entry, tenant);
                continue;
            }

            var tenantIdProp = entry.Property("TenantId");
            if (tenantIdProp is null)
            {
                throw new TenantConsistencyException(
                    $"Entity '{clrType.Name}' is [TenantScoped] but has no TenantId property.");
            }

            if (entry.State == EntityState.Added)
            {
                StampOnInsert(entry, tenantIdProp, tenant, clrType);
            }
            else if (entry.State == EntityState.Modified)
            {
                ForbidTenantChangeOnUpdate(tenantIdProp, clrType);
            }

            ValidateFkConsistency(entry, tenant, clrType, context);
        }
    }

    private static void StampOnInsert(
        EntityEntry entry,
        PropertyEntry tenantIdProp,
        ITenantContext tenant,
        Type clrType)
    {
        var current = (Guid)(tenantIdProp.CurrentValue ?? Guid.Empty);
        if (current == Guid.Empty)
        {
            tenantIdProp.CurrentValue = tenant.EffectiveTenantId;
        }
        else if (current != tenant.EffectiveTenantId && !tenant.IsCspAdmin)
        {
            throw new TenantConsistencyException(
                $"Entity '{clrType.Name}' was inserted with TenantId={current} but the active tenant " +
                $"is {tenant.EffectiveTenantId} and the actor is not CSP-Admin.");
        }

        // Feature 048 (T072 / FR-052): if the entity exposes the audit-log
        // attribution columns (ActorTenantId / ImpersonatedTenantId), populate
        // them from the active tenant context. We probe by metadata so this
        // works for AuditLogEntry without coupling the interceptor to that
        // concrete type.
        StampActorAttributionIfPresent(entry, tenant);
    }

    private static void StampActorAttributionIfPresent(EntityEntry entry, ITenantContext tenant)
    {
        var actorProp = entry.Metadata.FindProperty("ActorTenantId") is not null
            ? entry.Property("ActorTenantId")
            : null;
        if (actorProp is not null && actorProp.CurrentValue is null)
        {
            actorProp.CurrentValue = tenant.TenantId;
        }

        var impersonatedProp = entry.Metadata.FindProperty("ImpersonatedTenantId") is not null
            ? entry.Property("ImpersonatedTenantId")
            : null;
        if (impersonatedProp is not null && impersonatedProp.CurrentValue is null)
        {
            // Null when the actor is NOT impersonating (the common case);
            // set to the impersonated tenant id otherwise.
            impersonatedProp.CurrentValue = tenant.ImpersonatedTenantId;
        }
    }

    private static void ForbidTenantChangeOnUpdate(
        PropertyEntry tenantIdProp,
        Type clrType)
    {
        if (tenantIdProp.IsModified)
        {
            throw new TenantConsistencyException(
                $"TenantId on entity '{clrType.Name}' cannot be changed once set.");
        }
    }

    private static void ValidateFkConsistency(
        EntityEntry entry,
        ITenantContext tenant,
        Type clrType,
        DbContext context)
    {
        var ownTenantId = (Guid)(entry.Property("TenantId").CurrentValue ?? Guid.Empty);

        foreach (var nav in entry.Navigations)
        {
            // Only consider reference navigations (not collections), and only those that are loaded.
            if (nav.Metadata.IsCollection) continue;
            if (nav.CurrentValue is null) continue;

            var refType = nav.CurrentValue.GetType();
            var refIsTenantScoped = refType.GetCustomAttributes(typeof(TenantScopedAttribute), inherit: false).Length > 0;
            var refIsGlobal = refType.GetCustomAttributes(typeof(GlobalReferenceAttribute), inherit: false).Length > 0;

            // Feature 048 T136 / FR-080: cross-tenant references are rejected
            // unless the target entity is [GlobalReference]. Targets without
            // either attribute are unscoped (legacy / system tables) and are
            // allowed by default.
            if (refIsGlobal) continue;
            if (!refIsTenantScoped) continue;

            // Walk into the loaded navigation entity to read its TenantId.
            var refEntry = context.Entry(nav.CurrentValue);
            var refTenantProp = refEntry.Property("TenantId");
            if (refTenantProp.CurrentValue is null) continue;

            var refTenantId = (Guid)refTenantProp.CurrentValue;
            if (refTenantId != ownTenantId)
            {
                throw new TenantConsistencyException(
                    $"Cross-tenant reference rejected: '{clrType.Name}' (TenantId={ownTenantId}) " +
                    $"may not reference '{refType.Name}' (TenantId={refTenantId}).");
            }
        }
    }
}
