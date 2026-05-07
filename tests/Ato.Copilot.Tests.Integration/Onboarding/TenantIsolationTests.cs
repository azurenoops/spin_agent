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
/// T137 — Cross-tenant query-tampering test. Confirms that every wizard
/// entity surfaced by an endpoint is filtered at the EF query level by the
/// authenticated tenant id, and that requests bearing a different tenant
/// claim never see another tenant's data.
/// Constitution IV (Azure Government &amp; Compliance First) requires
/// per-tenant data residency.
/// </summary>
public class TenantIsolationTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid AdminUserA = Guid.NewGuid();
    private static readonly Guid AdminUserB = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        var dbName = $"Tenant_{Guid.NewGuid():N}";
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

        // Seed each artifact-source kind for both tenants so the test
        // covers the four ArtifactSourceKind values (Template,
        // EmassImportSession, SspPdfImportSession, NarrativeSeedDocument).
        SeedTenantArtifacts(db, TenantA, "A");
        SeedTenantArtifacts(db, TenantB, "B");
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private static void SeedTenantArtifacts(AtoCopilotContext db, Guid tenantId, string tag)
    {
        db.OrganizationDocumentTemplates.Add(new OrganizationDocumentTemplate
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            TemplateType = TemplateType.Ssp, Label = $"tpl-{tag}", Version = "1",
            OriginalFileName = "x.docx", StorageBlobKey = "k",
            FileFormat = TemplateFileFormat.Docx, FileSizeBytes = 1,
            ContentChecksumSha256 = "x",
            ValidationStatus = TemplateValidationStatus.Compliant,
            Status = TemplateStatus.Active,
        });
        db.EmassImportSessions.Add(new EmassImportSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            OriginalFileName = $"emass-{tag}.zip", StorageBlobKey = "k",
            FileSizeBytes = 1, Status = EmassImportStatus.Imported,
        });
        db.SspPdfImportSessions.Add(new SspPdfImportSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, BatchId = Guid.NewGuid(),
            OriginalFileName = $"ssp-{tag}.pdf", StorageBlobKey = "k",
            FileSizeBytes = 1, Status = SspPdfStatus.Imported,
        });
        db.NarrativeSeedDocuments.Add(new NarrativeSeedDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EvidenceArtifactId = Guid.Empty,
            Label = $"seed-{tag}", Tags = "[]",
            IndexingStatus = NarrativeSeedIndexingStatus.Indexed,
        });
    }

    private HttpClient ClientFor(Guid tenantId, Guid userId)
    {
        var c = _app.GetTestClient();
        c.DefaultRequestHeaders.Add("X-Test-User", $"{tenantId}|{userId}");
        return c;
    }

    [Fact]
    public async Task Imports_Inventory_OnlyShowsCallingTenantsArtifacts()
    {
        using var clientA = ClientFor(TenantA, AdminUserA);
        using var clientB = ClientFor(TenantB, AdminUserB);

        var aResp = await clientA.GetAsync("/api/onboarding/imports?pageSize=200");
        var bResp = await clientB.GetAsync("/api/onboarding/imports?pageSize=200");
        aResp.StatusCode.Should().Be(HttpStatusCode.OK);
        bResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var aBody = await aResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var bBody = await bResp.Content.ReadFromJsonAsync<JsonElement>(Json);

        // Each tenant should see exactly 4 rows (1 per artifact source kind).
        aBody.GetProperty("data").GetProperty("total").GetInt32().Should().Be(4);
        bBody.GetProperty("data").GetProperty("total").GetInt32().Should().Be(4);

        // Labels must not bleed across tenants.
        var aLabels = aBody.GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("label").GetString()).ToList();
        aLabels.Should().OnlyContain(l => l!.Contains("-A"));

        var bLabels = bBody.GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("label").GetString()).ToList();
        bLabels.Should().OnlyContain(l => l!.Contains("-B"));
    }

    [Fact]
    public async Task Imports_Dependents_404_WhenArtifactBelongsToOtherTenant()
    {
        // Find Tenant A's template id directly from the DB.
        await using var scope = _app.Services.CreateAsyncScope();
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await f.CreateDbContextAsync();
        var aTemplateId = await db.OrganizationDocumentTemplates
            .Where(t => t.TenantId == TenantA).Select(t => t.Id).FirstAsync();

        // Tenant B asks for tenant A's artifact dependents — must be 404.
        using var clientB = ClientFor(TenantB, AdminUserB);
        var resp = await clientB.GetAsync(
            $"/api/onboarding/imports/{aTemplateId}/dependents?kind=Template");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_Caller_Forbidden()
    {
        using var c = _app.GetTestClient(); // no X-Test-User header
        var resp = await c.GetAsync("/api/onboarding/imports?pageSize=10");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
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
