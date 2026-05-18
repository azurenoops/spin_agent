using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// Service-level invariants for <see cref="OrgControlOverrideService"/>
/// (Feature 048 follow-up — user ask #2).
/// </summary>
public class OrgControlOverrideServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly OrgControlOverrideService _sut;
    private static readonly Guid ActiveTenantId = Guid.NewGuid();

    public OrgControlOverrideServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"OrgControlOverride_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);

        _tenant.SetupGet(t => t.TenantId).Returns(ActiveTenantId);
        _tenant.SetupGet(t => t.EffectiveTenantId).Returns(ActiveTenantId);
        _tenant.SetupGet(t => t.IsCspAdmin).Returns(false);

        _sut = new OrgControlOverrideService(
            _factory,
            _tenant.Object,
            NullLogger<OrgControlOverrideService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task UpsertAsync_NewRow_PersistsAllFields()
    {
        // Arrange — no row exists for "AC-2".

        // Act
        var saved = await _sut.UpsertAsync(
            controlId: "ac-2",
            implementationStatus: ControlImplementationStatus.PartiallyImplemented,
            inheritanceApplicability: ControlInheritanceApplicability.Hybrid,
            justification: "Local SIEM ingests CSP audit events but org adds privileged-access review.",
            actor: "alice@org");

        // Assert
        saved.Should().NotBeNull();
        saved!.ControlId.Should().Be("AC-2"); // normalized to upper
        saved.ImplementationStatus.Should().Be(ControlImplementationStatus.PartiallyImplemented);
        saved.InheritanceApplicability.Should().Be(ControlInheritanceApplicability.Hybrid);
        saved.Justification.Should().Contain("SIEM");
        saved.CreatedBy.Should().Be("alice@org");
        saved.UpdatedBy.Should().Be("alice@org");

        await using var db = _factory.CreateDbContext();
        (await db.OrgControlOverrides.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_ExistingRow_UpdatesInPlace()
    {
        // Arrange — seed initial override.
        await _sut.UpsertAsync(
            controlId: "AC-2",
            implementationStatus: ControlImplementationStatus.Implemented,
            inheritanceApplicability: null,
            justification: "Initial",
            actor: "alice@org");

        // Act — second user updates with new fields.
        var updated = await _sut.UpsertAsync(
            controlId: "AC-2",
            implementationStatus: ControlImplementationStatus.PartiallyImplemented,
            inheritanceApplicability: ControlInheritanceApplicability.Hybrid,
            justification: "Updated",
            actor: "bob@org");

        // Assert — only one row exists; values reflect the second call.
        await using var db = _factory.CreateDbContext();
        var rows = await db.OrgControlOverrides.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].ImplementationStatus.Should().Be(ControlImplementationStatus.PartiallyImplemented);
        rows[0].InheritanceApplicability.Should().Be(ControlInheritanceApplicability.Hybrid);
        rows[0].Justification.Should().Be("Updated");
        rows[0].UpdatedBy.Should().Be("bob@org");
        // CreatedBy must remain the original author.
        rows[0].CreatedBy.Should().Be("alice@org");
        updated!.UpdatedBy.Should().Be("bob@org");
    }

    [Fact]
    public async Task UpsertAsync_BothFieldsNull_DeletesRow()
    {
        // Arrange — seed.
        await _sut.UpsertAsync(
            "SC-7",
            ControlImplementationStatus.Implemented,
            null,
            "stable",
            "alice@org");

        // Act — both override fields cleared.
        var result = await _sut.UpsertAsync(
            controlId: "SC-7",
            implementationStatus: null,
            inheritanceApplicability: null,
            justification: null,
            actor: "alice@org");

        // Assert — row deleted; method returns null.
        result.Should().BeNull();
        await using var db = _factory.CreateDbContext();
        (await db.OrgControlOverrides.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpsertAsync_OverrideWithoutJustification_Throws()
    {
        // Arrange / Act
        var act = async () => await _sut.UpsertAsync(
            controlId: "AU-2",
            implementationStatus: ControlImplementationStatus.Planned,
            inheritanceApplicability: null,
            justification: "  ", // whitespace-only is rejected
            actor: "alice@org");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.ParamName == "justification");
    }

    [Fact]
    public async Task UpsertAsync_BlankActor_Throws()
    {
        // Arrange / Act
        var act = async () => await _sut.UpsertAsync(
            "AC-2",
            ControlImplementationStatus.Implemented,
            null,
            "j",
            actor: "");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.ParamName == "actor");
    }

    [Fact]
    public async Task GetAsync_NoRow_ReturnsNull()
    {
        // Arrange — empty DB.

        // Act
        var result = await _sut.GetAsync("AC-2");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_AfterUpsert_ReturnsRow_CaseInsensitive()
    {
        // Arrange
        await _sut.UpsertAsync(
            "ac-2", ControlImplementationStatus.Implemented, null, "j", "alice@org");

        // Act — caller passes lowercased id.
        var result = await _sut.GetAsync("ac-2");

        // Assert
        result.Should().NotBeNull();
        result!.ControlId.Should().Be("AC-2");
    }

    [Fact]
    public async Task ListAsync_OrdersByControlId()
    {
        // Arrange
        await _sut.UpsertAsync("SC-7", ControlImplementationStatus.Implemented, null, "j", "alice@org");
        await _sut.UpsertAsync("AC-2", ControlImplementationStatus.Implemented, null, "j", "alice@org");
        await _sut.UpsertAsync("AU-2", ControlImplementationStatus.Implemented, null, "j", "alice@org");

        // Act
        var rows = await _sut.ListAsync();

        // Assert
        rows.Select(r => r.ControlId).Should().Equal("AC-2", "AU-2", "SC-7");
    }

    [Fact]
    public async Task DeleteAsync_RowExists_ReturnsTrueAndRemoves()
    {
        // Arrange
        await _sut.UpsertAsync(
            "AC-2", ControlImplementationStatus.Implemented, null, "j", "alice@org");

        // Act
        var removed = await _sut.DeleteAsync("AC-2", "alice@org");

        // Assert
        removed.Should().BeTrue();
        await using var db = _factory.CreateDbContext();
        (await db.OrgControlOverrides.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NoRow_ReturnsFalse()
    {
        // Arrange — empty DB.

        // Act
        var removed = await _sut.DeleteAsync("AC-2", "alice@org");

        // Assert
        removed.Should().BeFalse();
    }
}
