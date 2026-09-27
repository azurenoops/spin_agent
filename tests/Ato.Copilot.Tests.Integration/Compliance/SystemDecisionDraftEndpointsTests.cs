using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
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

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class SystemDecisionDraftEndpointsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Mock<ISapService> _sap = new();
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var database = Guid.NewGuid().ToString();
        builder.Services.AddScoped<ITenantContext>(_ => new TenantContext(Tenant));
        builder.Services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddScoped<IAuthorizationService, AuthorizationService>();
        builder.Services.AddSingleton(_sap.Object);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICurrentUserService, CurrentUserService>();
        builder.Services.AddAuthentication("DecisionDraftTest")
            .AddScheme<AuthenticationSchemeOptions, TestAuth>("DecisionDraftTest", _ => { });
        builder.Services.AddAuthorization(Policies.RegisterPolicies);
        _app = builder.Build(); _app.UseAuthentication(); _app.UseAuthorization();
        _app.MapSystemDecisionDraftEndpoints();
        await _app.StartAsync(); _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("Test-Role", ComplianceRoles.AuthorizingOfficial);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.RegisteredSystems.Add(new RegisteredSystem { Id = "a", Name = "Mission", TenantId = Tenant });
        db.EvidenceArtifacts.Add(new EvidenceArtifact { Id = "source", RegisteredSystemId = "a", TenantId = Tenant,
            FileName = "decision.pdf", ContentHash = Hash, UploadedBy = "uploader", ContentType = "application/pdf" });
        db.AuthorizationPackages.Add(new AuthorizationPackage { Id = "package", RegisteredSystemId = "a", TenantId = Tenant,
            Status = PackageStatus.Completed, ContentHash = Hash, FilePath = "retained-package.zip", GeneratedBy = "generator", ExpiresAt = DateTimeOffset.UtcNow.AddDays(30) });
        db.SecurityAssessmentPlans.Add(new SecurityAssessmentPlan { Id = "sap", RegisteredSystemId = "a", TenantId = Tenant,
            Title = "Initial plan", Content = "retained draft", BaselineLevel = "Low", GeneratedBy = "author" });
        await db.SaveChangesAsync();
    }
    private static ExternalAuthorizationRecordInput Input() => new("ATO", new DateTime(2026, 1, 1), new DateTime(2027, 1, 1),
        "Medium", "source", Hash, "External issuing authority", "package", Hash, "Recorded conditions", true, null);

    [Fact]
    public async Task ExternalRecord_RequiresAo_AndDoesNotTrustPostedActor()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("Test-Role"); _client.DefaultRequestHeaders.Add("Test-Role", ComplianceRoles.Viewer);
        // Act
        var denied = await _client.PostAsJsonAsync("/api/dashboard/systems/a/authorization/records", Input());
        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Arrange / Act
        _client.DefaultRequestHeaders.Remove("Test-Role"); _client.DefaultRequestHeaders.Add("Test-Role", ComplianceRoles.AuthorizingOfficial);
        var response = await _client.PostAsJsonAsync("/api/dashboard/systems/a/authorization/records", Input());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        result.GetProperty("recordedBy").GetString().Should().Be("real-actor");
        result.GetProperty("baselinePackageId").GetString().Should().Be("package");
    }

    [Fact]
    public async Task ExternalRecord_ChangedBaselineIsConflict()
    {
        // Arrange / Act
        var response = await _client.PostAsJsonAsync("/api/dashboard/systems/a/authorization/records", Input() with { ExpectedPackageHash = "wrong" });
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ExternalContext_ReturnsRetainedSourceAndBaselineChoices()
    {
        // Arrange / Act
        var response = await _client.GetFromJsonAsync<JsonElement>("/api/dashboard/systems/a/authorization/record-context");
        // Assert
        response.GetProperty("sourceEvidence")[0].GetProperty("contentHash").GetString().Should().Be(Hash);
        response.GetProperty("completedPackages")[0].GetProperty("id").GetString().Should().Be("package");
    }

    [Fact]
    public async Task DocumentMetadata_RequiresAuthentication()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("Test-Role");
        // Act
        var response = await _client.GetAsync("/api/dashboard/systems/a/authorization/record-context");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RecordLocation_IsReadableOnlyUnderItsSystem()
    {
        // Arrange
        var created = await _client.PostAsJsonAsync("/api/dashboard/systems/a/authorization/records", Input());
        var location = created.Headers.Location!.ToString();
        // Act
        var record = await _client.GetAsync(location);
        var wrong = await _client.GetAsync(location.Replace("/systems/a/", "/systems/b/"));
        // Assert
        record.StatusCode.Should().Be(HttpStatusCode.OK);
        wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SapUpdate_RequiresExistingAssessmentAuthority_AndExactSystemBinding()
    {
        // Arrange
        var request = new { title = "Manual title", assessmentLead = "Lead", scopeNotes = "Scope", assessmentApproach = "Approach", expectedContentHash = Hash };
        // Act
        var denied = await _client.PutAsJsonAsync("/api/v1/systems/a/sap/sap/draft", request);
        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _client.DefaultRequestHeaders.Remove("Test-Role"); _client.DefaultRequestHeaders.Add("Test-Role", ComplianceRoles.Auditor);
        // Act
        var wrong = await _client.PutAsJsonAsync("/api/v1/systems/b/sap/sap/draft", request);
        // Assert
        wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _sap.Verify(x => x.UpdateSapAsync(It.IsAny<SapUpdateInput>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SapUpdate_PassesManualFieldsToExistingService_WithCurrentHash()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("Test-Role"); _client.DefaultRequestHeaders.Add("Test-Role", ComplianceRoles.Auditor);
        var current = await _client.GetFromJsonAsync<JsonElement>("/api/v1/systems/a/sap/draft");
        var expectedHash = current.GetProperty("draftHash").GetString();
        _sap.Setup(x => x.UpdateSapAsync(It.IsAny<SapUpdateInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SapDocument { SapId = "sap", SystemId = "a" });
        // Act
        var response = await _client.PutAsJsonAsync("/api/v1/systems/a/sap/sap/draft", new
        { title = "Reviewed title", assessmentLead = "Assigned lead", scopeNotes = "Mission boundary", assessmentApproach = "Examine, Interview, Test", expectedContentHash = expectedHash });
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _sap.Verify(x => x.UpdateSapAsync(It.Is<SapUpdateInput>(value => value.SapId == "sap"
            && value.Title == "Reviewed title" && value.AssessmentLead == "Assigned lead"
            && value.ScopeNotes == "Mission boundary" && value.AssessmentApproach == "Examine, Interview, Test"
            && value.ExpectedContentHash == expectedHash), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SapUpdate_RejectsFinalizedPlanBeforeCallingService()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("Test-Role"); _client.DefaultRequestHeaders.Add("Test-Role", ComplianceRoles.Auditor);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.SecurityAssessmentPlans.SingleAsync()).Status = SapStatus.Finalized;
            await db.SaveChangesAsync();
        }
        // Act
        var response = await _client.PutAsJsonAsync("/api/v1/systems/a/sap/sap/draft",
            new { title = "Changed", assessmentLead = "Lead", scopeNotes = "Scope", assessmentApproach = "Approach", expectedContentHash = Hash });
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _sap.Verify(x => x.UpdateSapAsync(It.IsAny<SapUpdateInput>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(!Request.Headers.ContainsKey("Test-Role")
            ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "real-actor"),
                new Claim(ClaimTypes.Name, "Recording reviewer"), new Claim(ClaimTypes.Role, Request.Headers["Test-Role"].ToString())], Scheme.Name)), Scheme.Name)));
    }
}
