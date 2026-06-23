using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Agents.Compliance.Tools;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Tests for #519 / #524 — DELETE /systems/{systemId} endpoint behaviour
/// and compliance_delete_system MCP tool.
/// </summary>
public class DeleteSystemEndpointTests : IDisposable
{
    private readonly AtoCopilotContext _db;

    private const string ExistingId  = "sys-delete-001";
    private const string MissingId   = "sys-delete-missing";

    public DeleteSystemEndpointTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"DeleteSystem_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);

        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = ExistingId,
            Name = "Orphaned QA System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Government",
            CreatedBy = "qa-sweep",
            IsActive = true,
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task SoftDelete_ExistingSystem_SetsIsActiveFalse()
    {
        var system = await _db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == ExistingId && s.IsActive);

        system.Should().NotBeNull("system must exist before delete");
        system!.IsActive = false;
        system.ModifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var reloaded = await _db.RegisteredSystems.FindAsync(ExistingId);
        reloaded.Should().NotBeNull();
        reloaded!.IsActive.Should().BeFalse("soft-delete should set IsActive=false");
    }

    [Fact]
    public async Task SoftDelete_DoesNotRemove_SystemRow()
    {
        var system = await _db.RegisteredSystems.FirstAsync(s => s.Id == ExistingId);
        system.IsActive = false;
        await _db.SaveChangesAsync();

        var count = await _db.RegisteredSystems.CountAsync(s => s.Id == ExistingId);
        count.Should().Be(1, "soft-delete must keep the DB row");
    }

    [Fact]
    public async Task HardDelete_ExistingSystem_RemovesRow()
    {
        var system = await _db.RegisteredSystems.FirstAsync(s => s.Id == ExistingId);
        _db.RegisteredSystems.Remove(system);
        await _db.SaveChangesAsync();

        var count = await _db.RegisteredSystems.CountAsync(s => s.Id == ExistingId);
        count.Should().Be(0, "hard-delete must remove the row");
    }

    [Fact]
    public async Task HardDelete_MissingSystem_FindsNull()
    {
        var system = await _db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == MissingId);
        system.Should().BeNull("non-existent system should return null — 404 path");
    }

    [Fact]
    public async Task AfterHardDelete_ActiveQuery_ExcludesDeletedSystem()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "sys-real-001",
            Name = "Real Production System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionCritical,
            HostingEnvironment = "Azure Government",
            CreatedBy = "admin",
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        var orphan = await _db.RegisteredSystems.FirstAsync(s => s.Id == ExistingId);
        _db.RegisteredSystems.Remove(orphan);
        await _db.SaveChangesAsync();

        var activeSystems = await _db.RegisteredSystems.Where(s => s.IsActive).ToListAsync();
        activeSystems.Should().HaveCount(1, "only the real system should remain");
        activeSystems[0].Id.Should().Be("sys-real-001");
    }
}

/// <summary>
/// Tests for compliance_delete_system MCP tool (#524).
/// </summary>
public class DeleteSystemToolTests : IDisposable
{
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;
    private AtoCopilotContext _db;
    private readonly DeleteSystemTool _sut;

    private const string SystemId = "sys-mcp-delete-001";

    public DeleteSystemToolTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"DeleteSystemTool_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(_dbOptions);

        var factoryMock = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_dbOptions));

        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId,
            Name = "QA Test System 2026-06-23",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Government",
            CreatedBy = "qa-sweep",
            IsActive = true,
        });
        _db.SaveChanges();

        var logger = Mock.Of<ILogger<DeleteSystemTool>>();
        _sut = new DeleteSystemTool(factoryMock.Object, logger);
    }

    public void Dispose()
    {
        try { _db?.Dispose(); } catch (ObjectDisposedException) { }
        using var cleanup = new AtoCopilotContext(_dbOptions);
        cleanup.Database.EnsureDeleted();
    }

    [Fact]
    public async Task MissingSystemId_ReturnsInvalidInput()
    {
        var result = await _sut.ExecuteCoreAsync(
            new Dictionary<string, object?> { ["system_id"] = "" });
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task NotFoundSystemId_ReturnsNotFound()
    {
        var result = await _sut.ExecuteCoreAsync(
            new Dictionary<string, object?> { ["system_id"] = "does-not-exist" });
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("error");
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task SoftDelete_ReturnsPermanentFalse()
    {
        var result = await _sut.ExecuteCoreAsync(
            new Dictionary<string, object?> { ["system_id"] = SystemId, ["permanent"] = false });
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("permanent").GetBoolean().Should().BeFalse();

        // Recreate context (tool disposed its instance via await using)
        _db = new AtoCopilotContext(_dbOptions);
        var row = await _db.RegisteredSystems.FindAsync(SystemId);
        row.Should().NotBeNull();
        row!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task HardDelete_RemovesRow_ReturnsPermanentTrue()
    {
        var result = await _sut.ExecuteCoreAsync(
            new Dictionary<string, object?> { ["system_id"] = SystemId, ["permanent"] = true });
        var json = JsonDocument.Parse(result);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("data").GetProperty("permanent").GetBoolean().Should().BeTrue();

        // Recreate context (tool disposed its instance via await using)
        _db = new AtoCopilotContext(_dbOptions);
        var count = await _db.RegisteredSystems.CountAsync(s => s.Id == SystemId);
        count.Should().Be(0, "permanent=true must remove the row");
    }
}
