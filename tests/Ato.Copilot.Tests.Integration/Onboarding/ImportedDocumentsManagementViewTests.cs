using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration tests for the imports management view (T128 / FR-093).
/// </summary>
public class ImportedDocumentsManagementViewTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid AdminTenantId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        var dbName = $"Imports_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<IWizardArtifactInventoryService, WizardArtifactInventoryService>();
        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization(o =>
            o.AddPolicy(OnboardingAdministratorRequirement.PolicyName, p => p.RequireAssertion(_ => true)));

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapImportedDocumentsEndpoints();

        await _app.StartAsync();

        await using var scope = _app.Services.CreateAsyncScope();
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await f.CreateDbContextAsync();

        // Seed 250 templates so we can test cap.
        for (int i = 0; i < 250; i++)
        {
            db.OrganizationDocumentTemplates.Add(new OrganizationDocumentTemplate
            {
                Id = Guid.NewGuid(), TenantId = AdminTenantId,
                TemplateType = TemplateType.Ssp,
                Label = $"T{i}", Version = "1",
                OriginalFileName = "x.docx",
                StorageBlobKey = "k", FileFormat = TemplateFileFormat.Docx,
                FileSizeBytes = 1, ContentChecksumSha256 = "x",
                ValidationStatus = TemplateValidationStatus.Compliant,
                Status = TemplateStatus.Active,
            });
        }
        await db.SaveChangesAsync();

        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{AdminTenantId}|{AdminUserId}");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task Get_ReturnsPaginatedItems()
    {
        var response = await _client.GetAsync("/api/onboarding/imports?pageSize=50&page=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        var data = body.GetProperty("data");
        data.GetProperty("total").GetInt32().Should().Be(250);
        data.GetProperty("items").GetArrayLength().Should().Be(50);
    }

    [Fact]
    public async Task Get_PageSizeCapsAt200()
    {
        var response = await _client.GetAsync("/api/onboarding/imports?pageSize=999");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("data").GetProperty("pageSize").GetInt32().Should().Be(200);
        body.GetProperty("data").GetProperty("items").GetArrayLength().Should().Be(200);
    }

    [Fact]
    public async Task Get_FilterByKind_ReturnsOnlyMatchingRows()
    {
        var response = await _client.GetAsync("/api/onboarding/imports?kind=Template&pageSize=200");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var items = body.GetProperty("data").GetProperty("items");
        items.EnumerateArray().Should()
            .OnlyContain(i => i.GetProperty("kind").GetInt32() == (int)ArtifactSourceKind.Template
                || i.GetProperty("kind").GetString() == "Template");
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var header))
                return Task.FromResult(AuthenticateResult.NoResult());
            var parts = header.ToString().Split('|', 2);
            var claims = new List<Claim>
            {
                new("tid", parts[0]),
                new("oid", parts[1]),
                new(ClaimTypes.Name, "test"),
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, AuthScheme)),
                    AuthScheme)));
        }
    }
}
