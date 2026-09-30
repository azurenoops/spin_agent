using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Store = Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;
using Fixture = Ato.Copilot.Tests.Unit.ProviderAuthorizations.OfferingBoundaryOverviewTests.Fixture;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingCustomerActionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomerActions_CountDistinctActionableReviews_NotDeliveryJobsOrAssignedSystems(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var source = await CurrentSourceAsync(fixture);
        var draft = await ProposalAsync(fixture, source, "Draft");
        await ProposalAsync(fixture, source, "GenerationFailed");
        foreach (var status in new[] { "Approved", "Rejected", "PendingGeneration" })
            await ProposalAsync(fixture, source, status);
        await using (var db = fixture.Db())
        {
            db.Add(Receipt(fixture, source, draft));
            db.Add(new CapabilityResponsibilityDelivery
            {
                Id = Guid.NewGuid().ToString(), TenantId = fixture.Customer,
                RegisteredSystemId = source.SystemId, SubscriptionId = source.SubscriptionId, Outcome = "Pending"
            });
            await db.SaveChangesAsync();
        }
        await fixture.AddAssignmentAsync();

        // Act
        var overview = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        overview.CustomerActionCount.Should().Be(2, "only distinct actionable source-backed narrative proposals are customer work");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomerActions_ExcludeOtherOfferingsInactiveSubscriptionsAndUnselectedHistory(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var current = await CurrentSourceAsync(fixture);
        await ProposalAsync(fixture, current, "Draft");
        var other = await CurrentSourceAsync(fixture, fixture.OtherOffering);
        await ProposalAsync(fixture, other, "Draft");
        var inactive = await CurrentSourceAsync(fixture);
        await ProposalAsync(fixture, inactive, "Draft");
        var historical = await CurrentSourceAsync(fixture);
        await ProposalAsync(fixture, historical, "Draft");
        await using (var db = fixture.Db())
        {
            (await db.CapabilitySubscriptions.SingleAsync(x => x.Id == inactive.SubscriptionId)).IsActive = false;
            (await db.CapabilitySubscriptions.SingleAsync(x => x.Id == historical.SubscriptionId)).CurrentAdoptionSnapshotId = null;
            await db.SaveChangesAsync();
        }

        // Act
        var overview = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        overview.CustomerActionCount.Should().Be(1);
    }

    [Fact]
    public async Task CustomerActions_RequireExactTenantAndRetainedSourceSubscription()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(true);
        var source = await CurrentSourceAsync(fixture);
        var proposal = await ProposalAsync(fixture, source, "Draft");
        await using (var db = fixture.Db())
        {
            var receipt = await db.Set<NarrativeImpactReceipt>().SingleAsync(x => x.NarrativeProposalId == proposal);
            receipt.SourceContextJson = Store.Json(new NarrativeChangeSourceContext("source", "ProviderChanged", "baseline",
                "different-subscription", fixture.Provider, CspCapabilityId: source.CapabilityId));
            var foreignTenant = Guid.NewGuid();
            db.Tenants.Add(new() { Id = foreignTenant, DisplayName = "Other customer" });
            var foreign = new NarrativeProposal
            {
                TenantId = foreignTenant, RegisteredSystemId = source.SystemId, Status = "Draft", DeduplicationKey = Guid.NewGuid().ToString()
            };
            db.Add(foreign);
            var foreignReceipt = Receipt(fixture, source, foreign.Id);
            foreignReceipt.TenantId = foreignTenant;
            db.Add(foreignReceipt);
            await db.SaveChangesAsync();
        }

        // Act
        var overview = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        overview.CustomerActionCount.Should().Be(0, "a real empty scoped query is different from an unavailable service");
    }

    [Fact]
    public async Task CustomerActions_AddOnlyLatestExplicitPendingRelationshipReviews()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(true);
        var source = await CurrentSourceAsync(fixture);
        await ProposalAsync(fixture, source, "Draft");
        var pending = await fixture.AddAssignmentAsync();
        await fixture.AddRelationshipAsync(pending, "Undetermined", true);
        var completed = await fixture.AddAssignmentAsync();
        await fixture.AddRelationshipAsync(completed, "Undetermined", true, 1);
        await fixture.AddRelationshipAsync(completed, "SeparateMissionAuthorizationRequired", false);
        await using (var db = fixture.Db())
        {
            (await db.Set<MissionProviderRelationshipReview>().SingleAsync(x => x.AssignmentId == completed.Id && !x.ReviewRequired)).Revision = 2;
            await db.SaveChangesAsync();
        }
        var other = await fixture.AddAssignmentAsync(offeringId: fixture.OtherOffering);
        await fixture.AddRelationshipAsync(other, "Undetermined", true);

        // Act
        var overview = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        overview.CustomerActionCount.Should().Be(2, "one explicit current relationship review and one distinct narrative review");
    }

    [Fact]
    public async Task CustomerActions_ReturnUnknownWhenTheActualReviewStoreIsUnavailable()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(true);
        await using (var db = fixture.Db())
            await db.Database.ExecuteSqlRawAsync("DROP TABLE NarrativeImpactReceipts");

        // Act
        var overview = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        overview.CustomerActionCount.Should().BeNull();
    }

    private sealed record Source(string SubscriptionId, string SystemId, Guid CapabilityId);
    private static async Task<Source> CurrentSourceAsync(Fixture fixture, Guid? offeringId = null)
    {
        var offering = offeringId ?? fixture.Offering;
        var assignment = await fixture.AddAssignmentAsync(offeringId: offering);
        (Guid CapabilityId, Guid ReleaseId, Guid ContextId) capability;
        if (offering == fixture.Offering)
            capability = await fixture.AddPublishedAsync("Synthetic offering capability");
        else
        {
            await using var contextDb = fixture.Db();
            var boundary = new ProviderBoundaryRevision { ProviderId = fixture.Provider, OfferingId = offering };
            var impact = new ProviderAuthorizationImpactReview { ProviderId = fixture.Provider, OfferingId = offering };
            var component = new CspInheritedComponent { CspProfileId = fixture.Provider, Name = "Other source", Status = CspInheritedComponentStatus.Published };
            var row = new CspInheritedCapability { CspInheritedComponentId = component.Id, Name = "Other offering capability", Status = CspInheritedCapabilityStatus.Mapped };
            var release = new ProviderCapabilityRelease { CapabilityId = row.Id, Revision = 1, IdempotencyKey = Guid.NewGuid().ToString() };
            var context = new ProviderCatalogContextSnapshot
            {
                ProviderId = fixture.Provider, OfferingId = offering, CapabilityId = row.Id, ReleaseId = release.Id,
                ImpactReviewId = impact.Id,
                SnapshotJson = Store.Json(new ProviderPublicationContextMaterial(offering, 1, boundary.Id, "", null, null, [], [], []))
            };
            contextDb.AddRange(boundary, impact, component, row, release, context);
            await contextDb.SaveChangesAsync();
            capability = (row.Id, release.Id, context.Id);
        }
        await fixture.AddAdoptionAsync(assignment, capability);
        await using var db = fixture.Db();
        var adoption = await db.Set<CapabilityAdoptionSnapshot>().SingleAsync(x => x.AssignmentId == assignment.Id);
        var subscription = await db.CapabilitySubscriptions.SingleAsync(x => x.Id == adoption.SubscriptionId);
        subscription.CurrentAdoptionSnapshotId = adoption.Id;
        await db.SaveChangesAsync();
        return new(subscription.Id, assignment.SystemId, capability.CapabilityId);
    }

    private static async Task<Guid> ProposalAsync(Fixture fixture, Source source, string status)
    {
        await using var db = fixture.Db();
        var proposal = new NarrativeProposal
        {
            TenantId = fixture.Customer, RegisteredSystemId = source.SystemId, ControlId = "AU-2",
            NarrativeType = "Technical", Status = status, DeduplicationKey = Guid.NewGuid().ToString(),
        };
        db.Add(proposal);
        db.Add(Receipt(fixture, source, proposal.Id));
        await db.SaveChangesAsync();
        return proposal.Id;
    }

    private static NarrativeImpactReceipt Receipt(Fixture fixture, Source source, Guid proposalId) => new()
    {
        Id = Guid.NewGuid().ToString(), TenantId = fixture.Customer, RegisteredSystemId = source.SystemId,
        NarrativeProposalId = proposalId, SourceKind = "CspCapability", SourceId = source.CapabilityId.ToString(),
        SourceContextJson = Store.Json(new NarrativeChangeSourceContext("source", "ProviderChanged", "baseline",
            source.SubscriptionId, fixture.Provider, CspCapabilityId: source.CapabilityId)),
    };
}
