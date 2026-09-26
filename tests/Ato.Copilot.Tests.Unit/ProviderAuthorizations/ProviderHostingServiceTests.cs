using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderHostingServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TenantContextAccessor _accessor = new();
    private DbContextOptions<AtoCopilotContext> _options = null!;
    private readonly Guid _provider = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _offering = Guid.NewGuid();
    private readonly Guid _subscription = Guid.NewGuid();
    private readonly string _system = Guid.NewGuid().ToString();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        await using var db = new AtoCopilotContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.CspProfiles.Add(new() { Id = _provider, OnboardingState = OnboardingState.Active });
        db.Tenants.Add(new() { Id = _tenant, DisplayName = "Customer", OnboardingState = OnboardingState.Active });
        db.RegisteredSystems.Add(new() { Id = _system, TenantId = _tenant, Name = "Mission" });
        db.Add(new ProviderOffering { Id = _offering, OfferingId = _offering, ProviderId = _provider,
            Name = "Offering", EnvironmentsJson = "[\"AzureCloud\"]" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();
    private ProviderAzureScope Scope(string suffix = "") => new("AzureCloud", _tenant, _subscription,
        $"/subscriptions/{_subscription}{suffix}");
    private ProviderHostingService Service(TenantContext tenant)
    {
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(_options, _accessor));
        return new(new(factory.Object, tenant, NullLogger<ProviderAuthorizationStore>.Instance));
    }

    [Fact]
    public async Task Scope_ExactHistoricalReadAndReplay_PreserveImmutableMaterial()
    {
        // Arrange
        var tenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var request = new CreateProviderHostingScopeRequest(1, null, "First", [Scope()], [], []);
        var first = await service.CreateScopeAsync(_offering, request, "first", "provider", default);
        var second = await service.CreateScopeAsync(_offering,
            new(first.OfferingRevision, first.Snapshot.RevisionId, "Second", [Scope("/resourceGroups/new")], [], []),
            "second", "provider", default);
        // Act
        var replay = await service.CreateScopeAsync(_offering, request, "first", "provider", default);
        var exact = await service.ScopeAsync(_offering, first.Snapshot.RevisionId, default);
        var page = await service.ScopesAsync(_offering, 2, 1, default);
        // Assert
        replay.Snapshot.Should().Be(first.Snapshot);
        exact.Name.Should().Be("First");
        exact.Snapshot.Should().Be(first.Snapshot);
        exact.OfferingRevision.Should().Be(second.OfferingRevision);
        page.Total.Should().Be(2);
        page.Items.Single().Snapshot.Should().Be(first.Snapshot);
        await FluentActions.Awaiting(() => service.CreateScopeAsync(_offering, request with { Name = "Changed" },
            "first", "provider", default)).Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => service.ScopeAsync(Guid.NewGuid(), first.Snapshot.RevisionId, default))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task TechnicalScope_OutsideAuthorizationBoundary_AssignsWithoutCoverage()
    {
        // Arrange
        var tenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var request = new CreateProviderHostingScopeRequest(1, null, "Hosting", [Scope()], [], []);

        // Act
        var hosting = await service.CreateScopeAsync(_offering, request, "scope", "provider", default);
        var allocation = await service.AssignAsync(_offering,
            new(_tenant, _system, hosting.Snapshot.RevisionId, [Scope("/resourceGroups/mission")], []),
            "allocation", "provider", default);

        // Assert
        allocation.RelationshipState.Should().Be("Undetermined");
        allocation.HostingScope.Should().Be(hosting.Snapshot);
        await using var db = new AtoCopilotContext(_options);
        (await db.Set<ProviderAuthorizationRevision>().CountAsync()).Should().Be(0);
        (await db.Set<MissionProviderRelationshipReview>().CountAsync()).Should().Be(0);
        var stored = await db.Set<ProviderHostingScopeRevision>().SingleAsync();
        ProviderAuthorizationStore.Read<CreateProviderHostingScopeRequest>(stored.SnapshotJson).PermittedScopes.Should().Equal(Scope());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ProviderScope_RejectsCustomerOrImpersonation(bool admin, bool impersonation)
    {
        // Arrange
        var tenant = new TenantContext(_tenant) { IsCspAdmin = admin, ImpersonatedTenantId = impersonation ? _tenant : null };
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);

        // Act
        var act = () => service.CreateScopeAsync(_offering, new(1, null, "Hosting", [Scope()], [], []), "key", "actor", default);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task Lists_RejectInvalidPaging(int page, int size)
    {
        // Arrange
        var tenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        // Act
        var scopes = () => service.ScopesAsync(_offering, page, size, default);
        var assignments = () => service.AssignmentsAsync(_offering, page, size, default);
        // Assert
        await scopes.Should().ThrowAsync<ArgumentException>();
        await assignments.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Assignments_ListAndExactReadNames_RejectForeignTenantAndReplayChangedIntent()
    {
        // Arrange
        var tenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var hosting = await service.CreateScopeAsync(_offering, new(1, null, "Hosting", [Scope()], [], []), "scope", "actor", default);
        var request = new CreateProviderHostingAssignmentRequest(_tenant, _system, hosting.Snapshot.RevisionId, [Scope()], []);
        var first = await service.AssignAsync(_offering, request, "allocate", "actor", default);
        await using (var seed = new AtoCopilotContext(_options))
        {
            seed.Add(new MissionProviderRelationshipReview { ProviderId = _provider, OfferingId = _offering,
                AssignmentId = first.AssignmentId, AssignmentRevision = 1, TenantId = _tenant, SystemId = _system,
                State = "SeparateBoundaryConsumer", ReviewRequired = false });
            await seed.SaveChangesAsync();
        }
        // Act
        var replay = await service.AssignAsync(_offering, request, "allocate", "actor", default);
        var exact = await service.AssignmentAsync(_offering, first.AssignmentId, default);
        var list = await service.AssignmentsAsync(_offering, 1, 1, default);
        // Assert
        replay.AssignmentId.Should().Be(first.AssignmentId);
        exact.SystemName.Should().Be("Mission");
        exact.TargetTenantName.Should().Be("Customer");
        exact.RelationshipState.Should().Be("SeparateBoundaryConsumer");
        list.Total.Should().Be(1);
        await FluentActions.Awaiting(() => service.AssignAsync(_offering, request with { TargetTenantId = Guid.NewGuid() },
            "other", "actor", default)).Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => service.AssignAsync(_offering, request with { SystemId = "other" },
            "allocate", "actor", default)).Should().ThrowAsync<DbUpdateConcurrencyException>();
        await FluentActions.Awaiting(() => service.AssignmentAsync(_offering, Guid.NewGuid(), default))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => service.ScopeAsync(_offering, Guid.NewGuid(), default))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("/resourceGroups/mission2")]
    [InlineData("/resourceGroups/mission")]
    [InlineData("/resourceGroups/mission/providers/Microsoft.Compute/virtualMachines/excluded")]
    public async Task Assignment_RejectsPrefixConfusionOrExcludedSubscope(string assigned)
    {
        // Arrange
        var tenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        using var scope = _accessor.Push(tenant);
        var service = Service(tenant);
        var hosting = await service.CreateScopeAsync(_offering, new(1, null, "Hosting", [Scope("/resourceGroups/mission")],
            [new(Scope("/resourceGroups/mission/providers/Microsoft.Compute/virtualMachines/excluded"), "Excluded")], []), "scope", "actor", default);

        // Act
        var act = () => service.AssignAsync(_offering, new(_tenant, _system, hosting.Snapshot.RevisionId,
            [Scope(assigned)], []), "assignment", "actor", default);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
