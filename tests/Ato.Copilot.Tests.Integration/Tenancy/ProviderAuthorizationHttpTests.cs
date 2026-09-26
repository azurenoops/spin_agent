using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class ProviderAuthorizationHttpTests : IClassFixture<PackageImportFactory>
{
    private readonly PackageImportFactory _factory;
    private readonly HttpClient _client;

    public ProviderAuthorizationHttpTests(PackageImportFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.GetActiveContext().IsCspAdmin = true;
        factory.GetActiveContext().ImpersonatedTenantId = null;
    }

    [Fact]
    public async Task OfferingCreate_ReplaysExactIntent_AndRejectsChangedIntent()
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        var body = new { name = "Synthetic offering", description = "Not a real authorization", environments = new[] { "AzureUSGovernment" } };
        // Act
        var first = await Post("/api/csp/offerings", body, key);
        var replay = await Post("/api/csp/offerings", body, key);
        var mismatch = await Post("/api/csp/offerings", body with { name = "Different intent" }, key);
        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        var initial = await Json(first);
        initial.GetProperty("status").GetString().Should().Be("success");
        initial.TryGetProperty("metadata", out _).Should().BeTrue();
        (await Json(replay)).GetProperty("data").GetProperty("offeringId").GetGuid()
            .Should().Be(initial.GetProperty("data").GetProperty("offeringId").GetGuid());
        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task BoundarySuccessor_PreservesOriginal_AndChecksOfferingRevision()
    {
        // Arrange
        var offering = await CreateOffering();
        var id = offering.GetProperty("offeringId").GetGuid();
        var request = Boundary(offering.GetProperty("revision").GetInt64(), "Original boundary");
        // Act
        var first = await Post($"/api/csp/offerings/{id}/boundary-revisions", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var boundary = (await Json(first)).GetProperty("data");
        var originalId = boundary.GetProperty("boundaryRevisionId").GetGuid();
        var second = await Post($"/api/csp/offerings/{id}/boundary-revisions",
            Boundary(boundary.GetProperty("offeringRevision").GetInt64(), "Successor boundary", originalId));
        var stale = await Post($"/api/csp/offerings/{id}/boundary-revisions", request);
        var retained = await _client.GetAsync($"/api/csp/offerings/{id}/boundary-revisions/{originalId}");
        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        retained.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(retained)).GetProperty("data").GetProperty("name").GetString().Should().Be("Original boundary");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ProviderApis_RejectOrdinaryTenantAndSupport(bool admin, bool support)
    {
        // Arrange
        _factory.GetActiveContext().IsCspAdmin = admin;
        _factory.GetActiveContext().ImpersonatedTenantId = support ? Guid.NewGuid() : null;
        // Act
        var read = await _client.GetAsync("/api/csp/offerings");
        var create = await Post("/api/csp/offerings", new { name = "Denied", description = "", environments = new[] { "AzureCloud" } });
        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FindingSourceStatus_DoesNotCloseWorkflow_AndClosureRequiresEvidence()
    {
        // Arrange
        var offering = await CreateOffering();
        var id = offering.GetProperty("offeringId").GetGuid();
        // Act
        var created = await Post($"/api/csp/offerings/{id}/findings", new
        {
            expectedOfferingRevision = offering.GetProperty("revision").GetInt64(),
            title = "Synthetic finding", observation = "Review required", severityAsStated = "Moderate",
            controlIds = new[] { "AC-2" }, citations = Array.Empty<object>()
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var finding = (await Json(created)).GetProperty("data");
        var review = await Post($"/api/csp/offerings/{id}/findings/{finding.GetProperty("findingId").GetGuid()}/reviews",
            new { expectedRevision = finding.GetProperty("revision").GetInt64(), evidenceIds = Array.Empty<Guid>(), disposition = "AcceptClosure", rationale = "No evidence" });
        // Assert
        finding.GetProperty("workflowState").GetString().Should().Be("Open");
        review.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<JsonElement> CreateOffering()
    {
        var response = await Post("/api/csp/offerings", new
        {
            name = $"Synthetic {Guid.NewGuid():N}", description = "Synthetic only",
            environments = new[] { "AzureUSGovernment" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await Json(response)).GetProperty("data");
    }

    private static object Boundary(long revision, string name, Guid? predecessor = null) => new
    {
        expectedOfferingRevision = revision, predecessorRevisionId = predecessor, name,
        scopeStatement = "Synthetic bounded scope, not evidence of authorization",
        services = Array.Empty<string>(), componentSnapshotIds = Array.Empty<Guid>(),
        includedScopes = Array.Empty<object>(), exclusions = Array.Empty<object>(),
        providerResponsibilities = Array.Empty<string>(), customerResponsibilities = Array.Empty<string>(),
        citations = Array.Empty<object>()
    };

    private async Task<HttpResponseMessage> Post(string path, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await _client.SendAsync(request);
    }

    private static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
}
