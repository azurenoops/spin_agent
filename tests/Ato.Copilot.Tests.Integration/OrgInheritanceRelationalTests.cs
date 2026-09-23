using System.Data.Common;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed class OrgInheritanceRelationalTests : IAsyncLifetime
{
    private const string StaleDefaultId = "org-default-stale";
    private readonly string[] _overrideSources = ["Manual", "ProfileApply", "CrmImport", "BulkUpdate"];
    private readonly CommitFailureInterceptor _commitFailureInterceptor = new();
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private OrgInheritanceService _sut = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseSqlite(_connection).AddInterceptors(_commitFailureInterceptor));
        _serviceProvider = services.BuildServiceProvider();
        _sut = new OrgInheritanceService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrgInheritanceService>.Instance);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.EnsureCreatedAsync();

        var orgDerivedSystem = new RegisteredSystem
        {
            Id = "system-stale-default",
            Name = "Stale Default Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
        };
        var orgDerivedBaseline = new ControlBaseline
        {
            Id = "baseline-stale-default",
            RegisteredSystemId = orgDerivedSystem.Id,
            BaselineLevel = "Moderate",
            TotalControls = 1,
            ControlIds = ["AC-2"],
            CreatedBy = "test",
        };
        var staleDefault = new OrgInheritanceDefault
        {
            Id = StaleDefaultId,
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Retired Provider",
            SourceCapabilityIds = "retired-capability",
            SourceCapabilityNames = "Retired Capability",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow.AddDays(-1),
        };

        db.AddRange(orgDerivedSystem, orgDerivedBaseline, staleDefault);
        db.ControlInheritances.Add(new ControlInheritance
        {
            ControlBaselineId = orgDerivedBaseline.Id,
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Retired Provider",
            DesignationSource = "OrgDerived",
            OrgInheritanceDefaultId = staleDefault.Id,
            SetBy = "system",
        });
        foreach (var (source, index) in _overrideSources.Select((source, index) => (source, index)))
        {
            var system = new RegisteredSystem
            {
                Id = $"system-override-{index}",
                Name = $"Override Test System {index}",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionSupport,
                HostingEnvironment = "Azure Government",
                CreatedBy = "test",
            };
            var baseline = new ControlBaseline
            {
                Id = $"baseline-override-{index}",
                RegisteredSystemId = system.Id,
                BaselineLevel = "Moderate",
                TotalControls = 1,
                ControlIds = ["AC-2"],
                CreatedBy = "test",
            };
            db.AddRange(system, baseline);
            db.ControlInheritances.Add(new ControlInheritance
            {
                ControlBaselineId = baseline.Id,
                ControlId = "AC-2",
                InheritanceType = InheritanceType.Shared,
                Provider = $"Override Provider {index}",
                CustomerResponsibility = $"Responsibility {index}",
                DesignationSource = source,
                OrgInheritanceDefaultId = staleDefault.Id,
                SetBy = "reviewer",
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task PropagateToSystem_PreservesSubscriptionOwnedDesignation()
    {
        // Arrange
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var row = await db.ControlInheritances.SingleAsync(i => i.ControlBaselineId == "baseline-stale-default");
            row.DesignationSource = "CspSubscription";
            row.Provider = "Reviewed subscription provider";
            await db.SaveChangesAsync();
        }

        // Act
        await _sut.PropagateToSystemAsync("system-stale-default", "baseline-stale-default", new HashSet<string> { "AC-2" }, "fixture");

        // Assert
        await using var verifyScope = _serviceProvider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var designation = await verify.ControlInheritances.SingleAsync(i => i.ControlBaselineId == "baseline-stale-default");
        designation.DesignationSource.Should().Be("CspSubscription");
        designation.Provider.Should().Be("Reviewed subscription provider");
    }

    [Fact]
    public async Task DeriveOrgDefaults_RemovingReferencedDefault_ReconcilesDependentsAtomically()
    {
        // Arrange
        // No eligible organization mappings remain, so the seeded default is stale.

        // Act
        var result = await _sut.DeriveOrgDefaultsAsync("test-user");

        // Assert
        result.RemovedCount.Should().Be(1);
        result.AffectedSystems.Should().Be(_overrideSources.Length + 1);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.OrgInheritanceDefaults.AnyAsync(item => item.Id == StaleDefaultId)).Should().BeFalse();
        (await db.ControlInheritances.AnyAsync(item => item.DesignationSource == "OrgDerived")).Should().BeFalse();

        var overrides = await db.ControlInheritances.ToListAsync();
        overrides.Should().HaveCount(_overrideSources.Length);
        overrides.Select(item => item.DesignationSource).Should().BeEquivalentTo(_overrideSources);
        overrides.Should().OnlyContain(item =>
            item.OrgInheritanceDefaultId == null &&
            item.Provider != null &&
            item.CustomerResponsibility != null);
        (await db.InheritanceAuditEntries.CountAsync(item =>
            item.ControlId == "ORG-CASCADE")).Should().Be(_overrideSources.Length + 1);

        var repeat = await _sut.DeriveOrgDefaultsAsync("test-user");
        repeat.RemovedCount.Should().Be(0);
        (await db.ControlInheritances.CountAsync()).Should().Be(_overrideSources.Length);
    }

    [Fact]
    public async Task DeriveOrgDefaults_WhenCommitFails_RollsBackEntireReconciliation()
    {
        // Arrange
        _commitFailureInterceptor.FailNextCommit = true;

        // Act
        Func<Task> act = () => _sut.DeriveOrgDefaultsAsync("test-user");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated commit failure");
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.OrgInheritanceDefaults.AnyAsync(item => item.Id == StaleDefaultId)).Should().BeTrue();
        var designations = await db.ControlInheritances.ToListAsync();
        designations.Should().HaveCount(_overrideSources.Length + 1);
        designations.Should().OnlyContain(item => item.OrgInheritanceDefaultId == StaleDefaultId);
        designations.Should().ContainSingle(item => item.DesignationSource == "OrgDerived");
        (await db.InheritanceAuditEntries.AnyAsync()).Should().BeFalse();
    }

    private sealed class CommitFailureInterceptor : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new InvalidOperationException("Simulated commit failure");
            }

            return base.TransactionCommittingAsync(
                transaction,
                eventData,
                result,
                cancellationToken);
        }
    }
}
