using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class ProviderMonitoringHostTests(PackageImportFactory factory) : IClassFixture<PackageImportFactory>
{
    [Fact]
    public async Task Provider_monitoring_requires_ordinary_provider_authority_and_owns_only_its_offering()
    {
        // Arrange
        factory.GetActiveContext().IsCspAdmin = true;
        factory.GetActiveContext().ImpersonatedTenantId = null;
        using var client = factory.CreateClient();
        async Task<HttpResponseMessage> Post(string path, object body, string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        var createdOffering = await Post("/api/csp/offerings",
            new { name = "Provider monitoring offering", description = "Synthetic test", environments = new[] { "AzureUSGovernment" } }, Guid.NewGuid().ToString());
        createdOffering.StatusCode.Should().Be(HttpStatusCode.Created, await createdOffering.Content.ReadAsStringAsync());
        var offering = (await createdOffering.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("offeringId").GetGuid();
        var evidenceId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var provider = (await db.Set<ProviderOffering>().SingleAsync(x => x.Id == offering)).ProviderId;
            var finding = new ProviderFinding { ProviderId = provider, OfferingId = offering, Title = "Synthetic reviewed evidence" };
            db.Add(finding);
            db.Add(new ProviderFindingEvidence { Id = evidenceId, ProviderId = provider, OfferingId = offering, FindingId = finding.Id,
                FileName = "reviewed.txt", State = "Reviewed", CreatedAt = DateTimeOffset.UtcNow.AddDays(-40) });
            await db.SaveChangesAsync();
        }
        var root = $"/api/csp/offerings/{offering}/monitoring";
        var before = await client.GetFromJsonAsync<JsonElement>(root);
        var source = before.GetProperty("data").GetProperty("sources")[0];
        var input = new { name = "Evidence age", signal = "EvidenceFreshness", sourceId = evidenceId,
            condition = new { field = "Change.ageDays", @operator = "GreaterThanOrEqual", value = "30" },
            cadenceMinutes = 60, ownerId = "Provider reviewer", response = "CreateProviderImpactReview", isEnabled = true,
            expectedSourceRevision = source.GetProperty("sourceRevision").GetString() };

        // Act
        var create = await Post(root + "/rules", input, "create");
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var rule = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var ruleId = rule.GetProperty("id").GetGuid();
        var test = await Post(root + $"/rules/{ruleId}/test", new { }, "unused");
        var evaluate = await Post(root + $"/rules/{ruleId}/evaluate", new { expectedRevision = 1 }, "evaluate");
        var replay = await Post(root + $"/rules/{ruleId}/evaluate", new { expectedRevision = 1 }, "evaluate");

        // Assert
        test.StatusCode.Should().Be(HttpStatusCode.OK, await test.Content.ReadAsStringAsync());
        (await test.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("impactReviewId").ValueKind.Should().Be(JsonValueKind.Null);
        evaluate.StatusCode.Should().Be(HttpStatusCode.OK, await evaluate.Content.ReadAsStringAsync());
        var evaluation = (await evaluate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid().Should().Be(evaluation.GetProperty("id").GetGuid());
        var reviewId = evaluation.GetProperty("impactReviewId").GetGuid();
        var review = await client.GetFromJsonAsync<JsonElement>($"/api/csp/offerings/{offering}/impact-reviews/{reviewId}/details");
        review.GetProperty("data").GetProperty("review").GetProperty("disposition").GetString().Should().Be("PendingReview");

        // Act
        var foreign = await client.GetAsync($"/api/csp/offerings/{Guid.NewGuid()}/monitoring");
        factory.GetActiveContext().IsCspAdmin = false;
        var denied = await client.GetAsync(root);
        var deniedWrite = await Post(root + "/rules", input, "denied");
        factory.GetActiveContext().IsCspAdmin = true;
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        var unauthenticated = await anonymous.GetAsync(root);

        // Assert
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deniedWrite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var verification = factory.Services.CreateAsyncScope();
        var verify = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verify.Set<ProviderMonitoringRule>().CountAsync(x => x.OfferingId == offering)).Should().Be(1);
        (await verify.Set<CapabilityAdoptionSnapshot>().CountAsync(x => x.OfferingId == offering)).Should().Be(0);
    }
}
