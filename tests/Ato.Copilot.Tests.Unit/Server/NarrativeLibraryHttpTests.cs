using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Server;

public sealed class NarrativeLibraryHttpTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenantId = Guid.NewGuid();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Mock<IControlNarrativeService> _generator = new();
    private const string Root = "/api/systems/synthetic-system/narrative-library";

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Synthetic").AddScheme<AuthenticationSchemeOptions, SyntheticAuthentication>("Synthetic", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ITenantContext>(new TenantContext(_tenantId));
        builder.Services.AddDbContext<AtoCopilotContext>(options => options.UseSqlite(_connection));
        builder.Services.AddScoped<NarrativeLibraryService>();
        builder.Services.AddScoped<NarrativeProposalService>();
        builder.Services.AddScoped<ProviderNarrativeLibraryService>();
        _generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Reviewed synthetic proposal", [], []));
        builder.Services.AddSingleton(_generator.Object);
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapNarrativeLibraryEndpoints();
        _app.MapScopedNarrativeLibraryEndpoints();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            await db.Database.EnsureCreatedAsync();
            db.RegisteredSystems.Add(new() { TenantId = _tenantId, Id = "synthetic-system", Name = "Synthetic" });
            db.RmfRoleAssignments.AddRange(
                new() { TenantId = _tenantId, RegisteredSystemId = "synthetic-system", UserId = "author", RmfRole = RmfRole.Isso },
                new() { TenantId = _tenantId, RegisteredSystemId = "synthetic-system", UserId = "reviewer", RmfRole = RmfRole.Issm });
            db.ControlImplementations.Add(new() { TenantId = _tenantId, RegisteredSystemId = "synthetic-system", ControlId = "AC-2",
                PolicyNarrative = "Approved policy", TechnicalNarrative = "Approved technical", ApprovalStatus = SspSectionStatus.Approved });
            await db.SaveChangesAsync();
        }
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Synthetic-Actor", "author");
    }

    [Fact]
    public async Task ProposalDetailResolvesDurableIdBeyondListWindow()
    {
        // Arrange
        Guid id;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NarrativeProposalService>();
            id = (await service.QueueAsync(new(_tenantId, "synthetic-system", ["AC-2"], ["Policy"],
                "System", "synthetic-system", "source-editor"))).ProposalIds.Single();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.NarrativeProposals.SingleAsync()).CreatedAt = DateTime.UtcNow.AddDays(-1);
            db.NarrativeProposals.AddRange(Enumerable.Range(0, 501).Select(index => new NarrativeProposal
            {
                TenantId = _tenantId, RegisteredSystemId = "synthetic-system", ControlId = "AC-2",
                NarrativeType = "Policy", DeduplicationKey = $"newer-{index}", CreatedBy = "source-editor"
            }));
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"{Root}/proposals/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await response.Content.ReadFromJsonAsync<NarrativeProposalResponse>();
        proposal!.Id.Should().Be(id);
        proposal.Status.Should().Be("PendingGeneration");
        proposal.CanReview.Should().BeFalse();
    }

    [Fact]
    public async Task PendingGenerationCannotBeApprovedBeforeItBecomesAReviewedDraft()
    {
        // Arrange
        Guid id;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NarrativeProposalService>();
            id = (await service.QueueAsync(new(_tenantId, "synthetic-system", ["AC-2"], ["Policy"],
                "System", "synthetic-system", "source-editor"))).ProposalIds.Single();
        }
        _client.DefaultRequestHeaders.Remove("X-Synthetic-Actor");
        _client.DefaultRequestHeaders.Add("X-Synthetic-Actor", "reviewer");

        // Act
        var premature = await _client.PostAsJsonAsync($"{Root}/proposals/{id}/review", new ReviewNarrativeProposalRequest(1, "Approve", "Too early"));
        var generated = await _client.PostAsJsonAsync($"{Root}/proposals/{id}/generate", new { expectedRevision = 1 });
        var draft = await generated.Content.ReadFromJsonAsync<NarrativeProposalResponse>();
        var accepted = await _client.PostAsJsonAsync($"{Root}/proposals/{id}/review",
            new ReviewNarrativeProposalRequest(draft!.Revision, "Approve", "Reviewed generated proposal"));

        // Assert
        premature.StatusCode.Should().Be(HttpStatusCode.Conflict);
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var inspect = _app.Services.CreateAsyncScope();
        var db = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.NarrativeProposals.SingleAsync()).Id.Should().Be(id);
        (await db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Reviewed synthetic proposal");
    }

    [Theory]
    [InlineData("transport", HttpStatusCode.BadGateway, "GENERATION_FAILED")]
    [InlineData("timeout", HttpStatusCode.GatewayTimeout, "GENERATION_TIMEOUT")]
    [InlineData("cancelled", HttpStatusCode.BadGateway, "GENERATION_CANCELLED")]
    public async Task QueuedGenerationReturnsExplicitModelFailureAndRetainsRetryState(
        string failure, HttpStatusCode expectedStatus, string expectedCode)
    {
        // Arrange
        Guid id;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NarrativeProposalService>();
            id = (await service.QueueAsync(new(_tenantId, "synthetic-system", ["AC-2"], ["Technical"],
                "System", "synthetic-system", "source-editor"))).ProposalIds.Single();
        }
        Exception exception = failure switch
        {
            "transport" => new HttpRequestException("Synthetic model transport failure"),
            "timeout" => new TimeoutException("Synthetic model timeout"),
            _ => new OperationCanceledException("Synthetic model cancellation")
        };
        _generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        // Act
        var response = await _client.PostAsJsonAsync($"{Root}/proposals/{id}/generate", new { expectedRevision = 1 });

        // Assert
        response.StatusCode.Should().Be(expectedStatus);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString().Should().Be(expectedCode);
        var retained = (await _client.GetFromJsonAsync<NarrativeProposalResponse[]>($"{Root}/proposals"))!.Single();
        retained.Id.Should().Be(id);
        retained.Status.Should().Be("GenerationFailed");
        retained.GenerationErrorCode.Should().Be(expectedCode);
    }

    [Fact]
    public async Task QueuedGenerationEndpointRetriesSameIdAndPreservesApprovedContent()
    {
        // Arrange
        Guid proposalId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NarrativeProposalService>();
            proposalId = (await service.QueueAsync(new(_tenantId, "synthetic-system", ["AC-2"], ["Policy"],
                "System", "synthetic-system", "source-editor", "queued-impact"))).ProposalIds.Single();
        }
        _generator.SetupSequence(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI_NOT_AVAILABLE: Synthetic model unavailable"))
            .ReturnsAsync(new GroundedNarrativeDraft("Successful queued retry", [], []));

        // Act
        var unavailable = await _client.PostAsJsonAsync($"{Root}/proposals/{proposalId}/generate", new { expectedRevision = 1 });
        unavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var failed = (await _client.GetFromJsonAsync<NarrativeProposalResponse[]>($"{Root}/proposals"))!.Single();
        var retried = await _client.PostAsJsonAsync($"{Root}/proposals/{proposalId}/generate", new { expectedRevision = failed.Revision });
        retried.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = await retried.Content.ReadFromJsonAsync<NarrativeProposalResponse>();
        var stale = await _client.PostAsJsonAsync($"{Root}/proposals/{proposalId}/generate", new { expectedRevision = 1 });

        // Assert
        failed.Status.Should().Be("GenerationFailed");
        failed.GenerationErrorCode.Should().Be("AI_NOT_AVAILABLE");
        draft!.Id.Should().Be(proposalId);
        draft.Status.Should().Be("Draft");
        draft.ProposedContent.Should().Be("Successful queued retry");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var inspect = _app.Services.CreateAsyncScope();
        var db = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.NarrativeProposals.CountAsync()).Should().Be(1);
        (await db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Approved policy");
    }

    [Fact]
    public async Task QueuedGenerationEndpointRejectsUnassignedActorBeforeModelCall()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("X-Synthetic-Actor");
        _client.DefaultRequestHeaders.Add("X-Synthetic-Actor", "stranger");

        // Act
        var response = await _client.PostAsJsonAsync($"{Root}/proposals/{Guid.NewGuid()}/generate", new { expectedRevision = 1 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _generator.Verify(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReceiptHistoryEndpointShowsImmutableSourceDeliveries()
    {
        // Arrange
        Guid proposalId;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NarrativeProposalService>();
            var request = new NarrativeChangeImpactRequest(_tenantId, "synthetic-system", ["AC-2"], ["Technical"],
                "CspCapability", Guid.NewGuid().ToString(), "first-source", "impact-one",
                new("revision-one", "ProviderChanged", "baseline", "subscription"));
            proposalId = (await service.QueueAsync(request)).ProposalIds.Single();
            await service.QueueAsync(request with { Actor = "second-source", ImpactId = "impact-two",
                SourceContext = request.SourceContext! with { SourceRevision = "revision-two" } });
        }

        // Act
        var response = await _client.GetAsync($"{Root}/proposals/{proposalId}/impact-receipts?page=1&pageSize=1");
        var history = await response.Content.ReadFromJsonAsync<NarrativeImpactReceiptPage>();
        var missing = await _client.GetAsync($"{Root}/proposals/{Guid.NewGuid()}/impact-receipts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        history!.TotalCount.Should().Be(2);
        history.Items.Should().ContainSingle();
        history.Items[0].SourceActor.Should().Be("second-source");
        history.Items[0].SourceContext!.SourceRevision.Should().Be("revision-two");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OrganizationLibraryUsesOrganizationRouteAndCannotEnterProviderLibrary()
    {
        // Arrange
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var person = new Ato.Copilot.Core.Models.Onboarding.Person
            { TenantId = _tenantId, DisplayName = "Organization publisher", Email = "publisher@example.invalid" };
            db.Persons.Add(person);
            db.OrganizationRoleAssignments.Add(new() { TenantId = _tenantId, PersonId = person.Id, Person = person,
                Role = Ato.Copilot.Core.Models.Onboarding.OrganizationRole.Administrator });
            db.NistControls.Add(new() { Id = "ac-2", Title = "Account management" });
            await db.SaveChangesAsync();
            _client.DefaultRequestHeaders.Remove("X-Synthetic-Actor");
            _client.DefaultRequestHeaders.Add("X-Synthetic-Actor", person.Id.ToString());
        }
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Organization reference"), "title" },
            { new StringContent("Organization"), "scope" },
            { new StringContent(_tenantId.ToString()), "scopeId" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("AC-2\nPolicy Narrative:\nOrganization reference claim.")), "file", "reference.txt" }
        };

        // Act
        var imported = await _client.PostAsync("/api/narrative-library/imports", form);
        var reference = await imported.Content.ReadFromJsonAsync<NarrativeReferenceResponse>();
        var published = await _client.PostAsJsonAsync($"/api/narrative-library/{reference!.Id}/publish",
            new PublishNarrativeReferenceRequest(reference.Revision, true, reference.Passages));
        var provider = await _client.GetAsync("/api/csp/narrative-library");

        // Assert
        imported.StatusCode.Should().Be(HttpStatusCode.Created);
        published.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var inspect = _app.Services.CreateAsyncScope();
        var dbAfter = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await dbAfter.NarrativeReferences.SingleAsync()).ImportedForSystemId.Should().BeNull();
        (await dbAfter.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Approved policy");
    }

    [Fact]
    public async Task ImportEditPublishGenerateReviewUsesRealSqliteAndPreservesApprovedCompanion()
    {
        // Arrange
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Synthetic reference"), "title" },
            { new StringContent("System"), "scope" },
            { new StringContent("synthetic-system"), "scopeId" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("Unmapped reference")), "file", "reference.txt" }
        };

        // Act
        var imported = await _client.PostAsync($"{Root}/imports", form);
        imported.StatusCode.Should().Be(HttpStatusCode.Created);
        var reference = await imported.Content.ReadFromJsonAsync<NarrativeReferenceResponse>();
        var mappings = new[] { new NarrativeReferencePassage("AC-2", "Policy", "Reference claim; not implementation evidence") };
        var edited = await _client.PatchAsJsonAsync($"{Root}/{reference!.Id}",
            new UpdateNarrativeReferenceDraftRequest(1, "System", "synthetic-system", mappings));
        var published = await _client.PostAsJsonAsync($"{Root}/{reference.Id}/publish",
            new PublishNarrativeReferenceRequest(2, true, mappings));
        var generated = await _client.PostAsJsonAsync($"{Root}/proposals", new GenerateNarrativeProposalRequest("AC-2", "Policy", 1));
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await generated.Content.ReadFromJsonAsync<NarrativeProposalResponse>();
        _client.DefaultRequestHeaders.Remove("X-Synthetic-Actor");
        _client.DefaultRequestHeaders.Add("X-Synthetic-Actor", "reviewer");
        var accepted = await _client.PostAsJsonAsync($"{Root}/proposals/{proposal!.Id}/review", new ReviewNarrativeProposalRequest(1, "Approve", "Synthetic review"));

        // Assert
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        published.StatusCode.Should().Be(HttpStatusCode.OK);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var active = await db.ControlImplementations.SingleAsync();
        active.PolicyNarrative.Should().Be("Reviewed synthetic proposal");
        active.TechnicalNarrative.Should().Be("Approved technical");
        (await db.EvidenceArtifacts.CountAsync()).Should().Be(0);
        (await db.NarrativeReviews.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("stranger", HttpStatusCode.Forbidden)]
    public async Task ReadsRequireAuthenticationAndAssignment(string? actor, HttpStatusCode expected)
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("X-Synthetic-Actor");
        if (actor is not null) _client.DefaultRequestHeaders.Add("X-Synthetic-Actor", actor);

        // Act
        var response = await _client.GetAsync(Root);

        // Assert
        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task ModelUnavailableReturns503WithoutChangingActiveContent()
    {
        // Arrange
        _generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI_NOT_AVAILABLE: Synthetic unavailable model"));

        // Act
        var response = await _client.PostAsJsonAsync($"{Root}/proposals", new GenerateNarrativeProposalRequest("AC-2", "Policy", 1));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("AI_NOT_AVAILABLE");
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Approved policy");
        (await db.NarrativeProposals.CountAsync()).Should().Be(0);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    internal sealed class SyntheticAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Synthetic-Actor", out var actor))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("oid", actor.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
