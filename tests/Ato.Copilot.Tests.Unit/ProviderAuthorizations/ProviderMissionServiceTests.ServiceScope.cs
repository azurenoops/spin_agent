using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderMissionServiceTests
{
    [Theory]
    [InlineData("ProviderManaged", "Provider operates service", "Mission reviews service outputs")]
    [InlineData("SharedOperations", "Provider maintains shared controls", "Mission configures its tenant")]
    [InlineData("MissionOwnerManaged", "Provider supplies the service interface", "Mission operates its service tenant")]
    public async Task ManualService_AssignmentToMissionOwnerAdoption_PreservesAzureIsolationAndExplicitDuties(
        string management, string providerDuty, string customerDuty)
    {
        // Arrange: retain the fixture's independent Azure offering and source history.
        var azureOffering = _offering;
        var providerTenant = new TenantContext(Guid.Empty) { IsCspAdmin = true };
        var providerStore = new ProviderAuthorizationStore(new Factory(_options, _accessor), providerTenant,
            NullLogger<ProviderAuthorizationStore>.Instance);
        var authorizations = new ProviderAuthorizationService(providerStore, Mock.Of<ICspPackageService>());
        var hosting = new ProviderHostingService(providerStore);
        var serviceScope = new ProviderServiceScope("synthetic-m365", "Synthetic M365 collaboration", "Microsoft365DoD", "synthetic-tenant");
        ProviderOfferingResponse offering;
        ProviderBoundaryResponse boundary;
        ProviderHostingScopeResponse serviceHosting;
        ProviderHostingAssignmentResponse assignment;
        ProviderDecisionResponse decision;
        using (_accessor.Push(providerTenant))
        {
            offering = await authorizations.CreateAsync(new("Synthetic M365", "Manual, not connected", ["Microsoft365DoD"])
            { ServiceModel = "SoftwareAsAService", ManagementArrangement = management }, "service-create", "provider", default);
            _citation = new(Guid.NewGuid(), Guid.NewGuid(), "manual-service.json", "service:1", "Synthetic service duties");
            await using (var seed = new AtoCopilotContext(_options))
            {
                seed.Add(new CspPackage { Id = _citation.PackageId, ProviderId = _provider, OfferingId = offering.OfferingId,
                    Name = "Service-only source", IdempotencyKey = "service-only-source" });
                seed.Add(new CspPackageEntry { Id = _citation.ArtifactId, PackageId = _citation.PackageId,
                    ArchivePath = _citation.ArchivePath, SegmentsJson = ProviderAuthorizationStore.Json(new[]
                    {
                        new CspPackageSourceSegment("service-segment", "entry", _citation.ArtifactId.ToString(),
                            _citation.ArchivePath, _citation.Locator, _citation.Quote)
                    }) });
                await seed.SaveChangesAsync();
            }
            boundary = await authorizations.CreateBoundaryAsync(offering.OfferingId,
                new(offering.Revision, null, "Service boundary", "Only the named service instance", ["Collaboration"], [],
                    [serviceScope], [], [providerDuty], [customerDuty], [_citation]), "service-boundary", "provider", default);
            serviceHosting = await hosting.CreateScopeAsync(offering.OfferingId,
                new(boundary.OfferingRevision, null, "Named service relationship", [serviceScope], [], [_citation]),
                "service-hosting", "provider", default);
            assignment = await hosting.AssignAsync(offering.OfferingId,
                new(_tenant, _system, serviceHosting.Snapshot.RevisionId, [serviceScope], []), "service-allocation", "provider", default);

            var crossAzure = () => hosting.AssignAsync(azureOffering.Id,
                new(_tenant, _system, azureOffering.CurrentHostingScopeRevisionId!.Value, [serviceScope], []),
                "invalid-service-on-azure", "provider", default);
            var crossService = () => hosting.AssignAsync(offering.OfferingId,
                new(_tenant, _system, serviceHosting.Snapshot.RevisionId, [Scope], []), "invalid-azure-on-service", "provider", default);
            await crossAzure.Should().ThrowAsync<ArgumentException>();
            await crossService.Should().ThrowAsync<ArgumentException>();

            offering = await authorizations.GetAsync(offering.OfferingId, default);
            var draft = await authorizations.CreateDecisionAsync(offering.OfferingId,
                new(offering.Revision, boundary.BoundaryRevisionId, [], "ProviderDecision", "SYNTHETIC service reference",
                    "Synthetic authority", "ATO", "2025-04-17", null, null, "NoExpiryStated",
                    "Named service instance only", [], [_citation]) { IssuingAuthorityType = "organization" },
                "service-decision", "provider", default);
            decision = await authorizations.RecordDecisionAsync(offering.OfferingId, draft.RecordId,
                new(draft.Revision, draft.RevisionId, draft.SnapshotHash, "Reviewed exact synthetic source"), "provider", default);
        }
        await using (var seed = new AtoCopilotContext(_options))
        {
            _offering = await seed.Set<ProviderOffering>().SingleAsync(x => x.Id == offering.OfferingId);
            _boundary = await seed.Set<ProviderBoundaryRevision>().SingleAsync(x => x.Id == boundary.BoundaryRevisionId);
            _decision = await seed.Set<ProviderAuthorizationRevision>().SingleAsync(x => x.Id == decision.RevisionId);
            _assignment = await seed.Set<ProviderHostingAssignment>().SingleAsync(x => x.Id == assignment.AssignmentId);
            (await seed.SystemRoleAssignments.SingleAsync()).Role = OrganizationRole.MissionOwner;
            await seed.SaveChangesAsync();
        }
        var (capabilityId, release) = await SeedPublishedAsync();
        var missionTenant = new TenantContext(_tenant) { PersonId = _person, IsWorkspaceRequest = true };
        using var missionScope = _accessor.Push(missionTenant);
        await using var db = new AtoCopilotContext(_options, _accessor);
        var mission = Service(db, missionTenant);

        // Act: actual scoped Mission Owner association and adoption, without an implicit customer confirmation.
        var relationship = await mission.AssociateAsync(_system, new(assignment.AssignmentId, assignment.Revision), "mission-owner", default);
        var applicability = (await mission.ApplicableAsync(_system, 1, 10, assignment.AssignmentId,
            offering.OfferingId, "Microsoft365DoD", capabilityId, release.Id, default)).Items.Single();
        applicability.ReasonCodes.Should().BeEmpty("the exact reviewed service scope must remain eligible");
        applicability.CanProposeAdoption.Should().BeTrue();
        var adoptionRequest = new AdoptProviderCapabilityRequest(assignment.AssignmentId, assignment.Revision, capabilityId,
            release.Id, applicability.Applicability.SnapshotHash, applicability.ApplicabilityPreviewHash);
        var adopted = await mission.AdoptAsync(_system, adoptionRequest, "mission-owner", default, "service-adoption");
        var replay = await mission.AdoptAsync(_system, adoptionRequest, "mission-owner", default, "service-adoption");

        // Assert
        relationship.AssignedScopes.Single().Should().Be(serviceScope);
        ProviderAuthorizationStore.Json(relationship.AssignedScopes).Should().NotContain("subscriptionId").And.NotContain("directoryTenantId");
        boundary.ProviderResponsibilities.Should().ContainSingle().Which.Should().Be(providerDuty);
        boundary.CustomerResponsibilities.Should().ContainSingle().Which.Should().Be(customerDuty);
        offering.ManagementArrangement.Should().Be(management);
        adopted.ReleaseId.Should().Be(release.Id);
        replay.AdoptionSnapshotId.Should().Be(adopted.AdoptionSnapshotId);
        adopted.Subscription.Responsibilities.CanConfirm.Should().BeFalse();
        (await db.CapabilitySubscriptions.SingleAsync()).CurrentAdoptionSnapshotId.Should().Be(adopted.AdoptionSnapshotId);
        (await db.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(0);
        (await db.Set<CapabilityAdoptionSnapshot>().CountAsync()).Should().Be(1);
        (await db.Set<ProviderHostingAssignment>().CountAsync()).Should().Be(0, "provider-owned allocation tables remain hidden from the Mission Owner context");
        await using var verify = new AtoCopilotContext(_options);
        (await verify.Set<ProviderHostingAssignment>().IgnoreQueryFilters().CountAsync(x => x.ProviderId == _provider
            && x.TargetTenantId == _tenant && x.SystemId == _system)).Should().Be(2);
        (await verify.Set<ProviderHostingAssignment>().IgnoreQueryFilters().SingleAsync(x => x.OfferingId == azureOffering.Id))
            .AssignedScopesJson.Should().Be(ProviderAuthorizationStore.Json(new[] { Scope }));
    }
}
