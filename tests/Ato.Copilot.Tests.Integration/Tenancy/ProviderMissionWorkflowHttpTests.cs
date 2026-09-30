using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Agents.Services.PackageImports;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using FluentAssertions;
using Ato.Copilot.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using PackageStatus = Ato.Copilot.Core.Interfaces.PackageImports.PackageStatus;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// Feature 079 US2 acceptance, deliberately red at the first disconnected production step.
/// Only identity, membership and system prerequisites are seeded. Offering, source receipt,
/// review, publication, allocation, association and adoption must come from production HTTP.
/// Reuses the structured JSON input contract from CspPackageLifecycleHttpTests, not a mock
/// of the analyzer, publication service or mission service. All source content is synthetic.
/// </summary>
public sealed class ProviderMissionWorkflowHttpTests : IClassFixture<WorkspaceMembershipFactory>, IDisposable
{
    private readonly WebApplicationFactory<McpProgram> factory;
    private readonly ITestOutputHelper output;
    private readonly string exportRoot = Path.Combine(AppContext.BaseDirectory, "TestResults", $"f079-acceptance-{Guid.NewGuid():N}");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid DirectoryId = Guid.Parse("07900000-0000-0000-0000-000000000001");
    private static readonly Guid SubscriptionId = Guid.Parse("07900000-0000-0000-0000-000000000002");
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private static readonly Guid Reviewer = Guid.Parse("07900000-0000-0000-0000-000000000005");
    private static readonly Guid Unassigned = Guid.Parse("07900000-0000-0000-0000-000000000006");
    private static readonly Guid Foreign = Guid.Parse("07900000-0000-0000-0000-000000000007");
    private const string Source = """
        {"components":[{
            "id":"synthetic-component","type":"service","name":"SYNTHETIC F079 audit service",
            "description":"Synthetic provider records administrative events."}],
          "capabilities":[{
            "id":"synthetic-capability","name":"SYNTHETIC F079 event logging",
            "description":"Synthetic provider records administrative events.",
            "componentIds":["synthetic-component"],"controlId":"AU-2","responsibility":"Shared"}],
          "authorizationReferences":[{
            "reference":"SYNTHETIC F079 ATO: audit service only; no expiry stated; customer must review events.",
            "issuer":"SYNTHETIC Review Organization","issuedAt":"2025-01-01"}]}
        """;

    public ProviderMissionWorkflowHttpTests(WorkspaceMembershipFactory factory, ITestOutputHelper output)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            // Keep production receipt/export workers but prevent unrelated monitoring, cache warmup,
            // archival and retention tasks from calling external services during this local slice.
            foreach (var service in services.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType != typeof(TenancySeedHostedService)
                && s.ImplementationType != typeof(Ato.Copilot.Mcp.Services.CspPackageWorker)
                && s.ImplementationType != typeof(Ato.Copilot.Agents.Compliance.Services.SspExportBackgroundService)).ToArray())
                services.Remove(service);
            services.RemoveAll<ICspPackageAnalyzer>();
            services.AddSingleton<ICspPackageAnalyzer>(sp =>
                new CspPackageAnalyzer(sp.GetRequiredService<ILogger<CspPackageAnalyzer>>(), chatClient: null));
            services.PostConfigure<ExportSettings>(settings => settings.DataPath = exportRoot);
        }));
        this.output = output;
    }

    [Fact]
    public async Task WorkingProfilePreview_CannotBePromotedToFinalExport()
    {
        // Arrange
        var actor = Guid.NewGuid();
        var system = Guid.NewGuid().ToString();
        await using (var previewSeed = factory.Services.CreateAsyncScope())
        {
            var db = previewSeed.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var person = new Person { TenantId = Tenant, DisplayName = "Synthetic preview author", Email = $"{actor:N}@example.invalid" };
            db.Persons.Add(person);
            db.RegisteredSystems.Add(new() { Id = system, TenantId = Tenant, Name = "Synthetic working-preview system" });
            db.OrganizationMemberships.Add(new()
            {
                TenantId = Tenant, DirectoryTenantId = DirectoryId, ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture"
            });
            db.SystemRoleAssignments.Add(new()
            {
                TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = system, Role = OrganizationRole.MissionOwner
            });
            await db.SaveChangesAsync();
        }
        using var mission = Client(actor);
        var root = $"/api/dashboard/systems/{system}";
        var preview = await Post<JsonElement>(mission, root + "/documents/ssp/preview", new { }, envelope: false);

        // Act
        var rejected = await Post<JsonElement>(mission, root + "/exports",
            new { format = "json", sourcePreviewId = preview.GetProperty("previewId").GetGuid() },
            HttpStatusCode.BadRequest, envelope: false);

        // Assert
        rejected.GetProperty("error").GetString().Should().Contain("Working profile previews are review-only");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().SspExports
            .AnyAsync(row => row.SystemId == system && row.Status != "Preview")).Should().BeFalse();
    }

    [Fact]
    public async Task MissionRelationshipReview_PersistsSeparateBoundaryWithoutChangingSiblingOrAuthorization()
    {
        // Arrange
        Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING").Should().BeNullOrEmpty(
            "relationship review acceptance uses isolated local SQLite, never a live database");
        using var provider = Client(Guid.Parse("07900000-0000-0000-0000-000000000003"), csp: true);
        var actor = Guid.NewGuid();
        var system = Guid.NewGuid().ToString();
        await using (var seedScope = factory.Services.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            seed.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
            var person = new Person { TenantId = Tenant, DisplayName = "Synthetic scope reviewer",
                Email = $"{actor:N}@example.invalid" };
            seed.AddRange(person, new RegisteredSystem { Id = system, TenantId = Tenant, Name = "Synthetic review mission" });
            seed.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = DirectoryId,
                ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
            seed.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id,
                RegisteredSystemId = system, Role = OrganizationRole.MissionOwner });
            await seed.SaveChangesAsync();
        }
        using var mission = Client(actor);
        var missionRoot = $"/api/dashboard/systems/{system}/provider-relationships";
        var scopes = new[] { new ProviderAzureScope("AzureCloud", DirectoryId, SubscriptionId,
            $"/subscriptions/{SubscriptionId:D}/resourceGroups/synthetic-review") };
        var relationships = new List<MissionProviderRelationshipResponse>();
        var offerings = new List<ProviderOfferingResponse>();
        foreach (var name in new[] { "Selected", "Sibling" })
        {
            var offering = await Post<ProviderOfferingResponse>(provider, "/api/csp/offerings",
                new CreateProviderOfferingRequest($"SYNTHETIC {name} review", "Local acceptance only.", ["AzureCloud"]),
                HttpStatusCode.Created);
            var root = $"/api/csp/offerings/{offering.OfferingId:D}";
            var hosting = await Post<ProviderHostingScopeResponse>(provider, root + "/hosting-scope-revisions",
                new CreateProviderHostingScopeRequest(offering.Revision, null, "Synthetic hosting", scopes, [], []),
                HttpStatusCode.Created);
            var assignment = await Post<ProviderHostingAssignmentResponse>(provider, root + "/hosting-assignments",
                new CreateProviderHostingAssignmentRequest(Tenant, system, hosting.Snapshot.RevisionId, scopes, []),
                HttpStatusCode.Created);
            relationships.Add(await Post<MissionProviderRelationshipResponse>(mission, missionRoot,
                new CreateMissionProviderRelationshipRequest(assignment.AssignmentId, assignment.Revision)));
            offerings.Add(await Get<ProviderOfferingResponse>(provider, root));
        }
        var selected = relationships[0];
        var sibling = relationships[1];
        var before = (await Get<JsonElement>(mission, missionRoot)).GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("relationshipId").GetGuid() == selected.RelationshipId);
        before.GetProperty("canAssociate").GetBoolean().Should().BeFalse();
        before.GetProperty("canReviewRelationship").GetBoolean().Should().BeTrue();
        before.GetProperty("canReviewCoveredScope").GetBoolean().Should().BeFalse();
        before.GetProperty("reviewRequired").GetBoolean().Should().BeTrue();
        var relationshipRoot = $"{missionRoot}/{selected.RelationshipId:D}";

        // Act
        var preview = await Post<MissionProviderRelationshipPreviewResponse>(mission, relationshipRoot + "/previews",
            new PreviewMissionProviderRelationshipRequest(selected.Revision, selected.AssignmentRevision,
                "SeparateBoundaryConsumer", null, null, [], "Mission retains its separate authorization boundary."));
        preview.CanReview.Should().BeTrue();
        await Post<MissionProviderRelationshipResponse>(mission, relationshipRoot + "/review",
            new ReviewMissionProviderRelationshipRequest(preview.Revision, preview.PreviewId, preview.PreviewHash,
                "Reviewed this exact hosting relationship; not a mission authorization decision."));
        using var refreshedClient = Client(actor);
        var refreshed = (await Get<Page<MissionProviderRelationshipResponse>>(refreshedClient, missionRoot)).Items;

        // Assert
        var persisted = refreshed.Single(x => x.RelationshipId == selected.RelationshipId);
        persisted.State.Should().Be("SeparateBoundaryConsumer");
        persisted.ReviewRequired.Should().BeFalse();
        persisted.ReviewedAt.Should().NotBeNull();
        persisted.AuthorizationRevisionId.Should().BeNull();
        persisted.BoundaryRevisionId.Should().BeNull();
        refreshed.Single(x => x.RelationshipId == sibling.RelationshipId).Should().BeEquivalentTo(sibling);
        foreach (var offering in offerings)
            (await Get<ProviderOfferingResponse>(provider, $"/api/csp/offerings/{offering.OfferingId:D}"))
                .Should().BeEquivalentTo(offering);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.AuthorizationDecisions.IgnoreQueryFilters().CountAsync(x => x.RegisteredSystemId == system))
            .Should().Be(0);
        (await db.Set<ProviderAuthorizationRecord>().IgnoreQueryFilters()
            .CountAsync(x => offerings.Select(o => o.OfferingId).Contains(x.OfferingId))).Should().Be(0);
        (await db.Set<MissionProviderRelationshipReview>().IgnoreQueryFilters()
            .SingleAsync(x => x.Id == selected.RelationshipId)).ReviewRequired.Should().BeFalse();
    }

    [Fact]
    public async Task ReviewedProviderSource_MissionOwnerAdoption_ProducesActualOscalWithoutInventingAuthorization()
    {
        // Arrange: refuse the factory's optional external database mode before starting the host.
        Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING").Should().BeNullOrEmpty(
            "this acceptance must use the factory's isolated local SQLite, never a live database");
        using var provider = Client(Guid.Parse("07900000-0000-0000-0000-000000000003"), csp: true);
        var (actor, system) = await SeedMissionAsync();
        await SeedDocumentInventoryPrerequisiteAsync(system);
        using var mission = Client(actor);
        var missionRoot = $"/api/dashboard/systems/{system}";
        await AuthorDocumentPrerequisitesAsync(mission, missionRoot);
        var scopes = new[]
        {
            new ProviderAzureScope("AzureCloud", DirectoryId, SubscriptionId,
                $"/subscriptions/{SubscriptionId:D}/resourceGroups/synthetic-f079")
        };

        // Act: publish the reviewed service before allocating any customer.
        var offering = await Post<ProviderOfferingResponse>(provider, "/api/csp/offerings",
            new CreateProviderOfferingRequest("SYNTHETIC F079 offering", "Local acceptance only; not a real authorization.", ["AzureCloud"]),
            HttpStatusCode.Created);
        var root = $"/api/csp/offerings/{offering.OfferingId:D}";
        var boundary = await Post<ProviderBoundaryResponse>(provider, root + "/boundary-revisions",
            new CreateProviderBoundaryRequest(offering.Revision, null, "SYNTHETIC boundary", "Synthetic audit service only",
                ["Audit"], [], scopes, [], ["Record administrative events"], ["Review events and retain customer evidence"], []),
            HttpStatusCode.Created);
        var hosting = await Post<ProviderHostingScopeResponse>(provider, root + "/hosting-scope-revisions",
            new CreateProviderHostingScopeRequest(boundary.OfferingRevision, null, "SYNTHETIC hosting", scopes, [], []),
            HttpStatusCode.Created);
        offering = await Get<ProviderOfferingResponse>(provider, root);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("SYNTHETIC F079 structured source"), "name");
        form.Add(new StringContent(boundary.BoundaryRevisionId.ToString("D")), "boundaryRevisionId");
        form.Add(new StringContent(offering.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedOfferingRevision");
        var sourceContent = new ByteArrayContent(Encoding.UTF8.GetBytes(Source));
        sourceContent.Headers.ContentType = new("application/json");
        form.Add(sourceContent, "files", "synthetic-f079.json");
        using var upload = new HttpRequestMessage(HttpMethod.Post, root + "/package-versions") { Content = form };
        upload.Headers.Add("Idempotency-Key", "f079-source-receipt");
        var receipt = await Read<ProviderPackageReceiptResponse>(await provider.SendAsync(upload), HttpStatusCode.Accepted);
        receipt.PackageVersion.OfferingId.Should().Be(offering.OfferingId);
        receipt.PackageVersion.BoundaryRevisionId.Should().Be(boundary.BoundaryRevisionId);
        var packageRoot = $"/api/csp/package-imports/{receipt.Package.PackageId:D}";
        var status = await WaitForAnalysis(provider, packageRoot);
        status.ProcessingState.Should().Be("ReadyForReview", JsonSerializer.Serialize(status, Json));
        status.PublicationState.Should().Be("Unpublished");
        var candidates = (await Get<Page<PackageCandidateResponse>>(provider, packageRoot + "/candidates")).Items;
        candidates.Select(c => c.Type).Should().BeEquivalentTo(["Component", "Capability", "Responsibility", "AuthorizationReference"]);
        candidates.Should().OnlyContain(c => c.ReviewState == "NeedsReview" && c.Citations.Count > 0);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(0, "upload does not publish");
        }

        // Human review is an actual version-checked edit, not a seeded Reviewed flag.
        foreach (var candidate in candidates)
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, packageRoot + $"/candidates/{candidate.CandidateId:D}")
            {
                Content = JsonContent.Create(new EditPackageCandidateRequest(candidate.Revision, candidate.Name,
                    candidate.Description, candidate.ComponentType, "Unclassified", "Audit", candidate.ControlDuties,
                    candidate.ContributorIds, "Reviewed", "Reviewed synthetic source for acceptance", null,
                    candidate.AuthorizationReference))
            };
            var reviewed = await Read<PackageCandidateResponse>(await provider.SendAsync(request), HttpStatusCode.OK);
            reviewed.ReviewState.Should().Be("Reviewed");
        }
        candidates = (await Get<Page<PackageCandidateResponse>>(provider, packageRoot + "/candidates")).Items;
        var reference = candidates.Single(c => c.Type == "AuthorizationReference");
        offering = await Get<ProviderOfferingResponse>(provider, root);
        // AuthorizationReference is an extracted reference, not an AuthorizationDecisionClaim.
        // Use the supported human-entered decision route backed by the actual retained citations;
        // never relabel the candidate or seed a reviewed decision to bypass provenance checks.
        var draft = await Post<ProviderDecisionResponse>(provider, root + "/authorization-records",
            new CreateProviderDecisionRequest(offering.Revision, boundary.BoundaryRevisionId, [],
                "ProviderDecision", reference.AuthorizationReference!.Reference, reference.AuthorizationReference.Issuer,
                "ATO", "2025-01-01", null, null, "NoExpiryStated", "Synthetic audit service only",
                ["Customer must review events"],
                reference.Citations.Select(c => new ProviderCitation(receipt.Package.PackageId, c.ArtifactId,
                    c.ArchivePath, c.Locator, c.Quote)).ToArray()) { IssuingAuthorityType = "organization" },
            HttpStatusCode.Created);
        var recorded = await Post<ProviderDecisionResponse>(provider, root + $"/authorization-records/{draft.RecordId:D}/record",
            new RecordProviderDecisionRequest(draft.Revision, draft.RevisionId, draft.SnapshotHash,
                "Human reviewed retained synthetic source; this records metadata, not an actual authority decision"));
        recorded.MetadataReviewState.Should().Be("Recorded");
        var selected = candidates.Where(c => c.Type is "Component" or "Capability").ToArray();
        var changes = new List<ProviderImpactChange>();
        foreach (var candidate in selected)
        {
            var option = await Get<JsonElement>(provider, root + $"/impact-options/{candidate.Type}/{candidate.CandidateId:D}");
            changes.Add(option.GetProperty("change").Deserialize<ProviderImpactChange>(Json)!);
        }
        offering = await Get<ProviderOfferingResponse>(provider, root);
        var impact = await Post<ProviderImpactPreviewResponse>(provider, root + "/impact-previews",
            new CreateProviderImpactPreviewRequest(offering.Revision, changes, [recorded.RevisionId], boundary.BoundaryRevisionId,
                hosting.Snapshot.RevisionId, [receipt.PackageVersion.PackageVersionId]), HttpStatusCode.Created);
        impact.Blockers.Should().BeEmpty("actual impact preview: {0}", JsonSerializer.Serialize(impact, Json));
        await Post<ProviderImpactReviewResponse>(provider, root + $"/impact-reviews/{impact.ReviewId:D}/review",
            new ReviewProviderImpactRequest(impact.Revision, impact.PreviewId, impact.PreviewHash,
                "AcceptForPublication", "Human reviewed exact synthetic offering and shared duties"));
        status = await Get<PackageStatus>(provider, packageRoot);
        var preview = await Post<PackagePreviewResponse>(provider, packageRoot + "/approval-previews",
            new PackagePreviewRequest(status.Revision, selected.Select(c => new PackageSelection(c.CandidateId, c.Revision)).ToArray(),
                [impact.ReviewId]));
        preview.Blockers.Should().BeEmpty("actual approval preview: {0}", JsonSerializer.Serialize(preview, Json));
        var recoveredReview = await Get<PackageReviewStateResponse>(provider, packageRoot + "/review-state");
        recoveredReview.Preview.Should().NotBeNull();
        recoveredReview.Preview!.PreviewHash.Should().Be(preview.PreviewHash);
        recoveredReview.PreviewIsStale.Should().BeFalse("recovery must validate the retained exact impact-review context");
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        await Post<PackagePreviewResponse>(provider, packageRoot + "/approve", decision);
        var publication = await Post<PackagePublicationResponse>(provider, packageRoot + "/publish", decision, key: "f079-publication");
        publication.PublicationState.Should().Be("Published");
        var replay = await Post<PackagePublicationResponse>(provider, packageRoot + "/publish", decision, key: "f079-publication");
        replay.Records.Should().BeEquivalentTo(publication.Records);
        var capability = publication.Records.Single(r => r.Type == "Capability");
        capability.ReleaseId.Should().NotBeNull();
        await AssertNoAdoptionOrInheritance(system);

        var publishedOffering = await Get<ProviderOfferingResponse>(provider, root);
        var assignment = await Post<ProviderHostingAssignmentResponse>(provider, root + "/hosting-assignments",
            new CreateProviderHostingAssignmentRequest(Tenant, system, hosting.Snapshot.RevisionId, scopes, []),
            HttpStatusCode.Created);
        var relationships = await Get<JsonElement>(mission, missionRoot + "/provider-relationships");
        relationships.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("relationshipId").ValueKind.Should().Be(JsonValueKind.Null,
                "provider allocation must not implicitly associate a mission");
        await AssertNoAdoptionOrInheritance(system);

        // Explicit ordinary Mission Owner handoff: no CSP role and no impersonation.
        var relationship = await Post<MissionProviderRelationshipResponse>(mission, missionRoot + "/provider-relationships",
            new CreateMissionProviderRelationshipRequest(assignment.AssignmentId, assignment.Revision));
        relationship.RelationshipId.Should().NotBeNull();
        await AssertNoAdoptionOrInheritance(system);
        var applicable = (await Get<Page<ApplicableProviderCapabilityResponse>>(mission,
            missionRoot + "/applicable-provider-capabilities")).Items.Single();
        var releaseId = capability.ReleaseId!.Value;
        applicable.ReleaseId.Should().Be(releaseId);
        applicable.ReasonCodes.Should().BeEmpty("actual applicability: {0}", JsonSerializer.Serialize(applicable, Json));
        applicable.CanProposeAdoption.Should().BeTrue("customer allocation must not stale the published definition");
        (await Get<ProviderOfferingResponse>(provider, root)).Revision.Should().Be(publishedOffering.Revision);
        var adoptionRequest = new AdoptProviderCapabilityRequest(assignment.AssignmentId, assignment.Revision, capability.RecordId,
            releaseId, applicable.Applicability.SnapshotHash, applicable.ApplicabilityPreviewHash);
        using var unassigned = Client(Unassigned);
        using var foreign = Client(Foreign, tenant: WorkspaceMembershipFactory.TenantBId);
        foreach (var deniedClient in new[] { unassigned, foreign })
        {
            await Post<JsonElement>(deniedClient, missionRoot + "/provider-capability-adoptions",
                adoptionRequest, HttpStatusCode.NotFound, envelope: false);
            await Read<JsonElement>(await deniedClient.GetAsync(missionRoot + "/documents/ssp/preview"),
                HttpStatusCode.NotFound, envelope: false);
        }
        await AssertNoAdoptionOrInheritance(system);
        var adoption = await Post<ProviderCapabilityAdoptionResponse>(mission, missionRoot + "/provider-capability-adoptions",
            adoptionRequest);
        adoption.ReleaseId.Should().Be(releaseId);
        adoption.Subscription.Responsibilities.Items.Should().OnlyContain(i => i.ConfirmedAt == null,
            "Mission Owner adoption cannot silently confirm customer responsibilities");

        // A later customer's allocation must leave the first mission's release/context pins readable.
        var otherSystem = Guid.NewGuid().ToString();
        CapabilityAdoptionSnapshot retainedPin;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            retainedPin = await db.Set<CapabilityAdoptionSnapshot>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.Id == adoption.AdoptionSnapshotId);
            db.RegisteredSystems.Add(new()
            {
                Id = otherSystem, TenantId = Tenant, Name = "SYNTHETIC later customer mission"
            });
            await db.SaveChangesAsync();
        }
        await Post<ProviderHostingAssignmentResponse>(provider, root + "/hosting-assignments",
            new CreateProviderHostingAssignmentRequest(Tenant, otherSystem, hosting.Snapshot.RevisionId, scopes, []),
            HttpStatusCode.Created);
        var stillApplicable = (await Get<Page<ApplicableProviderCapabilityResponse>>(mission,
            missionRoot + "/applicable-provider-capabilities")).Items.Single();
        stillApplicable.ReleaseId.Should().Be(adoption.ReleaseId);
        stillApplicable.Applicability.SnapshotHash.Should().Be(adoption.ContextSnapshotHash);
        stillApplicable.ReasonCodes.Should().BeEmpty();
        stillApplicable.CanProposeAdoption.Should().BeTrue();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var pin = await db.Set<CapabilityAdoptionSnapshot>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.Id == adoption.AdoptionSnapshotId);
            pin.Should().BeEquivalentTo(retainedPin);
            (await db.CapabilitySubscriptions.IgnoreQueryFilters().SingleAsync(x => x.Id == adoption.Subscription.Id))
                .CurrentAdoptionSnapshotId.Should().Be(adoption.AdoptionSnapshotId);
        }

        // A distinct assigned ISSM performs explicit responsibility review; never elevate the MO.
        using var reviewer = Client(Reviewer);
        var responsibilitiesPath = missionRoot + "/capability-subscriptions/responsibilities";
        var responsibilities = await Get<CapabilityResponsibilityResponse>(reviewer, responsibilitiesPath, envelope: false);
        var duty = responsibilities.Items.Single(i => i.CapabilityId == capability.RecordId && i.ControlId == "AU-2");
        var confirmation = new ConfirmCapabilityResponsibilitiesRequest(responsibilities.BaselineId!,
            duty.SourceRevision, duty.ReviewRevision,
            [new("AU-2", "Shared", "SYNTHETIC F079 offering", "Review events and retain customer evidence")],
            true, true, "ISSM reviewed synthetic provider coverage and remaining customer duties");
        var confirmationPath = missionRoot + $"/capability-subscriptions/{capability.RecordId:D}/responsibilities";
        using (var denied = await mission.PutAsJsonAsync(confirmationPath, confirmation))
            await Read<JsonElement>(denied, HttpStatusCode.Forbidden, envelope: false);
        var confirmed = await Read<CapabilityResponsibilityResponse>(
            await reviewer.PutAsJsonAsync(confirmationPath, confirmation), HttpStatusCode.OK, envelope: false);
        confirmed.Items.Should().Contain(i => i.CapabilityId == capability.RecordId && i.ConfirmedAt != null
            && i.Allocation!.CustomerResponsibility == "Review events and retain customer evidence");

        // Retain real provider evidence, then explicitly grant only the reviewed summary.
        offering = await Get<ProviderOfferingResponse>(provider, root);
        var finding = await Post<ProviderFindingResponse>(provider, root + "/findings",
            new CreateProviderFindingRequest(offering.Revision, "SYNTHETIC audit evidence",
                "Synthetic observation; customer review remains required", "Low", ["AU-2"], []), HttpStatusCode.Created);
        using var evidenceForm = new MultipartFormDataContent();
        evidenceForm.Add(new StringContent(finding.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedFindingRevision");
        evidenceForm.Add(new StringContent("Synthetic audit event extract; not proof of mission compliance"), "description");
        var evidenceBytes = new ByteArrayContent(Encoding.UTF8.GetBytes("SYNTHETIC PRIVATE audit-event source"));
        evidenceBytes.Headers.ContentType = new("text/plain");
        evidenceForm.Add(evidenceBytes, "file", "synthetic-private-events.txt");
        using var evidenceUpload = new HttpRequestMessage(HttpMethod.Post, root + $"/findings/{finding.FindingId:D}/evidence")
        { Content = evidenceForm };
        evidenceUpload.Headers.Add("Idempotency-Key", "f079-evidence");
        var evidence = await Read<ProviderFindingEvidenceResponse>(await provider.SendAsync(evidenceUpload), HttpStatusCode.Created);
        var share = await Post<ProviderEvidenceShareResponse>(provider, root + $"/evidence/{evidence.EvidenceId:D}/shares",
            new ApproveProviderEvidenceShareRequest(assignment.AssignmentId, assignment.Revision, evidence.EvidenceRevision,
                "SYNTHETIC approved event summary; mission must review events"), HttpStatusCode.Created);
        var missionEvidence = await Get<Page<ProviderEvidenceShareResponse>>(mission, missionRoot + "/provider-evidence");
        missionEvidence.Items.Should().ContainSingle().Which.ShareId.Should().Be(share.ShareId);
        using (var privateSource = await mission.GetAsync(packageRoot + $"/artifacts/{reference.Citations[0].ArtifactId:D}/content"))
            await Read<JsonElement>(privateSource, HttpStatusCode.Forbidden, envelope: false);
        using (var privateEvidence = await mission.GetAsync(root + $"/findings/{finding.FindingId:D}/evidence/{evidence.EvidenceId:D}/content"))
            await Read<JsonElement>(privateEvidence, HttpStatusCode.Forbidden, envelope: false);
        using var summaryResponse = await mission.GetAsync(missionRoot + $"/provider-evidence/{share.ShareId:D}/content");
        var approvedSummaryBytes = await summaryResponse.Content.ReadAsByteArrayAsync();
        var summary = await Read<JsonElement>(summaryResponse, HttpStatusCode.OK, envelope: false);
        summary.GetProperty("summary").GetString().Should().Be(share.Summary);

        // Assert: actual production OSCAL generation, never a saved synthetic export body.
        var document = await Post<JsonElement>(mission, missionRoot + "/documents/ssp/preview", new { },
            key: "f079-retained-preview", envelope: false);
        var replayedDocument = await Post<JsonElement>(mission, missionRoot + "/documents/ssp/preview", new { },
            key: "f079-retained-preview", envelope: false);
        replayedDocument.GetProperty("previewId").GetGuid().Should().Be(document.GetProperty("previewId").GetGuid());
        replayedDocument.GetProperty("content").GetString().Should().Be(document.GetProperty("content").GetString());
        replayedDocument.GetProperty("contentHash").GetString().Should().Be(document.GetProperty("contentHash").GetString());
        var content = document.GetProperty("content").GetString()!;
        using var previewOscal = JsonDocument.Parse(content);
        previewOscal.RootElement.GetProperty("system-security-plan").GetProperty("system-characteristics")
            .GetProperty("system-name").GetString().Should().Be("SYNTHETIC F079 mission");
        // Retained previews are explicitly working-data review artifacts, not final approved sources.
        // Exercise that guard, then generate through the separate production final-export path.
        document.GetProperty("canGenerate").GetBoolean().Should().BeFalse();
        var rejected = await Post<JsonElement>(mission, missionRoot + "/exports",
            new { format = "json", sourcePreviewId = document.GetProperty("previewId").GetGuid() },
            HttpStatusCode.BadRequest, key: "f079-retained-export", envelope: false);
        rejected.GetProperty("errorCode").GetString().Should().Be("VALIDATION_ERROR");
        rejected.GetProperty("error").GetString().Should().Contain("Working profile previews are review-only");
        var exported = await Read<JsonElement>(await mission.PostAsJsonAsync(missionRoot + "/exports", new { format = "json" }),
            HttpStatusCode.Accepted, envelope: false);
        var exportPath = missionRoot + $"/exports/{exported.GetProperty("exportId").GetGuid():D}";
        var exportTimer = Stopwatch.StartNew();
        JsonElement export;
        do
        {
            export = await Get<JsonElement>(mission, exportPath, envelope: false);
            if (export.GetProperty("status").GetString() is "Completed" or "Failed") break;
            await Task.Delay(100);
        } while (exportTimer.Elapsed < TimeSpan.FromSeconds(45));
        if (export.GetProperty("status").GetString() == "Failed")
        {
            // Diagnose the genuine terminal artifact failure with the same bundled production
            // validator; never substitute a schema stub or write a successful export record.
            await using var diagnostics = factory.Services.CreateAsyncScope();
            var schema = diagnostics.ServiceProvider.GetRequiredService<IOscalSchemaValidationService>();
            output.WriteLine("Working-preview schema diagnostics (not the final artifact): {0}",
                JsonSerializer.Serialize(await schema.ValidateAsync(content, "ssp"), Json));
            using var unavailable = await mission.GetAsync(exportPath + "/download");
            output.WriteLine("GET {0}/download -> {1}\n{2}", exportPath, (int)unavailable.StatusCode,
                await unavailable.Content.ReadAsStringAsync());
        }
        export.GetProperty("status").GetString().Should().Be("Completed", "actual export state: {0}", export);
        var finalManifest = export.GetProperty("sourceManifest").Deserialize<DocumentSourceManifest>(Json)!;
        finalManifest.HasWorkingProfileSources.Should().BeFalse();
        finalManifest.Profiles.Should().ContainSingle().Which.Kind.Should().Be("ApprovedProfile");
        finalManifest.Evidence.Should().ContainSingle().Which.ShareId.Should().Be(share.ShareId);
        using (var download = await mission.GetAsync(exportPath + "/download"))
        {
            var bytes = await download.Content.ReadAsByteArrayAsync();
            download.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0}/download -> {1}: {2}",
                exportPath, (int)download.StatusCode, Encoding.UTF8.GetString(bytes));
            content = Encoding.UTF8.GetString(bytes);
            SHA256.HashData(bytes).Should().Equal(Convert.FromHexString(export.GetProperty("contentHash").GetString()!),
                "the final artifact digest must match its downloaded bytes, not a working preview");
            await using var exportScope = factory.Services.CreateAsyncScope();
            var exportDb = exportScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var retained = await exportDb.SspExports.SingleAsync(row => row.Id == exported.GetProperty("exportId").GetGuid());
            retained.SourcePreviewId.Should().BeNull("working profile previews are never final-export sources");
            retained.SourceTenantId.Should().Be(Tenant);
            retained.RequestedPersonId.Should().Be(await exportDb.OrganizationMemberships
                .Where(member => member.TenantId == Tenant && member.DirectoryTenantId == DirectoryId && member.ObjectId == actor)
                .Select(member => member.PersonId).SingleAsync());
            var manifest = JsonSerializer.Deserialize<DocumentSourceManifest>(retained.SourceManifestJson!)!;
            manifest.HasWorkingProfileSources.Should().BeFalse();
            manifest.Evidence.Should().ContainSingle(pin => pin.ShareId == share.ShareId
                && pin.ContentHash == share.ContentHash);
            manifest.Responsibilities.Should().Contain(pin => pin.CapabilityId == capability.RecordId
                && pin.ReviewedSourceRevision == duty.SourceRevision);
            SHA256.HashData(bytes).Should().Equal(Convert.FromHexString(retained.ContentHash!),
                "the digest must match exactly regardless of hexadecimal letter casing");
            output.WriteLine("Terminal artifact verified: exportId={0}; bytes={1}; SHA256={2}",
                exported.GetProperty("exportId").GetGuid(), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        using var oscal = JsonDocument.Parse(content);
        var ssp = oscal.RootElement.GetProperty("system-security-plan");
        ssp.TryGetProperty("back-matter", out var backMatter).Should().BeTrue(
            "the final approved-source export must retain the explicitly shared summary, not just the working preview");
        var retainedSummary = backMatter.GetProperty("resources").EnumerateArray()
            .Single(r => r.GetProperty("uuid").GetString() == share.ShareId.ToString());
        retainedSummary.GetProperty("description").GetString().Should().Be(share.Summary);
        var base64 = retainedSummary.GetProperty("base64");
        base64.GetProperty("media-type").GetString().Should().Be("application/json");
        Convert.FromBase64String(base64.GetProperty("value").GetString()!).Should().Equal(approvedSummaryBytes,
            "the exported summary must be the exact approved service bytes, not a reconstructed or private attachment");
        var retainedDuty = ssp.GetProperty("system-implementation").GetProperty("props").EnumerateArray()
            .Where(p => p.GetProperty("name").GetString() == "capability-responsibility")
            .Select(p => JsonSerializer.Deserialize<DocumentResponsibilityReference>(p.GetProperty("value").GetString()!, Json)!)
            .Single(p => p.CapabilityId == capability.RecordId && p.ControlId == "AU-2");
        retainedDuty.CustomerResponsibility.Should().Be("Review events and retain customer evidence");
        retainedDuty.ConfirmedBy.Should().Be(Reviewer.ToString());
        retainedDuty.ReviewedSourceRevision.Should().Be(duty.SourceRevision);
        content.Should().Contain(releaseId.ToString()).And.Contain(recorded.Reference)
            .And.Contain("Review events and retain customer evidence").And.Contain(share.Summary)
            .And.Contain("SYNTHETIC authored AU-2 implementation")
            .And.NotContain("SYNTHETIC PRIVATE audit-event source").And.NotContain("synthetic-private-events.txt");
        await using var verify = factory.Services.CreateAsyncScope();
        var validation = await verify.ServiceProvider.GetRequiredService<IOscalSchemaValidationService>()
            .ValidateAsync(content, "ssp");
        validation.IsValid.Should().BeTrue("actual final OSCAL schema diagnostics: {0}", JsonSerializer.Serialize(validation, Json));
        var verifyDb = verify.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verifyDb.AuthorizationDecisions.CountAsync(x => x.RegisteredSystemId == system)).Should().Be(0,
            "source review, publication, adoption and generation never issue a mission ATO");
    }

    private async Task AuthorDocumentPrerequisitesAsync(HttpClient mission, string root)
    {
        // Missing required implementation data remains a truthful blocker, even when a system exists.
        var missing = await Get<JsonElement>(mission, root + "/documents/ssp/preview", envelope: false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var schema = scope.ServiceProvider.GetRequiredService<IOscalSchemaValidationService>();
            var validation = await schema.ValidateAsync(missing.GetProperty("content").GetString()!, "ssp");
            validation.IsValid.Should().BeFalse();
            validation.Violations.Should().Contain(v =>
                v.JsonPath == "/system-security-plan/control-implementation/implemented-requirements");
        }

        using var reviewer = Client(Reviewer);
        // The assigned ISSM records categorization through the same authorized HTTP contract
        // as the UI. Moderate matches the existing baseline; no provider decision is inferred.
        var categorization = await Post<JsonElement>(reviewer, root + "/categorization", new SetCategorizationRequest
        {
            IsNationalSecuritySystem = false,
            Justification = "SYNTHETIC local acceptance categorization; no real mission data",
            InformationTypes =
                [new SetCategorizationInfoTypeInput
                {
                    Sp80060Id = "C.3.1.3", Name = "SYNTHETIC audit event information", Category = "Synthetic",
                    ConfidentialityImpact = "Moderate", IntegrityImpact = "Moderate", AvailabilityImpact = "Moderate",
                    UsesProvisional = false, AdjustmentJustification = "Synthetic Moderate test boundary"
                }]
        }, envelope: false);
        categorization.GetProperty("nistBaseline").GetString().Should().Be("Moderate");

        // MO authors and submits; the separate assigned ISSM explicitly approves the retained profile.
        var profilePath = root + "/profile/MissionAndPurpose";
        await Read<JsonElement>(await mission.PutAsJsonAsync(profilePath, new
        {
            content = "SYNTHETIC mission purpose: retain and review audit events for the local acceptance system."
        }), HttpStatusCode.OK, envelope: false);
        await Post<JsonElement>(mission, root + "/profile/submit",
            new { sectionTypes = new[] { "MissionAndPurpose" } }, envelope: false);
        var approval = await Post<JsonElement>(reviewer, profilePath + "/review",
            new { decision = "approve", comments = "ISSM reviewed synthetic mission purpose" }, envelope: false);
        approval.GetProperty("newStatus").GetString().Should().Be("Approved");

        // The required implementation is authored via production HTTP, not seeded or created by adoption.
        var narrative = await Post<JsonElement>(reviewer, root + "/narratives", new
        {
            controlId = "AU-2", implementationStatus = "Planned",
            narrative = "SYNTHETIC authored AU-2 implementation: mission personnel plan to review audit events and retain review records."
        }, HttpStatusCode.Created, envelope: false);
        narrative.GetProperty("approvalStatus").GetString().Should().Be("Draft",
            "authoring a prerequisite must not invent narrative approval or mission authorization");
    }

    private async Task SeedDocumentInventoryPrerequisiteAsync(string system)
    {
        // Existing mission inventory/boundary is a document prerequisite, not a provider allocation,
        // association, inherited capability, evidence item, authorization decision or generated artifact.
        var boundary = new AuthorizationBoundaryDefinition
        {
            TenantId = Tenant, RegisteredSystemId = system, Name = "SYNTHETIC mission baseline boundary",
            IsPrimary = true, CreatedBy = "fixture-document-prerequisite"
        };
        var component = new SystemComponent
        {
            TenantId = Tenant, RegisteredSystemId = system, Name = "SYNTHETIC local audit workstation",
            Description = "Synthetic existing mission inventory; no discovered or connected cloud resource.",
            ComponentType = ComponentType.Thing, SubType = "Workstation", Status = ComponentStatus.Active,
            AuthorizationBoundaryDefinitionId = boundary.Id, CreatedBy = "fixture-document-prerequisite"
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.AddRange(boundary, component, new BoundaryComponentAssignment
        {
            TenantId = Tenant, AuthorizationBoundaryDefinitionId = boundary.Id, SystemComponentId = component.Id,
            IsInScope = true, CreatedBy = "fixture-document-prerequisite"
        });
        await db.SaveChangesAsync();
    }

    private HttpClient Client(Guid actor, bool csp = false, Guid? tenant = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", csp ? "csp" : "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        if (csp) client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        else client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", (tenant ?? Tenant).ToString());
        return client;
    }

    private async Task<(Guid Actor, string System)> SeedMissionAsync()
    {
        var actor = Guid.Parse("07900000-0000-0000-0000-000000000004");
        var person = new Person { TenantId = Tenant, DisplayName = "SYNTHETIC Mission Owner", Email = "f079@example.invalid" };
        var system = new RegisteredSystem
        {
            TenantId = Tenant, Name = "SYNTHETIC F079 mission", Description = "Synthetic mission for deterministic acceptance",
            Acronym = "F079", HostingEnvironment = "Test", CreatedBy = "fixture", IsActive = true,
            OperationalStatus = OperationalStatus.UnderDevelopment
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
        db.AddRange(person, system);
        await db.SaveChangesAsync();
        db.OrganizationMemberships.Add(new()
        {
            TenantId = Tenant, DirectoryTenantId = DirectoryId, ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture"
        });
        db.SystemRoleAssignments.Add(new()
        {
            TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = system.Id,
            Role = OrganizationRole.MissionOwner, IsInherited = false
        });
        var reviewer = new Person { TenantId = Tenant, DisplayName = "SYNTHETIC ISSM", Email = "f079-issm@example.invalid" };
        db.Persons.Add(reviewer);
        db.OrganizationMemberships.Add(new()
        {
            TenantId = Tenant, DirectoryTenantId = DirectoryId, ObjectId = Reviewer, PersonId = reviewer.Id, GrantedBy = "fixture"
        });
        db.SystemRoleAssignments.Add(new()
        {
            TenantId = Tenant, PersonId = reviewer.Id, RegisteredSystemId = system.Id,
            Role = OrganizationRole.Issm, IsInherited = false
        });
        foreach (var (id, tenant) in new[] { (Unassigned, Tenant), (Foreign, WorkspaceMembershipFactory.TenantBId) })
        {
            var reader = new Person { TenantId = tenant, DisplayName = "SYNTHETIC unauthorized reader", Email = $"{id:N}@example.invalid" };
            db.Persons.Add(reader);
            db.OrganizationMemberships.Add(new()
            {
                TenantId = tenant, DirectoryTenantId = DirectoryId, ObjectId = id, PersonId = reader.Id, GrantedBy = "fixture"
            });
        }
        db.ControlBaselines.Add(new()
        {
            TenantId = Tenant, RegisteredSystemId = system.Id, BaselineLevel = "Moderate",
            ControlIds = ["AU-2"], TotalControls = 1, CreatedBy = "fixture"
        });
        if (!await db.NistControls.AnyAsync(c => c.Id == "AU-2"))
            db.NistControls.Add(new()
            {
                Id = "AU-2", Family = "AU", Title = "SYNTHETIC audit control catalog prerequisite",
                Description = "Local catalog lookup prerequisite, not an implementation or approval.",
                ImpactLevel = "Moderate", Baselines = ["Moderate"]
            });
        await db.SaveChangesAsync();
        return (actor, system.Id);
    }

    private async Task AssertNoAdoptionOrInheritance(string system)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.CapabilitySubscriptions.CountAsync(x => x.RegisteredSystemId == system)).Should().Be(0);
        (await db.ControlInheritances.CountAsync(x => db.ControlBaselines.Any(b =>
            b.Id == x.ControlBaselineId && b.RegisteredSystemId == system))).Should().Be(0);
    }

    private async Task<PackageStatus> WaitForAnalysis(HttpClient client, string path)
    {
        var timer = Stopwatch.StartNew();
        PackageStatus status;
        do
        {
            status = await Get<PackageStatus>(client, path);
            if (status.ProcessingState is not ("Received" or "Processing")) return status;
            await Task.Delay(100);
        } while (timer.Elapsed < TimeSpan.FromSeconds(45));
        throw new Xunit.Sdk.XunitException($"Worker did not complete {path}: {JsonSerializer.Serialize(status, Json)}");
    }

    private async Task<T> Get<T>(HttpClient client, string path, bool envelope = true) =>
        await Read<T>(await client.GetAsync(path), HttpStatusCode.OK, envelope);

    private async Task<T> Post<T>(HttpClient client, string path, object body,
        HttpStatusCode expected = HttpStatusCode.OK, string? key = null, bool envelope = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await Read<T>(await client.SendAsync(request), expected, envelope);
    }

    private async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected, bool envelope = true)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            var evidence = $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}" +
                $" -> {(int)response.StatusCode} {response.StatusCode}\n{body}";
            output.WriteLine(evidence);
            response.StatusCode.Should().Be(expected, "production HTTP workflow must complete; exact response:\n{0}", evidence);
            using var document = JsonDocument.Parse(body);
            return (envelope ? document.RootElement.GetProperty("data") : document.RootElement).Deserialize<T>(Json)!;
        }
    }

    private sealed record Page<T>(IReadOnlyList<T> Items);

    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(exportRoot)) Directory.Delete(exportRoot, recursive: true);
    }
}
