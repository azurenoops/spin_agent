using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services.Roles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Roles;

/// <summary>
/// T018a [US1] — Failing tests pinning <see cref="ICallerEffectiveRoleResolver"/> per
/// <c>specs/049-unified-rmf-role-assignments/contracts/internal-services.md § 6</c>.
///
/// <para>
/// The resolver returns a <see cref="CallerEffectiveRole"/> struct carrying:
/// </para>
/// <list type="number">
///   <item>The caller's highest-privileged <see cref="RmfRole"/> over the gradient
///         <c>Issm &gt; Isso &gt; {AO, Sca, SystemOwner, MissionOwner}</c>, or <c>null</c>
///         when no RmfRole-bearing row exists.</item>
///   <item><c>IsTenantAdministrator</c> = true iff the caller holds an active
///         <see cref="OrganizationRole.Administrator"/> assignment.</item>
/// </list>
///
/// <para>Sources read (in priority order):</para>
/// <list type="bullet">
///   <item><c>OrganizationRoleAssignment</c> (active, RemovedAt null) — also sets the Administrator bit.</item>
///   <item><c>SystemRoleAssignment</c> (active, RemovedAt null) across all systems the principal touches.</item>
///   <item>Legacy <c>RmfRoleAssignment</c> (IsActive=true) — FR-024 read-side fallback.</item>
/// </list>
/// </summary>
public class CallerEffectiveRoleResolverTests
{
    private sealed class StaticFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly string _dbName;
        public StaticFactory(string dbName) => _dbName = dbName;
        public AtoCopilotContext CreateDbContext() => new(
            new DbContextOptionsBuilder<AtoCopilotContext>().UseInMemoryDatabase(_dbName).Options);
    }

    private static (IDbContextFactory<AtoCopilotContext> factory, string dbName) NewFactory()
    {
        var dbName = $"caller_{Guid.NewGuid():N}";
        return (new StaticFactory(dbName), dbName);
    }

    private static async Task SeedPersonAsync(
        IDbContextFactory<AtoCopilotContext> factory,
        Guid tenantId,
        Guid personId,
        Guid? entraObjectId = null)
    {
        await using var db = factory.CreateDbContext();
        db.Persons.Add(new Person
        {
            Id = personId,
            TenantId = tenantId,
            EntraObjectId = entraObjectId,
            DisplayName = $"P-{personId:N}".Substring(0, 10),
            Email = $"{personId:N}@x.mil",
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Caller_with_no_roles_returns_None()
    {
        // Arrange
        var (factory, _) = NewFactory();
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        await SeedPersonAsync(factory, tenantId, personId);
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantId, personId, CancellationToken.None);

        // Assert
        caller.Should().Be(CallerEffectiveRole.None,
            "no rows in any source table → CallerEffectiveRole.None");
    }

    [Fact]
    public async Task Caller_with_Isso_only_returns_Isso_no_Administrator()
    {
        // Arrange
        var (factory, _) = NewFactory();
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        await SeedPersonAsync(factory, tenantId, personId);
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId,
                Role = OrganizationRole.Isso,
                PersonId = personId,
                IsPrimary = false,
            });
            await db.SaveChangesAsync();
        }
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantId, personId, CancellationToken.None);

        // Assert
        caller.RmfRole.Should().Be(RmfRole.Isso);
        caller.IsTenantAdministrator.Should().BeFalse();
    }

    [Fact]
    public async Task Caller_EntraOid_resolves_to_tenant_person_role()
    {
        // Arrange
        var (factory, _) = NewFactory();
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var entraObjectId = Guid.NewGuid();
        await SeedPersonAsync(factory, tenantId, personId, entraObjectId);
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId,
                Role = OrganizationRole.Isso,
                PersonId = personId,
            });
            await db.SaveChangesAsync();
        }
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantId, entraObjectId, CancellationToken.None);

        // Assert
        caller.RmfRole.Should().Be(RmfRole.Isso);
        caller.IsTenantAdministrator.Should().BeFalse();
    }

    [Fact]
    public async Task Caller_EntraOid_does_not_resolve_role_from_another_tenant()
    {
        // Arrange
        var (factory, _) = NewFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var entraObjectId = Guid.NewGuid();
        var personAId = Guid.NewGuid();
        var personBId = Guid.NewGuid();
        await SeedPersonAsync(factory, tenantA, personAId, entraObjectId);
        await SeedPersonAsync(factory, tenantB, personBId, entraObjectId);
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantA,
                Role = OrganizationRole.Issm,
                PersonId = personAId,
            });
            await db.SaveChangesAsync();
        }
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantB, entraObjectId, CancellationToken.None);

        // Assert
        caller.Should().Be(CallerEffectiveRole.None);
    }

    [Fact]
    public async Task Caller_with_Isso_and_Issm_returns_Issm()
    {
        // Arrange — gradient: Issm > Isso → caller resolves to Issm.
        var (factory, _) = NewFactory();
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        await SeedPersonAsync(factory, tenantId, personId);
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId, Role = OrganizationRole.Isso, PersonId = personId,
            });
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId, Role = OrganizationRole.Issm, PersonId = personId,
            });
            await db.SaveChangesAsync();
        }
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantId, personId, CancellationToken.None);

        // Assert
        caller.RmfRole.Should().Be(RmfRole.Issm,
            "the privilege gradient Issm > Isso must elect Issm as the effective RmfRole");
        caller.IsTenantAdministrator.Should().BeFalse();
    }

    [Fact]
    public async Task Caller_with_Administrator_only_sets_bit_and_null_RmfRole()
    {
        // Arrange — Administrator has no RmfRole image (TryMap returns null).
        var (factory, _) = NewFactory();
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        await SeedPersonAsync(factory, tenantId, personId);
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId, Role = OrganizationRole.Administrator, PersonId = personId,
            });
            await db.SaveChangesAsync();
        }
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantId, personId, CancellationToken.None);

        // Assert
        caller.IsTenantAdministrator.Should().BeTrue();
        caller.RmfRole.Should().BeNull(
            "Administrator has no RmfRole image — it sets only the IsTenantAdministrator bit");
    }

    [Fact]
    public async Task Caller_with_Administrator_and_Issm_sets_both_fields()
    {
        // Arrange
        var (factory, _) = NewFactory();
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        await SeedPersonAsync(factory, tenantId, personId);
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId, Role = OrganizationRole.Administrator, PersonId = personId,
            });
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId, Role = OrganizationRole.Issm, PersonId = personId,
            });
            await db.SaveChangesAsync();
        }
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantId, personId, CancellationToken.None);

        // Assert
        caller.IsTenantAdministrator.Should().BeTrue();
        caller.RmfRole.Should().Be(RmfRole.Issm,
            "the two fields are independent — a caller may hold BOTH Administrator and an RmfRole-bearing row");
    }

    [Fact]
    public async Task Cross_tenant_roles_do_NOT_count()
    {
        // Arrange — caller holds Issm in tenant A; query for tenant B.
        var (factory, _) = NewFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var personId = Guid.NewGuid();
        await SeedPersonAsync(factory, tenantA, personId);
        await using (var db = factory.CreateDbContext())
        {
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantA, Role = OrganizationRole.Issm, PersonId = personId,
            });
            await db.SaveChangesAsync();
        }
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantB, personId, CancellationToken.None);

        // Assert
        caller.Should().Be(CallerEffectiveRole.None,
            "the resolver is tenant-scoped — roles held in a different tenant MUST NOT count");
    }

    [Fact]
    public async Task Legacy_RmfRoleAssignment_is_honored_as_fallback()
    {
        // Arrange — only a legacy row exists; the resolver still elects its RmfRole.
        // The bridge: legacy RmfRoleAssignment.UserId carries the Person.Email
        // (Feature 049 keeps the legacy table read-only; matching by Email is the
        // documented FR-024 compatibility shim).
        var (factory, _) = NewFactory();
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var email = $"{personId:N}@x.mil";

        await using (var db = factory.CreateDbContext())
        {
            db.Persons.Add(new Person
            {
                Id = personId,
                TenantId = tenantId,
                DisplayName = "Legacy Issm",
                Email = email,
            });
            db.RmfRoleAssignments.Add(new RmfRoleAssignment
            {
                Id = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                RegisteredSystemId = Guid.NewGuid().ToString(),
                RmfRole = RmfRole.Issm,
                UserId = email,
                AssignedBy = "test",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }
        ICallerEffectiveRoleResolver resolver = new CallerEffectiveRoleResolver(factory);

        // Act
        var caller = await resolver.ResolveAsync(tenantId, personId, CancellationToken.None);

        // Assert
        caller.RmfRole.Should().Be(RmfRole.Issm,
            "FR-024: legacy RmfRoleAssignment rows MUST be honored as a fallback source when no Org or System row exists");
        caller.IsTenantAdministrator.Should().BeFalse();
    }
}
