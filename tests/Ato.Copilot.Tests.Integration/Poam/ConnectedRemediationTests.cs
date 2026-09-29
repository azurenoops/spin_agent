using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Agents.Compliance.Services;
using TaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Tests.Integration.Poam;

public class ConnectedRemediationTests : IDisposable
{
    private readonly AtoCopilotContext db = new(new DbContextOptionsBuilder<AtoCopilotContext>()
        .UseInMemoryDatabase($"connected-{Guid.NewGuid()}").Options);

    private PoamSyncService Sync => new(db, new PoamService(db, NullLogger<PoamService>.Instance),
        NullLogger<PoamSyncService>.Instance);

    private async Task SeedAsync()
    {
        db.RegisteredSystems.AddRange(new RegisteredSystem { Id = "system-a", Name = "A" },
            new RegisteredSystem { Id = "system-b", Name = "B" });
        db.Assessments.AddRange(new ComplianceAssessment { Id = "a", RegisteredSystemId = "system-a", SubscriptionId = "shared" },
            new ComplianceAssessment { Id = "b", RegisteredSystemId = "system-b", SubscriptionId = "shared" });
        db.RemediationBoards.AddRange(new RemediationBoard { Id = "board-a", AssessmentId = "a", SubscriptionId = "shared" },
            new RemediationBoard { Id = "board-b", AssessmentId = "b", SubscriptionId = "shared" });
        db.RemediationTasks.AddRange(new RemediationTask { Id = "task-a", BoardId = "board-a" },
            new RemediationTask { Id = "task-a2", BoardId = "board-a" },
            new RemediationTask { Id = "task-b", BoardId = "board-b" });
        db.PoamItems.AddRange(new PoamItem { Id = "poam-a", RegisteredSystemId = "system-a" },
            new PoamItem { Id = "poam-a2", RegisteredSystemId = "system-a" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Links_SupportMultipleTasksAndSharedTasks_AndRetryIsIdempotent()
    {
        // Arrange
        await SeedAsync();
        // Act
        await Sync.LinkAsync("poam-a", "task-a", "actor");
        await Sync.LinkAsync("poam-a", "task-a2", "actor");
        await Sync.LinkAsync("poam-a2", "task-a", "actor");
        await Sync.LinkAsync("poam-a", "task-a", "actor");
        // Assert
        (await db.PoamHistoryEntries.CountAsync(h => h.EventType == PoamHistoryEventType.TaskLinked)).Should().Be(3);
    }

    [Fact]
    public async Task Link_RejectsOtherSystemEvenWhenSubscriptionMatches()
    {
        // Arrange
        await SeedAsync();
        // Act
        var act = () => Sync.LinkAsync("poam-a", "task-b", "actor");
        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.PoamHistoryEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Completion_DoesNotCascadeToTask()
    {
        // Arrange
        await SeedAsync();
        await Sync.LinkAsync("poam-a", "task-a", "actor");
        // Act
        await Sync.CascadeStatusChangeAsync("poam-a", PoamStatus.Completed, CascadeOrigin.FromPoam, "actor");
        // Assert
        (await db.RemediationTasks.FindAsync("task-a"))!.Status.Should().Be(TaskStatus.Backlog);
    }

    [Fact]
    public async Task Unlink_RequiresExplicitTaskWhenMultiple_AndPreservesOtherLinks()
    {
        // Arrange
        await SeedAsync();
        await Sync.LinkAsync("poam-a", "task-a", "actor");
        await Sync.LinkAsync("poam-a", "task-a2", "actor");
        // Act
        var ambiguous = () => Sync.UnlinkAsync("poam-a", "actor");
        // Assert
        await ambiguous.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AMBIGUOUS_UNLINK*");
        await Sync.UnlinkAsync("poam-a", "task-a", "actor");
        await Sync.UnlinkAsync("poam-a", "task-a", "actor");
        (await db.PoamTaskLinks.ToListAsync()).Should().ContainSingle().Which.RemediationTaskId.Should().Be("task-a2");
        (await db.PoamHistoryEntries.CountAsync(h => h.EventType == PoamHistoryEventType.TaskUnlinked)).Should().Be(1);
    }

    [Fact]
    public async Task ExceptionCreation_RejectsForeignFinding()
    {
        // Arrange
        await SeedAsync();
        db.Findings.Add(new ComplianceFinding { Id = "foreign", AssessmentId = "b", ControlId = "AC-2" });
        await db.SaveChangesAsync();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(db);
        var service = new DeviationService(factory.Object, NullLogger<DeviationService>.Instance);
        // Act
        var act = () => service.CreateDeviationAsync("system-a", new CreateDeviationRequest {
            FindingId = "foreign", ControlId = "AC-2", DeviationType = "RiskAcceptance", CatSeverity = "CatII",
            Justification = "A reason", ExpirationDate = DateTime.UtcNow.AddDays(20) }, "actor");
        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PoamExports_IncludeSharedTaskEvidenceAndExceptionReferences()
    {
        // Arrange
        await SeedAsync();
        await Sync.LinkAsync("poam-a", "task-a", "actor");
        var task = await db.RemediationTasks.FindAsync("task-a");
        task!.EvidenceReferencesJson = """[{"Id":"evidence-id","Name":"proof","ContentHash":"hash","LinkedAt":"2026-01-01T00:00:00Z","LinkedBy":"actor"}]""";
        db.Deviations.Add(new Deviation { Id = "exception-id", RegisteredSystemId = "system-a",
            PoamEntryId = "poam-a", Status = DeviationStatus.Pending, ExpirationDate = DateTime.UtcNow.AddDays(10) });
        await db.SaveChangesAsync();
        var service = new PoamService(db, NullLogger<PoamService>.Instance);
        // Act
        var csv = System.Text.Encoding.UTF8.GetString(await service.ExportCsvAsync("system-a", includeAll: true));
        var json = System.Text.Encoding.UTF8.GetString(await service.ExportOscalJsonAsync("system-a", includeAll: true));
        // Assert
        csv.Should().Contain("RemediationTaskIds").And.Contain("task-a").And.Contain("evidence-id").And.Contain("exception-id");
        json.Should().Contain("remediation-task-id").And.Contain("task-a").And.Contain("evidence-id").And.Contain("exception-id");
    }

    [Theory]
    [InlineData(DeviationStatus.Pending, 5, "system-a")]
    [InlineData(DeviationStatus.Approved, -1, "system-a")]
    [InlineData(DeviationStatus.Approved, 5, "system-b")]
    public async Task PoamRiskAcceptance_RejectsIneffectiveOrCrossSystemException(DeviationStatus status, int days, string system)
    {
        // Arrange
        await SeedAsync();
        db.Deviations.Add(new Deviation { Id = "exception", RegisteredSystemId = system,
            Status = status, ExpirationDate = DateTime.UtcNow.AddDays(days) });
        await db.SaveChangesAsync();
        var poam = await db.PoamItems.FindAsync("poam-a");
        var service = new PoamService(db, NullLogger<PoamService>.Instance);
        // Act
        var act = () => service.UpdateStatusAsync("poam-a", PoamStatus.RiskAccepted, poam!.RowVersion, "actor", deviationId: "exception");
        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        poam!.Status.Should().Be(PoamStatus.Ongoing);
    }

    [Fact]
    public async Task FormalOscalPoam_IncludesConnectedTaskReferences()
    {
        // Arrange
        await SeedAsync();
        await Sync.LinkAsync("poam-a", "task-a", "actor");
        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        await using var provider = services.BuildServiceProvider();
        var exporter = new OscalPoamExportService(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalPoamExportService>.Instance);
        // Act
        var result = await exporter.ExportAsync("system-a");
        // Assert
        result.OscalJson.Should().Contain("remediation-task-id").And.Contain("task-a");
    }

    [Fact]
    public async Task LegacyCreation_CreatesMultipleNumberedTasks_WithoutMetadataCascades()
    {
        // Arrange
        await SeedAsync();
        var poam = await db.PoamItems.FindAsync("poam-a");
        poam!.Weakness = "A weakness";
        poam.SecurityControlNumber = "AC-2";
        poam.ScheduledCompletionDate = DateTime.UtcNow.AddDays(20);
        await db.SaveChangesAsync();
        // Act
        var first = await Sync.CreateTaskFromPoamAsync("poam-a", "board-a", "actor");
        var second = await Sync.CreateTaskFromPoamAsync("poam-a", "board-a", "actor");
        await Sync.CascadeMetadataChangeAsync("poam-a", DateTime.UtcNow.AddDays(90), CatSeverity.CatIII, CascadeOrigin.FromPoam, "actor");
        // Assert
        first.TaskNumber.Should().Be("REM-001");
        second.TaskNumber.Should().Be("REM-002");
        first.RegisteredSystemId.Should().Be("system-a");
        (await db.PoamTaskLinks.CountAsync(l => l.PoamItemId == "poam-a")).Should().Be(2);
        first.DueDate.Should().Be(poam.ScheduledCompletionDate);
        poam.RemediationTaskId.Should().BeNull();
    }

    [Fact]
    public async Task LegacyScalarLinks_AreRetainedAsJunctionRowsBeforeAddingAnother()
    {
        // Arrange
        await SeedAsync();
        var poam = await db.PoamItems.FindAsync("poam-a");
        var task = await db.RemediationTasks.FindAsync("task-a");
        poam!.RemediationTaskId = task!.Id;
        task.PoamItemId = poam.Id;
        await db.SaveChangesAsync();
        // Act
        await Sync.LinkAsync("poam-a", "task-a2", "actor");
        await Sync.UnlinkAsync("poam-a2", "actor");
        // Assert
        (await db.PoamTaskLinks.CountAsync(l => l.PoamItemId == "poam-a")).Should().Be(2);
        (await db.PoamTaskLinks.SingleAsync(l => l.RemediationTaskId == "task-a")).LinkedBy.Should().Be("legacy-migration");
    }

    public void Dispose() => db.Dispose();
}
