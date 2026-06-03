using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for the remap parent functionality of <see cref="CspCapabilityService"/> (#161).
/// Covers parent ID update, history event recording, and null (root) remap.
/// </summary>
public class CspRemapTests : IAsyncDisposable
{
    private readonly DbContextOptions<AtoCopilotContext> _options;
    private readonly AtoCopilotContext _context;
    private readonly Mock<ICapabilityHistoryService> _historyMock;
    private readonly CspCapabilityService _service;

    public CspRemapTests()
    {
        _options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CspRemap_{Guid.NewGuid():N}")
            .Options;

        _context = new AtoCopilotContext(_options);

        var mockFactory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        mockFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options));

        _historyMock = new Mock<ICapabilityHistoryService>();
        _historyMock
            .Setup(h => h.RecordEventAsync(
                It.IsAny<string>(), It.IsAny<CapabilityHistoryEventType>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new CspCapabilityService(
            mockFactory.Object,
            _historyMock.Object,
            Mock.Of<ILogger<CspCapabilityService>>());
    }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    // ─── Helper: seed a capability directly ──────────────────────────────────

    private async Task<CspCapability> SeedCapabilityAsync(string? parentId = null)
    {
        var cap = new CspCapability
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Capability",
            ParentCapabilityId = parentId,
            Status = CapabilityStatus.Active,
            NeedsReview = false,
            CreatedBy = "seed",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _context.CspCapabilities.Add(cap);
        await _context.SaveChangesAsync();
        return cap;
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemapParent_UpdatesParentId()
    {
        // Arrange
        var cap = await SeedCapabilityAsync(parentId: "old-parent-id");

        // Act
        var updated = await _service.RemapParentAsync(cap.Id, "new-parent-id", "user-remap");

        // Assert
        updated.ParentCapabilityId.Should().Be("new-parent-id");
    }

    [Fact]
    public async Task RemapParent_RecordsHistoryEvent()
    {
        // Arrange
        var cap = await SeedCapabilityAsync(parentId: "old-parent-id");

        // Act
        await _service.RemapParentAsync(cap.Id, "new-parent-id", "user-remap");

        // Assert
        _historyMock.Verify(h => h.RecordEventAsync(
            cap.Id,
            CapabilityHistoryEventType.ParentChanged,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemapToNull_MakesCapabilityRoot()
    {
        // Arrange — capability currently has a parent
        var cap = await SeedCapabilityAsync(parentId: "some-parent");

        // Act — remap to null (root)
        var updated = await _service.RemapParentAsync(cap.Id, newParentId: null, "user-remap");

        // Assert
        updated.ParentCapabilityId.Should().BeNull("null parent means the capability is a root");
    }
}
