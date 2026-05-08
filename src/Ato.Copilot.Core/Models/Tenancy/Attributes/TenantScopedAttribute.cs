namespace Ato.Copilot.Core.Models.Tenancy.Attributes;

/// <summary>
/// Marks an entity as tenant-scoped. The entity MUST expose a
/// <c>Guid TenantId { get; set; }</c> property. Tenant-scoped entities
/// participate in:
/// <list type="bullet">
///   <item>EF Core <c>HasQueryFilter</c> registration (filters by
///   <c>ITenantContext.EffectiveTenantId</c>).</item>
///   <item>The <c>TenantStampingSaveChangesInterceptor</c> (stamps
///   <c>TenantId</c> on insert and validates FK consistency).</item>
///   <item>SQL Server Row-Level Security (RLS) policy installation
///   (defense-in-depth at the database layer).</item>
/// </list>
/// A startup self-check (<c>AtoCopilotContext.AssertScopingAttributesPresent</c>)
/// fails fast at app start if a discovered entity is neither
/// <see cref="TenantScopedAttribute"/> nor <see cref="GlobalReferenceAttribute"/>.
/// See feature 048 spec FR-003 / FR-020 and data-model.md §2.1.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TenantScopedAttribute : Attribute
{
    /// <summary>
    /// Optional natural-key column to suggest as the secondary key on the
    /// composite <c>(TenantId, …)</c> index for performance optimization.
    /// </summary>
    public string? CompositeIndexHint { get; init; }
}
