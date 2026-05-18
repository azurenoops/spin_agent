using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>
/// T157 [US7]: In <c>SingleTenant</c> mode, the entire <c>/api/csp/onboarding/*</c>
/// surface returns <c>404 SINGLE_TENANT_MODE</c> and no <c>CspProfile</c> row
/// is ever created. Acceptance scenario 4 from spec.md US7.
/// </summary>
/// <remarks>
/// RED until T163 (CspOnboardingEndpoints short-circuits in SingleTenant mode)
/// is implemented. Uses a dedicated fixture so the deployment-mode env var
/// can be flipped without polluting other tests.
/// </remarks>
public class CspOnboardingSingleTenantTests
    : IClassFixture<CspOnboardingSingleTenantTests.SingleTenantFactory>
{
    private readonly SingleTenantFactory _factory;
    private readonly HttpClient _client;

    public CspOnboardingSingleTenantTests(SingleTenantFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("GET", "/api/csp/onboarding/state")]
    [InlineData("POST", "/api/csp/onboarding/identity")]
    [InlineData("POST", "/api/csp/onboarding/support")]
    [InlineData("POST", "/api/csp/onboarding/classification")]
    [InlineData("POST", "/api/csp/onboarding/submit")]
    public async Task AllPaths_InSingleTenantMode_Return404_SingleTenantMode(
        string method,
        string path)
    {
        // Arrange
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            req.Content = JsonContent.Create(new { });
        }

        // Act
        using var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("SINGLE_TENANT_MODE");
    }

    [Fact]
    public async Task NoCspProfile_RowIsCreated_AfterAnySingleTenantOnboardingCall()
    {
        // Arrange
        // (multiple onboarding calls — none should persist anything)
        await _client.GetAsync("/api/csp/onboarding/state");
        await _client.PostAsJsonAsync("/api/csp/onboarding/identity", new
        {
            legalEntityName = "Should Not Persist",
            displayName = "Nope",
        });

        // Act
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // The SingleTenant fixture intentionally does NOT pre-create the
        // `CspProfiles` table — the endpoints must short-circuit so far
        // upstream that the table never even gets touched. Treat
        // "no such table" as a stronger form of "0 rows".
        int profileCount;
        try
        {
            profileCount = await db.Set<Ato.Copilot.Core.Models.Tenancy.CspProfile>()
                .IgnoreQueryFilters()
                .CountAsync();
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
            when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            profileCount = 0;
        }

        // Assert
        profileCount.Should().Be(0,
            "SingleTenant-mode onboarding endpoints must short-circuit BEFORE writing to the DB");
    }

    /// <summary>
    /// Single-tenant fixture: same SQLite-backed boot as the multi-tenant
    /// fixture but pins <c>Deployment:Mode = SingleTenant</c> via in-memory
    /// configuration (NOT env vars — env vars are process-global and race
    /// with the sibling <c>MultiTenantWebApplicationFactory</c>).
    /// </summary>
    public sealed class SingleTenantFactory : WebApplicationFactory<McpProgram>
    {
        private readonly string _sqliteFile = Path.Combine(
            Path.GetTempPath(),
            $"ato-copilot-tests-singletenant-{Guid.NewGuid():N}.db");

        public SingleTenantFactory()
        {
            // Set env vars for ALL config that does NOT differ between
            // sibling fixtures (DB provider/connection use SQLite in both).
            // Anything fixture-specific (Deployment:Mode) MUST go through
            // ConfigureAppConfiguration to avoid cross-fixture contamination
            // since env vars are process-global.
            Environment.SetEnvironmentVariable("ATO_Database__Provider", "Sqlite");
            Environment.SetEnvironmentVariable("ATO_ConnectionStrings__DefaultConnection",
                $"Data Source={_sqliteFile};Mode=ReadWriteCreate");
            Environment.SetEnvironmentVariable("ATO_Auth__Impersonation__SigningKey",
                "ato-copilot-tests-impersonation-signing-key-stable-32B!");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("ATO_Tenant__Resolution__BypassForTests", "true");
            Environment.SetEnvironmentVariable("ATO_Auth__BypassForTests", "true");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            // Pin Deployment:Mode = SingleTenant via in-memory configuration,
            // NOT env vars — env vars are process-global and would race with
            // the sibling MultiTenantWebApplicationFactory.
            builder.ConfigureAppConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Deployment:Mode"] = "SingleTenant",
                });
            });

            builder.ConfigureServices(services =>
            {
                services.Configure<HostOptions>(o =>
                    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

                // Same SQL-Server-only hosted service the multi-tenant factory
                // strips out — its StartAsync hard-fails on SQLite which would
                // tear down the test host. (Mirrors the multi-tenant fixture's
                // teardown protection per Feature 048 / T112.)
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    var d = services[i];
                    if (d.ServiceType == typeof(IHostedService) &&
                        (d.ImplementationType == typeof(Ato.Copilot.Core.Services.BoundaryMigrationService) ||
                         d.ImplementationInstance?.GetType() == typeof(Ato.Copilot.Core.Services.BoundaryMigrationService)))
                    {
                        services.RemoveAt(i);
                    }
                }
            });
        }
    }
}
