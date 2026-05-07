using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="OrganizationRoleAssignmentService"/>
/// (T058 / FR-020..FR-026 / FR-002 last-Administrator invariant).
/// </summary>
public class OrganizationRoleAssignmentServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly OrganizationRoleAssignmentService _sut;

    public OrganizationRoleAssignmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"OrganizationRoleAssignmentServiceTests_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
        _sut = new OrganizationRoleAssignmentService(
            _factory, _audit.Object,
            NullLogger<OrganizationRoleAssignmentService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    private async Task<Person> SeedPersonAsync(Guid tenantId, string name = "Sample")
    {
        await using var db = _factory.CreateDbContext();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = name,
            Email = $"{name.ToLowerInvariant()}@example.mil",
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    [Fact]
    public async Task AddAsync_FirstAssignment_IsPrimary_NoWarnings()
    {
        var tenantId = Guid.NewGuid();
        var person = await SeedPersonAsync(tenantId);

        var result = await _sut.AddAsync(
            tenantId, OrganizationRole.Issm, person.Id, Guid.NewGuid(), Guid.NewGuid());

        result.Assignment.IsPrimary.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_SecondIssm_EmitsWarning()
    {
        var tenantId = Guid.NewGuid();
        var p1 = await SeedPersonAsync(tenantId, "First");
        var p2 = await SeedPersonAsync(tenantId, "Second");

        await _sut.AddAsync(tenantId, OrganizationRole.Issm, p1.Id, Guid.NewGuid(), Guid.NewGuid());
        var result = await _sut.AddAsync(
            tenantId, OrganizationRole.Issm, p2.Id, Guid.NewGuid(), Guid.NewGuid());

        result.Warnings.Should().ContainSingle().Which.Should().Contain("Multiple Issm");
    }

    [Fact]
    public async Task AddAsync_SecondIsso_NoWarning()
    {
        var tenantId = Guid.NewGuid();
        var p1 = await SeedPersonAsync(tenantId, "First");
        var p2 = await SeedPersonAsync(tenantId, "Second");

        await _sut.AddAsync(tenantId, OrganizationRole.Isso, p1.Id, Guid.NewGuid(), Guid.NewGuid());
        var result = await _sut.AddAsync(
            tenantId, OrganizationRole.Isso, p2.Id, Guid.NewGuid(), Guid.NewGuid());

        result.Warnings.Should().BeEmpty();
        result.Assignment.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_DuplicatePersonRole_Throws()
    {
        var tenantId = Guid.NewGuid();
        var person = await SeedPersonAsync(tenantId);
        await _sut.AddAsync(tenantId, OrganizationRole.Isso, person.Id, Guid.NewGuid(), Guid.NewGuid());

        var act = async () => await _sut.AddAsync(
            tenantId, OrganizationRole.Isso, person.Id, Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RemoveAsync_LastAdmin_ThrowsLastAdminProtected()
    {
        var tenantId = Guid.NewGuid();
        var person = await SeedPersonAsync(tenantId);
        var added = await _sut.AddAsync(
            tenantId, OrganizationRole.Administrator, person.Id, Guid.NewGuid(), Guid.NewGuid());

        var act = async () => await _sut.RemoveAsync(
            tenantId, added.Assignment.Id, Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Be(WizardErrorCodes.LastAdminProtected);
    }

    [Fact]
    public async Task RemoveAsync_AdminWithReplacement_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var p1 = await SeedPersonAsync(tenantId, "Primary");
        var p2 = await SeedPersonAsync(tenantId, "Backup");

        var added1 = await _sut.AddAsync(
            tenantId, OrganizationRole.Administrator, p1.Id, Guid.NewGuid(), Guid.NewGuid());
        await _sut.AddAsync(
            tenantId, OrganizationRole.Administrator, p2.Id, Guid.NewGuid(), Guid.NewGuid());

        await _sut.RemoveAsync(tenantId, added1.Assignment.Id, Guid.NewGuid(), Guid.NewGuid());

        var remaining = await _sut.ListAsync(tenantId);
        remaining.Should().ContainSingle()
            .Which.PersonId.Should().Be(p2.Id);
    }

    [Fact]
    public async Task ListAsync_ExcludesRemoved()
    {
        var tenantId = Guid.NewGuid();
        var p1 = await SeedPersonAsync(tenantId, "Keep");
        var p2 = await SeedPersonAsync(tenantId, "Remove");

        await _sut.AddAsync(tenantId, OrganizationRole.Isso, p1.Id, Guid.NewGuid(), Guid.NewGuid());
        var doomed = await _sut.AddAsync(
            tenantId, OrganizationRole.Isso, p2.Id, Guid.NewGuid(), Guid.NewGuid());
        await _sut.RemoveAsync(tenantId, doomed.Assignment.Id, Guid.NewGuid(), Guid.NewGuid());

        var list = await _sut.ListAsync(tenantId);
        list.Should().ContainSingle().Which.PersonId.Should().Be(p1.Id);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
