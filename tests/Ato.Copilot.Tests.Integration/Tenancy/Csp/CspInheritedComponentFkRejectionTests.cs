using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy.Csp;

/// <summary>
/// T193 [US9] / FR-080 confirmation suite for the CSP-inherited entities.
/// </summary>
/// <remarks>
/// <para>
/// The <c>CspInheritedComponent</c> and <c>CspInheritedCapability</c> entities
/// carry <c>[GlobalReference]</c>, so the
/// <see cref="TenantStampingSaveChangesInterceptor"/> must:
/// <list type="bullet">
///   <item>Skip them in the cross-tenant FK rejection loop (so a tenant-local
///         entity can hold a navigation / FK pointer to one without
///         tripping FR-080).</item>
///   <item>Not stamp them with a <c>TenantId</c> (they don't have one).</item>
/// </list>
/// while the same interceptor must STILL reject any tenant-scoped entity that
/// is inserted with a <c>TenantId</c> belonging to another tenant when the
/// caller is not CSP-Admin.
/// </para>
/// <para>
/// These tests are confirmation / regression tests — they exercise the
/// existing FR-080 + <c>[GlobalReference]</c> machinery against the new
/// CSP-inherited entities. T210 is a no-op verification step backed by this
/// suite; if any of these tests fail in CI the FR-080 wiring has regressed
/// and the implementation slice is not safe to merge.
/// </para>
/// </remarks>
[Collection("Tenancy")]
public class CspInheritedComponentFkRejectionTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;

    public CspInheritedComponentFkRejectionTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Save_GlobalCspComponent_AndTenantScopedRow_TogetherInTenantA_DoesNotThrow()
    {
        // Arrange — Tenant A is the active tenant; CSP-Admin so we can
        // explicitly stamp TenantId on the OrgInheritanceDefault. The
        // interceptor must accept the [GlobalReference] CspInheritedComponent
        // alongside the [TenantScoped] OrgInheritanceDefault in the same
        // SaveChanges call.
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var cspProfileId = await db.Set<CspProfile>().IgnoreQueryFilters()
            .Select(p => p.Id)
            .FirstOrDefaultAsync();
        cspProfileId.Should().NotBe(Guid.Empty,
            "fixture must seed a CspProfile in MultiTenant mode");

        var globalComponent = new CspInheritedComponent
        {
            Id = Guid.NewGuid(),
            CspProfileId = cspProfileId,
            Name = $"GlobalRef-Test-{Guid.NewGuid():N}",
            Description = "[GlobalReference] component",
            ComponentType = CspComponentType.Service,
            SourceFormat = SourceFormat.Pdf,
            Status = CspInheritedComponentStatus.Published,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedBy = "T193-test",
        };
        db.CspInheritedComponents.Add(globalComponent);

        var tenantLocal = new OrgInheritanceDefault
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            Id = Guid.NewGuid().ToString(),
            ControlId = $"AC-2-T193-{Random.Shared.Next(10000)}",
            InheritanceType = InheritanceType.Inherited,
            Provider = globalComponent.Name,
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "Capability 1",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        };
        db.OrgInheritanceDefaults.Add(tenantLocal);

        // Act
        Func<Task> act = async () => await db.SaveChangesAsync();

        // Assert
        await act.Should().NotThrowAsync(
            "[GlobalReference] entities must coexist with [TenantScoped] writes in the same SaveChanges (FR-080).");

        // Verify the global row landed without a TenantId stamp.
        var savedGlobal = await db.CspInheritedComponents
            .FirstAsync(c => c.Id == globalComponent.Id);
        savedGlobal.Name.Should().Be(globalComponent.Name);

        // Verify the tenant-scoped row was stamped with Tenant A's id.
        var savedLocal = await db.OrgInheritanceDefaults
            .IgnoreQueryFilters()
            .FirstAsync(o => o.Id == tenantLocal.Id);
        savedLocal.TenantId.Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
    }

    [Fact]
    public async Task Read_CspInheritedComponent_FromOtherTenantSession_IsVisible()
    {
        // Arrange — write a CspInheritedComponent under Tenant A's session.
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        Guid componentId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var cspProfileId = await db.Set<CspProfile>().IgnoreQueryFilters()
                .Select(p => p.Id)
                .FirstAsync();
            var component = new CspInheritedComponent
            {
                Id = Guid.NewGuid(),
                CspProfileId = cspProfileId,
                Name = $"CrossRead-Test-{Guid.NewGuid():N}",
                Description = "Cross-tenant readable",
                ComponentType = CspComponentType.Service,
                SourceFormat = SourceFormat.Pdf,
                Status = CspInheritedComponentStatus.Published,
                ImportedAt = DateTimeOffset.UtcNow,
                ImportedBy = "T193-test",
            };
            db.CspInheritedComponents.Add(component);
            await db.SaveChangesAsync();
            componentId = component.Id;
        }

        // Act — flip to Tenant B and re-read without IgnoreQueryFilters.
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        ctx.IsCspAdmin = false;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        CspInheritedComponent? readFromTenantB;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            readFromTenantB = await db.CspInheritedComponents
                .FirstOrDefaultAsync(c => c.Id == componentId);
        }

        // Assert
        readFromTenantB.Should().NotBeNull(
            "[GlobalReference] entities are excluded from the auto-tenant query filter, " +
            "so every tenant must see them (FR-104, FR-105).");
    }

    [Fact]
    public async Task Insert_TenantScopedRow_WithCrossTenantTenantId_AsNonCspAdmin_IsRejected()
    {
        // Arrange — active tenant is Tenant A, caller is NOT CSP-Admin. We
        // explicitly tamper TenantId on a TenantScoped entity to point at
        // Tenant B. The interceptor must reject this on Insert (FR-021 +
        // FR-080: "still rejects" half of T193's contract).
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = false;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Production wires the accessor's AsyncLocal in middleware; under
        // the test fixture we push it manually so the interceptor's
        // FR-021/FR-080 enforcement runs (otherwise it short-circuits when
        // accessor.Current is null).
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        using var _ = accessor.Push(new TenantContext(
            MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            isCspAdmin: false));

        var tampered = new OrgInheritanceDefault
        {
            // Cross-tenant tampering: caller is in Tenant A but writes with
            // Tenant B's id.
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId,
            Id = Guid.NewGuid().ToString(),
            ControlId = $"AC-3-T193-{Random.Shared.Next(10000)}",
            InheritanceType = InheritanceType.Inherited,
            Provider = "test-provider",
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "Capability 1",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        };
        db.OrgInheritanceDefaults.Add(tampered);

        // Act
        Func<Task> act = async () => await db.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<TenantConsistencyException>(
            "FR-080 must reject cross-tenant TenantId tampering by non-CSP-Admin callers, " +
            "even when the entity in question is [TenantScoped] (not [GlobalReference]).");
    }
}
