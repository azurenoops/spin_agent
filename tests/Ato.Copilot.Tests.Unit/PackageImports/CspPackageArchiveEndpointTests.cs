using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Endpoints.Csp;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageArchiveEndpointTests
{
    [Theory]
    [InlineData("admin", HttpStatusCode.OK, false)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized, false)]
    [InlineData("customer", HttpStatusCode.Forbidden, false)]
    [InlineData("support", HttpStatusCode.Forbidden, false)]
    [InlineData("admin", HttpStatusCode.OK, true)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized, true)]
    [InlineData("customer", HttpStatusCode.Forbidden, true)]
    [InlineData("support", HttpStatusCode.Forbidden, true)]
    public async Task Archive_RealEndpointEnforcesAuthorizationAndRetainsHistory(string role, HttpStatusCode expected, bool supersede)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var seeded = supersede ? await CspPackageSupersedeReviewTests.SeedAsync(fixture) : default;
        var receipt = supersede ? seeded.Source : await fixture.AnalyzeAsync();
        fixture.Tenant.SetupGet(x => x.IsCspAdmin).Returns(role is "admin" or "support");
        fixture.Tenant.SetupGet(x => x.ImpersonatedTenantId).Returns(role == "support" ? Guid.NewGuid() : null);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICspPackageService>(fixture.Service);
        builder.Services.AddSingleton<ITenantContext>(fixture.Tenant.Object);
        builder.Services.Configure<DeploymentOptions>(x => x.Mode = DeploymentMode.MultiTenant);
        builder.Services.AddAuthentication("Synthetic").AddScheme<AuthenticationSchemeOptions, SyntheticAuthHandler>("Synthetic", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapCspPackageImportEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();
        if (role == "anonymous") client.DefaultRequestHeaders.Add("X-Synthetic-Anonymous", "true");
        var path = $"/api/csp/package-imports/{receipt.PackageId}";
        var operation = supersede ? "/supersede-review" : "/archive";
        object Body(string reason) => supersede
            ? new SupersedePackageReviewRequest(receipt.Revision, seeded.Replacement.PackageId, reason)
            : new ArchivePackageRequest(receipt.Revision, reason);
        var body = Body("Superseded synthetic source");
        // Act
        var response = await client.PostAsJsonAsync(path + operation, body);
        // Assert
        response.StatusCode.Should().Be(expected);
        if (role != "admin") return;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("data").GetProperty(supersede ? "supersededBy" : "archivedBy").GetString().Should().Be("synthetic-actor");
        (await client.PostAsJsonAsync(path + operation, body)).StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await client.GetFromJsonAsync<JsonElement>(path + "/history");
        history.GetProperty("data").GetProperty("audits").EnumerateArray()
            .Count(x => x.GetProperty("action").GetString() == (supersede ? "ReviewSuperseded" : "Archived")).Should().Be(1);
        (await client.PostAsJsonAsync(path + operation, Body("different"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync(path + operation, Body(""))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private sealed class SyntheticAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Synthetic-Anonymous") ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "synthetic-actor")], Scheme.Name)), Scheme.Name)));
    }
}
