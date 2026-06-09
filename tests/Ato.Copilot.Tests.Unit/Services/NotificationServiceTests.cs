using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Models.Kanban;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for NotificationService:
/// EnqueueAsync writing to channel, dispatch loop logging.
/// </summary>
public class NotificationServiceTests : IDisposable
{
    private readonly NotificationService _service;
    private readonly Mock<ILogger<NotificationService>> _loggerMock = new();

    public NotificationServiceTests()
    {
        _service = new NotificationService(_loggerMock.Object, Options.Create(new NotificationOptions()));
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task EnqueueAsync_DoesNotThrow()
    {
        var message = new NotificationMessage
        {
            EventType = NotificationEventType.TaskAssigned,
            TaskId = "t1",
            TaskNumber = "REM-001",
            BoardId = "b1",
            TargetUserId = "alice",
            Title = "Assigned",
            Details = "Assigned by CO",
        };

        var act = () => _service.EnqueueAsync(message);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnqueueAsync_MultipleMessages_DoesNotThrow()
    {
        for (int i = 0; i < 10; i++)
        {
            await _service.EnqueueAsync(new NotificationMessage
            {
                EventType = NotificationEventType.StatusChanged,
                TaskId = $"t{i}",
                TaskNumber = $"REM-{i:D3}",
                BoardId = "b1",
                TargetUserId = "user",
                Title = $"Status changed {i}",
            });
        }

        // Give dispatch loop time to process
        await Task.Delay(100);

        // No exceptions should have been thrown
    }

    [Fact]
    public void Dispose_CompletesGracefully()
    {
        var svc = new NotificationService(Mock.Of<ILogger<NotificationService>>(), Options.Create(new NotificationOptions()));

        var act = () => svc.Dispose();

        act.Should().NotThrow();
    }
}
