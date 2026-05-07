using System.Diagnostics;
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
/// T142 — Performance spot-check for SC-014: the
/// <c>/admin/imported-documents</c> view must render up to 200 artifact rows
/// (with `dependentsCount` populated) in under 2 seconds on the seeded dev DB.
/// </summary>
public class ImportedDocumentsPerformanceTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        var dbName = $"Perf_{Guid.NewGuid():N}";
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

        // Seed 200 templates + 5 dependencies on each — enough to exercise the
        // grouping + count path used to populate `dependentsCount`.
        var templateIds = new List<Guid>();
        for (int i = 0; i < 200; i++)
        {
            var id = Guid.NewGuid();
            templateIds.Add(id);
            db.OrganizationDocumentTemplates.Add(new OrganizationDocumentTemplate
            {
                Id = id, TenantId = TenantId,
                TemplateType = TemplateType.Ssp,
                Label = $"T{i:D3}", Version = "1",
                OriginalFileName = "x.docx", StorageBlobKey = "k",
                FileFormat = TemplateFileFormat.Docx, FileSizeBytes = 1,
                ContentChecksumSha256 = "x",
                ValidationStatus = TemplateValidationStatus.Compliant,
                Status = TemplateStatus.Active,
            });
        }
        foreach (var t in templateIds)
        {
            for (int d = 0; d < 5; d++)
            {
                db.WizardArtifactDependencies.Add(new WizardArtifactDependency
                {
                    Id = Guid.NewGuid(), TenantId = TenantId,
                    SourceArtifactType = ArtifactSourceKind.Template,
                    SourceArtifactId = t,
                    DependentType = ArtifactDependentKind.SspExport,
                    DependentId = Guid.NewGuid(),
                    IsStale = d == 0, // 1 stale dependent per template
                });
            }
        }
        await db.SaveChangesAsync();

        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{TenantId}|{AdminId}");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task TwoHundredRowInventory_RendersWithin2Seconds()
    {
        // Warm the EF model + first JIT pass.
        var warmup = await _client.GetAsync("/api/onboarding/imports?pageSize=200");
        warmup.StatusCode.Should().Be(HttpStatusCode.OK);

        var sw = Stopwatch.StartNew();
        var resp = await _client.GetAsync("/api/onboarding/imports?pageSize=200");
        sw.Stop();
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        sw.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "SC-014 requires the imported-documents 200-row view to render in under 2 s on the seeded dev DB");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var items = body.GetProperty("data").GetProperty("items");
        items.GetArrayLength().Should().Be(200);
        // Verify dependentsCount is populated on each row (the grouping path
        // is the dominant cost — guard against drift).
        items.EnumerateArray().Should()
            .OnlyContain(i => i.GetProperty("dependentsCount").GetInt32() == 5);
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
