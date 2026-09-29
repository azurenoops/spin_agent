using Ato.Copilot.Core.Models.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.ProviderAuthorizations.OfferingBoundaryOverviewTests.Fixture;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingMissionReleaseProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentPin_PreservesOlderRelease_AndUsesOnlySameOfferingLatestRevision(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var assignment = await fixture.AddAssignmentAsync();
        var adopted = await fixture.AddPublishedAsync("Adopted capability", revision: 2);
        var newer = await fixture.AddPublishedAsync("Adopted capability", capabilityId: adopted.CapabilityId, revision: 5);
        await fixture.AddPublishedAsync("Adopted capability", capabilityId: adopted.CapabilityId,
            revision: 9, offeringId: fixture.OtherOffering);
        await fixture.AddAdoptionAsync(assignment, adopted);
        await using (var db = fixture.Db())
        {
            var adoption = await db.Set<CapabilityAdoptionSnapshot>().SingleAsync();
            var subscription = await db.CapabilitySubscriptions.SingleAsync();
            subscription.CurrentAdoptionSnapshotId = adoption.Id;
            db.Add(new CapabilityAdoptionSnapshot
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, TenantId = fixture.Customer,
                SystemId = assignment.SystemId, AssignmentId = assignment.Id,
                SubscriptionId = subscription.Id, CapabilityId = adopted.CapabilityId,
                ReleaseId = newer.ReleaseId, ContextSnapshotId = newer.ContextId
            });
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        var mission = result.MissionSystems.Items.Single();
        mission.TargetTenantId.Should().Be(fixture.Customer);
        mission.TargetTenantName.Should().Be("Synthetic tenant");
        mission.SystemName.Should().Be("Synthetic mission");
        mission.AdoptedCapabilityCount.Should().Be(1);
        mission.AdoptedReleases.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            adopted.CapabilityId, CapabilityName = "Adopted capability", adopted.ReleaseId,
            Revision = 2L, CurrentReleaseRevision = (long?)5, UpdateAvailable = true
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OtherAssignmentPin_AndUnpinnedLegacyHistory_DoNotClaimAdoptedRelease(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var assignment = await fixture.AddAssignmentAsync();
        var other = await fixture.AddAssignmentAsync(systemId: assignment.SystemId);
        var capability = await fixture.AddPublishedAsync("Capability");
        await fixture.AddAdoptionAsync(assignment, capability);
        await using (var db = fixture.Db())
        {
            var subscription = await db.CapabilitySubscriptions.SingleAsync();
            var pin = new CapabilityAdoptionSnapshot
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, TenantId = fixture.Customer,
                SystemId = other.SystemId, AssignmentId = other.Id, SubscriptionId = subscription.Id,
                CapabilityId = capability.CapabilityId, ReleaseId = capability.ReleaseId, ContextSnapshotId = capability.ContextId
            };
            db.Add(pin);
            subscription.CurrentAdoptionSnapshotId = pin.Id;
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        var mission = result.MissionSystems.Items.Single(x => x.AssignmentId == assignment.Id);
        mission.AdoptedCapabilityCount.Should().Be(1);
        mission.AdoptedReleases.Should().BeEmpty();
        var otherMission = result.MissionSystems.Items.Single(x => x.AssignmentId == other.Id);
        otherMission.AdoptedReleases.Should().ContainSingle().Which.UpdateAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData("unpinned", false)]
    [InlineData("unpinned", true)]
    [InlineData("inactive", true)]
    [InlineData("tenant", false)]
    [InlineData("subscription-tenant", true)]
    [InlineData("system", false)]
    [InlineData("subscription-system", true)]
    [InlineData("provider", false)]
    [InlineData("offering", false)]
    [InlineData("capability", true)]
    [InlineData("missing-release", true)]
    [InlineData("mismatched-release", true)]
    public async Task UnverifiedPins_DoNotClaimARelease(string kind, bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var assignment = await fixture.AddAssignmentAsync();
        var capability = await fixture.AddPublishedAsync("Capability");
        await fixture.AddAdoptionAsync(assignment, capability);
        await using (var db = fixture.Db())
        {
            var adoption = await db.Set<CapabilityAdoptionSnapshot>().SingleAsync();
            var subscription = await db.CapabilitySubscriptions.SingleAsync();
            if (kind is "provider" or "offering")
            {
                db.Remove(adoption);
                await db.SaveChangesAsync();
                db.Add(adoption);
            }
            subscription.CurrentAdoptionSnapshotId = kind == "unpinned" ? null : adoption.Id;
            switch (kind)
            {
                case "inactive": subscription.IsActive = false; break;
                case "tenant": adoption.TenantId = Guid.NewGuid(); break;
                case "subscription-tenant": subscription.RoutingTenantId = Guid.NewGuid(); break;
                case "system": adoption.SystemId = Guid.NewGuid().ToString(); break;
                case "subscription-system": subscription.RegisteredSystemId = Guid.NewGuid().ToString(); break;
                case "provider": adoption.ProviderId = Guid.NewGuid(); break;
                case "offering": adoption.OfferingId = fixture.OtherOffering; break;
                case "capability": subscription.CspInheritedCapabilityId = Guid.NewGuid().ToString(); break;
                case "missing-release": adoption.ReleaseId = Guid.NewGuid(); break;
                case "mismatched-release":
                    var other = await fixture.AddPublishedAsync("Other capability");
                    adoption.ReleaseId = other.ReleaseId;
                    break;
            }
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.BoundaryOverviewAsync(fixture.Offering, 1, 1, 10, default);

        // Assert
        result.MissionSystems.Items.Single().AdoptedReleases.Should().BeEmpty();
        if (kind == "unpinned")
            result.MissionSystems.Items.Single().AdoptedCapabilityCount.Should().Be(1);
    }
}
