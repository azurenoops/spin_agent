using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class AutomaticCatalogBindingTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TenantContextAccessor _accessor = new();
    private AtoCopilotContext _db = null!;
    private AutomaticCatalogBindingService _service = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        _db = new(options, _accessor);
        await _db.Database.EnsureCreatedAsync();
        _service = new(new Factory(options, _accessor), _accessor, NullLogger<AutomaticCatalogBindingService>.Instance);
    }

    [Fact]
    public async Task AutomaticAssociation_UsesRecordedFrameworkAcrossTenants_WithoutApprovingContent()
    {
        // Arrange
        var first = Seed("NIST-800-53-R5");
        var second = Seed("FEDRAMP-R5");
        await _db.SaveChangesAsync();
        var originalIds = first.ControlIds.ToArray();

        // Act
        await _service.BackfillAsync();
        await _service.BackfillAsync();

        // Assert
        _db.ChangeTracker.Clear();
        var bindings = await _db.BaselineCatalogBindings.ToListAsync();
        bindings.Should().HaveCount(2);
        bindings.Single(x => x.ControlBaselineId == first.Id).FrameworkIdentifier.Should().Be("NIST-800-53-R5");
        bindings.Single(x => x.ControlBaselineId == second.Id).FrameworkIdentifier.Should().Be("FEDRAMP-R5");
        bindings.Single(x => x.ControlBaselineId == first.Id).TenantId.Should().Be(first.TenantId);
        bindings.Should().OnlyContain(x => x.BoundBy == "system:catalog-association");
        (await _db.ControlBaselines.SingleAsync(x => x.Id == first.Id)).ControlIds.Should().Equal(originalIds);
        (await _db.ControlImplementations.CountAsync()).Should().Be(0);
        (await _db.NarrativeReviews.CountAsync()).Should().Be(0);
        (await _db.NarrativeVersions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SourceRefresh_DoesNotReplaceAnExistingPinnedBinding()
    {
        // Arrange
        Seed("NIST-800-53-R5");
        await _db.SaveChangesAsync();
        await _service.BackfillAsync();
        var original = await _db.BaselineCatalogBindings.AsNoTracking().SingleAsync();
        var framework = await _db.ComplianceFrameworks.SingleAsync();
        framework.RequirementCatalogJson = framework.RequirementCatalogJson!.Replace("test-1", "test-2");
        await _db.SaveChangesAsync();

        // Act
        await _service.BackfillAsync();

        // Assert
        var retained = await _db.BaselineCatalogBindings.AsNoTracking().SingleAsync();
        retained.Id.Should().Be(original.Id);
        retained.ContentHash.Should().Be(original.ContentHash);
        retained.CatalogVersion.Should().Be("test-1");
    }

    [Fact]
    public async Task LegacyNistBaseline_ResolvesFrameworkFromBaselineContractWithoutUserSelection()
    {
        // Arrange
        var baseline = Seed("NIST-800-53-R5");
        baseline.SourceFrameworkIdentifier = null;
        await _db.SaveChangesAsync();

        // Act
        await _service.StartAsync(default);

        // Assert
        _db.ChangeTracker.Clear();
        var persisted = await _db.ControlBaselines.SingleAsync();
        persisted.SourceFrameworkIdentifier.Should().Be("NIST-800-53-R5");
        persisted.RequirementCatalogBindingId.Should().NotBeNull();
        persisted.ControlIds.Should().Equal("AC-11");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnavailableOrIncompatibleSource_RecordsOperationalGapWithoutChoosingAnotherFramework(bool absent)
    {
        // Arrange
        var baseline = Seed("NIST-800-53-R5");
        if (absent) _db.ComplianceFrameworks.Local.First().RequirementCatalogJson = null;
        else baseline.ControlIds = ["UNKNOWN-CONTROL"];
        await _db.SaveChangesAsync();

        // Act
        await _service.BackfillAsync();

        // Assert
        _db.ChangeTracker.Clear();
        var persisted = await _db.ControlBaselines.SingleAsync();
        persisted.RequirementCatalogBindingId.Should().BeNull();
        persisted.CatalogResolutionMessage.Should().NotBeNullOrWhiteSpace();
        (await _db.BaselineCatalogBindings.CountAsync()).Should().Be(0);
    }

    private ControlBaseline Seed(string identifier)
    {
        var tenant = Guid.NewGuid();
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic automatic source", CreatedBy = "fixture" };
        var baseline = new ControlBaseline { TenantId = tenant, RegisteredSystemId = system.Id,
            SourceFrameworkIdentifier = identifier, BaselineLevel = "Moderate", ControlIds = ["AC-11"], CreatedBy = "fixture" };
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Requirements", "catalog.json"));
        _db.AddRange(system, baseline, new ComplianceFramework { Identifier = identifier, Name = identifier,
            CatalogUrl = "https://example.invalid/catalog", RequirementCatalogJson = raw });
        return baseline;
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class Factory(DbContextOptions<AtoCopilotContext> options, TenantContextAccessor accessor)
        : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options, accessor);
    }
}
