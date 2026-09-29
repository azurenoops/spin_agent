using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class ProviderEvidenceSharingHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WorkspaceMembershipFactory _factory;
    private static readonly Guid DirectoryId = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public ProviderEvidenceSharingHttpTests(WorkspaceMembershipFactory factory) => _factory = factory;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionRoutes_ExplicitSummaryGrant_RetainsPrivateSourceAndRechecksTenantSystemRevocation(bool serviceRelationship)
    {
        // Arrange
        Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING").Should().BeNullOrEmpty("test must use isolated SQLite");
        using var provider = Client(Guid.NewGuid(), true);
        await _factory.EnsureActiveCspProfileAsync();
        var (actor, system, membership) = await SeedMission(WorkspaceMembershipFactory.TenantAId);
        var (otherActor, otherSystem, _) = await SeedMission(WorkspaceMembershipFactory.TenantBId);
        using var mission = Client(actor);
        using var other = Client(otherActor);
        var subscription = Guid.NewGuid();
        var azureScope = new ProviderAzureScope("AzureCloud", DirectoryId, subscription, $"/subscriptions/{subscription}");
        ProviderScope scope = serviceRelationship
            ? new ProviderServiceScope("synthetic-m365-service", "Synthetic collaboration", "Microsoft365DoD", "Synthetic service tenant")
            : azureScope;
        var offering = await Post<ProviderOfferingResponse>(provider, "/api/csp/offerings",
            new CreateProviderOfferingRequest("Synthetic evidence offering", "", [scope.ScopeEnvironment])
            {
                ServiceModel = serviceRelationship ? "SoftwareAsAService" : null,
                ManagementArrangement = serviceRelationship ? "SharedOperations" : null,
            });
        var root = $"/api/csp/offerings/{offering.OfferingId}";
        var boundary = await Post<ProviderBoundaryResponse>(provider, root + "/boundary-revisions",
            new CreateProviderBoundaryRequest(1, null, "Synthetic boundary", "Synthetic scope", ["Service"],
                [], [scope], [], ["Provider"], ["Customer"], []));
        var hosting = await Post<ProviderHostingScopeResponse>(provider, root + "/hosting-scope-revisions",
            new CreateProviderHostingScopeRequest(boundary.OfferingRevision, null, "Hosting", [scope], [], []));
        var assignment = await Post<ProviderHostingAssignmentResponse>(provider, root + "/hosting-assignments",
            new CreateProviderHostingAssignmentRequest(WorkspaceMembershipFactory.TenantAId, system,
                hosting.Snapshot.RevisionId, [scope], []));
        await Post<JsonElement>(mission, $"/api/dashboard/systems/{system}/provider-relationships",
            new CreateMissionProviderRelationshipRequest(assignment.AssignmentId, assignment.Revision));
        offering = await Read<ProviderOfferingResponse>(await provider.GetAsync(root));
        var finding = await Post<ProviderFindingResponse>(provider, root + "/findings",
            new CreateProviderFindingRequest(offering.Revision, "Finding", "Private observation", "Moderate", ["AU-2"], []));
        const string privateText = "PRIVATE retained source bytes";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(finding.Revision.ToString()), "expectedFindingRevision");
        form.Add(new StringContent("Private evidence description"), "description");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(privateText));
        file.Headers.ContentType = new("text/plain");
        form.Add(file, "file", "private-source.txt");
        using var upload = new HttpRequestMessage(HttpMethod.Post, root + $"/findings/{finding.FindingId}/evidence") { Content = form };
        upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var evidence = await Read<ProviderFindingEvidenceResponse>(await provider.SendAsync(upload));
        var missionRoot = $"/api/dashboard/systems/{system}/provider-evidence";
        (await Read<JsonElement>(await mission.GetAsync(missionRoot))).GetProperty("items").GetArrayLength().Should().Be(0);

        // Act: only the explicitly approved text crosses the provider boundary.
        var body = new ApproveProviderEvidenceShareRequest(assignment.AssignmentId, assignment.Revision,
            evidence.EvidenceRevision, "Provider-reviewed mission summary");
        if (serviceRelationship)
        {
            var azureOffering = await Post<ProviderOfferingResponse>(provider, "/api/csp/offerings",
                new CreateProviderOfferingRequest("Independent synthetic Azure offering", "", ["AzureCloud"]));
            var azureRoot = $"/api/csp/offerings/{azureOffering.OfferingId}";
            var azureHosting = await Post<ProviderHostingScopeResponse>(provider, azureRoot + "/hosting-scope-revisions",
                new CreateProviderHostingScopeRequest(azureOffering.Revision, null, "Separate Azure scope", [azureScope], [], []));
            var azureAssignment = await Post<ProviderHostingAssignmentResponse>(provider, azureRoot + "/hosting-assignments",
                new CreateProviderHostingAssignmentRequest(WorkspaceMembershipFactory.TenantAId, system,
                    azureHosting.Snapshot.RevisionId, [azureScope], []));
            await Post<JsonElement>(mission, $"/api/dashboard/systems/{system}/provider-relationships",
                new CreateMissionProviderRelationshipRequest(azureAssignment.AssignmentId, azureAssignment.Revision));
            using var wrongScope = new HttpRequestMessage(HttpMethod.Post, azureRoot + "/hosting-assignments")
            {
                Content = JsonContent.Create(new CreateProviderHostingAssignmentRequest(WorkspaceMembershipFactory.TenantAId,
                    system, azureHosting.Snapshot.RevisionId, [scope], []))
            };
            wrongScope.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            (await provider.SendAsync(wrongScope)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var wrongEvidence = new HttpRequestMessage(HttpMethod.Post, root + $"/evidence/{evidence.EvidenceId}/shares")
            {
                Content = JsonContent.Create(body with { AssignmentId = azureAssignment.AssignmentId, ExpectedAssignmentRevision = azureAssignment.Revision })
            };
            wrongEvidence.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            (await provider.SendAsync(wrongEvidence)).StatusCode.Should().Be(HttpStatusCode.NotFound,
                "a same-provider Azure allocation cannot acquire evidence from the independent SaaS offering");
            JsonSerializer.Serialize(scope, Json).Should().NotContain("subscriptionId").And.NotContain("directoryTenantId");
        }
        var key = Guid.NewGuid().ToString();
        var grant = await Post<ProviderEvidenceShareResponse>(provider, root + $"/evidence/{evidence.EvidenceId}/shares", body, key);
        var replay = await Post<ProviderEvidenceShareResponse>(provider, root + $"/evidence/{evidence.EvidenceId}/shares", body, key);
        var download = await mission.GetAsync(missionRoot + $"/{grant.ShareId}/content");
        var bytes = await download.Content.ReadAsByteArrayAsync();

        // Assert
        replay.Should().BeEquivalentTo(grant);
        grant.SourceSha256.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(privateText))));
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Headers.CacheControl!.NoStore.Should().BeTrue();
        grant.ContentHash.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)));
        Encoding.UTF8.GetString(bytes).Should().Contain(body.Summary).And.NotContain(privateText).And.NotContain("private-source.txt");
        (await mission.GetAsync(root + $"/findings/{finding.FindingId}/evidence/{evidence.EvidenceId}/content"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync($"/api/dashboard/systems/{otherSystem}/provider-evidence/{grant.ShareId}/content"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mission.GetAsync($"/api/dashboard/systems/{otherSystem}/provider-evidence/{grant.ShareId}/content"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(missionRoot)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var catalogRoot = $"/api/dashboard/systems/{system}/evidence-catalog";
        var catalog = await mission.GetFromJsonAsync<JsonElement>(catalogRoot + "?view=provider");
        catalog.GetProperty("totalCount").GetInt32().Should().Be(1);
        catalog.GetProperty("items")[0].GetProperty("linksKnown").GetBoolean().Should().BeFalse();
        var catalogDetail = await mission.GetFromJsonAsync<JsonElement>(catalogRoot + $"/provider:{grant.ShareId}");
        catalogDetail.GetProperty("availability").GetString().Should().Be("SummaryOnly");
        catalogDetail.GetProperty("owner").ValueKind.Should().Be(JsonValueKind.Null);
        catalogDetail.GetProperty("permissions").GetProperty("canLink").GetBoolean().Should().BeFalse();
        catalogDetail.GetProperty("permissions").GetProperty("canDownload").GetBoolean().Should().BeTrue();
        catalogDetail.GetRawText().Should().NotContain("private-source.txt").And.NotContain("Private evidence description")
            .And.NotContain(privateText).And.NotContain("storagePath");
        (await other.GetAsync($"/api/dashboard/systems/{otherSystem}/evidence-catalog/provider:{grant.ShareId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        await Post<ProviderEvidenceShareResponse>(provider, root + $"/evidence-shares/{grant.ShareId}/revoke",
            new RevokeProviderEvidenceShareRequest(grant.Revision, "Provider withdrawal"));
        (await mission.GetAsync(missionRoot + $"/{grant.ShareId}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mission.GetAsync(catalogRoot + $"/provider:{grant.ShareId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var replacement = await Post<ProviderEvidenceShareResponse>(provider, root + $"/evidence/{evidence.EvidenceId}/shares",
            body with { PreviousVersionId = grant.ShareId, Version = 2 });
        await using (var services = _factory.Services.CreateAsyncScope())
        {
            var db = services.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Database.IsSqlite().Should().BeTrue();
            var row = await db.OrganizationMemberships.IgnoreQueryFilters().SingleAsync(x => x.Id == membership);
            row.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        (await mission.GetAsync(missionRoot + $"/{replacement.ShareId}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(Guid actor, bool provider = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        if (provider) client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        return client;
    }

    private async Task<(Guid Actor, string System, Guid Membership)> SeedMission(Guid tenant)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var actor = Guid.NewGuid();
        var person = new Person { TenantId = tenant, DisplayName = "Mission owner", Email = $"{actor}@example.invalid" };
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic evidence mission", IsActive = true };
        var membership = new OrganizationMembership { TenantId = tenant, DirectoryTenantId = DirectoryId,
            ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" };
        db.AddRange(person, system, membership);
        db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person.Id, RegisteredSystemId = system.Id, Role = OrganizationRole.MissionOwner });
        await db.SaveChangesAsync();
        return (actor, system.Id, membership.Id);
    }

    private static async Task<T> Post<T>(HttpClient client, string path, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await Read<T>(await client.SendAsync(request));
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"{response.RequestMessage?.RequestUri}: {response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Deserialize<T>(Json)!;
    }
}
