using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Workers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.ScanImport;

public sealed class ScanImportWarningStatusIntegrationTests
{
    [Theory]
    [InlineData("CKL", true)]
    [InlineData("XCCDF", true)]
    [InlineData("Nessus", true)]
    [InlineData("CKL", false)]
    [InlineData("XCCDF", false)]
    [InlineData("Nessus", false)]
    public async Task WarningCompletion_PreservesDurableStatusWarningAndResultIdentity(string type, bool captured)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var importer = new Mock<IScanImportService>();
        var tenant = new Tenant { Id = Guid.NewGuid(), DisplayName = "Warning test" };
        var system = new RegisteredSystem { TenantId = tenant.Id, Name = "Warning system" };
        var assessment = new ComplianceAssessment { TenantId = tenant.Id, RegisteredSystemId = system.Id };
        var provenance = new AssessmentResultProvenance { ExecutionToken = "test-execution",
            ExecutionLeaseExpiresAt = DateTime.UtcNow.AddMinutes(30) }.Serialize();
        const string Warning = "Two rules need manual review.";
        var record = new ScanImportRecord { TenantId = tenant.Id, RegisteredSystemId = system.Id,
            AssessmentId = assessment.Id, FileName = "scan.xml", FileHash = new string('a', 64),
            ImportStatus = ScanImportStatus.Queued, ResultProvenanceJson = provenance,
            WorkspaceOperationKey = new string('b', 64), Warnings = [Warning], TotalEntries = 3 };
        List<string> returnedWarnings = captured ? [] : [Warning];
        var parsed = new ImportResult(record.Id, captured ? ScanImportStatus.Completed : ScanImportStatus.CompletedWithWarnings,
            "benchmark", null, 3, 0, 1, 0, 2, 0, 0, 0, 0, 0, 0, 0, 1, returnedWarnings, [], null);
        importer.Setup(x => x.ImportCklAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<byte[]>(),
            It.IsAny<string>(), ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parsed);
        importer.Setup(x => x.ImportXccdfAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<byte[]>(),
            It.IsAny<string>(), ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parsed);
        importer.Setup(x => x.ImportNessusAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<byte[]>(),
            It.IsAny<string>(), ImportConflictResolution.Skip, false, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NessusImportResult(record.Id, parsed.Status, "report", 3, 0, 0, 0, 1, 0, 1,
                1, 0, 0, 0, 0, 0, 1, true, false, returnedWarnings, null));
        await using var services = new ServiceCollection()
            .AddDbContext<AtoCopilotContext>(o => o.UseSqlite(connection))
            .AddSingleton(importer.Object).BuildServiceProvider();
        await using (var seed = services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            await db.Database.EnsureCreatedAsync();
            db.AddRange(tenant, system, assessment, record);
            await db.SaveChangesAsync();
        }
        var terminal = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Mock<IClientProxy>();
        client.Setup(x => x.SendCoreAsync("ImportProgress", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, object?[] args, CancellationToken _) =>
            {
                var json = JsonSerializer.SerializeToElement(args[0]);
                if (json.GetProperty("status").GetString()!.StartsWith("Completed", StringComparison.Ordinal))
                    terminal.TrySetResult(json);
            }).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(client.Object);
        var hub = new Mock<IHubContext<ImportProgressHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var queue = new ScanImportQueue();
        var tracker = new ScanImportStatusTracker();
        tracker.Register(record.Id, system.Id);
        var path = Path.Combine(AppContext.BaseDirectory, $"scan-warning-{Guid.NewGuid():N}.upload");
        await File.WriteAllTextAsync(path, "<scan/>");
        using var worker = new ScanImportBackgroundWorker(queue, tracker, hub.Object,
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ScanImportBackgroundWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Act
            queue.TryEnqueue(new ScanImportJob { JobId = record.Id, SystemId = system.Id, TenantId = tenant.Id,
                ImportType = type, FileName = record.FileName, TemporaryFilePath = path,
                Capture = captured ? new(record.Id, assessment.Id, record.WorkspaceOperationKey, provenance) : null }).Should().BeTrue();
            var progress = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Assert
            progress.GetProperty("status").GetString().Should().Be("CompletedWithWarnings");
            progress.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()).Should().Contain(Warning);
            progress.GetProperty("resultId").GetString().Should().Be("import:" + record.Id);
            tracker.TryGet(record.Id)!.Status.ToString().Should().Be("CompletedWithWarnings");
            tracker.RequestCancel(record.Id).Should().BeFalse("warning completion is terminal");
            if (captured)
            {
                await using var check = services.CreateAsyncScope();
                var db = check.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                (await db.ScanImportRecords.SingleAsync()).ImportStatus.Should().Be(ScanImportStatus.CompletedWithWarnings);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
