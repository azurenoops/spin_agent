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
/// SC-013 — replacing a source artifact MUST flag every dependent stale across
/// all four <see cref="ArtifactSourceKind"/> categories.
/// </summary>
public class WizardArtifactDependencyServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IWizardJobRunner> _jobRunner = new();
    private readonly WizardArtifactDependencyService _sut;

    public WizardArtifactDependencyServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"WizardArtifactDeps_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
        _sut = new WizardArtifactDependencyService(
            _factory,
            _jobRunner.Object,
            NullLogger<WizardArtifactDependencyService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Theory]
    [InlineData(ArtifactSourceKind.Template)]
    [InlineData(ArtifactSourceKind.EmassImportSession)]
    [InlineData(ArtifactSourceKind.SspPdfImportSession)]
    [InlineData(ArtifactSourceKind.NarrativeSeedDocument)]
    public async Task FlagDependentsStaleAsync_FlagsAllDependents(ArtifactSourceKind sourceKind)
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        const int dependentCount = 3;

        for (var i = 0; i < dependentCount; i++)
        {
            await _sut.LinkAsync(
                tenantId,
                sourceKind,
                sourceId,
                sourceVersionTag: "v1",
                ArtifactDependentKind.RegisteredSystem,
                Guid.NewGuid());
        }

        var flagged = await _sut.FlagDependentsStaleAsync(
            tenantId, sourceKind, sourceId,
            staleReason: "source replaced");

        flagged.Should().Be(dependentCount);

        await using var db = _factory.CreateDbContext();
        var rows = await db.WizardArtifactDependencies
            .Where(d => d.TenantId == tenantId && d.SourceArtifactType == sourceKind)
            .ToListAsync();
        rows.Should().HaveCount(dependentCount);
        rows.Should().AllSatisfy(r =>
        {
            r.IsStale.Should().BeTrue();
            r.StaleSince.Should().NotBeNull();
            r.StaleReason.Should().Be("source replaced");
        });
    }

    [Fact]
    public async Task FlagDependentsStaleAsync_DoesNotFlagOtherTenantsDependents()
    {
        var sourceKind = ArtifactSourceKind.Template;
        var sourceId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await _sut.LinkAsync(tenantA, sourceKind, sourceId, "v1",
            ArtifactDependentKind.RegisteredSystem, Guid.NewGuid());
        await _sut.LinkAsync(tenantB, sourceKind, sourceId, "v1",
            ArtifactDependentKind.RegisteredSystem, Guid.NewGuid());

        var flagged = await _sut.FlagDependentsStaleAsync(
            tenantA, sourceKind, sourceId, "stale");

        flagged.Should().Be(1);

        await using var db = _factory.CreateDbContext();
        var bRow = await db.WizardArtifactDependencies
            .FirstAsync(d => d.TenantId == tenantB);
        bRow.IsStale.Should().BeFalse();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
