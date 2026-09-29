using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services.Ticketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public class TaskTicketServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly Mock<ITicketingProvider> _provider = new();
    private readonly Mock<ISystemWorkspaceAccessService> _access = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly TaskTicketService _service;

    public TaskTicketServiceTests()
    {
        _db = new(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.EffectiveTenantId).Returns(_tenantId);
        tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());
        _provider.SetupGet(x => x.ProviderType).Returns(TicketingProvider.Jira);
        Permit(true);
        _db.RemediationBoards.Add(new() { Id = "board", TenantId = _tenantId, SubscriptionId = "shared-subscription" });
        _db.RemediationTasks.Add(new() { Id = "task", TenantId = _tenantId, BoardId = "board", RegisteredSystemId = "system", Title = "Fix it" });
        _db.TicketingIntegrations.Add(new()
        {
            Id = "integration", TenantId = _tenantId, RegisteredSystemId = "system",
            Provider = TicketingProvider.Jira, BaseUrl = "https://tickets.example", SyncEnabled = true,
            ProjectKeyOrTableName = "TEST", KeyVaultSecretUri = "reference"
        });
        _db.SaveChanges();
        _service = new(_db, [_provider.Object], tenant.Object, _access.Object);
    }

    [Fact]
    public async Task Create_RetryAndUnlink_NeverCreatesAnotherRemoteTicket()
    {
        // Arrange
        _provider.Setup(x => x.CreateTaskAsync(It.IsAny<TaskTicketCreate>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = true, ExternalRef = "TEST-1" });
        // Act
        var first = await _service.CreateAsync("system", "task");
        await _service.CreateAsync("system", "task");
        await _service.UnlinkAsync("system", "task", first.Link!.RowVersion);
        var retry = () => _service.CreateAsync("system", "task");
        // Assert
        await retry.Should().ThrowAsync<InvalidOperationException>();
        _provider.Verify(x => x.CreateTaskAsync(It.IsAny<TaskTicketCreate>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()), Times.Once);
        _db.Set<TaskTicketAudit>().Count().Should().Be(3);
    }

    [Fact]
    public async Task Create_Failure_RetainsUncertainLeaseAndBlocksRetry()
    {
        // Arrange
        _provider.Setup(x => x.CreateTaskAsync(It.IsAny<TaskTicketCreate>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = false });
        // Act
        var failed = await _service.CreateAsync("system", "task");
        var retry = () => _service.CreateAsync("system", "task");
        // Assert
        failed.Link!.State.Should().Be("Uncertain");
        await retry.Should().ThrowAsync<InvalidOperationException>();
        _provider.Verify(x => x.CreateTaskAsync(It.IsAny<TaskTicketCreate>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkAndRefresh_ClosureDoesNotChangeTask_StaleVersionCannotUnlink()
    {
        // Arrange
        _provider.Setup(x => x.PullAsync("TEST-2", It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = true, ExternalRef = "TEST-2", ExternalStatus = "Closed", ExternalAssignee = "Owner" });
        // Act
        var linked = await _service.LinkAsync("system", "task", "TEST-2", null);
        var refreshed = await _service.RefreshAsync("system", "task", linked.Link!.RowVersion);
        var stale = () => _service.UnlinkAsync("system", "task", linked.Link.RowVersion);
        // Assert
        refreshed.Link!.ExternalStatus.Should().Be("Closed");
        refreshed.Link.ExternalAssignee.Should().Be("Owner");
        _db.RemediationTasks.Single().Status.Should().Be(Ato.Copilot.Core.Models.Kanban.TaskStatus.Backlog);
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task DeniedAndWrongSystem_NeverCallsProvider()
    {
        // Arrange
        Permit(false);
        // Act
        var denied = () => _service.CreateAsync("system", "task");
        var wrongSystem = () => _service.GetAsync("other", "task");
        // Assert
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        await wrongSystem.Should().ThrowAsync<KeyNotFoundException>();
        _provider.Verify(x => x.CreateTaskAsync(It.IsAny<TaskTicketCreate>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AccessRevokedDuringRequest_DoesNotReturnOrPersistRemoteSnapshot()
    {
        // Arrange
        _provider.Setup(x => x.PullAsync("TEST-3", It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Permit(false);
                return Task.FromResult(new TicketSyncResult { Success = true, ExternalRef = "TEST-3", ExternalStatus = "Closed" });
            });
        // Act
        var action = () => _service.LinkAsync("system", "task", "TEST-3", null);
        // Assert
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        _db.Set<TaskTicketLink>().Should().BeEmpty();
    }

    [Fact]
    public async Task CrossTenantTask_IsNotAccessibleEvenWithSystemPermission()
    {
        // Arrange
        _db.RemediationTasks.Single().TenantId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        // Act
        var read = () => _service.GetAsync("system", "task");
        var create = () => _service.CreateAsync("system", "task");
        // Assert
        await read.Should().ThrowAsync<KeyNotFoundException>();
        await create.Should().ThrowAsync<KeyNotFoundException>();
        _provider.Verify(x => x.CreateTaskAsync(It.IsAny<TaskTicketCreate>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubscriptionMatchingSystemId_DoesNotEstablishTaskOwnership()
    {
        // Arrange
        _db.RemediationTasks.Single().RegisteredSystemId = null;
        _db.RemediationBoards.Single().SubscriptionId = "system";
        await _db.SaveChangesAsync();
        // Act
        var read = () => _service.GetAsync("system", "task");
        // Assert
        await read.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RefreshFailure_RetainsPreviousSuccessfulSnapshotAndTime()
    {
        // Arrange
        _provider.SetupSequence(x => x.PullAsync("TEST-2", It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = true, ExternalStatus = "Open", ExternalAssignee = "Owner" })
            .ReturnsAsync(new TicketSyncResult { Success = false, Error = "sensitive backend error" });
        var linked = await _service.LinkAsync("system", "task", "TEST-2", null);
        // Act
        var result = await _service.RefreshAsync("system", "task", linked.Link!.RowVersion);
        // Assert
        result.Link!.ExternalStatus.Should().Be("Open");
        result.Link.LastSuccessfulSyncAt.Should().Be(linked.Link.LastSuccessfulSyncAt);
        result.Link.LastError.Should().Contain("retained").And.NotContain("sensitive");
    }

    [Fact]
    public async Task UncertainCreation_CanRecoverByLinkButCannotUnlinkPendingClaim()
    {
        // Arrange
        _provider.Setup(x => x.CreateTaskAsync(It.IsAny<TaskTicketCreate>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = false });
        var pending = await _service.CreateAsync("system", "task");
        _provider.Setup(x => x.PullAsync("TEST-9", It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketSyncResult { Success = true, ExternalStatus = "Open" });
        // Act
        var unlink = () => _service.UnlinkAsync("system", "task", pending.Link!.RowVersion);
        await unlink.Should().ThrowAsync<InvalidOperationException>();
        var recovered = await _service.LinkAsync("system", "task", "TEST-9", pending.Link!.RowVersion);
        // Assert
        recovered.Link!.CorrelationKey.Should().Be(pending.Link.CorrelationKey);
        recovered.Link.State.Should().Be("Linked");
        _provider.Verify(x => x.CreateTaskAsync(It.IsAny<TaskTicketCreate>(), It.IsAny<TicketingIntegration>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void Permit(bool allowed) => _access.Setup(x => x.GetAccessAsync(
        _tenantId, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SystemWorkspaceAccessResponse("system", [], new(
            true, false, false, false, false, false, false, allowed, false)));

    public void Dispose() => _db.Dispose();
}
