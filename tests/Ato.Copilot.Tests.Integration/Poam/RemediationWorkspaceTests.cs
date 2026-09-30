using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Onboarding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Ato.Copilot.State.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Poam;

public class RemediationWorkspaceTests : IDisposable
{
    private readonly Guid tenant = Guid.NewGuid();
    private readonly AtoCopilotContext db = new(new DbContextOptionsBuilder<AtoCopilotContext>()
        .UseInMemoryDatabase($"workspace-{Guid.NewGuid()}").Options);
    private RemediationWorkspaceService Service => new(db, Mock.Of<IKanbanService>());

    private async Task SeedAsync()
    {
        db.NistControls.Add(new NistControl { Id = "AC-2", Family = "AC", Title = "Account Management" });
        db.RegisteredSystems.Add(new RegisteredSystem { Id = "sys", TenantId = tenant, Name = "System" });
        db.RemediationBoards.Add(new RemediationBoard { Id = "board", TenantId = tenant, SubscriptionId = "shared" });
        db.RemediationTasks.Add(new RemediationTask { Id = "task", TenantId = tenant, BoardId = "board",
            RegisteredSystemId = "sys", Title = "Task", ControlId = "AC-2", DueDate = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ManualFinding_RetryUsesSameFinding_AndRetainsNamedManualProvenance()
    {
        // Arrange
        await SeedAsync();
        var request = new CreateRemediationFinding("request-1", "Finding", "Description", "AC-2", "High");
        // Act
        var id = await Service.CreateFindingAsync(tenant, "sys", request, "actor", default);
        var retry = await Service.CreateFindingAsync(tenant, "sys", request, "actor", default);
        var workspace = await Service.GetAsync(tenant, "sys", new(true, null), default);
        // Assert
        retry.Should().Be(id);
        workspace.Findings.Should().ContainSingle().Which.Provenance!.SourceType.Should().Be("Manual");
        workspace.Counts.Findings.Should().Be(1);
    }

    [Fact]
    public async Task ManualFinding_SameRequestWithDifferentIntent_Conflicts()
    {
        // Arrange
        await SeedAsync();
        var request = new CreateRemediationFinding("request-1", "Finding", "Description", "AC-2", "High");
        await Service.CreateFindingAsync(tenant, "sys", request, "actor", default);
        // Act
        var act = () => Service.CreateFindingAsync(tenant, "sys", request with { Title = "Changed" }, "actor", default);
        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Workspace_ExcludesUnownedAndOtherTenantTasks_AndCountsOnlyItsRows()
    {
        // Arrange
        await SeedAsync();
        db.RemediationTasks.AddRange(new RemediationTask { Id = "unowned", TenantId = tenant, BoardId = "board" },
            new RemediationTask { Id = "foreign", TenantId = Guid.NewGuid(), BoardId = "board", RegisteredSystemId = "sys" });
        await db.SaveChangesAsync();
        // Act
        var workspace = await Service.GetAsync(tenant, "sys", new(true, null), default);
        // Assert
        workspace.Tasks.Should().ContainSingle().Which.Id.Should().Be("task");
        workspace.Counts.OverdueTasks.Should().Be(1);
    }

    [Fact]
    public async Task Evidence_RejectsOtherSystem_AndStaleTaskVersion()
    {
        // Arrange
        await SeedAsync();
        db.EvidenceArtifacts.Add(new EvidenceArtifact { Id = "e", TenantId = tenant, RegisteredSystemId = "other" });
        await db.SaveChangesAsync();
        var task = await db.RemediationTasks.FindAsync("task");
        // Act
        var foreign = () => Service.LinkEvidenceAsync(tenant, "sys", "task", new(task!.RowVersion, "e"), "actor", default);
        var stale = () => Service.VerifyAsync(tenant, "sys", "task", new(Guid.NewGuid(), "Passed", "Reviewed"), "actor", default);
        // Assert
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Verification_IsIndependent_AndEvidenceChangeInvalidatesIt()
    {
        // Arrange
        await SeedAsync();
        var task = await db.RemediationTasks.FindAsync("task");
        await Service.VerifyAsync(tenant, "sys", "task", new(task!.RowVersion, "Passed", "Manual inspection"), "reviewer", default);
        db.EvidenceArtifacts.Add(new EvidenceArtifact { Id = "e", TenantId = tenant, RegisteredSystemId = "sys",
            FileName = "proof.pdf", ContentHash = "hash" });
        await db.SaveChangesAsync();
        // Act
        await Service.LinkEvidenceAsync(tenant, "sys", "task", new(task.RowVersion, "e"), "actor", default);
        // Assert
        task.VerificationStatus.Should().Be("NotVerified");
        task.Status.Should().Be(Ato.Copilot.Core.Models.Kanban.TaskStatus.Backlog);
        var workspace = await Service.GetAsync(tenant, "sys", new(true, null), default);
        workspace.Tasks.Single().Evidence.Single().ContentHash.Should().Be("hash");
        workspace.Tasks.Single().History.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(DeviationStatus.Pending, 5, false)]
    [InlineData(DeviationStatus.Approved, -1, false)]
    [InlineData(DeviationStatus.Approved, 5, true)]
    public async Task ExceptionEffect_RequiresCurrentApprovedDecision(DeviationStatus status, int days, bool expected)
    {
        // Arrange
        await SeedAsync();
        db.Deviations.Add(new Deviation { Id = "d", TenantId = tenant, RegisteredSystemId = "sys",
            Status = status, ExpirationDate = DateTime.UtcNow.AddDays(days) });
        await db.SaveChangesAsync();
        // Act
        var workspace = await Service.GetAsync(tenant, "sys", new(true, null), default);
        // Assert
        workspace.Exceptions.Single().IsEffective.Should().Be(expected);
    }

    [Fact]
    public async Task CanonicalClose_RequiresPassedVerification_OrExplicitPrivilegedSkip()
    {
        // Arrange
        await SeedAsync();
        var task = await db.RemediationTasks.FindAsync("task");
        task!.Status = Ato.Copilot.Core.Models.Kanban.TaskStatus.InReview;
        await db.SaveChangesAsync();
        var canonical = new KanbanService(db, NullLogger<KanbanService>.Instance, Mock.Of<INotificationService>(),
            Mock.Of<IAgentStateManager>(), Mock.Of<IAtoComplianceEngine>(), Mock.Of<IRemediationEngine>());
        // Act
        var implicitBypass = () => canonical.MoveTaskAsync("task", Ato.Copilot.Core.Models.Kanban.TaskStatus.Done,
            "actor", "Actor", ComplianceRoles.Administrator);
        var unprivilegedSkip = () => canonical.MoveTaskAsync("task", Ato.Copilot.Core.Models.Kanban.TaskStatus.Done,
            "actor", "Actor", ComplianceRoles.SecurityLead, "Skip", true);
        // Assert
        await implicitBypass.Should().ThrowAsync<InvalidOperationException>();
        await unprivilegedSkip.Should().ThrowAsync<UnauthorizedAccessException>();
        task.VerificationStatus = "Passed";
        await canonical.MoveTaskAsync("task", Ato.Copilot.Core.Models.Kanban.TaskStatus.Done, "actor", "Actor", ComplianceRoles.SecurityLead);
        task.Status.Should().Be(Ato.Copilot.Core.Models.Kanban.TaskStatus.Done);
    }

    [Fact]
    public async Task FindingProvenance_RetainsOriginalNamedPlanPin()
    {
        // Arrange
        await SeedAsync();
        var pin = new AssessmentPlanPin("original-plan", 7, "original-hash", "Original approved plan", "Finalized", ["AC-2"], []);
        db.Assessments.Add(new ComplianceAssessment { Id = "assessment", TenantId = tenant, RegisteredSystemId = "sys",
            ResultProvenanceJson = new AssessmentResultProvenance { Plan = pin }.Serialize(), ScanType = "combined" });
        db.Findings.Add(new ComplianceFinding { Id = "f", TenantId = tenant, AssessmentId = "assessment",
            ControlId = "AC-2", Title = "Retained observation" });
        db.SecurityAssessmentPlans.Add(new SecurityAssessmentPlan { Id = "new-plan", TenantId = tenant,
            RegisteredSystemId = "sys", Title = "New current plan", Content = "Different" });
        await db.SaveChangesAsync();
        // Act
        var workspace = await Service.GetAsync(tenant, "sys", new(true, null), default);
        // Assert
        workspace.Findings.Single().Provenance!.Plan.Should().BeEquivalentTo(pin);
        workspace.Findings.Single().Provenance!.SourceName.Should().Contain("combined assessment");
    }

    [Fact]
    public async Task Workspace_PreservesTaskFieldsAndExceptionDecision_AndListsOnlyAuthorizedWorkers()
    {
        // Arrange
        await SeedAsync();
        var task = await db.RemediationTasks.FindAsync("task");
        task!.AffectedResources = ["resource-1"];
        task.ValidationCriteria = "Retained criteria";
        task.RemediationScript = "retained script";
        task.RemediationScriptType = "PowerShell";
        var worker = new Person { TenantId = tenant, DisplayName = "Named worker", Email = "worker@example.invalid" };
        var reader = new Person { TenantId = tenant, DisplayName = "Assessor", Email = "reader@example.invalid" };
        var revoked = new Person { TenantId = tenant, DisplayName = "Revoked worker", Email = "revoked@example.invalid" };
        db.Persons.AddRange(worker, reader, revoked);
        var workerIdentity = Guid.NewGuid();
        db.OrganizationMemberships.AddRange(new() { TenantId = tenant, PersonId = worker.Id, ObjectId = workerIdentity },
            new() { TenantId = tenant, PersonId = reader.Id, ObjectId = Guid.NewGuid() },
            new() { TenantId = tenant, PersonId = revoked.Id, ObjectId = Guid.NewGuid(), RevokedAt = DateTimeOffset.UtcNow });
        db.SystemRoleAssignments.AddRange(new() { TenantId = tenant, PersonId = worker.Id, RegisteredSystemId = "sys", Role = OrganizationRole.Isso },
            new() { TenantId = tenant, PersonId = reader.Id, RegisteredSystemId = "sys", Role = OrganizationRole.Assessor },
            new() { TenantId = tenant, PersonId = revoked.Id, RegisteredSystemId = "sys", Role = OrganizationRole.Issm });
        db.Deviations.Add(new Deviation { TenantId = tenant, RegisteredSystemId = "sys", ReviewedBy = "Reviewer",
            ReviewerRole = "AO", ReviewedAt = DateTime.UtcNow, CompensatingControls = "Retained conditions" });
        await db.SaveChangesAsync();
        // Act
        var workspace = await Service.GetAsync(tenant, "sys", new(true, null), default);
        // Assert
        workspace.Tasks.Single().AffectedResources.Should().Equal("resource-1");
        workspace.Tasks.Single().ValidationCriteria.Should().Be("Retained criteria");
        workspace.Tasks.Single().RemediationScript.Should().Be("retained script");
        workspace.Tasks.Single().RemediationScriptType.Should().Be("PowerShell");
        workspace.Owners.Should().ContainSingle().Which.Should().Be(new RemediationOwner(workerIdentity.ToString(), "Named worker"));
        workspace.Exceptions.Single().ReviewedBy.Should().Be("Reviewer");
        workspace.Exceptions.Single().ReviewerRole.Should().Be("AO");
        workspace.Exceptions.Single().ReviewedAt.Should().NotBeNull();
        workspace.Exceptions.Single().CompensatingControls.Should().Be("Retained conditions");
        (await db.SystemRoleAssignments.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task ExistingTaskLink_ValidatesVersionAndScope_AndPreservesOriginalFinding()
    {
        // Arrange
        await SeedAsync();
        db.Assessments.AddRange(new ComplianceAssessment { Id = "a", TenantId = tenant, RegisteredSystemId = "sys" },
            new ComplianceAssessment { Id = "other-a", TenantId = tenant, RegisteredSystemId = "other" });
        db.Findings.AddRange(new ComplianceFinding { Id = "f1", TenantId = tenant, AssessmentId = "a", ControlId = "AC-2" },
            new ComplianceFinding { Id = "f2", TenantId = tenant, AssessmentId = "a", ControlId = "AC-2" },
            new ComplianceFinding { Id = "foreign", TenantId = tenant, AssessmentId = "other-a", ControlId = "AC-2" });
        await db.SaveChangesAsync();
        var task = await db.RemediationTasks.FindAsync("task");
        var before = task!.RowVersion;
        // Act
        var stale = () => Service.LinkFindingTaskAsync(tenant, "sys", "f1", "task", Guid.NewGuid(), "actor", default);
        var foreign = () => Service.LinkFindingTaskAsync(tenant, "sys", "foreign", "task", before, "actor", default);
        // Assert
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        task.FindingId.Should().BeNull();
        await Service.LinkFindingTaskAsync(tenant, "sys", "f1", "task", before, "actor", default);
        task.FindingId.Should().Be("f1");
        task.RowVersion.Should().NotBe(before);
        var reparent = () => Service.LinkFindingTaskAsync(tenant, "sys", "f2", "task", task.RowVersion, "actor", default);
        await reparent.Should().ThrowAsync<InvalidOperationException>();
        await Service.LinkFindingTaskAsync(tenant, "sys", "f1", "task", task.RowVersion, "actor", default);
        task.History.Should().ContainSingle().Which.EventType.Should().Be(HistoryEventType.RelationshipLinked);
        (await db.DashboardActivities.SingleAsync(a => a.EventType == "FindingTaskLinked")).RelatedEntityId.Should().Be("f1");
        task.Status.Should().Be(Ato.Copilot.Core.Models.Kanban.TaskStatus.Backlog);
        (await db.Findings.FindAsync("f1"))!.Status.Should().Be(FindingStatus.Open);
    }

    public void Dispose() => db.Dispose();
}
