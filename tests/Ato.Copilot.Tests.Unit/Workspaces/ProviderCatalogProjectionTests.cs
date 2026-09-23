using System.Data.Common;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Workspaces;

public sealed class ProviderCatalogProjectionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(200)]
    public async Task CapabilityPage_BatchesAllEnrichmentInSixReadsWithoutWrites(int pageSize)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection)
            .AddInterceptors(counter).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        var profile = new CspProfile { DisplayName = "Provider", LegalEntityName = "Provider Ltd" };
        var primary = new CspInheritedComponent
        {
            CspProfileId = profile.Id, Name = "Primary", ComponentType = CspComponentType.Platform
        };
        var contributor = new CspInheritedComponent
        {
            CspProfileId = profile.Id, Name = "Contributor", ComponentType = CspComponentType.Service
        };
        db.Set<CspProfile>().Add(profile);
        db.CspInheritedComponents.AddRange(primary, contributor);
        for (var i = 0; i < 200; i++)
        {
            var capability = new CspInheritedCapability
            {
                CspInheritedComponentId = primary.Id, Name = $"Capability {i:D3}"
            };
            db.CspInheritedCapabilities.Add(capability);
            db.ProviderCapabilityWorkingRevisions.Add(new ProviderCapabilityWorkingRevision
            {
                CapabilityId = capability.Id, ContributorsJson = JsonSerializer.Serialize(new[] { contributor.Id.ToString() })
            });
        }
        await db.SaveChangesAsync();
        counter.Reads = 0;
        counter.Writes = 0;
        var service = new WorkspaceOperationsService(new ContextFactory(options));

        // Act
        var page = await service.ListProviderCatalogAsync(new(PageSize: pageSize, Grouping: "capability"), default);

        // Assert
        page.Total.Should().Be(200);
        page.Items.Should().HaveCount(pageSize);
        page.Items.Should().OnlyContain(x => x.SupportingComponents != null && x.SupportingComponents.Count == 2);
        counter.Reads.Should().BeLessThanOrEqualTo(6);
        counter.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Overview_EmptyDeployment_DoesNotInventProviderPackageOrAuthorization()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new WorkspaceOperationsService(new ContextFactory(options));

        // Act
        var overview = await service.GetProviderCatalogOverviewAsync(-1, 1000, default);

        // Assert
        overview.ProviderName.Should().BeNull();
        overview.AuthorizationRecord.Should().BeNull();
        overview.SourceArtifacts.Items.Should().BeEmpty();
        overview.SourceArtifacts.Total.Should().Be(0);
        overview.SourceArtifacts.Page.Should().Be(1);
        overview.SourceArtifacts.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task Detail_ExistingReleaseDoesNotInventWorkingStateOrNarrative()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var db = new AtoCopilotContext(options);
        await db.Database.EnsureCreatedAsync();
        var profile = new CspProfile { DisplayName = "Provider", LegalEntityName = "Provider Ltd" };
        var component = new CspInheritedComponent { CspProfileId = profile.Id, Name = "Source without artifacts" };
        var capability = new CspInheritedCapability { CspInheritedComponentId = component.Id, Name = "Published source" };
        db.Set<CspProfile>().Add(profile);
        db.CspInheritedComponents.Add(component);
        db.CspInheritedCapabilities.Add(capability);
        db.ProviderCapabilityReleases.Add(new ProviderCapabilityRelease
        {
            CapabilityId = capability.Id, Revision = 7, SnapshotJson = "{}", IdempotencyKey = "release"
        });
        await db.SaveChangesAsync();
        var service = new WorkspaceOperationsService(new ContextFactory(options));

        // Act
        var detail = await service.GetProviderCapabilityAsync(capability.Id, default);

        // Assert
        detail.Should().NotBeNull();
        detail!.Capability.ReleasedRevision.Should().Be(7);
        detail.Capability.WorkingRevision.Should().BeNull();
        detail.Capability.WorkingApprovalState.Should().BeNull();
        detail.SourceArtifacts.Should().BeEmpty();
        detail.SourceEvidenceReferences.Should().BeNull();
        detail.ImplementationNarrative.Should().BeNull();
    }

    private sealed class ContextFactory(DbContextOptions<AtoCopilotContext> options) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public int Writes { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Reads++;
            else
                Writes++;
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Writes++;
            return ValueTask.FromResult(result);
        }
    }
}
