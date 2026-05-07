using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration test confirming that a newly-registered system inherits org-level
/// role assignments via <see cref="RegisteredSystemRoleSnapshotter"/> (T061 / FR-024)
/// and that per-system mutations do not affect the org-level row (FR-025).
/// </summary>
public class RoleAssignmentInheritanceTests : IDisposable
{
    private readonly string _dbName = $"InheritanceTests_{Guid.NewGuid():N}";
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IWizardAuditService> _audit = new();

    public RoleAssignmentInheritanceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task RegisterSystem_InheritsOrgRoleAssignments()
    {
        var tenantId = Guid.NewGuid();
        var roleService = new OrganizationRoleAssignmentService(
            _factory, _audit.Object,
            NullLogger<OrganizationRoleAssignmentService>.Instance);
        var personService = new PersonService(
            _factory, Mock.Of<IDirectorySearchClient>(), _audit.Object,
            NullLogger<PersonService>.Instance);

        var issm = await personService.CreateLocalAsync(
            tenantId, "Issm Alice", "issm@x.mil", null, Guid.NewGuid(), Guid.NewGuid());
        var isso = await personService.CreateLocalAsync(
            tenantId, "Isso Bob", "isso@x.mil", null, Guid.NewGuid(), Guid.NewGuid());

        await roleService.AddAsync(tenantId, OrganizationRole.Issm, issm.Id, Guid.NewGuid(), Guid.NewGuid());
        await roleService.AddAsync(tenantId, OrganizationRole.Isso, isso.Id, Guid.NewGuid(), Guid.NewGuid());

        var snapshotter = new RegisteredSystemRoleSnapshotter(
            _factory, NullLogger<RegisteredSystemRoleSnapshotter>.Instance);

        var systemId = Guid.NewGuid().ToString();
        var copied = await snapshotter.SnapshotAsync(
            tenantId, systemId, Guid.NewGuid(), Guid.NewGuid());

        copied.Should().Be(2);
        var inherited = await snapshotter.ListEffectiveAsync(systemId);
        inherited.Should().HaveCount(2);
        inherited.Should().AllSatisfy(s => s.IsInherited.Should().BeTrue());
        inherited.Select(s => s.Role).Should().BeEquivalentTo(new[]
        {
            OrganizationRole.Issm, OrganizationRole.Isso,
        });
    }

    [Fact]
    public async Task SnapshotAsync_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var roleService = new OrganizationRoleAssignmentService(
            _factory, _audit.Object,
            NullLogger<OrganizationRoleAssignmentService>.Instance);
        var personService = new PersonService(
            _factory, Mock.Of<IDirectorySearchClient>(), _audit.Object,
            NullLogger<PersonService>.Instance);

        var issm = await personService.CreateLocalAsync(
            tenantId, "Issm Alice", "issm@x.mil", null, Guid.NewGuid(), Guid.NewGuid());
        await roleService.AddAsync(
            tenantId, OrganizationRole.Issm, issm.Id, Guid.NewGuid(), Guid.NewGuid());

        var snapshotter = new RegisteredSystemRoleSnapshotter(
            _factory, NullLogger<RegisteredSystemRoleSnapshotter>.Instance);
        var systemId = Guid.NewGuid().ToString();

        var first = await snapshotter.SnapshotAsync(
            tenantId, systemId, Guid.NewGuid(), Guid.NewGuid());
        var second = await snapshotter.SnapshotAsync(
            tenantId, systemId, Guid.NewGuid(), Guid.NewGuid());

        first.Should().Be(1);
        second.Should().Be(0, "snapshotter is idempotent — re-running is a no-op");
        var inherited = await snapshotter.ListEffectiveAsync(systemId);
        inherited.Should().HaveCount(1);
    }

    [Fact]
    public async Task PerSystemMutation_DoesNotMutateOrgRow()
    {
        var tenantId = Guid.NewGuid();
        var roleService = new OrganizationRoleAssignmentService(
            _factory, _audit.Object,
            NullLogger<OrganizationRoleAssignmentService>.Instance);
        var personService = new PersonService(
            _factory, Mock.Of<IDirectorySearchClient>(), _audit.Object,
            NullLogger<PersonService>.Instance);

        var issm = await personService.CreateLocalAsync(
            tenantId, "Issm Alice", "issm@x.mil", null, Guid.NewGuid(), Guid.NewGuid());
        var orgAssignment = await roleService.AddAsync(
            tenantId, OrganizationRole.Issm, issm.Id, Guid.NewGuid(), Guid.NewGuid());

        var snapshotter = new RegisteredSystemRoleSnapshotter(
            _factory, NullLogger<RegisteredSystemRoleSnapshotter>.Instance);
        var systemId = Guid.NewGuid().ToString();
        await snapshotter.SnapshotAsync(tenantId, systemId, Guid.NewGuid(), Guid.NewGuid());

        // FR-025: simulate a per-system override by removing the inherited row and
        // adding a non-inherited row pointing to a different person.
        var newPerson = await personService.CreateLocalAsync(
            tenantId, "Override Carol", "carol@x.mil", null, Guid.NewGuid(), Guid.NewGuid());
        await using (var db = _factory.CreateDbContext())
        {
            var inherited = await db.SystemRoleAssignments
                .Where(s => s.RegisteredSystemId == systemId &&
                            s.Role == OrganizationRole.Issm &&
                            s.IsInherited)
                .ToListAsync();
            foreach (var row in inherited)
            {
                row.RemovedAt = DateTimeOffset.UtcNow;
            }
            db.SystemRoleAssignments.Add(new SystemRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RegisteredSystemId = systemId,
                Role = OrganizationRole.Issm,
                PersonId = newPerson.Id,
                IsInherited = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Org row must remain pointed at the original person.
        await using (var db = _factory.CreateDbContext())
        {
            var orgRow = await db.OrganizationRoleAssignments
                .AsNoTracking()
                .FirstAsync(r => r.Id == orgAssignment.Assignment.Id);
            orgRow.PersonId.Should().Be(issm.Id, "FR-025: per-system overrides MUST NOT mutate the organization-level default");
            orgRow.RemovedAt.Should().BeNull();
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
