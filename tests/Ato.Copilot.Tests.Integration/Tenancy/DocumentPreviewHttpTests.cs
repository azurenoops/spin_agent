using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>Real production mapper, membership/assignment middleware and OSCAL generator; no authorization bypass.</summary>
public sealed partial class DocumentPreviewHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly WorkspaceMembershipFactory factory;
    private static readonly Guid DirectoryId = Guid.Parse("acab0079-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;

    public DocumentPreviewHttpTests(WorkspaceMembershipFactory factory) => this.factory = factory;

    [Fact]
    public async Task RetainedWorkingPreviewKeys_ReplayConcurrentRequests_WithoutAllowingFinalExports()
    {
        // Arrange
        var fixture = await SeedAsync();
        using var client = Client(fixture.Actor);
        async Task<HttpResponseMessage> Post(string path, string key, object? body = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("Idempotency-Key", key);
            if (body != null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request);
        }

        // Act
        var previews = await Task.WhenAll(Post(PreviewPath(fixture.System), "same-preview"), Post(PreviewPath(fixture.System), "same-preview"));
        var first = await previews[0].Content.ReadFromJsonAsync<JsonElement>();
        var replay = await previews[1].Content.ReadFromJsonAsync<JsonElement>();
        previews.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var previewId = first.GetProperty("previewId").GetGuid();
        var exportsPath = $"/api/dashboard/systems/{fixture.System}/exports";
        var body = new { format = "json", sourcePreviewId = previewId };
        var exports = await Task.WhenAll(Post(exportsPath, "same-export", body), Post(exportsPath, "same-export", body));
        using var nextResponse = await Post(PreviewPath(fixture.System), "new-preview");
        var nextPreview = await nextResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var changed = await Post(exportsPath, "same-export",
            new { format = "json", sourcePreviewId = nextPreview.GetProperty("previewId").GetGuid() });

        // Assert
        replay.GetProperty("previewId").GetGuid().Should().Be(previewId);
        replay.GetProperty("content").GetString().Should().Be(first.GetProperty("content").GetString());
        exports.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.BadRequest);
        changed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        first.GetProperty("canGenerate").GetBoolean().Should().BeFalse();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SspExports.CountAsync(e => e.SystemId == fixture.System && e.SourcePreviewId == previewId)).Should().Be(0);
        var settings = factory.Services.GetRequiredService<IOptions<ExportSettings>>().Value;
        foreach (var path in await db.SspExports.Where(e => e.SystemId == fixture.System && e.FilePath != null).Select(e => e.FilePath!).ToListAsync())
            File.Delete(Path.Combine(settings.ExportsPath, path));
        foreach (var response in previews.Concat(exports)) response.Dispose();
    }

    [Fact]
    public async Task RetainedPreview_TracksExactBytesAndManifest_AndRejectsAnotherSystemAtExport()
    {
        // Arrange
        var fixture = await SeedAsync();
        var other = await SeedAsync();
        using var client = Client(fixture.Actor);

        // Act
        using var previewResponse = await client.PostAsync(PreviewPath(fixture.System), null);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var previewId = preview.GetProperty("previewId").GetGuid();
        using var detailResponse = await client.GetAsync($"/api/dashboard/systems/{fixture.System}/exports/{previewId}");
        using var wrongSystemResponse = await Client(other.Actor).PostAsJsonAsync(
            $"/api/dashboard/systems/{other.System}/exports", new { format = "json", sourcePreviewId = previewId });

        // Assert
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("status").GetString().Should().Be("Preview");
        detail.GetProperty("contentHash").GetString().Should().Be(preview.GetProperty("contentHash").GetString());
        detail.GetProperty("sourceManifest").GetProperty("scope").GetString().Should().Be("WorkingProfilePreview");
        preview.GetProperty("canGenerate").GetBoolean().Should().BeFalse();
        wrongSystemResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var retained = await db.SspExports.SingleAsync(x => x.Id == previewId);
        var settings = factory.Services.GetRequiredService<IOptions<ExportSettings>>().Value;
        var path = Path.Combine(settings.ExportsPath, retained.FilePath!);
        try { (await File.ReadAllTextAsync(path)).Should().Be(preview.GetProperty("content").GetString()); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Preview_UsesPinnedRecordedSource_OrReturnsActualSourceGap(bool hasIssueDate)
    {
        // Arrange
        var fixture = await SeedAsync();
        var source = await SeedProviderSourceAsync(fixture.System, hasIssueDate);
        using var client = Client(fixture.Actor);

        // Act
        using var response = await client.GetAsync(PreviewPath(fixture.System));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var content = result.GetProperty("content").GetString()!;
        using var json = JsonDocument.Parse(content);
        var ssp = json.RootElement.GetProperty("system-security-plan");
        var implementation = ssp.GetProperty("system-implementation");
        content.Should().NotContain("DEMO successor draft").And.NotContain("private-source.txt");
        if (hasIssueDate)
        {
            var authorization = implementation.GetProperty("leveraged-authorizations")[0];
            authorization.GetProperty("title").GetString().Should().Be("DEMO Shared Services Decision");
            authorization.GetProperty("date-authorized").GetString().Should().Be("2025-04-17");
            var issuerId = authorization.GetProperty("party-uuid").GetString();
            ssp.GetProperty("metadata").GetProperty("parties").EnumerateArray().Should().Contain(p =>
                p.GetProperty("uuid").GetString() == issuerId && p.GetProperty("name").GetString() == "DEMO Review Authority");
            var properties = authorization.GetProperty("props").EnumerateArray().ToDictionary(
                p => p.GetProperty("name").GetString()!, p => p.GetProperty("value").GetString());
            properties["adoption-id"].Should().Be(source.AdoptionId.ToString());
            properties["release-id"].Should().Be(source.ReleaseId.ToString());
            result.GetProperty("sourceGaps").EnumerateArray().Should().NotContain(g =>
                g.GetProperty("code").GetString() == "PROVIDER_PROVENANCE_UNVERIFIED");
        }
        else
        {
            implementation.TryGetProperty("leveraged-authorizations", out _).Should().BeFalse();
            result.GetProperty("sourceGaps").EnumerateArray().Should().Contain(g =>
                g.GetProperty("code").GetString() == "PROVIDER_PROVENANCE_UNVERIFIED"
                && g.GetProperty("message").GetString()!.Contains("issue date"));
        }
    }

    [Fact]
    public async Task Preview_ReturnsActualGeneratedContentAndGaps_WithoutIssuingDecisionOrSavingExport()
    {
        // Arrange
        var fixture = await SeedAsync();
        using var client = Client(fixture.Actor);

        // Act
        using var response = await client.GetAsync(PreviewPath(fixture.System));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("systemId").GetString().Should().Be(fixture.System);
        result.GetProperty("isPreview").GetBoolean().Should().BeTrue();
        result.GetProperty("sourceState").GetString().Should().Be("CurrentWorkingData");
        result.GetProperty("contentType").GetString().Should().Be("application/json");
        var content = result.GetProperty("content").GetString()!;
        using var oscal = JsonDocument.Parse(content);
        var characteristics = oscal.RootElement.GetProperty("system-security-plan").GetProperty("system-characteristics");
        characteristics.GetProperty("system-name").GetString().Should().Be("DEMO route-backed mission");
        characteristics.GetProperty("description").GetString().Should().Contain("DEMO retained mission description")
            .And.Contain("DRAFT / UNAPPROVED").And.Contain("NotStarted");
        result.GetProperty("contentHash").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
        result.GetProperty("sourceGaps").EnumerateArray().Should().Contain(g =>
            g.GetProperty("message").GetString()!.Contains("No control baseline"));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SspExports.CountAsync(x => x.SystemId == fixture.System)).Should().Be(0);
        (await db.AuthorizationDecisions.CountAsync(x => x.RegisteredSystemId == fixture.System)).Should().Be(0);
    }

    [Fact]
    public async Task Preview_DeniesAnonymous()
    {
        // Arrange
        var fixture = await SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");

        // Act
        using var response = await client.GetAsync(PreviewPath(fixture.System));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Preview_DeniesUnassignedAndRevokedReaders()
    {
        // Arrange
        var fixture = await SeedAsync();
        var unassigned = await SeedAsync(assignRole: false);
        using var client = Client(fixture.Actor);
        using var unauthorized = Client(unassigned.Actor);

        // Act
        using var denied = await unauthorized.GetAsync(PreviewPath(fixture.System));
        using var before = await client.GetAsync(PreviewPath(fixture.System));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var assignment = await db.SystemRoleAssignments.SingleAsync(x => x.PersonId == fixture.Person
                && x.RegisteredSystemId == fixture.System);
            assignment.RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        using var revoked = await client.GetAsync(PreviewPath(fixture.System));

        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        revoked.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Preview_DeniesForeignTenantSystem()
    {
        // Arrange
        var fixture = await SeedAsync();
        var foreign = await SeedAsync(WorkspaceMembershipFactory.TenantBId);
        using var client = Client(fixture.Actor);

        // Act
        using var response = await client.GetAsync(PreviewPath(foreign.System));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("DEMO retained mission description");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_RejectsWrongSystemEvenWhenRouteSystemReadable(bool foreignTenant)
    {
        // Arrange
        var fixture = await SeedAsync();
        var other = await SeedAsync(foreignTenant ? WorkspaceMembershipFactory.TenantBId : Tenant);
        if (!foreignTenant)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.SystemRoleAssignments.Add(new()
            {
                TenantId = Tenant, PersonId = fixture.Person, RegisteredSystemId = other.System,
                Role = OrganizationRole.MissionOwner, IsInherited = false
            });
            await db.SaveChangesAsync();
        }
        var export = await SeedExportAsync(other.System);
        using var client = Client(fixture.Actor);
        try
        {
            // Act
            using var response = await client.GetAsync(
                $"/api/dashboard/systems/{fixture.System}/exports/{export.Id}/download");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        }
        finally
        {
            File.Delete(export.Path);
        }
    }

    [Fact]
    public async Task Download_ReadsExactRetainedBytesForAuthorizedOwningSystem()
    {
        // Arrange
        var fixture = await SeedAsync();
        var export = await SeedExportAsync(fixture.System);
        using var client = Client(fixture.Actor);
        try
        {
            // Act
            using var response = await client.GetAsync(
                $"/api/dashboard/systems/{fixture.System}/exports/{export.Id}/download");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            (await response.Content.ReadAsStringAsync()).Should().Be("{\"retained\":\"DEMO exact generated bytes\"}");
        }
        finally
        {
            File.Delete(export.Path);
        }
    }

    private static string PreviewPath(string system) => $"/api/dashboard/systems/{system}/documents/ssp/preview";

    private HttpClient Client(Guid actor)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        return client;
    }

    private async Task<(Guid Actor, Guid Person, string System)> SeedAsync(Guid? tenantId = null, bool assignRole = true)
    {
        using var host = factory.CreateClient();
        var tenant = tenantId ?? Tenant;
        var actor = Guid.NewGuid();
        var person = new Person { TenantId = tenant, DisplayName = "DEMO reader", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem
        {
            TenantId = tenant, Name = "DEMO route-backed mission", Description = "DEMO retained mission description",
            Acronym = "DEMO", HostingEnvironment = "Test", CreatedBy = "fixture", IsActive = true
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.AddRange(person, system);
        await db.SaveChangesAsync();
        db.OrganizationMemberships.Add(new()
        {
            TenantId = tenant, DirectoryTenantId = DirectoryId, ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture"
        });
        if (assignRole)
            db.SystemRoleAssignments.Add(new()
            {
                TenantId = tenant, PersonId = person.Id, RegisteredSystemId = system.Id,
                Role = OrganizationRole.MissionOwner, IsInherited = false
            });
        await db.SaveChangesAsync();
        return (actor, person.Id, system.Id);
    }

    private async Task<(Guid Id, string Path)> SeedExportAsync(string system)
    {
        var settings = factory.Services.GetRequiredService<IOptions<ExportSettings>>().Value;
        Directory.CreateDirectory(settings.ExportsPath);
        var fileName = $"preview-http-{Guid.NewGuid():N}.json";
        var path = Path.Combine(settings.ExportsPath, fileName);
        await File.WriteAllTextAsync(path, "{\"retained\":\"DEMO exact generated bytes\"}");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var export = new SspExport
        {
            SystemId = system, Format = "json", Status = "Completed", FilePath = fileName,
            GeneratedBy = "fixture", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        db.Add(export);
        await db.SaveChangesAsync();
        return (export.Id, path);
    }

    private async Task<(Guid AdoptionId, Guid ReleaseId)> SeedProviderSourceAsync(string system, bool hasIssueDate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var provider = await db.CspProfiles.Select(x => x.Id).FirstAsync();
        var offeringId = Guid.NewGuid();
        var offering = new ProviderOffering { Id = offeringId, OfferingId = offeringId, ProviderId = provider, Name = "DEMO offering" };
        var boundary = new ProviderBoundaryRevision { ProviderId = provider, OfferingId = offeringId };
        var hosting = new ProviderHostingScopeRevision { ProviderId = provider, OfferingId = offeringId };
        var assignment = new ProviderHostingAssignment
        {
            ProviderId = provider, OfferingId = offeringId, TargetTenantId = Tenant, SystemId = system,
            HostingScopeRevisionId = hosting.Id
        };
        var record = new ProviderAuthorizationRecord { ProviderId = provider, OfferingId = offeringId };
        var source = new CreateProviderDecisionRequest(1, boundary.Id, [], "ProviderDecision",
            "DEMO Shared Services Decision", "DEMO Review Authority", "ATO",
            hasIssueDate ? "2025-04-17" : null, null, null, "NoExpiryStated", "DEMO reviewed scope",
            ["DEMO customer duty remains"],
            [new(Guid.NewGuid(), Guid.NewGuid(), "private-source.txt", "line:1", "DEMO private citation")])
            { IssuingAuthorityType = "organization" };
        var decision = new ProviderAuthorizationRevision
        {
            ProviderId = provider, OfferingId = offeringId, RecordId = record.Id, BoundaryRevisionId = boundary.Id,
            SnapshotJson = ProviderAuthorizationStore.Json(source), MetadataReviewState = "Recorded",
            RecordedBy = "DEMO reviewer", RecordedAt = DateTimeOffset.UtcNow
        };
        decision.SnapshotHash = ProviderAuthorizationStore.Hash(decision.SnapshotJson);
        var successor = new ProviderAuthorizationRevision
        {
            ProviderId = provider, OfferingId = offeringId, RecordId = record.Id, BoundaryRevisionId = boundary.Id,
            Revision = 2, SnapshotJson = ProviderAuthorizationStore.Json(source with { Reference = "DEMO successor draft" })
        };
        successor.SnapshotHash = ProviderAuthorizationStore.Hash(successor.SnapshotJson);
        record.CurrentRevisionId = successor.Id;
        var release = new ProviderCapabilityRelease
        {
            CapabilityId = Guid.NewGuid(), Revision = 1, SnapshotHash = "DEMO release working hash",
            SnapshotJson = "{}", PublishedBy = "DEMO reviewer"
        };
        var material = new ProviderPublicationContextMaterial(offeringId, 1, boundary.Id, "DEMO boundary hash",
            hosting.Id, "DEMO hosting hash", [new(record.Id, decision.Id, decision.SnapshotHash, "DEMO lifecycle hash")], [], []);
        var review = new ProviderAuthorizationImpactReview
        {
            ProviderId = provider, OfferingId = offeringId, ContextJson = ProviderAuthorizationStore.Json(material),
            Disposition = "AcceptForPublication", ReviewedBy = "DEMO reviewer", ReviewedAt = DateTimeOffset.UtcNow
        };
        review.ContextSnapshotHash = ProviderAuthorizationStore.Hash(review.ContextJson);
        var context = new ProviderCatalogContextSnapshot
        {
            ProviderId = provider, OfferingId = offeringId, ReleaseId = release.Id, CapabilityId = release.CapabilityId,
            ImpactReviewId = review.Id, SnapshotJson = review.ContextJson, SnapshotHash = review.ContextSnapshotHash
        };
        var subscription = new CapabilitySubscription
        {
            RegisteredSystemId = system, RoutingTenantId = Tenant, IsActive = true,
            CspInheritedCapabilityId = release.CapabilityId.ToString()
        };
        var adoption = new CapabilityAdoptionSnapshot
        {
            ProviderId = provider, OfferingId = offeringId, TenantId = Tenant, SystemId = system,
            SubscriptionId = subscription.Id, AssignmentId = assignment.Id, AssignmentRevision = assignment.Revision,
            CapabilityId = release.CapabilityId, ReleaseId = release.Id, ContextSnapshotId = context.Id
        };
        adoption.SnapshotJson = ProviderAuthorizationStore.Json(new
        {
            Request = new AdoptProviderCapabilityRequest(assignment.Id, assignment.Revision, release.CapabilityId,
                release.Id, context.SnapshotHash, "DEMO applicability preview hash"),
            Selected = new { ReleaseSnapshotHash = release.SnapshotHash, ReleaseRevision = release.Revision }
        });
        adoption.SnapshotHash = ProviderAuthorizationStore.Hash(adoption.SnapshotJson);
        subscription.CurrentAdoptionSnapshotId = adoption.Id;
        subscription.AdoptionSelectionRevision = 1;
        db.AddRange(offering, boundary, hosting, assignment, record, decision, successor, release, review, context, subscription, adoption);
        await db.SaveChangesAsync();
        return (adoption.Id, release.Id);
    }
}
