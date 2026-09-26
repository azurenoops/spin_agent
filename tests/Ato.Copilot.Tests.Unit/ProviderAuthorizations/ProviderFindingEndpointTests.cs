using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Mcp.Endpoints.Csp;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderFindingEndpointTests : IAsyncLifetime
{
    private readonly Mock<IProviderFindingService> _service = new(MockBehavior.Strict);
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Guid _offering = Guid.NewGuid();
    private readonly Guid _finding = Guid.NewGuid();
    private string Root => $"/api/csp/offerings/{_offering}";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_service.Object);
        builder.Services.AddAuthentication("Synthetic")
            .AddScheme<AuthenticationSchemeOptions, SyntheticAuthHandler>("Synthetic", _ => { });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapProviderFindingEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Lists_DefaultPaging_UseStandardEnvelope()
    {
        // Arrange
        _service.Setup(x => x.ListFindingsAsync(_offering, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProviderFindingResponse>([], 1, 25, 0));
        // Act
        var response = await _client.GetAsync(Root + "/findings");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.GetProperty("status").GetString().Should().Be("success");
        json.GetProperty("data").GetProperty("pageSize").GetInt32().Should().Be(25);
        json.TryGetProperty("metadata", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_RequiresIdempotency_AndReturnsCreatedLocation()
    {
        // Arrange
        var input = new CreateProviderFindingRequest(1, "Synthetic", "Observation", null, [], []);
        _service.Setup(x => x.CreateFindingAsync(_offering, It.IsAny<CreateProviderFindingRequest>(), "create", "synthetic-actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderFindingResponse(_finding, _offering, 1, input.Title, input.Observation, null,
                "Open", null, [], [], DateTimeOffset.UtcNow));
        // Act
        var missing = await _client.PostAsJsonAsync(Root + "/findings", input);
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/findings") { Content = JsonContent.Create(input) };
        request.Headers.Add("Idempotency-Key", "create");
        var response = await _client.SendAsync(request);
        // Assert
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("findingId").GetGuid().Should().Be(_finding);
    }

    [Fact]
    public async Task Upload_ParsesRevisionAndBytes_AndReturnsPendingReceipt()
    {
        // Arrange
        _service.Setup(x => x.SubmitEvidenceAsync(_offering, _finding, It.IsAny<SubmitProviderFindingEvidenceRequest>(),
            "upload", "synthetic-actor", It.IsAny<CancellationToken>()))
            .Returns(async (Guid offering, Guid finding, SubmitProviderFindingEvidenceRequest input, string _, string _, CancellationToken ct) =>
            {
                using var reader = new StreamReader(input.Content);
                (await reader.ReadToEndAsync(ct)).Should().Be("Synthetic content");
                input.ExpectedFindingRevision.Should().Be(3);
                input.Description.Should().Be("Evidence description");
                return new(Guid.NewGuid(), finding, offering, 4, input.FileName, input.MediaType, 17, "hash",
                    input.Description, "PendingReview", DateTimeOffset.UtcNow, null);
            });
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("3"), "expectedFindingRevision");
        form.Add(new StringContent("Evidence description"), "description");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Synthetic content")), "file", "synthetic.txt");
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + $"/findings/{_finding}/evidence") { Content = form };
        request.Headers.Add("Idempotency-Key", "upload");
        // Act
        var response = await _client.SendAsync(request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("state").GetString().Should().Be("PendingReview");
    }

    [Fact]
    public async Task Content_IsProtectedAttachment_NotJsonOrPublicCache()
    {
        // Arrange
        var evidence = Guid.NewGuid();
        _service.Setup(x => x.ContentAsync(_offering, _finding, evidence, "synthetic-actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderFindingContent(new MemoryStream(Encoding.UTF8.GetBytes("Retained")), "evidence.txt", "text/plain"));
        // Act
        var response = await _client.GetAsync(Root + $"/findings/{_finding}/evidence/{evidence}/content");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().Be("Retained");
    }

    [Fact]
    public async Task ReviewAndStorageErrors_AreExplicitEnvelopes()
    {
        // Arrange
        _service.Setup(x => x.ReviewAsync(_offering, _finding, It.IsAny<ReviewProviderFindingRequest>(), "synthetic-actor", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Stale review"));
        var evidence = Guid.NewGuid();
        _service.Setup(x => x.ContentAsync(_offering, _finding, evidence, "synthetic-actor", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Synthetic restricted storage error"));
        // Act
        var review = await _client.PostAsJsonAsync(Root + $"/findings/{_finding}/reviews",
            new ReviewProviderFindingRequest(1, [], "AcceptClosure", "Stale"));
        var content = await _client.GetAsync(Root + $"/findings/{_finding}/evidence/{evidence}/content");
        // Assert
        review.StatusCode.Should().Be(HttpStatusCode.Conflict);
        content.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var json = await content.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetProperty("errorCode").GetString().Should().Be("PROVIDER_EVIDENCE_UNAVAILABLE");
        (await content.Content.ReadAsStringAsync()).Should().NotContain("Synthetic restricted");
    }

    [Fact]
    public async Task AllRoutes_RequireAuthentication_BeforeCallingService()
    {
        // Arrange
        _client.DefaultRequestHeaders.Add("X-Synthetic-Anonymous", "true");
        var routes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, "/findings"), (HttpMethod.Post, "/findings"),
            (HttpMethod.Get, "/poam-items"), (HttpMethod.Post, "/poam-items"),
            (HttpMethod.Patch, $"/poam-items/{Guid.NewGuid()}"),
            (HttpMethod.Get, $"/findings/{_finding}/evidence"),
            (HttpMethod.Post, $"/findings/{_finding}/evidence"),
            (HttpMethod.Get, $"/findings/{_finding}/evidence/{Guid.NewGuid()}/content"),
            (HttpMethod.Post, $"/findings/{_finding}/reviews")
        };
        // Act
        foreach (var route in routes)
        {
            using var request = new HttpRequestMessage(route.Method, Root + route.Path);
            var response = await _client.SendAsync(request);
            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PoamAndEvidenceRoutes_ForwardExactIdsRevisionsAndPaging()
    {
        // Arrange
        var poamId = Guid.NewGuid();
        var plan = new ProviderPoamResponse(poamId, _offering, 1, "Plan", [_finding], "Correct", null,
            [], "Open", null, [], DateTimeOffset.UtcNow);
        _service.Setup(x => x.ListPoamAsync(_offering, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProviderPoamResponse>([plan], 2, 10, 11));
        _service.Setup(x => x.CreatePoamAsync(_offering, It.IsAny<CreateProviderPoamRequest>(),
            "plan-key", "synthetic-actor", It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        _service.Setup(x => x.UpdatePoamAsync(_offering, poamId,
            It.Is<UpdateProviderPoamRequest>(r => r.ExpectedRevision == 1 && r.WorkflowState == "InProgress"),
            "synthetic-actor", It.IsAny<CancellationToken>())).ReturnsAsync(plan with { Revision = 2, WorkflowState = "InProgress" });
        _service.Setup(x => x.ListEvidenceAsync(_offering, _finding, 2, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProviderFindingEvidenceResponse>([], 2, 5, 0));
        using var create = new HttpRequestMessage(HttpMethod.Post, Root + "/poam-items")
        {
            Content = JsonContent.Create(new CreateProviderPoamRequest(1, "Plan", [_finding], "Correct", null, [], []))
        };
        create.Headers.Add("Idempotency-Key", "plan-key");
        // Act
        var created = await _client.SendAsync(create);
        var listed = await _client.GetAsync(Root + "/poam-items?page=2&pageSize=10");
        var updated = await _client.PatchAsJsonAsync(Root + $"/poam-items/{poamId}",
            new UpdateProviderPoamRequest(1, "Correct", null, [], "InProgress"));
        var evidence = await _client.GetAsync(Root + $"/findings/{_finding}/evidence?page=2&pageSize=5");
        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location.Should().NotBeNull();
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await listed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("total").GetInt32().Should().Be(11);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("revision").GetInt32().Should().Be(2);
        evidence.StatusCode.Should().Be(HttpStatusCode.OK);
        _service.VerifyAll();
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("wrong-name")]
    [InlineData("bad-revision")]
    [InlineData("oversized")]
    [InlineData("duplicate-description")]
    public async Task Upload_RejectsMalformedOrUnboundedMultipartBeforeService(string kind)
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(kind == "bad-revision" ? "-1" : "1"), "expectedFindingRevision");
        form.Add(new StringContent("Synthetic"), "description");
        if (kind == "duplicate-description") form.Add(new StringContent("Duplicate"), "description");
        if (kind != "missing-file")
            form.Add(new ByteArrayContent(new byte[kind == "oversized" ? 10 * 1024 * 1024 + 1 : 1]),
                kind == "wrong-name" ? "unexpected" : "file", "synthetic.bin");
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + $"/findings/{_finding}/evidence") { Content = form };
        request.Headers.Add("Idempotency-Key", "malformed");
        // Act
        var response = await _client.SendAsync(request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("INVALID_PROVIDER_REQUEST");
        _service.VerifyNoOtherCalls();
    }

    private sealed class SyntheticAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Synthetic-Anonymous")
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "synthetic-actor")], Scheme.Name)), Scheme.Name)));
    }
}
