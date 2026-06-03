using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="CapabilityHistoryService"/> using EF InMemory provider.
/// Tests were written FIRST (TDD red phase) before the service was implemented (#159).
/// </summary>
public class CapabilityHistoryServiceTests : IAsyncDisposable
{
    private readonly AtoCopilotContext _context;
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly CapabilityHistoryService _service;

    public CapabilityHistoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CapabilityHistory_{Guid.NewGuid():N}")
            .Options;

        _context = new AtoCopilotContext(options);

        var mockFactory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        mockFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(options));

        _factory = mockFactory.Object;
        _service = new CapabilityHistoryService(_factory, Mock.Of<ILogger<CapabilityHistoryService>>());
    }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    // ─── RecordEvent ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordEvent_CreatesHistoryEvent()
    {
        // Act
        await _service.RecordEventAsync(
            capabilityId: "cap-abc",
            eventType: CapabilityHistoryEventType.Created,
            newValue: "{\"name\":\"Test Cap\"}",
            actorId: "user-1",
            actorName: "Alice");

        // Assert
        var events = await _context.CapabilityHistoryEvents.ToListAsync();
        events.Should().HaveCount(1);
        events[0].CapabilityId.Should().Be("cap-abc");
        events[0].EventType.Should().Be(CapabilityHistoryEventType.Created);
        events[0].NewValue.Should().Be("{\"name\":\"Test Cap\"}");
        events[0].ActorId.Should().Be("user-1");
        events[0].ActorName.Should().Be("Alice");
    }

    [Fact]
    public async Task GetHistory_ReturnsEventsForCapability()
    {
        // Arrange — 3 events for cap-1, 1 for cap-2
        await _service.RecordEventAsync("cap-1", CapabilityHistoryEventType.Created);
        await _service.RecordEventAsync("cap-1", CapabilityHistoryEventType.Updated);
        await _service.RecordEventAsync("cap-1", CapabilityHistoryEventType.StatusChanged);
        await _service.RecordEventAsync("cap-2", CapabilityHistoryEventType.Created);

        // Act
        var history = await _service.GetHistoryAsync("cap-1");

        // Assert
        history.Should().HaveCount(3);
        history.Should().OnlyContain(e => e.CapabilityId == "cap-1");
    }

    [Fact]
    public async Task GetHistory_OrderedByOccurredAtDescending()
    {
        // Arrange — record events with slight delay to ensure ordering
        await _service.RecordEventAsync("cap-order", CapabilityHistoryEventType.Created);
        await Task.Delay(5);
        await _service.RecordEventAsync("cap-order", CapabilityHistoryEventType.Updated);
        await Task.Delay(5);
        await _service.RecordEventAsync("cap-order", CapabilityHistoryEventType.StatusChanged);

        // Act
        var history = await _service.GetHistoryAsync("cap-order");

        // Assert — most recent first
        history.Should().HaveCount(3);
        history[0].EventType.Should().Be(CapabilityHistoryEventType.StatusChanged);
        history[1].EventType.Should().Be(CapabilityHistoryEventType.Updated);
        history[2].EventType.Should().Be(CapabilityHistoryEventType.Created);
    }

    [Fact]
    public async Task GetRecentEvents_RespectsMaxCount()
    {
        // Arrange — record 10 events
        for (var i = 0; i < 10; i++)
        {
            await _service.RecordEventAsync($"cap-{i}", CapabilityHistoryEventType.Created);
        }

        // Act
        var recent = await _service.GetRecentEventsAsync(maxCount: 5);

        // Assert
        recent.Should().HaveCount(5);
    }

    [Fact]
    public async Task RecordEvent_WithNullOptionalFields_Succeeds()
    {
        // Act — should not throw even with null optionals
        var act = async () => await _service.RecordEventAsync(
            capabilityId: "cap-null-test",
            eventType: CapabilityHistoryEventType.Updated,
            previousValue: null,
            newValue: null,
            notes: null);

        await act.Should().NotThrowAsync();

        var events = await _context.CapabilityHistoryEvents
            .Where(e => e.CapabilityId == "cap-null-test")
            .ToListAsync();
        events.Should().HaveCount(1);
        events[0].PreviousValue.Should().BeNull();
        events[0].NewValue.Should().BeNull();
        events[0].Notes.Should().BeNull();
    }
}
