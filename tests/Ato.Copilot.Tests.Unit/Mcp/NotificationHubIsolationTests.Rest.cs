using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Mcp.Authentication;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public partial class NotificationHubIsolationTests
{
    private TestServer HttpServer(bool requireCac = false) => new(new WebHostBuilder().ConfigureServices(services =>
    {
        foreach (var registration in _registrations) services.Add(registration);
        services.AddRouting();
        services.AddHttpContextAccessor();
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        var validator = new Mock<IEntraJwtTokenValidator>();
        var user = Http(_tenantA).User;
        ((ClaimsIdentity)user.Identity!).AddClaim(new("exp", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString()));
        validator.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityTokenException("Invalid test credential"));
        validator.Setup(v => v.ValidateAsync("progress-test", It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var otherActor = new ClaimsPrincipal(new ClaimsIdentity(user.Claims
            .Select(c => c.Type == "oid" ? new Claim("oid", Guid.NewGuid().ToString()) : c), "test"));
        validator.Setup(v => v.ValidateAsync("other-actor", It.IsAny<CancellationToken>())).ReturnsAsync(otherActor);
        var cac = new ClaimsPrincipal(new ClaimsIdentity(user.Claims.Concat(
            [new Claim("amr", "mfa"), new Claim("amr", "rsa")]), "test"));
        validator.Setup(v => v.ValidateAsync("cac-token", It.IsAny<CancellationToken>())).ReturnsAsync(cac);
        var csp = new ClaimsPrincipal(new ClaimsIdentity(user.Claims.Append(new Claim(ClaimTypes.Role, "CSP.Admin")), "test"));
        validator.Setup(v => v.ValidateAsync("csp-token", It.IsAny<CancellationToken>())).ReturnsAsync(csp);
        services.AddSingleton(validator.Object);
        services.Configure<AzureAdOptions>(options => options.RequireCac = requireCac);
        services.AddAuthentication(CacPassthroughAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CacPassthroughAuthHandler>(CacPassthroughAuthHandler.SchemeName, _ => { });
    }).Configure(app =>
    {
        app.Use(async (http, next) =>
        {
            if (http.Request.Headers.TryGetValue("X-Test-Oid", out var actor))
            {
                var claims = new List<Claim> { new("oid", actor.ToString()),
                    new("tid", http.Request.Headers["X-Test-Tid"].ToString()),
                    new(ClaimTypes.NameIdentifier, http.Request.Headers["X-Test-NameId"].FirstOrDefault() ?? actor.ToString()) };
                if (http.Request.Headers["X-Test-Csp"] == "true") claims.Add(new(ClaimTypes.Role, "CSP.Admin"));
                http.User = new ClaimsPrincipal(new ClaimsIdentity(claims,
                    http.Request.Headers["X-Test-AuthType"].FirstOrDefault() ?? "TrustedTestIdentity"));
            }
            await next();
        });
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapNotificationEndpoints();
            endpoints.MapScanImportEndpoints();
            endpoints.MapHub<PackageHub>("/hubs/package", options => options.CloseOnAuthenticationExpiration = true);
            endpoints.MapHub<ImportProgressHub>("/hubs/import-progress", options => options.CloseOnAuthenticationExpiration = true);
        });
    }));

    private HttpClient HttpClient(TestServer server, Guid tenant, Guid? actor = null, Guid? directory = null)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", (actor ?? _actor).ToString());
        client.DefaultRequestHeaders.Add("X-Test-Tid", (directory ?? _directory).ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", tenant.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        return client;
    }

    [Fact]
    public async Task RestMarkRead_RejectsAnotherActorsNotificationWithoutMutation()
    {
        // Arrange
        await SeedAsync();
        var victim = Notification(_tenantA);
        victim.UserId = Guid.NewGuid().ToString();
        await using (var db = await Db())
        {
            db.AlertNotifications.Add(victim);
            await db.SaveChangesAsync();
        }
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);

        // Act
        var response = await client.PostAsJsonAsync("/api/dashboard/notifications/mark-read", new { notificationIds = new[] { victim.Id } });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var verify = await Db();
        (await verify.AlertNotifications.SingleAsync(n => n.Id == victim.Id)).IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task RestListAndSummary_UseSelectedOrganizationNotUserIdAlone()
    {
        // Arrange
        await SeedAsync();
        var a = Notification(_tenantA);
        var b = Notification(_tenantB);
        await using (var db = await Db())
        {
            db.AlertNotifications.AddRange(a, b);
            await db.SaveChangesAsync();
        }
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);

        // Act
        var list = await client.GetAsync("/api/dashboard/notifications");
        var summary = await client.GetAsync("/api/dashboard/notifications/summary");

        // Assert
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("id").GetGuid()).Should().Equal(a.Id);
        (await summary.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unreadCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task RestPreferences_RejectForgedUserAssertion()
    {
        // Arrange
        await SeedAsync();
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);

        // Act
        var response = await client.GetAsync($"/api/dashboard/notifications/preferences?userId={Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<AlertNotification> HiddenSystemNotificationAsync()
    {
        await using var db = await Db();
        var system = new RegisteredSystem { TenantId = _tenantA, Name = "Not assigned", IsActive = true };
        var alert = new ComplianceAlert { Id = Guid.NewGuid(), TenantId = _tenantA, RegisteredSystemId = system.Id };
        var notification = Notification(_tenantA);
        notification.AlertId = alert.Id;
        notification.SentAt = DateTimeOffset.UtcNow;
        db.RegisteredSystems.Add(system);
        db.ComplianceAlerts.Add(alert);
        db.AlertNotifications.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    }

    [Fact]
    public async Task RestVisibility_AppliesBeforeLimitCountsAndMarkAll()
    {
        // Arrange
        await SeedAsync();
        var visible = Notification(_tenantA);
        var foreign = Notification(_tenantB);
        var anotherActor = Notification(_tenantA);
        anotherActor.UserId = Guid.NewGuid().ToString();
        var hidden = await HiddenSystemNotificationAsync();
        await using (var db = await Db())
        {
            db.AlertNotifications.AddRange(visible, foreign, anotherActor);
            await db.SaveChangesAsync();
        }
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);

        // Act
        var list = await client.GetAsync("/api/dashboard/notifications?limit=1");
        var summary = await client.GetAsync("/api/dashboard/notifications/summary");
        var marked = await client.PostAsync("/api/dashboard/notifications/mark-all-read", null);

        // Assert
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("id").GetGuid()).Should().Equal(visible.Id);
        var counts = await summary.Content.ReadFromJsonAsync<JsonElement>();
        counts.GetProperty("unreadCount").GetInt32().Should().Be(1);
        counts.GetProperty("totalCount").GetInt32().Should().Be(1);
        (await marked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("markedCount").GetInt32().Should().Be(1);
        await using var verify = await Db();
        (await verify.AlertNotifications.Where(n => n.IsRead).Select(n => n.Id).ToListAsync()).Should().Equal(visible.Id);
        (await verify.AlertNotifications.SingleAsync(n => n.Id == hidden.Id)).IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task RestMarkRead_MixedBatchIsAtomic_AndAlreadyReadIsIdempotent()
    {
        // Arrange
        await SeedAsync();
        var own = Notification(_tenantA);
        var other = Notification(_tenantB);
        await using (var db = await Db())
        {
            db.AlertNotifications.AddRange(own, other);
            await db.SaveChangesAsync();
        }
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);

        // Act
        var mixed = await client.PostAsJsonAsync("/api/dashboard/notifications/mark-read", new { notificationIds = new[] { own.Id, other.Id } });
        await using (var verify = await Db())
            (await verify.AlertNotifications.CountAsync(n => n.IsRead)).Should().Be(0);
        var first = await client.PostAsJsonAsync("/api/dashboard/notifications/mark-read", new { notificationIds = new[] { own.Id } });
        var second = await client.PostAsJsonAsync("/api/dashboard/notifications/mark-read", new { notificationIds = new[] { own.Id } });

        // Assert
        mixed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("markedCount").GetInt32().Should().Be(1);
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("markedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task RestRoleRemoval_HidesExistingNotificationsWithoutRemovingMembership()
    {
        // Arrange
        await SeedAsync();
        var notification = Notification(_tenantA);
        await using (var db = await Db())
        {
            db.AlertNotifications.Add(notification);
            (await db.SystemRoleAssignments.SingleAsync(r => r.TenantId == _tenantA)).RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);
        client.DefaultRequestHeaders.Add("X-Test-Csp", "true");

        // Act
        var list = await client.GetAsync("/api/dashboard/notifications");
        var read = await client.PostAsJsonAsync("/api/dashboard/notifications/mark-read", new { notificationIds = new[] { notification.Id } });

        // Assert
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(0);
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var verify = await Db();
        (await verify.AlertNotifications.SingleAsync(n => n.Id == notification.Id)).IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task RestPreferences_AreActorAndTenantBound_AndNameIdentifierIsNotAuthority()
    {
        // Arrange
        await SeedAsync();
        using var server = HttpServer();
        using var a = HttpClient(server, _tenantA);
        using var b = HttpClient(server, _tenantB);
        a.DefaultRequestHeaders.Add("X-Test-NameId", Guid.NewGuid().ToString());
        var preferences = new { poamOverdueAlerts = false, atoExpirationAlerts = false, complianceDriftAlerts = true, alertDaysBefore = 7 };

        // Act
        var save = await a.PutAsJsonAsync("/api/dashboard/notifications/preferences", preferences);
        var foreign = await b.GetAsync("/api/dashboard/notifications/preferences");
        var own = await a.GetAsync("/api/dashboard/notifications/preferences");

        // Assert
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alertDaysBefore").GetInt32().Should().Be(7);
        (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alertDaysBefore").GetInt32().Should().Be(30);
        await using var verify = await Db();
        var saved = await verify.NotificationPreferences.SingleAsync();
        saved.TenantId.Should().Be(_tenantA);
        saved.UserId.Should().Be(_actor.ToString());
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/summary")]
    [InlineData("GET", "/preferences")]
    [InlineData("PUT", "/preferences")]
    [InlineData("POST", "/mark-read")]
    [InlineData("POST", "/mark-all-read")]
    public async Task RestEveryEndpoint_RevalidatesMembership(string method, string suffix)
    {
        // Arrange
        await SeedAsync();
        await using (var db = await Db())
        {
            (await db.OrganizationMemberships.SingleAsync(m => m.TenantId == _tenantA)).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/dashboard/notifications{suffix}");
        if (method == "PUT") request.Content = JsonContent.Create(new { alertDaysBefore = 7 });
        if (suffix == "/mark-read") request.Content = JsonContent.Create(new { notificationIds = new[] { Guid.NewGuid() } });

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("actor", HttpStatusCode.Forbidden)]
    [InlineData("directory", HttpStatusCode.Forbidden)]
    [InlineData("missing-workspace", HttpStatusCode.Conflict)]
    [InlineData("csp-workspace", HttpStatusCode.Conflict)]
    [InlineData("invalid-identity", HttpStatusCode.Unauthorized)]
    [InlineData("duplicate-assertion", HttpStatusCode.BadRequest)]
    public async Task RestIdentityAndWorkspaceFailures_AreExplicit(string invalid, HttpStatusCode expected)
    {
        // Arrange
        await SeedAsync();
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA, actor: invalid == "actor" ? Guid.NewGuid() : null,
            directory: invalid == "directory" ? Guid.NewGuid() : null);
        var path = "/api/dashboard/notifications";
        if (invalid is "missing-workspace" or "csp-workspace")
        {
            client.DefaultRequestHeaders.Remove("X-Workspace-Kind");
            client.DefaultRequestHeaders.Remove("X-Workspace-Tenant-Id");
            if (invalid == "csp-workspace")
            {
                client.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
                client.DefaultRequestHeaders.Add("X-Test-Csp", "true");
            }
        }
        if (invalid == "invalid-identity")
        {
            client.DefaultRequestHeaders.Remove("X-Test-Oid");
            client.DefaultRequestHeaders.Add("X-Test-Oid", "not-a-guid");
        }
        if (invalid == "duplicate-assertion") path += $"?userId={_actor}&userId={_actor}";

        // Act
        var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task RestExpiredSupport_IsDenied_OrdinaryIgnoresCookie()
    {
        // Arrange
        await SeedAsync();
        _support.Setup(s => s.ValidateWorkspaceTokenAsync("expired", It.IsAny<CancellationToken>())).ReturnsAsync(new ImpersonationCookiePayload(_actor.ToString(),
            Guid.Empty, _tenantA, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1), _directory));
        using var server = HttpServer();
        using var ordinary = HttpClient(server, _tenantA);
        ordinary.DefaultRequestHeaders.Add("Cookie", "ato-impersonate=expired");
        using var support = HttpClient(server, _tenantA);
        support.DefaultRequestHeaders.Remove("X-Workspace-Mode");
        support.DefaultRequestHeaders.Add("X-Workspace-Mode", "support");
        support.DefaultRequestHeaders.Add("Cookie", "ato-impersonate=expired");
        support.DefaultRequestHeaders.Add("X-Test-Csp", "true");

        // Act
        var ordinaryResult = await ordinary.GetAsync("/api/dashboard/notifications/summary");
        var supportResult = await support.GetAsync("/api/dashboard/notifications/summary");

        // Assert
        ordinaryResult.StatusCode.Should().Be(HttpStatusCode.OK);
        supportResult.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _support.Verify(s => s.ValidateWorkspaceTokenAsync("expired", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("[\"00000000-0000-0000-0000-000000000000\"]")]
    public async Task RestMarkRead_RejectsInvalidBatch(string? ids)
    {
        // Arrange
        await SeedAsync();
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);
        using var content = new StringContent($"{{\"notificationIds\":{ids ?? "null"}}}", System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/dashboard/notifications/mark-read", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RestAmbiguousHistoricalRecipient_CannotReadOrOverwritePreferences()
    {
        // Arrange
        await SeedAsync();
        await using (var db = await Db())
        {
            var person = new Person { TenantId = _tenantA, DisplayName = "Historical actor" };
            db.Persons.Add(person);
            db.OrganizationMemberships.Add(new OrganizationMembership { TenantId = _tenantA,
                ObjectId = _actor, DirectoryTenantId = Guid.NewGuid(), PersonId = person.Id,
                GrantedBy = "test", RevokedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        using var server = HttpServer();
        using var client = HttpClient(server, _tenantA);

        // Act
        var read = await client.GetAsync("/api/dashboard/notifications");
        var write = await client.PutAsJsonAsync("/api/dashboard/notifications/preferences", new { alertDaysBefore = 1 });

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var verify = await Db();
        (await verify.NotificationPreferences.CountAsync()).Should().Be(0);
    }
}
