using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Workspaces;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderImpactPublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assessment_LegacyUnlinkedPublicationStillSucceedsWithoutAuthority(bool unrelatedOfferingExists)
    {
        // Arrange
        if (unrelatedOfferingExists)
        {
            await LinkAsync();
            await using var seed = new AtoCopilotContext(_options);
            var boundary = await seed.Set<ProviderBoundaryRevision>().SingleAsync();
            var snapshot = ProviderAuthorizationStore.Read<CreateProviderBoundaryRequest>(boundary.SnapshotJson)
                with { ComponentSnapshotIds = [] };
            boundary.SnapshotJson = ProviderAuthorizationStore.Json(snapshot);
            boundary.SnapshotHash = ProviderAuthorizationStore.Hash(boundary.SnapshotJson);
            await seed.SaveChangesAsync();
        }
        var working = await WorkingAsync();
        var workspace = new WorkspaceOperationsService(Factory());
        var preview = await workspace.GeneratePublicationPreviewAsync(_capability, working.Revision, default);
        await workspace.ApproveWorkingRevisionAsync(_capability,
            new(working.Revision, preview.PreviewId, preview.PreviewHash), "human", default);

        // Act
        var published = await workspace.PublishAsync(_capability,
            new(working.Revision, working.Revision, preview.PreviewId, preview.PreviewHash, "legacy-no-authority"),
            "human", default);

        // Assert
        published.ReleaseId.Should().NotBeEmpty();
        await using var db = new AtoCopilotContext(_options);
        (await db.ProviderCapabilityReleases.SingleAsync()).Id.Should().Be(published.ReleaseId);
        (await db.Set<ProviderAuthorizationRevision>().CountAsync()).Should().Be(0);
        (await db.Set<ProviderCatalogContextSnapshot>().CountAsync()).Should().Be(0);
        (await db.Set<ProviderAuthorizationImpactReview>().CountAsync()).Should().Be(0);
        (await db.AuthorizationDecisions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Assessment_PreExistingInheritedOnlyBlockerRetainsExactMessageAndAcceptanceGate()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync(recordKind: "InheritedMicrosoftReference");
        var service = Impact();
        var preview = await service.PreviewAsync(_offering, Request(await WorkingAsync()), "inherited-only", "human", default);

        // Act
        var accept = () => service.ReviewAsync(_offering, preview.ReviewId,
            new(preview.Revision, preview.PreviewId, preview.PreviewHash, "AcceptForPublication", "Not provider authority"),
            "human", default);

        // Assert
        preview.Blockers.Should().ContainSingle().Which.Should().Be(new ProviderImpactBlocker(
            "PROVIDER_DECISION_REQUIRED",
            "Inherited cloud references alone do not establish a recorded provider boundary decision."));
        await accept.Should().ThrowAsync<ProviderPublicationConflictException>()
            .WithMessage("*Resolve the impact blockers*");
        (await service.GetAsync(_offering, preview.ReviewId, default)).Disposition.Should().Be("PendingReview");
        await using var db = new AtoCopilotContext(_options);
        (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Assessment_WithoutAuthorizationCreatesPreviewButCannotAcceptOrPublish()
    {
        // Arrange
        await LinkAsync();
        var working = await WorkingAsync();
        var request = Request(working) with { AuthorizationRevisionIds = [] };
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "assessment-without-authority");
        var root = $"/api/csp/offerings/{_offering}";

        // Act
        var result = await client.PostAsJsonAsync(root + "/impact-previews", request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Created);
        var preview = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")
            .Deserialize<ProviderImpactPreviewResponse>(ProviderAuthorizationStore.JsonOptions)!;
        preview.Blockers.Should().ContainSingle(x => x.Code == "PROVIDER_DECISION_REQUIRED")
            .Which.Message.Should().Contain("coverage is not established");
        var details = await client.GetAsync(root + $"/impact-reviews/{preview.ReviewId}/details");
        details.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await details.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("context").GetProperty("authorizationRevisionIds").GetArrayLength().Should().Be(0);
        data.GetProperty("blockers").EnumerateArray().Should().Contain(x => x.GetProperty("code").GetString() == "PROVIDER_DECISION_REQUIRED");
        var acceptance = await client.PostAsJsonAsync(root + $"/impact-reviews/{preview.ReviewId}/review",
            new ReviewProviderImpactRequest(1, preview.PreviewId, preview.PreviewHash, "AcceptForPublication", "Cannot establish authority"));
        acceptance.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var publish = () => new WorkspaceOperationsService(Factory()).GeneratePublicationPreviewAsync(
            _capability, working.Revision, [preview.ReviewId], default);
        await publish.Should().ThrowAsync<ProviderPublicationConflictException>();
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.Set<ProviderAuthorizationRevision>().CountAsync()).Should().Be(0);
            (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
            var row = await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId);
            row.Disposition.Should().Be("PendingReview");
            row.ReviewedBy.Should().BeNull();
            // A disposition alone cannot override the existing publication blocker gate.
            row.Disposition = "AcceptForPublication";
            row.ReviewedBy = "synthetic-corruption";
            row.ReviewedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await publish.Should().ThrowAsync<ProviderPublicationConflictException>()
            .WithMessage("*unblocked exact impact preview*");
    }

    [Theory]
    [InlineData("RequestChanges")]
    [InlineData("Reject")]
    public async Task Assessment_WithoutAuthorizationCanRetainExplicitNonAcceptance(string disposition)
    {
        // Arrange
        await LinkAsync();
        var request = Request(await WorkingAsync()) with { AuthorizationRevisionIds = [] };
        var service = Impact();

        // Act
        var preview = await service.PreviewAsync(_offering, request, "non-acceptance", "human", default);
        var review = await service.ReviewAsync(_offering, preview.ReviewId,
            new(1, preview.PreviewId, preview.PreviewHash, disposition, "Coverage not established"), "human", default);

        // Assert
        review.Disposition.Should().Be(disposition);
        review.ContextSnapshotHash.Should().Be(preview.ContextSnapshotHash);
        await using var db = new AtoCopilotContext(_options);
        (await db.Set<ProviderAuthorizationRevision>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Assessment_ExactInt64OptionPreviewDetailsRoundTripPreservesCanonicalHashesAndIdempotency()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        const long ticks = 638000000000000123;
        ProviderImpactChange canonical;
        await using (var db = new AtoCopilotContext(_options))
        {
            (await db.CspInheritedComponents.SingleAsync(x => x.Id == _component)).ImportedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            await db.SaveChangesAsync();
            canonical = await ProviderImpactService.ChangeAsync(db, _provider, "Component", _component, default);
        }
        var numericRequest = new CreateProviderImpactPreviewRequest(1, [canonical], [_decision], _boundary, null, []);
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        var root = $"/api/csp/offerings/{_offering}";
        client.DefaultRequestHeaders.Add("Idempotency-Key", "exact-int64");

        // Act
        var optionResponse = await client.GetAsync(root + $"/impact-options/Component/{_component}");

        // Assert
        optionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var option = (await optionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        option.GetProperty("change").GetProperty("expectedRevision").GetString().Should().Be(ticks.ToString(CultureInfo.InvariantCulture));
        var input = JsonSerializer.SerializeToNode(numericRequest, ProviderAuthorizationStore.JsonOptions)!.AsObject();
        input["changes"] = new JsonArray(JsonNode.Parse(option.GetProperty("change").GetRawText()));
        var stringResponse = await client.PostAsync(root + "/impact-previews",
            new StringContent(input.ToJsonString(), Encoding.UTF8, "application/json"));
        stringResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var preview = (await stringResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")
            .Deserialize<ProviderImpactPreviewResponse>(ProviderAuthorizationStore.JsonOptions)!;
        preview.Blockers.Should().BeEmpty();
        var replay = await client.PostAsJsonAsync(root + "/impact-previews", numericRequest);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")
            .GetProperty("reviewId").GetGuid().Should().Be(preview.ReviewId);
        string retainedInput;
        string retainedContext;
        await using (var db = new AtoCopilotContext(_options))
        {
            var row = await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId);
            retainedInput = row.InputJson;
            retainedContext = row.ContextJson;
            var stored = JsonDocument.Parse(row.InputJson).RootElement.GetProperty("changes")[0].GetProperty("expectedRevision");
            stored.ValueKind.Should().Be(JsonValueKind.Number);
            stored.GetInt64().Should().Be(ticks);
            JsonDocument.Parse(row.ContextJson).RootElement.GetProperty("changes")[0].GetProperty("expectedRevision")
                .ValueKind.Should().Be(JsonValueKind.Number);
            ProviderAuthorizationStore.Hash(row.ContextJson).Should().Be(preview.ContextSnapshotHash);
        }
        var detailResponse = await client.GetAsync(root + $"/impact-reviews/{preview.ReviewId}/details");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = (await detailResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        details.GetProperty("context").GetProperty("changes")[0].GetProperty("expectedRevision").GetString()
            .Should().Be(ticks.ToString(CultureInfo.InvariantCulture));
        details.GetProperty("changes")[0].GetProperty("expectedRevision").GetString()
            .Should().Be(ticks.ToString(CultureInfo.InvariantCulture));
        details.GetProperty("review").GetProperty("stale").GetBoolean().Should().BeFalse();
        var restored = await client.PostAsync(root + "/impact-previews",
            new StringContent(details.GetProperty("context").GetRawText(), Encoding.UTF8, "application/json"));
        restored.StatusCode.Should().Be(HttpStatusCode.Created);
        (await restored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("previewHash").GetString().Should().Be(preview.PreviewHash);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "exact-int64-independent-numeric");
        var independent = await client.PostAsJsonAsync(root + "/impact-previews", numericRequest);
        independent.StatusCode.Should().Be(HttpStatusCode.Created);
        var numericPreview = (await independent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        numericPreview.GetProperty("reviewId").GetGuid().Should().NotBe(preview.ReviewId);
        numericPreview.GetProperty("previewHash").GetString().Should().Be(preview.PreviewHash);
        numericPreview.GetProperty("contextSnapshotHash").GetString().Should().Be(preview.ContextSnapshotHash);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "exact-int64");
        input["changes"]![0]!["expectedRevision"] = (ticks + 1).ToString(CultureInfo.InvariantCulture);
        var conflicting = await client.PostAsync(root + "/impact-previews",
            new StringContent(input.ToJsonString(), Encoding.UTF8, "application/json"));
        conflicting.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var verify = new AtoCopilotContext(_options);
        var retained = await verify.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId);
        retained.InputJson.Should().Be(retainedInput);
        retained.ContextJson.Should().Be(retainedContext);
        retained.PreviewHash.Should().Be(preview.PreviewHash);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("9007199254740993")]
    [InlineData("9223372036854775807")]
    public void Assessment_StringAndLegacyNumericRevisionInputsHaveIdenticalCanonicalSerialization(string revision)
    {
        // Arrange
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var hash = new string('A', 64);
        var numericJson = $$"""{"kind":"Component","recordId":"{{id}}","expectedRevision":{{revision}},"proposedSnapshotHash":"{{hash}}"}""";
        var stringJson = $$"""{"kind":"Component","recordId":"{{id}}","expectedRevision":"{{revision}}","proposedSnapshotHash":"{{hash}}"}""";

        // Act
        var numeric = ProviderAuthorizationStore.Read<ProviderImpactChange>(numericJson);
        var exact = ProviderAuthorizationStore.Read<ProviderImpactChange>(stringJson);

        // Assert
        exact.Should().Be(numeric);
        ProviderAuthorizationStore.Json(exact).Should().Be(numericJson);
        ProviderAuthorizationStore.Hash(ProviderAuthorizationStore.Json(exact)).Should().Be(ProviderAuthorizationStore.Hash(numericJson));
    }

    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    public void Assessment_InvalidInt64StringsAreRejected(string revision)
    {
        // Arrange
        var json = $$"""{"kind":"Component","recordId":"11111111-1111-1111-1111-111111111111","expectedRevision":"{{revision}}","proposedSnapshotHash":"{{new string('A', 64)}}"}""";

        // Act
        var read = () => ProviderAuthorizationStore.Read<ProviderImpactChange>(json);

        // Assert
        read.Should().Throw<JsonException>();
    }
}
