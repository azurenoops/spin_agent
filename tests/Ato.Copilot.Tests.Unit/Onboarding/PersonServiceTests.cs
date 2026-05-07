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
/// Unit tests for <see cref="PersonService"/> (T057 / FR-022 / research §R1).
/// </summary>
public class PersonServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IDirectorySearchClient> _directory = new();
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly PersonService _sut;

    public PersonServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"PersonServiceTests_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
        _sut = new PersonService(
            _factory, _directory.Object, _audit.Object,
            NullLogger<PersonService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task CreateLocalAsync_WritesRowAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var person = await _sut.CreateLocalAsync(
            tenantId, "Jane Doe", "jane@example.mil", null, actor, Guid.NewGuid());

        person.Id.Should().NotBe(Guid.Empty);
        person.IsLinkedToDirectory.Should().BeFalse();
        person.EntraObjectId.Should().BeNull();

        _audit.Verify(a => a.RecordAsync(
            tenantId, actor, WizardAuditAction.PersonCreated,
            nameof(Ato.Copilot.Core.Models.Onboarding.Person), person.Id,
            null, It.IsAny<string>(), null,
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateLocalAsync_DuplicateEmailForTenant_Throws()
    {
        var tenantId = Guid.NewGuid();
        await _sut.CreateLocalAsync(
            tenantId, "Jane", "jane@example.mil", null, Guid.NewGuid(), Guid.NewGuid());

        var act = async () => await _sut.CreateLocalAsync(
            tenantId, "Jane2", "jane@example.mil", null, Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListAsync_ReturnsTenantScoped()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await _sut.CreateLocalAsync(tenantA, "Alice", "a@x.mil", null, Guid.NewGuid(), Guid.NewGuid());
        await _sut.CreateLocalAsync(tenantB, "Bob", "b@x.mil", null, Guid.NewGuid(), Guid.NewGuid());

        var resultA = await _sut.ListAsync(tenantA);
        var resultB = await _sut.ListAsync(tenantB);

        resultA.Should().ContainSingle(p => p.Email == "a@x.mil");
        resultB.Should().ContainSingle(p => p.Email == "b@x.mil");
    }

    [Fact]
    public async Task SearchDirectoryAsync_DelegatesToDirectoryClient()
    {
        _directory.Setup(d => d.SearchAsync("ja", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new DirectoryPersonDto(Guid.NewGuid(), "Jane Doe", "jane@example.mil", "Cyber"),
            });

        var hits = await _sut.SearchDirectoryAsync("ja");

        hits.Should().ContainSingle(h => h.Email == "jane@example.mil");
    }

    [Fact]
    public async Task PromoteToDirectoryAsync_SetsLinkAndPreservesId()
    {
        var tenantId = Guid.NewGuid();
        var person = await _sut.CreateLocalAsync(
            tenantId, "Carol", "carol@example.mil", null, Guid.NewGuid(), Guid.NewGuid());
        var originalId = person.Id;
        var oid = Guid.NewGuid();

        var promoted = await _sut.PromoteToDirectoryAsync(
            tenantId, person.Id, oid, Guid.NewGuid(), Guid.NewGuid());

        promoted.Id.Should().Be(originalId, "research §R1: id is stable across promotion");
        promoted.IsLinkedToDirectory.Should().BeTrue();
        promoted.EntraObjectId.Should().Be(oid);
        promoted.LastPromotedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PromoteToDirectoryAsync_AlreadyLinked_Throws()
    {
        var tenantId = Guid.NewGuid();
        var person = await _sut.CreateLocalAsync(
            tenantId, "Carol", "carol@example.mil", null, Guid.NewGuid(), Guid.NewGuid());
        await _sut.PromoteToDirectoryAsync(
            tenantId, person.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var act = async () => await _sut.PromoteToDirectoryAsync(
            tenantId, person.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SearchLocalAsync_FiltersByNameOrEmail()
    {
        var tenantId = Guid.NewGuid();
        await _sut.CreateLocalAsync(tenantId, "Alice Sample", "alice@x.mil", null, Guid.NewGuid(), Guid.NewGuid());
        await _sut.CreateLocalAsync(tenantId, "Bob Tester", "bob@x.mil", null, Guid.NewGuid(), Guid.NewGuid());

        var byName = await _sut.SearchLocalAsync(tenantId, "Alice");
        var byEmail = await _sut.SearchLocalAsync(tenantId, "bob@");

        byName.Should().ContainSingle(p => p.DisplayName.StartsWith("Alice"));
        byEmail.Should().ContainSingle(p => p.Email == "bob@x.mil");
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
