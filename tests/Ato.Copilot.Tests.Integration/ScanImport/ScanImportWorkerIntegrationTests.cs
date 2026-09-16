using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Workers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.ScanImport;

public sealed class ScanImportWorkerIntegrationTests
{
    [Fact]
    public async Task Worker_DispatchesCklXccdfAndNessusJobsToMatchingImporters()
    {
        // Arrange
        var importService = new Mock<IScanImportService>(MockBehavior.Strict);
        importService
            .Setup(service => service.ImportCklAsync(
                It.IsAny<string>(), null, It.IsAny<byte[]>(), It.IsAny<string>(),
                ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateImportResult(2));
        importService
            .Setup(service => service.ImportXccdfAsync(
                It.IsAny<string>(), null, It.IsAny<byte[]>(), It.IsAny<string>(),
                ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateImportResult(3));
        importService
            .Setup(service => service.ImportNessusAsync(
                It.IsAny<string>(), null, It.IsAny<byte[]>(), It.IsAny<string>(),
                ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateNessusResult(4));

        var queue = new ScanImportQueue();
        var tracker = new ScanImportStatusTracker();
        var worker = CreateWorker(queue, tracker, importService.Object);
        await worker.StartAsync(CancellationToken.None);
        var jobs = new List<ScanImportJob>();

        try
        {
            foreach (var (jobId, importType) in new[]
                     {
                         ("ckl-job", "CKL"),
                         ("xccdf-job", "XCCDF"),
                         ("nessus-job", "Nessus"),
                     })
            {
                tracker.Register(jobId);
                var job = CreateJob(jobId, importType);
                jobs.Add(job);
                queue.TryEnqueue(job).Should().BeTrue();
            }

            // Act
            await WaitForStatusAsync(tracker, "ckl-job", ImportJobStatus.Completed);
            await WaitForStatusAsync(tracker, "xccdf-job", ImportJobStatus.Completed);
            await WaitForStatusAsync(tracker, "nessus-job", ImportJobStatus.Completed);

            // Assert
            importService.Verify(service => service.ImportCklAsync(
                It.IsAny<string>(), null, It.IsAny<byte[]>(), It.IsAny<string>(),
                ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            importService.Verify(service => service.ImportXccdfAsync(
                It.IsAny<string>(), null, It.IsAny<byte[]>(), It.IsAny<string>(),
                ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            importService.Verify(service => service.ImportNessusAsync(
                It.IsAny<string>(), null, It.IsAny<byte[]>(), It.IsAny<string>(),
                ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            jobs.Should().OnlyContain(job => !File.Exists(job.TemporaryFilePath));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task RequestCancel_WhenImporterReturnsAfterCancellation_RetainsCancelledStatus()
    {
        // Arrange
        var importStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowImportToReturn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var importService = new Mock<IScanImportService>(MockBehavior.Strict);
        importService
            .Setup(service => service.ImportXccdfAsync(
                It.IsAny<string>(), null, It.IsAny<byte[]>(), It.IsAny<string>(),
                ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string? _, byte[] _, string _, ImportConflictResolution _, bool _, string _, CancellationToken _) =>
            {
                importStarted.TrySetResult();
                await allowImportToReturn.Task;
                return CreateImportResult(1);
            });

        var queue = new ScanImportQueue();
        var tracker = new ScanImportStatusTracker();
        const string JobId = "cancel-race-job";
        tracker.Register(JobId);
        var worker = CreateWorker(queue, tracker, importService.Object);
        await worker.StartAsync(CancellationToken.None);

        try
        {
            var job = CreateJob(JobId, "XCCDF");
            queue.TryEnqueue(job).Should().BeTrue();
            await importStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Act
            tracker.RequestCancel(JobId).Should().BeTrue();
            allowImportToReturn.TrySetResult();

            // Assert
            await WaitForStatusAsync(tracker, JobId, ImportJobStatus.Cancelled);
            await WaitForFileDeletionAsync(job.TemporaryFilePath);
            tracker.TryGet(JobId)!.Status.Should().Be(ImportJobStatus.Cancelled);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task RequestCancel_BeforeWorkerStarts_DeletesTemporaryFileWithoutImporting()
    {
        // Arrange
        var importService = new Mock<IScanImportService>(MockBehavior.Strict);
        var queue = new ScanImportQueue();
        var tracker = new ScanImportStatusTracker();
        const string JobId = "queued-cancel-job";
        tracker.Register(JobId);
        var job = CreateJob(JobId, "XCCDF");
        queue.TryEnqueue(job).Should().BeTrue();
        tracker.RequestCancel(JobId).Should().BeTrue();
        var worker = CreateWorker(queue, tracker, importService.Object);

        // Act
        await worker.StartAsync(CancellationToken.None);

        try
        {
            // Assert
            await WaitForFileDeletionAsync(job.TemporaryFilePath);
            tracker.TryGet(JobId)!.Status.Should().Be(ImportJobStatus.Cancelled);
            importService.VerifyNoOtherCalls();
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task RequestCancel_CancelsActiveImporterAndCannotTransitionToCompleted()
    {
        // Arrange
        var importStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var importCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var importService = new Mock<IScanImportService>(MockBehavior.Strict);
        importService
            .Setup(service => service.ImportXccdfAsync(
                It.IsAny<string>(), null, It.IsAny<byte[]>(), It.IsAny<string>(),
                ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string? _, byte[] _, string _, ImportConflictResolution _, bool _, string _, CancellationToken cancellationToken) =>
            {
                importStarted.TrySetResult();
                using var registration = cancellationToken.Register(() => importCancelled.TrySetResult());
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateImportResult(1);
            });

        var queue = new ScanImportQueue();
        var tracker = new ScanImportStatusTracker();
        const string JobId = "cancel-job";
        tracker.Register(JobId);
        var worker = CreateWorker(queue, tracker, importService.Object);
        await worker.StartAsync(CancellationToken.None);

        try
        {
            queue.TryEnqueue(CreateJob(JobId, "XCCDF")).Should().BeTrue();
            await importStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Act
            tracker.RequestCancel(JobId).Should().BeTrue();

            // Assert
            await importCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitForStatusAsync(tracker, JobId, ImportJobStatus.Cancelled);
            tracker.TryGet(JobId)!.Status.Should().Be(ImportJobStatus.Cancelled);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    private static ScanImportBackgroundWorker CreateWorker(
        ScanImportQueue queue,
        ScanImportStatusTracker tracker,
        IScanImportService importService)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => importService)
            .BuildServiceProvider();

        var client = new Mock<IClientProxy>();
        client
            .Setup(proxy => proxy.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(hubClients => hubClients.Group(It.IsAny<string>())).Returns(client.Object);
        var hub = new Mock<IHubContext<ImportProgressHub>>();
        hub.SetupGet(context => context.Clients).Returns(clients.Object);

        return new ScanImportBackgroundWorker(
            queue,
            tracker,
            hub.Object,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScanImportBackgroundWorker>.Instance);
    }

    private static ScanImportJob CreateJob(string jobId, string importType) => new()
    {
        JobId = jobId,
        SystemId = "system-1",
        FileName = $"scan.{importType.ToLowerInvariant()}",
        TemporaryFilePath = CreateTemporaryScanFile(),
        ImportType = importType,
        ImportedBy = "integration-test",
    };

    private static string CreateTemporaryScanFile()
    {
        var temporaryFilePath = Path.Combine(
            Path.GetTempPath(),
            $"ato-copilot-scan-test-{Guid.NewGuid():N}.upload");
        File.WriteAllBytes(temporaryFilePath, "<scan />"u8.ToArray());
        return temporaryFilePath;
    }

    private static ImportResult CreateImportResult(int totalEntries) => new(
        "record-1", ScanImportStatus.Completed, "benchmark-1", null,
        totalEntries, 0, totalEntries, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        [], [], null);

    private static NessusImportResult CreateNessusResult(int totalEntries) => new(
        "record-1", ScanImportStatus.Completed, "report-1", totalEntries,
        0, 0, 0, 0, totalEntries, 1, totalEntries, 0, 0, 0, 0, 0, 0,
        true, false, [], null);

    private static async Task WaitForStatusAsync(
        ScanImportStatusTracker tracker,
        string jobId,
        ImportJobStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (tracker.TryGet(jobId)?.Status != expected)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForFileDeletionAsync(string filePath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (File.Exists(filePath))
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
