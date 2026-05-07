using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Unit tests for <see cref="OrganizationContextService"/> (T048 / FR-010..FR-014).
/// </summary>
public class OrganizationContextServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly OrganizationContextService _sut;

    public OrganizationContextServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"OrganizationContextServiceTests_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);

        _sut = new OrganizationContextService(
            _factory,
            _audit.Object,
            NullLogger<OrganizationContextService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task GetAsync_NoRow_ReturnsNull()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_FreshTenant_CreatesRowAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var input = new OrganizationContextInput(
            OrganizationName: "Test Agency",
            Branch: BranchAffiliation.CivilAgency,
            SubOrganization: "Bureau of Compliance");

        var result = await _sut.UpsertAsync(tenantId, input, actor, Guid.NewGuid());

        result.Should().NotBeNull();
        result.OrganizationName.Should().Be("Test Agency");
        result.Branch.Should().Be(BranchAffiliation.CivilAgency);
        result.SubOrganization.Should().Be("Bureau of Compliance");
        result.CreatedBy.Should().Be(actor);

        _audit.Verify(a => a.RecordAsync(
            tenantId,
            actor,
            WizardAuditAction.OrganizationContextSaved,
            nameof(OrganizationContext),
            It.IsAny<Guid?>(),
            null,
            It.Is<string?>(s => s != null && s.Contains("Test Agency")),
            null,
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_ExistingRow_UpdatesAndCapturesBeforeAfter()
    {
        var tenantId = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await _sut.UpsertAsync(tenantId,
            new OrganizationContextInput("Original", BranchAffiliation.Army), actor, Guid.NewGuid());

        var updated = await _sut.UpsertAsync(tenantId,
            new OrganizationContextInput("Updated", BranchAffiliation.Navy), actor, Guid.NewGuid());

        updated.OrganizationName.Should().Be("Updated");
        updated.Branch.Should().Be(BranchAffiliation.Navy);

        _audit.Verify(a => a.RecordAsync(
            tenantId,
            actor,
            WizardAuditAction.OrganizationContextSaved,
            nameof(OrganizationContext),
            It.IsAny<Guid?>(),
            It.Is<string?>(s => s != null && s.Contains("Original")),
            It.Is<string?>(s => s != null && s.Contains("Updated")),
            null,
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_EmptyOrganizationName_Throws()
    {
        var act = async () => await _sut.UpsertAsync(
            Guid.NewGuid(),
            new OrganizationContextInput("   ", BranchAffiliation.CivilAgency),
            Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Organization name*required*");
    }

    [Fact]
    public async Task UpsertAsync_IndustryPartnerOtherWithoutQualifier_Throws()
    {
        var act = async () => await _sut.UpsertAsync(
            Guid.NewGuid(),
            new OrganizationContextInput("ACME Corp", BranchAffiliation.IndustryPartnerOther),
            Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Branch qualifier*required*");
    }

    [Fact]
    public async Task UpsertAsync_IndustryPartnerOtherWithQualifier_Succeeds()
    {
        var result = await _sut.UpsertAsync(
            Guid.NewGuid(),
            new OrganizationContextInput("ACME Corp", BranchAffiliation.IndustryPartnerOther,
                BranchQualifier: "Defense Industrial Base"),
            Guid.NewGuid(), Guid.NewGuid());

        result.Branch.Should().Be(BranchAffiliation.IndustryPartnerOther);
        result.BranchQualifier.Should().Be("Defense Industrial Base");
    }

    [Fact]
    public async Task UpsertAsync_InvalidUrl_Throws()
    {
        var act = async () => await _sut.UpsertAsync(
            Guid.NewGuid(),
            new OrganizationContextInput("Acme", BranchAffiliation.CivilAgency,
                AuthoritativeRepositoryUrl: "not a url"),
            Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*absolute http*URL*");
    }

    [Fact]
    public async Task UpsertAsync_InvalidEmail_Throws()
    {
        var act = async () => await _sut.UpsertAsync(
            Guid.NewGuid(),
            new OrganizationContextInput("Acme", BranchAffiliation.CivilAgency,
                PrimaryPocEmail: "not-an-email"),
            Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*RFC-5322*");
    }

    [Fact]
    public async Task UpsertAsync_TenantsAreIsolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actor = Guid.NewGuid();

        await _sut.UpsertAsync(tenantA,
            new OrganizationContextInput("Org A", BranchAffiliation.Army), actor, Guid.NewGuid());
        await _sut.UpsertAsync(tenantB,
            new OrganizationContextInput("Org B", BranchAffiliation.Navy), actor, Guid.NewGuid());

        (await _sut.GetAsync(tenantA))!.OrganizationName.Should().Be("Org A");
        (await _sut.GetAsync(tenantB))!.OrganizationName.Should().Be("Org B");
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
