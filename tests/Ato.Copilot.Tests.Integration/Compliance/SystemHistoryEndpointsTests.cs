using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints;
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

public sealed class SystemHistoryEndpointsTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Mock<ISystemWorkspaceAccessService> _access = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private bool _canRead = true;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var database = $"system-history-{Guid.NewGuid():N}";
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.TenantId).Returns(TenantId);
        tenant.SetupGet(x => x.EffectiveTenantId).Returns(TenantId);
        tenant.SetupGet(x => x.IsWorkspaceRequest).Returns(true);
        builder.Services.AddSingleton(tenant.Object);
        builder.Services.AddDbContext<AtoCopilotContext>(options => options.UseInMemoryDatabase(database));
        builder.Services.AddSingleton(_access.Object);
        _access.Setup(x => x.GetAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Guid? __, string id, bool ___, CancellationToken ____) =>
                Task.FromResult(new SystemWorkspaceAccessResponse(id, ["Reader"],
                    new SystemWorkspacePermissions(_canRead, false, false, false, false, false, false, false, false))));
        builder.Services.AddAuthentication("HistoryTest")
            .AddScheme<AuthenticationSchemeOptions, HistoryAuthenticationHandler>("HistoryTest", _ => { });
        builder.Services.AddAuthorization(Policies.RegisterPolicies);
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapDashboardSystemHistoryEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-History-Authenticated", "true");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.RegisteredSystems.AddRange(
            new RegisteredSystem { Id = "system-a", Name = "Mission A", TenantId = TenantId, IsActive = true },
            new RegisteredSystem { Id = "system-b", Name = "Mission B", TenantId = TenantId, IsActive = true },
            new RegisteredSystem { Id = "foreign-system", Name = "Foreign", TenantId = OtherTenant, IsActive = true });
        db.DashboardActivities.AddRange(
            Activity("event-a", "system-a", TenantId, "ProfileReviewed", 3),
            Activity("event-b", "system-a", TenantId, "EvidenceAdded", 2),
            Activity("event-c", "system-b", TenantId, "OtherSystemEvent", 1),
            Activity("foreign-event", "system-a", OtherTenant, "ForeignTenantEvent", 4));
        await db.SaveChangesAsync();
    }

    private static DashboardActivity Activity(string id, string system, Guid tenant, string type, int day) => new()
    {
        Id = id, RegisteredSystemId = system, TenantId = tenant, EventType = type, Actor = "Reviewer",
        Timestamp = new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc), Summary = $"Retained {type}",
        RelatedEntityType = "SystemProfile", RelatedEntityId = "profile-a",
    };

    [Fact]
    public async Task History_OnlyReturnsSelectedSystemAndTenant_WithStablePagination()
    {
        // Arrange
        const string path = "/api/dashboard/systems/system-a/history?pageSize=1";
        // Act
        var first = await _client.GetFromJsonAsync<JsonElement>(path);
        var second = await _client.GetFromJsonAsync<JsonElement>($"{path}&page=2");
        // Assert
        first.GetProperty("totalCount").GetInt32().Should().Be(2);
        first.GetProperty("items")[0].GetProperty("id").GetString().Should().Be("event-a");
        second.GetProperty("items")[0].GetProperty("id").GetString().Should().Be("event-b");
    }

    [Fact]
    public async Task History_AppliesEventTypeAndDateFilters()
    {
        // Arrange
        const string path = "/api/dashboard/systems/system-a/history?eventType=EvidenceAdded&from=2026-09-02&to=2026-09-03";
        // Act
        var result = await _client.GetFromJsonAsync<JsonElement>(path);
        // Assert
        result.GetProperty("totalCount").GetInt32().Should().Be(1);
        result.GetProperty("items")[0].GetProperty("eventType").GetString().Should().Be("EvidenceAdded");
    }

    [Fact]
    public async Task History_RequiresReadPermissionBeforeReturningEvents()
    {
        // Arrange
        _canRead = false;
        // Act
        var response = await _client.GetAsync("/api/dashboard/systems/system-a/history");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task History_RejectsAnotherTenantSystemEvenWhenAccessServiceAllowsRead()
    {
        // Arrange / Act
        var response = await _client.GetAsync("/api/dashboard/systems/foreign-system/history");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task History_RequiresAuthentication()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("X-History-Authenticated");
        // Act
        var response = await _client.GetAsync("/api/dashboard/systems/system-a/history");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("page=2147483647&pageSize=200")]
    [InlineData("from=2026-09-03&to=2026-09-01")]
    public async Task History_RejectsInvalidQuery(string query)
    {
        // Arrange / Act
        var response = await _client.GetAsync($"/api/dashboard/systems/system-a/history?{query}");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class HistoryAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(!Request.Headers.ContainsKey("X-History-Authenticated")
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "reader-a")], Scheme.Name)), Scheme.Name)));
    }
}
