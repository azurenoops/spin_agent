using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class BoundaryDefinitionServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly BoundaryDefinitionService _sut;
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;

    private const string SystemId = "sys-001";
    private const string PrimaryBoundaryId = "bnd-primary";

    public BoundaryDefinitionServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"BoundaryTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(_dbOptions);
        var logger = Mock.Of<ILogger<BoundaryDefinitionService>>();
        _sut = new BoundaryDefinitionService(_db, logger);

        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId,
            Name = "Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });

        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = PrimaryBoundaryId,
            RegisteredSystemId = SystemId,
            Name = "Test System — Primary",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "migration",
        });

        _db.SaveChanges();
    }

    // ─── List ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsAllBoundariesForSystem()
    {
        var result = await _sut.ListAsync(SystemId);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Test System — Primary");
        result[0].IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_EmptyForUnknownSystem()
    {
        var result = await _sut.ListAsync("unknown-system");

        result.Should().BeEmpty();
    }

    // ─── Create ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CreatesNewBoundary()
    {
        var request = new CreateBoundaryDefinitionRequest("Dev/Test", "Logical", "Dev environment");

        var result = await _sut.CreateAsync(SystemId, request, "tester");

        result.Name.Should().Be("Dev/Test");
        result.BoundaryType.Should().Be("Logical");
        result.IsPrimary.Should().BeFalse();
        result.Description.Should().Be("Dev environment");

        var all = await _sut.ListAsync(SystemId);
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_Throws()
    {
        var request = new CreateBoundaryDefinitionRequest("Test System — Primary", "Logical", null);

        var act = () => _sut.CreateAsync(SystemId, request, "tester");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task CreateAsync_InvalidSystem_Throws()
    {
        var request = new CreateBoundaryDefinitionRequest("New", "Logical", null);

        var act = () => _sut.CreateAsync("bad-system", request, "tester");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateAsync_InvalidBoundaryType_Throws()
    {
        var request = new CreateBoundaryDefinitionRequest("New", "InvalidType", null);

        var act = () => _sut.CreateAsync(SystemId, request, "tester");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid boundary type*");
    }

    [Fact]
    public async Task CreateAsync_CreatesAuditLogEntry()
    {
        var request = new CreateBoundaryDefinitionRequest("Audit Test", "Physical", null);

        await _sut.CreateAsync(SystemId, request, "auditor");

        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "BoundaryDefinition.Created");
        audit.Should().NotBeNull();
        audit!.UserId.Should().Be("auditor");
        audit.Details.Should().Contain("Audit Test");
    }

    // ─── Update ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UpdatesBoundary()
    {
        var created = await _sut.CreateAsync(SystemId,
            new CreateBoundaryDefinitionRequest("Original", "Logical", null), "tester");

        var result = await _sut.UpdateAsync(created.Id,
            new CreateBoundaryDefinitionRequest("Updated", "Physical", "New desc"));

        result.Name.Should().Be("Updated");
        result.BoundaryType.Should().Be("Physical");
    }

    [Fact]
    public async Task UpdateAsync_DuplicateName_Throws()
    {
        await _sut.CreateAsync(SystemId,
            new CreateBoundaryDefinitionRequest("Second", "Logical", null), "tester");

        var act = () => _sut.UpdateAsync(PrimaryBoundaryId,
            new CreateBoundaryDefinitionRequest("Second", "Logical", null));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        var act = () => _sut.UpdateAsync("bad-id",
            new CreateBoundaryDefinitionRequest("X", "Logical", null));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ─── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DeletesAndReassignsToPrimary()
    {
        var created = await _sut.CreateAsync(SystemId,
            new CreateBoundaryDefinitionRequest("Temp", "Logical", null), "tester");

        // Add a component assigned to the temp boundary
        _db.SystemComponents.Add(new SystemComponent
        {
            Id = "comp-1",
            RegisteredSystemId = SystemId,
            Name = "Test Component",
            ComponentType = ComponentType.Thing,
            Status = ComponentStatus.Active,
            AuthorizationBoundaryDefinitionId = created.Id,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.DeleteAsync(created.Id, "deleter");

        result.DeletedId.Should().Be(created.Id);
        result.ReassignedComponents.Should().Be(1);
        result.PrimaryBoundaryId.Should().Be(PrimaryBoundaryId);

        // Component should now belong to Primary
        var comp = await _db.SystemComponents.FindAsync("comp-1");
        comp!.AuthorizationBoundaryDefinitionId.Should().Be(PrimaryBoundaryId);
    }

    [Fact]
    public async Task DeleteAsync_PrimaryBoundary_Throws()
    {
        var act = () => _sut.DeleteAsync(PrimaryBoundaryId, "tester");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Primary*");
    }

    [Fact]
    public async Task DeleteAsync_NotFound_Throws()
    {
        var act = () => _sut.DeleteAsync("bad-id", "tester");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task DeleteAsync_CreatesAuditLogEntry()
    {
        var created = await _sut.CreateAsync(SystemId,
            new CreateBoundaryDefinitionRequest("AuditDel", "Hybrid", null), "tester");

        await _sut.DeleteAsync(created.Id, "auditor");

        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "BoundaryDefinition.Deleted");
        audit.Should().NotBeNull();
        audit!.UserId.Should().Be("auditor");
        audit.Details.Should().Contain("AuditDel");
    }

    // ─── Boundary-value tests ────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ZeroBoundaries_ReturnsEmpty()
    {
        // Create a system with no boundaries
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "sys-empty",
            Name = "Empty System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.ListAsync("sys-empty");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_MaxBoundaries_AllReturned()
    {
        // Create 20 boundaries (boundary-value test for large count)
        for (int i = 0; i < 20; i++)
        {
            await _sut.CreateAsync(SystemId,
                new CreateBoundaryDefinitionRequest($"Boundary-{i}", "Logical", null), "tester");
        }

        var result = await _sut.ListAsync(SystemId);
        result.Should().HaveCount(21); // 20 + Primary
    }

    // ─── GetById ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsCorrectBoundary()
    {
        var result = await _sut.GetByIdAsync(PrimaryBoundaryId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(PrimaryBoundaryId);
        result.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync("nonexistent");

        result.Should().BeNull();
    }
}
