using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

[Collection("SqlServerSchema")]
public sealed class ProviderOfferingAssociationSqlServerTests(BoundarySchemaSqlServerFixture fixture)
{
    [SkippableFact]
    public async Task CreateAsync_WithSqlServerRetryStrategy_PersistsAggregateAndRevisionAtomically()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.EnsureCreatedAsync();
        var provider = new CspProfile { DisplayName = "Synthetic provider", LegalEntityName = "Synthetic provider" };
        var offering = new ProviderOffering { ProviderId = provider.Id, Name = "Synthetic offering" };
        offering.OfferingId = offering.Id;
        var component = new CspInheritedComponent
        {
            CspProfileId = provider.Id,
            Name = "Synthetic component",
            Status = CspInheritedComponentStatus.Published
        };
        var capability = new CspInheritedCapability
        {
            CspInheritedComponentId = component.Id,
            Name = "Synthetic capability",
            Status = CspInheritedCapabilityStatus.Mapped,
            MappedNistControlIds = ["AU-2"]
        };
        var release = new ProviderCapabilityRelease
        {
            CapabilityId = capability.Id,
            Revision = 1,
            SnapshotHash = "synthetic-release",
            SnapshotJson = "{}",
            IdempotencyKey = "synthetic-release",
            PublishedBy = "provider-admin"
        };
        var package = new CspPackage
        {
            ProviderId = provider.Id,
            OfferingId = offering.Id,
            Name = "Synthetic package",
            IdempotencyKey = "synthetic-package"
        };
        var entry = new CspPackageEntry
        {
            PackageId = package.Id,
            ArchivePath = "synthetic.json",
            SegmentsJson = ProviderAuthorizationStore.Json(new[]
            {
                new CspPackageSourceSegment("segment", "entry", Guid.NewGuid().ToString(), "synthetic.json",
                    "$/capabilities/0", "Synthetic source quote")
            })
        };
        db.AddRange(provider, offering, component, capability, release, package, entry);
        await db.SaveChangesAsync();
        var tenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(db.Database.GetConnectionString(), sql => sql.EnableRetryOnFailure())
            .Options;
        var store = new ProviderAuthorizationStore(new ContextFactory(options), tenant,
            NullLogger<ProviderAuthorizationStore>.Instance);
        var service = new ProviderOfferingAssociationService(db, store);
        var citation = new ProviderCitation(package.Id, entry.Id, entry.ArchivePath,
            "$/capabilities/0", "Synthetic source quote");
        var request = new CreateOfferingAssociationRequest(component.Id, capability.Id, release.Id,
            offering.Revision, [citation], ["AU-2"], ["Customer reviews events"], "Primary");

        // Act
        var result = await service.CreateAsync(offering.Id, request, "provider-admin", default);

        // Assert
        result.Revision.Should().Be(1);
        var aggregate = await db.ProviderOfferingAssociations.SingleAsync(x => x.Id == result.AssociationId);
        var revision = await db.ProviderOfferingAssociationRevisions.SingleAsync(x => x.AssociationId == result.AssociationId);
        aggregate.CurrentRevisionId.Should().Be(revision.Id);
        revision.Status.Should().Be("Draft");
    }

    private sealed class ContextFactory(DbContextOptions<AtoCopilotContext> options)
        : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }
}
