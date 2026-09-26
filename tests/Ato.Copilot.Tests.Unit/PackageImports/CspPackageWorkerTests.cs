using Ato.Copilot.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageWorkerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PollFailure_IsLogged_WhileRequestedCancellationStopsCleanly(bool cancelled)
    {
        // Arrange
        using var stopping = new CancellationTokenSource();
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var logger = new Mock<ILogger<CspPackageWorker>>();
        Exception failure = cancelled
            ? new OperationCanceledException("Synthetic cancellation")
            : new InvalidOperationException("Synthetic unavailable scope");
        scopes.Setup(x => x.CreateScope()).Callback(() =>
        {
            if (cancelled) stopping.Cancel();
        }).Throws(failure);
        using var worker = new CspPackageWorker(scopes.Object, logger.Object);
        // Act
        await worker.StartAsync(stopping.Token);
        await worker.StopAsync(CancellationToken.None);
        // Assert
        scopes.Verify(x => x.CreateScope(), Times.Once);
        logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("CspPackage.WorkerPollFailed")),
            failure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            cancelled ? Times.Never() : Times.Once());
    }
}
