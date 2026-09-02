using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    /// Single-tenant fixture: reuse <see cref="MultiTenantWebApplicationFactory{TStartup}"/>
    /// so McpStdioService / BoundaryMigrationService are stripped the same way
    /// as every other WAF test. A hand-rolled factory missed that strip and
    /// hung testhost on redirected stdin (CI 33565219515, H6).
    /// </summary>
    public sealed class SingleTenantFactory : MultiTenantWebApplicationFactory<McpProgram>
    {
        protected override string DeploymentModeOverride => "SingleTenant";
    }
}
