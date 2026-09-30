using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class DocumentPreviewHttpTests
{
    [Fact]
    public async Task MissionOwnerOnly_AdoptsThroughProductionRoute_WithoutConfirmationOrAoAuthority()
    {
        // Arrange: exact published provider context is a fixture; no mission adoption is seeded.
        var fixture = await SeedAsync();
        var published = await SeedPublishedForAdoptionAsync(fixture.System);
        var unassigned = await SeedAsync(assignRole: false);
        var foreign = await SeedAsync(WorkspaceMembershipFactory.TenantBId);
        using var mission = Client(fixture.Actor);
        using var unrelated = Client(unassigned.Actor);
        using var otherTenant = Client(foreign.Actor);
        foreach (var client in new[] { mission, unrelated, otherTenant })
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        otherTenant.DefaultRequestHeaders.Remove("X-Workspace-Tenant-Id");
        otherTenant.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", WorkspaceMembershipFactory.TenantBId.ToString());
        var root = $"/api/dashboard/systems/{fixture.System}";
        using var associated = await mission.PostAsJsonAsync(root + "/provider-relationships",
            new CreateMissionProviderRelationshipRequest(published.AssignmentId, 1));
        associated.StatusCode.Should().Be(HttpStatusCode.OK, await associated.Content.ReadAsStringAsync());
        using var applicabilityResponse = await mission.GetAsync(root + "/applicable-provider-capabilities");
        applicabilityResponse.StatusCode.Should().Be(HttpStatusCode.OK, await applicabilityResponse.Content.ReadAsStringAsync());
        var applicability = await applicabilityResponse.Content.ReadFromJsonAsync<JsonElement>();
        var item = applicability.GetProperty("data").GetProperty("items")[0];
        var request = new AdoptProviderCapabilityRequest(published.AssignmentId, 1, published.CapabilityId, published.ReleaseId,
            item.GetProperty("applicability").GetProperty("snapshotHash").GetString()!,
            item.GetProperty("applicabilityPreviewHash").GetString()!);

        // Act
        using var deniedPerson = await unrelated.PostAsJsonAsync(root + "/provider-capability-adoptions", request);
        using var deniedTenant = await otherTenant.PostAsJsonAsync(root + "/provider-capability-adoptions", request);
        using var adopted = await mission.PostAsJsonAsync(root + "/provider-capability-adoptions", request);
        using var confirmation = await mission.PutAsJsonAsync(root + $"/capability-subscriptions/{published.CapabilityId}/responsibilities",
            new { baselineId = "unconfirmed", sourceRevision = "unconfirmed", reviewRevision = "unconfirmed", allocations = Array.Empty<object>() });

        // Assert
        deniedPerson.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deniedTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        adopted.StatusCode.Should().Be(HttpStatusCode.OK, await adopted.Content.ReadAsStringAsync());
        confirmation.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        item.GetProperty("canProposeAdoption").GetBoolean().Should().BeTrue();
        item.GetProperty("canConfirmResponsibilities").GetBoolean().Should().BeFalse();
        var data = (await adopted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("subscription").GetProperty("responsibilities").GetProperty("canConfirm").GetBoolean().Should().BeFalse();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var subscription = await db.CapabilitySubscriptions.SingleAsync(s => s.RegisteredSystemId == fixture.System);
        subscription.CurrentAdoptionSnapshotId.Should().Be(data.GetProperty("adoptionSnapshotId").GetGuid());
        (await db.Set<CapabilityResponsibilityConfirmation>().CountAsync(c => c.RegisteredSystemId == fixture.System)).Should().Be(0);
        (await db.AuthorizationDecisions.CountAsync(d => d.RegisteredSystemId == fixture.System)).Should().Be(0);
    }

    private async Task<(Guid AssignmentId, Guid CapabilityId, Guid ReleaseId)> SeedPublishedForAdoptionAsync(string system)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var provider = await db.CspProfiles.Select(p => p.Id).FirstAsync();
        var offering = new ProviderOffering { ProviderId = provider, Name = "DEMO published offering" };
        offering.OfferingId = offering.Id;
        var azureScope = new ProviderAzureScope("AzureCloud", Tenant, Tenant, $"/subscriptions/{Tenant}");
        var package = new CspPackage { ProviderId = provider, Name = "DEMO source", IdempotencyKey = Guid.NewGuid().ToString() };
        var entry = new CspPackageEntry { PackageId = package.Id, ArchivePath = "source.txt" };
        var citation = new ProviderCitation(package.Id, entry.Id, entry.ArchivePath, "line:1", "DEMO recorded decision");
        entry.SegmentsJson = ProviderAuthorizationStore.Json(new[] { new CspPackageSourceSegment("s", "entry", entry.Id.ToString(), entry.ArchivePath, citation.Locator, citation.Quote) });
        var boundary = new ProviderBoundaryRevision
        {
            ProviderId = provider, OfferingId = offering.Id,
            SnapshotJson = ProviderAuthorizationStore.Json(new CreateProviderBoundaryRequest(1, null, "DEMO boundary", "DEMO scope", ["Audit"],
                [], [azureScope], [], ["Log events"], ["Review logs"], [citation]))
        };
        boundary.SnapshotHash = Hash(boundary.SnapshotJson);
        var hosting = new ProviderHostingScopeRevision
        {
            ProviderId = provider, OfferingId = offering.Id,
            SnapshotJson = ProviderAuthorizationStore.Json(new CreateProviderHostingScopeRequest(1, null, "DEMO hosting", [azureScope], [], []))
        };
        hosting.SnapshotHash = Hash(hosting.SnapshotJson);
        offering.CurrentBoundaryRevisionId = boundary.Id;
        offering.CurrentHostingScopeRevisionId = hosting.Id;
        var assignment = new ProviderHostingAssignment
        {
            ProviderId = provider, OfferingId = offering.Id, TargetTenantId = Tenant, SystemId = system,
            HostingScopeRevisionId = hosting.Id, AssignedScopesJson = ProviderAuthorizationStore.Json(new[] { azureScope })
        };
        var record = new Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderAuthorizationRecord { ProviderId = provider, OfferingId = offering.Id };
        var decision = new ProviderAuthorizationRevision
        {
            ProviderId = provider, OfferingId = offering.Id, RecordId = record.Id, BoundaryRevisionId = boundary.Id,
            MetadataReviewState = "Recorded", RecordedBy = "DEMO reviewer", RecordedAt = DateTimeOffset.UtcNow,
            SnapshotJson = ProviderAuthorizationStore.Json(new CreateProviderDecisionRequest(1, boundary.Id, [], "ProviderDecision", "DEMO decision",
                "DEMO authority", "ATO", "2025-04-17", null, null, "NoExpiryStated", "DEMO scope", [], [citation])
                { IssuingAuthorityType = "organization" })
        };
        decision.SnapshotHash = Hash(decision.SnapshotJson);
        record.CurrentRevisionId = decision.Id;
        var component = new CspInheritedComponent { CspProfileId = provider, Name = "DEMO component", Status = CspInheritedComponentStatus.Published };
        var capability = new CspInheritedCapability
        {
            CspInheritedComponentId = component.Id, Name = "DEMO logging", Status = CspInheritedCapabilityStatus.Mapped,
            MappedNistControlIds = ["AU-2"]
        };
        var release = new ProviderCapabilityRelease
        {
            CapabilityId = capability.Id, Revision = 1, SnapshotHash = "DEMO release hash", PublishedBy = "DEMO reviewer",
            SnapshotJson = JsonSerializer.Serialize(new { DutiesJson = "{\"AU-2\":\"Shared\"}" })
        };
        var material = new ProviderPublicationContextMaterial(offering.Id, 1, boundary.Id, boundary.SnapshotHash,
            hosting.Id, hosting.SnapshotHash, [new(record.Id, decision.Id, decision.SnapshotHash, "DEMO lifecycle")], [], []);
        var review = new ProviderAuthorizationImpactReview
        {
            ProviderId = provider, OfferingId = offering.Id, Disposition = "AcceptForPublication",
            ReviewedBy = "DEMO reviewer", ReviewedAt = DateTimeOffset.UtcNow, ContextJson = ProviderAuthorizationStore.Json(material)
        };
        review.ContextSnapshotHash = Hash(review.ContextJson);
        var context = new ProviderCatalogContextSnapshot
        {
            ProviderId = provider, OfferingId = offering.Id, CapabilityId = capability.Id, ReleaseId = release.Id,
            ImpactReviewId = review.Id, SnapshotJson = review.ContextJson, SnapshotHash = review.ContextSnapshotHash
        };
        db.AddRange(offering, boundary, hosting, assignment, record, decision, package, entry, component, capability, release, review, context);
        await db.SaveChangesAsync();
        return (assignment.Id, capability.Id, release.Id);
    }
}
