namespace Ato.Copilot.Core.Models.Tenancy.Attributes;

/// <summary>
/// Marks an entity as global reference data living in the system tenant.
/// The entity MAY have a <c>TenantId</c> field (always set to the system
/// tenant id <c>00000000-0000-0000-0000-000000000000</c>). Global-reference
/// entities are:
/// <list type="bullet">
///   <item>Excluded from <c>HasQueryFilter</c> registration (readable from any
///   tenant context).</item>
///   <item>Excluded from RLS policy installation; the RLS predicate's
///   system-tenant short-circuit is what makes them readable cross-tenant.</item>
///   <item>NOT writable from a non-CSP-Admin context (enforced by the
///   stamping interceptor).</item>
/// </list>
/// Examples: NIST control catalog, CCI mappings, ATT&amp;CK techniques, the
/// <c>Tenant</c> entity itself, etc.
/// See feature 048 spec FR-080..FR-083 and data-model.md §2.2.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GlobalReferenceAttribute : Attribute
{
}
