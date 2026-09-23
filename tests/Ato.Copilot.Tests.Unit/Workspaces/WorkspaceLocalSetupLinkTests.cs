using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Workspaces;

public sealed class WorkspaceLocalSetupLinkTests
{
    private const string SystemId = "system";
    private const string CapabilityId = "capability";
    private const string ComponentId = "component";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IDbContextFactory<AtoCopilotContext> _factory = new TestFactory(
        new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"workspace-local-link-{Guid.NewGuid():N}").Options);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepare_ExistingLocal_ReportsPendingSystemLinkWithoutSetupWrites(bool selectComponent)
    {
        // Arrange
        await SeedAsync();
        var sut = new WorkspaceOperationsService(_factory);
        var request = new PrepareCapabilitySetupRequest(
            "prepare", "local", CapabilityId, SystemId, selectComponent ? [ComponentId] : [], false);

        // Act
        var prepared = await sut.PrepareSetupAsync(_tenantId, request, [SystemId], default);
        var replay = await sut.PrepareSetupAsync(_tenantId, request, [SystemId], default);
        var refreshed = await sut.GetCapabilitySetupAsync(_tenantId, prepared.Operation.OperationId, default);

        // Assert
        prepared.Existing.Should().BeFalse();
        replay.Existing.Should().BeTrue();
        replay.Operation.Should().BeEquivalentTo(prepared.Operation);
        refreshed.Should().BeEquivalentTo(prepared.Operation);
        prepared.Operation.Outcomes.Should().HaveCount(selectComponent ? 3 : 2)
            .And.OnlyContain(x => x.State == "Pending");
        prepared.Operation.Outcomes.Should().ContainSingle(x =>
            x.WriteKind == "system-link" && x.WriteId == SystemId);
        await using var db = await _factory.CreateDbContextAsync();
        (await db.CapabilitySetupOperations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.SecurityCapabilities.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.SystemComponents.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.SystemCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.CapabilitySubscriptions.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Complete_ExistingLocal_PersistsSystemLinkAndReplaysWithoutDuplicates(
        bool selectComponent, bool prepare)
    {
        // Arrange
        await SeedAsync();
        var sut = new WorkspaceOperationsService(_factory);
        var request = Request(selectComponent);
        if (prepare)
        {
            var prepared = await sut.PrepareSetupAsync(_tenantId,
                new(request.IdempotencyKey, request.Source, request.RecordId, request.SystemId,
                    request.ComponentIds, request.Subscribe), [SystemId], default);
            request = request with { PreparedOperationId = prepared.Operation.OperationId };
        }

        // Act
        var completed = await sut.CompleteSetupAsync(_tenantId, request, "owner", [SystemId], default);
        var replay = await sut.CompleteSetupAsync(_tenantId, request, "other-actor", [SystemId], default);

        // Assert
        replay.Should().BeEquivalentTo(completed);
        await AssertCompletedAsync(completed, selectComponent ? 1 : 0);
        await using var db = await _factory.CreateDbContextAsync();
        (await db.SystemCapabilityLinks.IgnoreQueryFilters().SingleAsync()).LinkedBy.Should().Be("owner");
        (await db.CapabilitySetupOperations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.SecurityCapabilities.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Pending", false)]
    [InlineData("Completed", false)]
    [InlineData(null, true)]
    [InlineData("Pending", true)]
    [InlineData("Completed", true)]
    public async Task Complete_LegacyCompletedLocal_VerifiesLinkAndRepairsOutcome(
        string? systemLinkState, bool existingLink)
    {
        // Arrange
        await SeedAsync();
        var operationId = await SeedLegacyCompletedAsync(systemLinkState, existingLink);
        var sut = new WorkspaceOperationsService(_factory);
        var request = Request(true) with { PreparedOperationId = operationId };

        // Act
        var repaired = await sut.CompleteSetupAsync(_tenantId, request, "owner", [SystemId], default);
        var replay = await sut.CompleteSetupAsync(_tenantId, request, "owner", [SystemId], default);

        // Assert
        repaired.OperationId.Should().Be(operationId);
        replay.Should().BeEquivalentTo(repaired);
        await AssertCompletedAsync(repaired, 1);
    }

    [Fact]
    public async Task Complete_LegacyWinnerWithoutLink_RepairsBeforeReturningFromWait()
    {
        // Arrange
        await SeedAsync();
        await SeedLegacyCompletedAsync(null, false);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var row = await db.CapabilitySetupOperations.IgnoreQueryFilters().SingleAsync();
            row.ExecutionClaimId = Guid.NewGuid();
            row.ClaimedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(_factory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var completion = sut.CompleteSetupAsync(_tenantId, Request(true), "owner", [SystemId], timeout.Token);
        await using (var winner = await _factory.CreateDbContextAsync())
        {
            var row = await winner.CapabilitySetupOperations.IgnoreQueryFilters().SingleAsync();
            row.ExecutionClaimId = null;
            row.Revision++;
            await winner.SaveChangesAsync();
        }
        var result = await completion;

        // Assert
        await AssertCompletedAsync(result, 1);
    }

    [Fact]
    public async Task Complete_InlineCompletedWithDeletedLink_RepairsWithoutRecreatingCapability()
    {
        // Arrange
        await SeedAsync();
        var sut = new WorkspaceOperationsService(_factory);
        var request = Request(false) with
        {
            RecordId = "inline",
            InlineLocalCapability = new("Inline", "Local", "AC", "Description", "Implemented", "owner")
        };
        var first = await sut.CompleteSetupAsync(_tenantId, request, "owner", [SystemId], default);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.SystemCapabilityLinks.Remove(await db.SystemCapabilityLinks.IgnoreQueryFilters().SingleAsync());
            await db.SaveChangesAsync();
        }

        // Act
        var repaired = await sut.CompleteSetupAsync(_tenantId, request, "owner", [SystemId], default);

        // Assert
        repaired.OperationId.Should().Be(first.OperationId);
        repaired.Outcomes.Should().ContainSingle(x => x.WriteKind == "system-link" && x.State == "Completed");
        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.SystemCapabilityLinks.IgnoreQueryFilters().SingleAsync()).SecurityCapabilityId
            .Should().Be("inline");
        (await verify.SecurityCapabilities.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_Local_RequiresCurrentSystemManagementPermission(bool legacyCompleted)
    {
        // Arrange
        await SeedAsync();
        if (legacyCompleted)
            await SeedLegacyCompletedAsync(null, false);
        var sut = new WorkspaceOperationsService(_factory);

        // Act
        Func<Task> complete = () => sut.CompleteSetupAsync(
            _tenantId, Request(legacyCompleted), "owner", ["other-system"], default);

        // Assert
        await complete.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*System setup permission*");
        await AssertNoSystemLinkAsync();
    }

    [Theory]
    [InlineData(false, "capability")]
    [InlineData(false, "system")]
    [InlineData(false, "inactive-system")]
    [InlineData(true, "capability")]
    [InlineData(true, "system")]
    [InlineData(true, "inactive-system")]
    public async Task Complete_Local_RejectsForeignOrInactiveReferencesBeforeLinking(
        bool legacyCompleted, string changedReference)
    {
        // Arrange
        await SeedAsync();
        if (legacyCompleted)
            await SeedLegacyCompletedAsync(null, false);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            if (changedReference == "capability")
                (await db.SecurityCapabilities.IgnoreQueryFilters().SingleAsync()).TenantId = Guid.NewGuid();
            else if (changedReference == "system")
                (await db.RegisteredSystems.IgnoreQueryFilters().SingleAsync()).TenantId = Guid.NewGuid();
            else
                (await db.RegisteredSystems.IgnoreQueryFilters().SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(_factory);

        // Act
        Func<Task> complete = () => sut.CompleteSetupAsync(
            _tenantId, Request(legacyCompleted), "owner", [SystemId], default);

        // Assert
        await complete.Should().ThrowAsync<KeyNotFoundException>();
        await AssertNoSystemLinkAsync();
        await using var verify = await _factory.CreateDbContextAsync();
        var operation = await verify.CapabilitySetupOperations.IgnoreQueryFilters().SingleAsync();
        operation.LastError.Should().NotBeNullOrWhiteSpace();
        JsonSerializer.Deserialize<SetupWriteOutcome[]>(operation.OutcomesJson)
            .Should().ContainSingle(x => x.WriteKind == "system-link" && x.State != "Completed");
    }

    [Fact]
    public async Task Complete_LegacyOperation_CannotBeReplayedInAnotherTenantOrSystem()
    {
        // Arrange
        await SeedAsync();
        var operationId = await SeedLegacyCompletedAsync(null, false);
        var foreignTenant = Guid.NewGuid();
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.Tenants.Add(new Tenant { Id = foreignTenant, DisplayName = "Other" });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(_factory);
        var request = Request(true) with { PreparedOperationId = operationId };

        // Act
        Func<Task> wrongTenant = () => sut.CompleteSetupAsync(
            foreignTenant, request, "owner", [SystemId], default);
        Func<Task> wrongSystem = () => sut.CompleteSetupAsync(
            _tenantId, request with { SystemId = "other-system" }, "owner", ["other-system"], default);

        // Assert
        await wrongTenant.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Prepared setup operation*");
        await wrongSystem.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different setup*");
        await AssertNoSystemLinkAsync();
    }

    [Fact]
    public async Task Complete_Local_RejectsComponentFromAnotherSystemWithoutCreatingSystemLink()
    {
        // Arrange
        await SeedAsync();
        await using (var db = await _factory.CreateDbContextAsync())
        {
            (await db.SystemComponents.IgnoreQueryFilters().SingleAsync()).RegisteredSystemId = "other-system";
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(_factory);

        // Act
        Func<Task> complete = () => sut.CompleteSetupAsync(
            _tenantId, Request(true), "owner", [SystemId], default);

        // Assert
        await complete.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*target system*");
        await AssertNoSystemLinkAsync();
        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProviderSetup_RemainsSubscriptionOnlyAndRejectsLocalComponents()
    {
        // Arrange
        await SeedAsync();
        var providerId = Guid.NewGuid();
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var component = new CspInheritedComponent
            {
                Id = Guid.NewGuid(), CspProfileId = Guid.NewGuid(), Name = "Published",
                Description = "Provider component", Status = CspInheritedComponentStatus.Published
            };
            db.CspInheritedComponents.Add(component);
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = providerId, CspInheritedComponentId = component.Id, Name = "Provider capability",
                Description = "Published capability", Status = CspInheritedCapabilityStatus.Mapped
            });
            await db.SaveChangesAsync();
        }
        var sut = new WorkspaceOperationsService(_factory);
        var request = new PrepareCapabilitySetupRequest(
            "provider", "provider", providerId.ToString("D"), SystemId, [], true);

        // Act
        Func<Task> invalidPrepare = () => sut.PrepareSetupAsync(
            _tenantId, request with { ComponentIds = [ComponentId] }, [SystemId], default);
        Func<Task> invalidComplete = () => sut.CompleteSetupAsync(_tenantId,
            new(request.IdempotencyKey, request.Source, request.RecordId, SystemId, [ComponentId], true),
            "owner", [SystemId], default);
        var prepared = await sut.PrepareSetupAsync(_tenantId, request, [SystemId], default);
        var completeRequest = new CompleteCapabilitySetupRequest(
            request.IdempotencyKey, request.Source, request.RecordId, SystemId, [], true,
            PreparedOperationId: prepared.Operation.OperationId);
        var completed = await sut.CompleteSetupAsync(
            _tenantId, completeRequest, "owner", [SystemId], default);
        var replay = await sut.CompleteSetupAsync(
            _tenantId, completeRequest, "owner", [SystemId], default);

        // Assert
        await invalidPrepare.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot link organization-owned components*");
        await invalidComplete.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot link organization-owned components*");
        prepared.Operation.Outcomes.Should().HaveCount(2).And.OnlyContain(x => x.State == "Pending");
        prepared.Operation.Outcomes.Select(x => x.WriteKind).Should().BeEquivalentTo("record", "subscription");
        replay.Should().BeEquivalentTo(completed);
        completed.Outcomes.Should().HaveCount(2).And.OnlyContain(x => x.State == "Completed");
        await AssertNoSystemLinkAsync();
        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await verify.CapabilitySubscriptions.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    private static CompleteCapabilitySetupRequest Request(bool selectComponent) =>
        new("complete", "local", CapabilityId, SystemId, selectComponent ? [ComponentId] : [], false);

    private async Task SeedAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Tenants.Add(new Tenant { Id = _tenantId, DisplayName = "Organization" });
        db.RegisteredSystems.Add(new RegisteredSystem
        {
            TenantId = _tenantId, Id = SystemId, Name = "System", CreatedBy = "owner"
        });
        db.SecurityCapabilities.Add(new SecurityCapability
        {
            TenantId = _tenantId, Id = CapabilityId, Name = "Existing local", Provider = "Local",
            Category = "AC", Description = "Description", Owner = "owner", CreatedBy = "owner"
        });
        db.SystemComponents.Add(new SystemComponent
        {
            TenantId = _tenantId, Id = ComponentId, RegisteredSystemId = SystemId,
            Name = "Component", ComponentType = ComponentType.Thing, CreatedBy = "owner"
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedLegacyCompletedAsync(string? systemLinkState, bool existingLink)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var outcomes = new List<SetupWriteOutcome>
        {
            new("record", CapabilityId, "Completed", null, DateTimeOffset.UtcNow),
            new("component-link", ComponentId, "Completed", null, DateTimeOffset.UtcNow)
        };
        if (systemLinkState is not null)
            outcomes.Add(new("system-link", SystemId, systemLinkState, null, DateTimeOffset.UtcNow));
        var row = new CapabilitySetupOperation
        {
            TenantId = _tenantId, IdempotencyKey = "complete", SourceKind = "local",
            SourceRecordId = CapabilityId, RegisteredSystemId = SystemId,
            ComponentIdsJson = JsonSerializer.Serialize(new[] { ComponentId }),
            RecordState = "Completed", ComponentLinksState = "Completed",
            OutcomesJson = JsonSerializer.Serialize(outcomes)
        };
        db.CapabilitySetupOperations.Add(row);
        db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
        {
            TenantId = _tenantId, SystemComponentId = ComponentId, SecurityCapabilityId = CapabilityId
        });
        if (existingLink)
            db.SystemCapabilityLinks.Add(new SystemCapabilityLink
            {
                TenantId = _tenantId, RegisteredSystemId = SystemId, SecurityCapabilityId = CapabilityId,
                LinkedBy = "original-actor"
            });
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task AssertCompletedAsync(CapabilitySetupResult result, int componentCount)
    {
        result.RecordState.Should().Be("Completed");
        result.ComponentLinksState.Should().Be("Completed");
        result.SubscriptionState.Should().Be("NotRequested");
        result.LastError.Should().BeNull();
        result.Outcomes.Should().HaveCount(2 + componentCount).And.OnlyContain(x => x.State == "Completed");
        result.Outcomes.Should().ContainSingle(x => x.WriteKind == "system-link" && x.WriteId == SystemId);
        await using var db = await _factory.CreateDbContextAsync();
        var link = await db.SystemCapabilityLinks.IgnoreQueryFilters().SingleAsync();
        link.TenantId.Should().Be(_tenantId);
        link.RegisteredSystemId.Should().Be(SystemId);
        link.SecurityCapabilityId.Should().Be(CapabilityId);
        (await db.ComponentCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(componentCount);
        (await db.CapabilitySubscriptions.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        var persisted = await db.CapabilitySetupOperations.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == result.OperationId);
        JsonSerializer.Deserialize<SetupWriteOutcome[]>(persisted.OutcomesJson)
            .Should().BeEquivalentTo(result.Outcomes);
        persisted.ExecutionClaimId.Should().BeNull();
    }

    private async Task AssertNoSystemLinkAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        (await db.SystemCapabilityLinks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private sealed class TestFactory(DbContextOptions<AtoCopilotContext> options)
        : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }
}
