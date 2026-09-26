using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderImpactPublicationTests
{
    [Theory]
    [InlineData("Boundary")]
    [InlineData("HostingScope")]
    [InlineData("Authorization")]
    [InlineData("Package")]
    [InlineData("Component")]
    [InlineData("Capability")]
    public async Task ImpactRead_OptionsAreNamedScopedAndExact(string kind)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        await WorkingAsync();
        var package = Guid.NewGuid();
        var component = Guid.NewGuid();
        var capability = Guid.NewGuid();
        await SeedCandidatePackageAsync(package, component, capability);
        await using (var db = new AtoCopilotContext(_options))
        {
            var json = ProviderAuthorizationStore.Json(new CreateProviderHostingScopeRequest(
                1, null, "Named hosting", [], [], []));
            db.Add(new ProviderHostingScopeRevision { ProviderId = _provider, OfferingId = _offering,
                SnapshotJson = json, SnapshotHash = ProviderAuthorizationStore.Hash(json) });
            await db.SaveChangesAsync();
        }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        var root = $"/api/csp/offerings/{_offering}/impact-options";

        // Act
        var response = await client.GetAsync(root + $"?kind={kind}&page=1&pageSize=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        page.GetProperty("items").GetArrayLength().Should().Be(1);
        page.GetProperty("pageSize").GetInt32().Should().Be(1);
        var option = page.GetProperty("items")[0];
        option.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        option.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        option.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        var exact = await client.GetAsync(root + $"/{kind}/{option.GetProperty("id").GetString()}");
        exact.StatusCode.Should().Be(HttpStatusCode.OK);
        (await exact.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").ToString().Should().Be(option.ToString());
        if (kind is "Authorization" or "Package")
            option.GetProperty("change").ValueKind.Should().Be(JsonValueKind.Null);
        else if (option.GetProperty("change").ValueKind != JsonValueKind.Null)
        {
            await using var db = new AtoCopilotContext(_options);
            var canonical = await ProviderImpactService.ChangeAsync(db, _provider, kind, option.GetProperty("id").GetGuid(), default);
            option.GetProperty("change").Deserialize<ProviderImpactChange>(ProviderAuthorizationStore.JsonOptions).Should().Be(canonical);
        }
    }

    [Fact]
    public async Task ImpactRead_CanonicalComponentTicksArePresentedAsAnExactDecimalString()
    {
        // Arrange
        await LinkAsync();
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-options/Component/{_component}");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        await using var db = new AtoCopilotContext(_options);
        var canonical = await ProviderImpactService.ChangeAsync(db, _provider, "Component", _component, default);
        data.GetProperty("change").GetProperty("expectedRevision").GetString()
            .Should().Be(canonical.ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        data.GetProperty("summary").GetString().Should().NotContain("Context only");
    }

    [Fact]
    public async Task ImpactRead_AllHistoricalBoundaryVersionsPageWithoutChangingIdentity()
    {
        // Arrange
        await LinkAsync();
        await using (var db = new AtoCopilotContext(_options))
        {
            var first = await db.Set<ProviderBoundaryRevision>().SingleAsync();
            for (var i = 2; i <= 27; i++)
                db.Add(new ProviderBoundaryRevision { ProviderId = _provider, OfferingId = _offering,
                    Revision = i, SnapshotJson = first.SnapshotJson, SnapshotHash = first.SnapshotHash });
            await db.SaveChangesAsync();
        }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var response = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-options?kind=Boundary&page=2&pageSize=25");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("total").GetInt32().Should().Be(27);
        data.GetProperty("items").GetArrayLength().Should().Be(2);
        data.GetProperty("items").EnumerateArray().Should().Contain(x => x.GetProperty("id").GetGuid() == _boundary);
    }

    [Theory]
    [InlineData("Unknown", 1, 25)]
    [InlineData("Boundary", 0, 25)]
    [InlineData("Boundary", 1, 101)]
    [InlineData("Boundary", int.MaxValue, 100)]
    public async Task ImpactRead_InvalidKindOrPagingFails(string kind, int page, int pageSize)
    {
        // Arrange
        await LinkAsync();
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-options?kind={kind}&page={page}&pageSize={pageSize}");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("X-Tenant")]
    [InlineData("X-Support")]
    public async Task ImpactRead_DeniesCustomerAndSupport(string header)
    {
        // Arrange
        await LinkAsync();
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(header, "1");

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-options?kind=Boundary");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ImpactRead_ExactOptionRejectsAnotherOfferingAndUnlinkedCapability()
    {
        // Arrange
        await LinkAsync();
        var other = Guid.NewGuid();
        var otherBoundary = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(_options))
        {
            var boundary = await db.Set<ProviderBoundaryRevision>().SingleAsync();
            var unrelated = ProviderAuthorizationStore.Json(
                ProviderAuthorizationStore.Read<CreateProviderBoundaryRequest>(boundary.SnapshotJson) with { ComponentSnapshotIds = [] });
            db.Add(new ProviderOffering { Id = other, ProviderId = _provider, OfferingId = other });
            db.Add(new ProviderBoundaryRevision { Id = otherBoundary, ProviderId = _provider, OfferingId = other,
                SnapshotJson = unrelated, SnapshotHash = ProviderAuthorizationStore.Hash(unrelated) });
            await db.SaveChangesAsync();
        }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var foreignBoundary = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-options/Boundary/{otherBoundary}");
        var unlinked = await client.GetAsync($"/api/csp/offerings/{other}/impact-options/Capability/{_capability}");

        // Assert
        foreignBoundary.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unlinked.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ImpactRead_DetailsRetainInputMembershipRationaleAndDoNotWrite()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var request = Request(working);
        var service = Impact();
        var preview = await service.PreviewAsync(_offering, request, "detail", "human", default);
        await service.ReviewAsync(_offering, preview.ReviewId,
            new(1, preview.PreviewId, preview.PreviewHash, "RequestChanges", "Keep this rationale"), "human", default);
        string retained;
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedCapabilities.SingleAsync(x => x.Id == _capability)).Name = "Current renamed capability";
            await db.SaveChangesAsync();
            retained = ProviderAuthorizationStore.Json(await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId));
        }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews/{preview.ReviewId}/details");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("context").Deserialize<CreateProviderImpactPreviewRequest>(ProviderAuthorizationStore.JsonOptions)
            .Should().BeEquivalentTo(request);
        data.GetProperty("context").TryGetProperty("previewId", out _).Should().BeFalse();
        data.GetProperty("context").TryGetProperty("dependencyGraph", out _).Should().BeFalse();
        data.GetProperty("rationale").GetString().Should().Be("Keep this rationale");
        data.GetProperty("affectedCapabilities").GetProperty("total").GetInt32().Should().Be(1);
        data.GetProperty("affectedCapabilities").GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Current renamed capability");
        data.GetProperty("summary").GetString().Should().Contain("no coverage");
        await using var verify = new AtoCopilotContext(_options);
        ProviderAuthorizationStore.Json(await verify.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId)).Should().Be(retained);
    }

    [Theory]
    [InlineData("offering", "OFFERING_STALE")]
    [InlineData("expired", "PREVIEW_EXPIRED")]
    [InlineData("invalidated", "REVIEW_INVALIDATED")]
    [InlineData("missing", "CONTEXT_UNAVAILABLE")]
    [InlineData("lifecycle", "CONTEXT_UNAVAILABLE")]
    [InlineData("invalid-input", "CONTEXT_UNAVAILABLE")]
    [InlineData("null-input", "CONTEXT_UNAVAILABLE")]
    [InlineData("bad-digest", "CONTEXT_UNAVAILABLE")]
    public async Task ImpactRead_StaleDetailsExplainRequiredAction(string change, string code)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var preview = await Impact().PreviewAsync(_offering, Request(await WorkingAsync()), "detail", "human", default);
        await using (var db = new AtoCopilotContext(_options))
        {
            var row = await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId);
            if (change == "offering") (await db.Set<ProviderOffering>().SingleAsync()).Revision++;
            if (change == "expired") row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            if (change == "invalidated") row.InvalidatedAt = DateTimeOffset.UtcNow;
            if (change == "missing") db.Remove(await db.Set<ProviderAuthorizationRevision>().SingleAsync());
            if (change == "lifecycle") row.InputJson = "{}";
            if (change == "invalid-input") row.InputJson = "{malformed";
            if (change == "null-input") row.InputJson = "null";
            if (change == "bad-digest") row.ContextSnapshotHash = new string('F', 64);
            await db.SaveChangesAsync();
        }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews/{preview.ReviewId}/details");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("review").GetProperty("stale").GetBoolean().Should().BeTrue();
        data.GetProperty("blockers").EnumerateArray().Should().Contain(x => x.GetProperty("code").GetString() == code);
        if (change is "lifecycle" or "invalid-input")
            data.GetProperty("context").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("affectedCapabilities").GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ImpactRead_ActualProviderRelationshipTargetsArePagedAndHistoricallyRetained()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var tenant = Guid.NewGuid();
        var unrelatedTenant = Guid.NewGuid();
        var unrelatedOffering = Guid.NewGuid();
        var hosting = Guid.NewGuid();
        var unrelatedHosting = Guid.NewGuid();
        var hosted = Guid.NewGuid().ToString();
        var subscribed = Guid.NewGuid().ToString();
        var unrelated = Guid.NewGuid().ToString();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Tenants.Add(new() { Id = tenant, DisplayName = "Customer" });
            db.Tenants.Add(new() { Id = unrelatedTenant, DisplayName = "Unrelated" });
            db.RegisteredSystems.Add(new() { Id = hosted, TenantId = tenant, Name = "Hosted mission" });
            db.RegisteredSystems.Add(new() { Id = subscribed, TenantId = tenant, Name = "Subscribed mission" });
            db.RegisteredSystems.Add(new() { Id = unrelated, TenantId = unrelatedTenant, Name = "Must not leak" });
            db.Add(new ProviderOffering { Id = unrelatedOffering, ProviderId = _provider, OfferingId = unrelatedOffering, Name = "Unlinked offering" });
            db.Add(new ProviderHostingScopeRevision { Id = hosting, ProviderId = _provider, OfferingId = _offering });
            db.Add(new ProviderHostingScopeRevision { Id = unrelatedHosting, ProviderId = _provider, OfferingId = unrelatedOffering });
            db.Add(new ProviderHostingAssignment { ProviderId = _provider, OfferingId = _offering, SystemId = hosted,
                HostingScopeRevisionId = hosting, TargetTenantId = tenant });
            db.Add(new ProviderHostingAssignment { ProviderId = _provider, OfferingId = unrelatedOffering,
                HostingScopeRevisionId = unrelatedHosting, SystemId = unrelated, TargetTenantId = unrelatedTenant });
            db.CapabilitySubscriptions.Add(new() { RegisteredSystemId = subscribed, CspInheritedCapabilityId = _capability.ToString(),
                RoutingCapabilityId = _capability.ToString(), RoutingTenantId = tenant, IsActive = true });
            await db.SaveChangesAsync();
        }
        var preview = await Impact().PreviewAsync(_offering, Request(working), "targets", "human", default);
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        var url = $"/api/csp/offerings/{_offering}/impact-reviews/{preview.ReviewId}/details";

        // Act
        var result = await client.GetAsync(url + "?capabilityPage=1&systemPage=1&pageSize=1");
        var next = await client.GetAsync(url + "?capabilityPage=1&systemPage=2&pageSize=1");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        next.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstData = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var secondData = (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        firstData.GetProperty("affectedSystems").GetProperty("total").GetInt32().Should().Be(2);
        var names = new[] { firstData, secondData }.Select(x => x.GetProperty("affectedSystems").GetProperty("items")[0].GetProperty("name").GetString());
        names.Should().BeEquivalentTo("Hosted mission", "Subscribed mission");
        firstData.ToString().Should().NotContain("Must not leak");
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Remove(await db.Set<ProviderHostingAssignment>().SingleAsync(x => x.SystemId == hosted));
            await db.SaveChangesAsync();
        }
        var retained = (await (await client.GetAsync(url)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        retained.GetProperty("affectedSystems").GetProperty("total").GetInt32().Should().Be(2);
        retained.GetProperty("affectedSystems").GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("recordId").GetString() == hosted).GetProperty("name").ValueKind.Should().Be(JsonValueKind.Null);
        retained.GetProperty("review").GetProperty("stale").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("targets")]
    [InlineData("blockers")]
    public async Task ImpactRead_CorruptProjectionFailsExplicitly(string part)
    {
        // Arrange
        await LinkAsync();
        var review = new ProviderAuthorizationImpactReview { ProviderId = _provider, OfferingId = _offering };
        if (part == "targets") review.TargetsJson = "{private-malformed";
        else review.BlockersJson = "{private-malformed";
        await using (var db = new AtoCopilotContext(_options)) { db.Add(review); await db.SaveChangesAsync(); }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews/{review.Id}/details");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await result.Content.ReadAsStringAsync();
        body.Should().Contain("PROVIDER_PROJECTION_UNAVAILABLE").And.NotContain("private-malformed");
    }

    [Fact]
    public async Task ImpactRead_LargeHistoricalRevisionRemainsExactlyRestorable()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        ProviderImpactChange component;
        await using (var db = new AtoCopilotContext(_options))
            component = await ProviderImpactService.ChangeAsync(db, _provider, "Component", _component, default);
        var preview = await Impact().PreviewAsync(_offering, new(1, [component], [_decision], _boundary, null, []),
            "unsafe", "human", default);
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews/{preview.ReviewId}/details");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("context").GetProperty("changes")[0].GetProperty("expectedRevision").GetString()
            .Should().Be(component.ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        data.GetProperty("changes")[0].GetProperty("expectedRevision").GetString()
            .Should().Be(component.ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Published")]
    public async Task ImpactRead_RejectedOrPublishedCandidateIsContextOnly(string state)
    {
        // Arrange
        await LinkAsync();
        var candidateId = Guid.NewGuid();
        await SeedCandidatePackageAsync(Guid.NewGuid(), Guid.NewGuid(), candidateId);
        await using (var db = new AtoCopilotContext(_options))
        {
            var candidate = await db.CspPackageCandidates.SingleAsync(x => x.Id == candidateId);
            candidate.ReviewState = state;
            var payload = ProviderAuthorizationStore.Read<Ato.Copilot.Core.Interfaces.PackageImports.PackageCandidateResponse>(candidate.PayloadJson);
            candidate.PayloadJson = ProviderAuthorizationStore.Json(payload with
            { ReviewState = state, PublishedRecordId = state == "Published" ? _capability : null });
            await db.SaveChangesAsync();
        }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-options/Capability/{candidateId}");
        var canonical = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-options/Capability/{_capability}");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("change").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("summary").GetString().Should().Contain(state);
        canonical.StatusCode.Should().Be(HttpStatusCode.OK);
        (await canonical.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("change").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ImpactRead_ResolvedPrerequisiteDoesNotRemainACurrentBlocker()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync("Unconfirmed");
        var preview = await Impact().PreviewAsync(_offering, Request(await WorkingAsync()), "unconfirmed", "human", default);
        preview.Blockers.Should().Contain(x => x.Code == "DECISION_NOT_ELIGIBLE");
        await using (var db = new AtoCopilotContext(_options))
        {
            var decision = await db.Set<ProviderAuthorizationRevision>().SingleAsync();
            decision.MetadataReviewState = "Recorded";
            decision.RecordedBy = "human";
            decision.RecordedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews/{preview.ReviewId}/details");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("blockers").EnumerateArray().Should().NotContain(x => x.GetProperty("code").GetString() == "DECISION_NOT_ELIGIBLE");
        data.GetProperty("blockers").EnumerateArray().Should().Contain(x => x.GetProperty("code").GetString() == "AUTHORIZATION_CONTEXT_STALE");
        data.GetProperty("review").GetProperty("stale").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ImpactRead_HistoricalTenantMismatchNeverResolvesAnotherTenantName()
    {
        // Arrange
        await LinkAsync();
        var realTenant = Guid.NewGuid();
        var wrongTenant = Guid.NewGuid();
        var system = Guid.NewGuid().ToString();
        var review = new ProviderAuthorizationImpactReview { ProviderId = _provider, OfferingId = _offering,
            TargetsJson = ProviderAuthorizationStore.Json(new[]
            { new ProviderImpactTargetResponse("MissionSystem", system, wrongTenant, system, "ReviewRequired") }) };
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Tenants.Add(new() { Id = realTenant, DisplayName = "Actual tenant" });
            db.RegisteredSystems.Add(new() { Id = system, TenantId = realTenant, Name = "Private mission name" });
            db.Add(review);
            await db.SaveChangesAsync();
        }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews/{review.Id}/details");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("context").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("affectedSystems").GetProperty("total").GetInt32().Should().Be(1);
        data.GetProperty("affectedSystems").GetProperty("items")[0].GetProperty("name").ValueKind.Should().Be(JsonValueKind.Null);
        data.ToString().Should().NotContain("Private mission name");
    }

    [Fact]
    public async Task ImpactRead_TrackerMetadataAppearsOnListGetAndHumanReviewWithoutNPlusOneQueries()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var preview = await Impact().PreviewAsync(_offering, Request(await WorkingAsync()), "tracker", "human", default);
        var counter = new ImpactReadCommandCounter();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).AddInterceptors(counter).Options;
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        var root = $"/api/csp/offerings/{_offering}/impact-reviews";

        // Act
        var listed = await client.GetAsync(root + "?pageSize=1");
        var oneRowQueries = counter.Count;
        var allListed = await client.GetAsync(root + "?pageSize=25");
        var allRowQueries = counter.Count - oneRowQueries;
        var get = await client.GetAsync(root + $"/{preview.ReviewId}");
        var reviewed = await client.PostAsJsonAsync(root + $"/{preview.ReviewId}/review",
            new ReviewProviderImpactRequest(1, preview.PreviewId, preview.PreviewHash, "RequestChanges", "Retained human reasoning"));

        // Assert
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        allListed.StatusCode.Should().Be(HttpStatusCode.OK);
        oneRowQueries.Should().Be(4, "provider lookup, offering lookup, count and page are the only reads");
        allRowQueries.Should().Be(oneRowQueries, "summaries must not introduce per-review lookups");
        var listedReview = (await allListed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("items")
            .EnumerateArray().Single(x => x.GetProperty("reviewId").GetGuid() == preview.ReviewId);
        foreach (var item in new[] { listedReview,
            (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data"),
            (await reviewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data") })
        {
            item.GetProperty("title").GetString().Should().Contain("Capability");
            item.GetProperty("summary").GetString().Should().Contain("1 capability").And.Contain("no semantic coverage");
            item.GetProperty("createdAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
            item.GetProperty("affectedCounts").Deserialize<ProviderAffectedCounts>(ProviderAuthorizationStore.JsonOptions)
                .Should().Be(preview.AffectedCounts);
            item.GetProperty("contextSnapshotHash").GetString().Should().Be(preview.ContextSnapshotHash);
        }
        reviewed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("SOURCE_CHANGE_REVIEW_REQUIRED", "Source")]
    [InlineData("PRIVATE_CHANGE_REVIEW_REQUIRED", "Private catalog")]
    public async Task ImpactRead_LifecycleCardsExposeRecordedReasonAndHistoricalTargetCounts(string code, string title)
    {
        // Arrange
        await LinkAsync();
        var system = Guid.NewGuid().ToString();
        var row = new ProviderAuthorizationImpactReview { ProviderId = _provider, OfferingId = _offering,
            BlockersJson = ProviderAuthorizationStore.Json(new[] { new ProviderImpactBlocker(code, "Synthetic retained change reason") }),
            TargetsJson = ProviderAuthorizationStore.Json(new[] {
                new ProviderImpactTargetResponse("Capability", _capability.ToString(), null, null, "ReviewRequired"),
                new ProviderImpactTargetResponse("MissionSystem", system, Guid.NewGuid(), system, "PendingReview") }) };
        await using (var db = new AtoCopilotContext(_options)) { db.Add(row); await db.SaveChangesAsync(); }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var response = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews/{row.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        result.GetProperty("title").GetString().Should().Contain(title);
        result.GetProperty("summary").GetString().Should().Contain("Synthetic retained change reason").And.Contain("no exact preview input");
        result.GetProperty("affectedCounts").GetProperty("capabilities").GetInt32().Should().Be(1);
        result.GetProperty("affectedCounts").GetProperty("systems").GetInt32().Should().Be(1);
        result.GetProperty("stale").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImpactRead_CorruptTrackerTargetsNeverBecomeZeroCounts(bool list)
    {
        // Arrange
        await LinkAsync();
        var row = new ProviderAuthorizationImpactReview { ProviderId = _provider, OfferingId = _offering, TargetsJson = "{malformed" };
        await using (var db = new AtoCopilotContext(_options)) { db.Add(row); await db.SaveChangesAsync(); }
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var result = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews" + (list ? "" : $"/{row.Id}"));

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await result.Content.ReadAsStringAsync()).Should().Contain("PROVIDER_PROJECTION_UNAVAILABLE");
    }

    private sealed class ImpactReadCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public void ImpactRead_OriginalSevenFieldReviewConstructionAndSerializationRemainCompatible()
    {
        // Arrange
        var id = Guid.NewGuid();
        var json = ProviderAuthorizationStore.Json(new { reviewId = id, revision = 1, disposition = "PendingReview",
            reviewedBy = (string?)null, reviewedAt = (DateTimeOffset?)null, contextSnapshotHash = "", stale = true });

        // Act
        var read = ProviderAuthorizationStore.Read<ProviderImpactReviewResponse>(json);
        var (reviewId, _, _, _, _, _, _) = read;

        // Assert
        reviewId.Should().Be(id);
        read.Should().Be(new ProviderImpactReviewResponse(id, 1, "PendingReview", null, null, "", true));
        read.Title.Should().BeNull();
        read.Summary.Should().BeNull();
        read.CreatedAt.Should().BeNull();
        read.AffectedCounts.Should().BeNull();
    }

    [Fact]
    public async Task ImpactRead_MixedChangeCardIsAvailableWithoutReturningUnsafeRevisionFences()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var hosting = new ProviderHostingScopeRevision { ProviderId = _provider, OfferingId = _offering,
            SnapshotJson = ProviderAuthorizationStore.Json(new CreateProviderHostingScopeRequest(1, null, "Hosting", [], [], [])) };
        hosting.SnapshotHash = ProviderAuthorizationStore.Hash(hosting.SnapshotJson);
        ProviderImpactChange component;
        ProviderImpactChange boundary;
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Add(hosting);
            (await db.Set<ProviderOffering>().SingleAsync()).CurrentHostingScopeRevisionId = hosting.Id;
            await db.SaveChangesAsync();
            component = await ProviderImpactService.ChangeAsync(db, _provider, "Component", _component, default);
            boundary = await ProviderImpactService.ChangeAsync(db, _provider, "Boundary", _boundary, default);
        }
        var request = Request(working) with { HostingScopeRevisionId = hosting.Id,
            Changes = [component, boundary, new("HostingScope", hosting.Id, 1, hosting.SnapshotHash), Request(working).Changes[0]] };
        var preview = await Impact().PreviewAsync(_offering, request, "mixed-card", "human", default);
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();

        // Act
        var response = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews/{preview.ReviewId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("title").GetString().Should().Be("Boundary, Capability, Component, Hosting scope change review");
        data.GetProperty("summary").GetString().Should().Contain("1 component").And.Contain("1 hosting scope");
        data.GetProperty("affectedCounts").Deserialize<ProviderAffectedCounts>(ProviderAuthorizationStore.JsonOptions).Should().Be(preview.AffectedCounts);
        data.TryGetProperty("changes", out _).Should().BeFalse();
        data.ToString().Should().NotContain(component.ExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
