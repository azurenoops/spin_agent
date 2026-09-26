using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Endpoints.Csp;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ProviderAuthorizationRecord = Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderAuthorizationRecord;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed partial class ProviderImpactPublicationTests
{
    private async Task<WebApplication> HttpApiAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.RemoveAll<IChatClient>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication("Synthetic")
            .AddScheme<AuthenticationSchemeOptions, ImpactAuthHandler>("Synthetic", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddScoped(sp =>
        {
            var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext!;
            var tenant = new TenantContext(Guid.Empty)
            {
                IsCspAdmin = !http.Request.Headers.ContainsKey("X-Tenant"),
                ImpersonatedTenantId = http.Request.Headers.ContainsKey("X-Support") ? Guid.NewGuid() : null
            };
            return new ProviderAuthorizationStore(Factory(), tenant, sp.GetRequiredService<ILogger<ProviderAuthorizationStore>>());
        });
        builder.Services.AddScoped<IProviderImpactService, ProviderImpactService>();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapProviderImpactEndpoints();
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Http_ExactPreviewReviewTargets_UseEnvelopeLocationAndIdempotency()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        var root = $"/api/csp/offerings/{_offering}";
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-preview");

        // Act
        var created = await client.PostAsJsonAsync(root + "/impact-previews", Request(working));
        var json = await created.Content.ReadFromJsonAsync<JsonElement>();
        var preview = json.GetProperty("data").Deserialize<ProviderImpactPreviewResponse>(ProviderAuthorizationStore.JsonOptions)!;
        var replay = await client.PostAsJsonAsync(root + "/impact-previews", Request(working));
        var review = await client.PostAsJsonAsync(root + $"/impact-reviews/{preview.ReviewId}/review",
            new ReviewProviderImpactRequest(preview.Revision, preview.PreviewId, preview.PreviewHash, "AcceptForPublication", "Exact human review"));
        var targets = await client.GetAsync(root + $"/impact-reviews/{preview.ReviewId}/affected-targets?page=1&pageSize=1");
        var listed = await client.GetAsync(root + "/impact-reviews");

        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location!.ToString().Should().EndWith(preview.ReviewId.ToString());
        json.GetProperty("status").GetString().Should().Be("success");
        json.TryGetProperty("metadata", out _).Should().BeTrue();
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("reviewId").GetGuid().Should().Be(preview.ReviewId);
        review.StatusCode.Should().Be(HttpStatusCode.OK);
        (await review.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("disposition").GetString().Should().Be("AcceptForPublication");
        targets.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await targets.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        page.GetProperty("items").GetArrayLength().Should().Be(1);
        page.GetProperty("total").GetInt32().Should().BeGreaterThan(1);
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("ProviderDecision", "Denied", "DECISION_NOT_ELIGIBLE", false)]
    [InlineData("ProviderDecision", "DATO", "DECISION_NOT_ELIGIBLE", false)]
    [InlineData("ProviderDecision", "Denial of Authorization to Operate", "DECISION_NOT_ELIGIBLE", false)]
    [InlineData("ProviderDecision", "Not Authorized", "DECISION_NOT_ELIGIBLE", false)]
    [InlineData("InheritedMicrosoftReference", "ATO", "PROVIDER_DECISION_REQUIRED", false)]
    [InlineData("ProviderDecision", "DATO", "DECISION_NOT_ELIGIBLE", true)]
    public async Task Http_CurrentAsRecorded_DoesNotAuthorizeFreshPublication(string recordKind,
        string decisionAsStated, string expectedBlocker, bool addInheritedReference)
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync("Current", recordKind, decisionAsStated);
        var working = await WorkingAsync();
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "source-eligibility");
        var root = $"/api/csp/offerings/{_offering}";
        await using var db = new AtoCopilotContext(_options);
        var source = await db.Set<ProviderAuthorizationRevision>().SingleAsync(x => x.Id == _decision);
        var request = Request(working);
        if (addInheritedReference)
        {
            var body = ProviderAuthorizationStore.Read<CreateProviderDecisionRequest>(source.SnapshotJson) with
            {
                RecordKind = "InheritedMicrosoftReference", DecisionAsStated = "ATO"
            };
            var record = new ProviderAuthorizationRecord { ProviderId = _provider, OfferingId = _offering };
            var inherited = new ProviderAuthorizationRevision
            {
                ProviderId = _provider, OfferingId = _offering, RecordId = record.Id, BoundaryRevisionId = _boundary,
                SnapshotJson = ProviderAuthorizationStore.Json(body), MetadataReviewState = "Recorded",
                RecordedBy = "human", RecordedAt = DateTimeOffset.UtcNow
            };
            inherited.SnapshotHash = ProviderAuthorizationStore.Hash(inherited.SnapshotJson);
            record.CurrentRevisionId = inherited.Id;
            db.Add(record);
            db.Add(inherited);
            await db.SaveChangesAsync();
            request = request with { AuthorizationRevisionIds = [_decision, inherited.Id] };
        }

        // Act
        var created = await client.PostAsJsonAsync(root + "/impact-previews", request);
        var json = await created.Content.ReadFromJsonAsync<JsonElement>();
        var preview = json.GetProperty("data").Deserialize<ProviderImpactPreviewResponse>(ProviderAuthorizationStore.JsonOptions)!;
        var review = await client.PostAsJsonAsync(root + $"/impact-reviews/{preview.ReviewId}/review",
            new ReviewProviderImpactRequest(preview.Revision, preview.PreviewId, preview.PreviewHash,
                "AcceptForPublication", "Source standing alone is insufficient"));

        // Assert
        ProviderAuthorizationService.Standing(source, []).Should().Be("CurrentAsRecorded");
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        preview.Blockers.Should().Contain(x => x.Code == expectedBlocker);
        review.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await review.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        error.GetProperty("errorCode").GetString().Should().Be("AUTHORIZATION_CONTEXT_STALE");
        (await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == preview.ReviewId))
            .Disposition.Should().Be("PendingReview");
        (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("X-Tenant", HttpStatusCode.Forbidden)]
    [InlineData("X-Support", HttpStatusCode.Forbidden)]
    [InlineData("X-Anonymous", HttpStatusCode.Unauthorized)]
    public async Task Http_OrdinaryProviderAccessIsRequired(string header, HttpStatusCode expected)
    {
        // Arrange
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(header, "true");

        // Act
        var response = await client.GetAsync($"/api/csp/offerings/{_offering}/impact-reviews");

        // Assert
        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Http_MissingKeyWrongOwnerAndStaleHash_AreActionableFailures()
    {
        // Arrange
        await LinkAsync();
        await SeedDecisionAsync();
        var working = await WorkingAsync();
        var preview = await Impact().PreviewAsync(_offering, Request(working), "preview", "human", default);
        await using var app = await HttpApiAsync();
        using var client = app.GetTestClient();
        var root = $"/api/csp/offerings/{_offering}";

        // Act
        var missing = await client.PostAsJsonAsync(root + "/impact-previews", Request(working));
        var wrong = await client.GetAsync($"/api/csp/offerings/{Guid.NewGuid()}/impact-reviews/{preview.ReviewId}");
        var stale = await client.PostAsJsonAsync(root + $"/impact-reviews/{preview.ReviewId}/review",
            new ReviewProviderImpactRequest(preview.Revision, preview.PreviewId, new string('0', 64), "AcceptForPublication", "Review"));

        // Assert
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        error.GetProperty("errorCode").GetString().Should().Be("AUTHORIZATION_CONTEXT_STALE");
        error.GetProperty("suggestion").GetString().Should().NotBeNullOrEmpty();
    }

    private sealed class ImpactAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Anonymous") ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "synthetic-human")], Scheme.Name)), Scheme.Name)));
    }
}
