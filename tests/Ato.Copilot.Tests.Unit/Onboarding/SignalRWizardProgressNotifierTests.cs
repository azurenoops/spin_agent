using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Hubs.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Verifies <see cref="SignalRWizardProgressNotifier"/> publishes to the canonical
/// per-tenant + per-job groups using the <c>WizardJobStatus</c> method name
/// (research §R2 / contracts/progress-events.md).
/// </summary>
public class SignalRWizardProgressNotifierTests
{
    [Fact]
    public async Task PublishAsync_SendsToTenantAndJobGroups()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var hub = new Mock<IHubContext<NotificationHub>>(MockBehavior.Strict);
        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        var tenantClient = new Mock<IClientProxy>(MockBehavior.Strict);
        var jobClient = new Mock<IClientProxy>(MockBehavior.Strict);

        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group($"wizard-{tenantId}")).Returns(tenantClient.Object);
        clients.Setup(c => c.Group($"wizard-{tenantId}-job-{jobId}")).Returns(jobClient.Object);

        tenantClient
            .Setup(c => c.SendCoreAsync(
                "WizardJobStatus",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        jobClient
            .Setup(c => c.SendCoreAsync(
                "WizardJobStatus",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new SignalRWizardProgressNotifier(
            hub.Object,
            NullLogger<SignalRWizardProgressNotifier>.Instance);

        var evt = new WizardJobStatusEvent(
            jobId, tenantId, WizardJobType.EmassParse,
            WizardJobState.InProgress, 25, "Parsing systems",
            ErrorCode: null, Suggestion: null,
            Timestamp: DateTimeOffset.UtcNow);

        await sut.PublishAsync(evt);

        clients.Verify(c => c.Group($"wizard-{tenantId}"), Times.Once);
        clients.Verify(c => c.Group($"wizard-{tenantId}-job-{jobId}"), Times.Once);
        tenantClient.Verify(c => c.SendCoreAsync(
            "WizardJobStatus",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
        jobClient.Verify(c => c.SendCoreAsync(
            "WizardJobStatus",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
