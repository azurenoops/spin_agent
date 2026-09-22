using System.Net;
using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Hubs.Notifications;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using WorkspaceMembershipFactory = Ato.Copilot.Tests.Integration.Tenancy.WorkspaceMembershipFactory;

namespace Ato.Copilot.Tests.Integration.Notifications;

public sealed class SupportSessionHubReplayTests(WorkspaceMembershipFactory factory) : IClassFixture<WorkspaceMembershipFactory>
{
    private readonly Guid _directory = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _tenant = WorkspaceMembershipFactory.TenantAId;
    private readonly List<string> _sent = [];
    private readonly List<string> _aborted = [];

    private HttpClient Administrator()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Test-Tid", _directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", _actor.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Roles", "CSP.Admin");
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "csp");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        return client;
    }

    private async Task<string> EnterSupportAsync(HttpClient administrator)
    {
        using var response = await administrator.PostAsync($"/api/tenants/{_tenant}/impersonate", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("ato-impersonate=", StringComparison.Ordinal)).Split(';')[0];
    }

    private async Task ExitSupportAsync(HttpClient administrator, string capturedCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/tenants/impersonation");
        request.Headers.Add("Cookie", capturedCookie);
        using var response = await administrator.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private async Task<ReplayResource> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContextAsync();
        db.Database.IsSqlite().Should().BeTrue("this regression verifies independent validators against a shared SQLite store");
        var connectionString = db.Database.GetConnectionString() ?? throw new InvalidOperationException("SQLite connection required");
        var person = new Person { TenantId = _tenant, DisplayName = "Support replay actor", Email = $"{_actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = _tenant, Name = $"Replay {_actor:N}", IsActive = true };
        var alert = new ComplianceAlert { Id = Guid.NewGuid(), TenantId = _tenant,
            AlertId = $"ALT-{Guid.NewGuid():N}", RegisteredSystemId = system.Id, Title = "Synthetic replay notification" };
        var notification = new AlertNotification { Id = Guid.NewGuid(), TenantId = _tenant, AlertId = alert.Id,
            UserId = _actor.ToString(), Recipient = _actor.ToString(), Channel = NotificationChannel.Chat };
        var package = new AuthorizationPackage { TenantId = _tenant, RegisteredSystemId = system.Id, GeneratedBy = _actor.ToString() };
        db.Persons.Add(person);
        db.RegisteredSystems.Add(system);
        db.OrganizationMemberships.Add(new OrganizationMembership { TenantId = _tenant, PersonId = person.Id,
            ObjectId = _actor, DirectoryTenantId = _directory, GrantedBy = "test" });
        db.SystemRoleAssignments.Add(new SystemRoleAssignment { TenantId = _tenant, PersonId = person.Id,
            RegisteredSystemId = system.Id, Role = OrganizationRole.MissionOwner });
        db.ComplianceAlerts.Add(alert);
        db.AlertNotifications.Add(notification);
        db.AuthorizationPackages.Add(package);
        await db.SaveChangesAsync();
        return new(connectionString, system.Id, notification, Guid.Parse(package.Id), Guid.NewGuid());
    }

    private ServiceProvider HubServices(ReplayResource resource)
    {
        var key = factory.Services.GetRequiredService<IConfiguration>()["Auth:Impersonation:SigningKey"]
            ?? throw new InvalidOperationException("Fixture signing key required");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AtoCopilotContext>(options => options.UseSqlite(resource.ConnectionString));
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddOptions<RoleClaimMappingsOptions>();
        services.AddSingleton(Mock.Of<ICspProfileService>());
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<ISystemWorkspaceAccessService, SystemWorkspaceAccessService>();
        services.AddSingleton<ITenantSupportSessionStore, TenantSupportSessionStore>();
        services.AddSingleton<ITenantImpersonationService>(provider =>
            ActivatorUtilities.CreateInstance<TenantImpersonationService>(provider, key));
        services.AddSingleton(_ =>
        {
            var tracker = new ScanImportStatusTracker();
            tracker.Register(resource.ImportId.ToString(), resource.SystemId);
            return tracker;
        });
        services.AddSignalR();
        services.AddWorkspaceNotifications();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private DefaultHttpContext CapturedRequest(string transport, string cookie, string mode = "support")
    {
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity(
            [new Claim("tid", _directory.ToString()), new Claim("oid", _actor.ToString()),
             new Claim(ClaimTypes.Role, "CSP.Admin")], "TrustedHubTest")) };
        http.Request.Path = $"/hubs/{transport}";
        http.Request.QueryString = new($"?workspaceKind=organization&workspaceTenantId={_tenant}&workspaceMode={mode}");
        http.Request.Headers.Cookie = cookie;
        return http;
    }

    private async Task SubscribeAsync(IServiceProvider services, ReplayResource resource, string transport,
        string connectionId, string cookie, string mode = "support")
    {
        var registry = services.GetRequiredService<NotificationConnectionRegistry>();
        await registry.ConnectAsync(connectionId, CapturedRequest(transport, cookie, mode), () => _aborted.Add(connectionId));
        if (transport == "notifications") await registry.RegisterAsync(connectionId, _actor.ToString());
        else
            await services.GetRequiredService<WorkspaceProgressDeliveryService>().SubscribeAsync(connectionId,
                transport == "package" ? ProgressResourceKind.Package : ProgressResourceKind.Import,
                (transport == "package" ? resource.PackageId : resource.ImportId).ToString());
    }

    private Task PublishAsync(IServiceProvider services, ReplayResource resource, string transport)
    {
        if (transport == "notifications")
            return services.GetRequiredService<NotificationDeliveryService>().BroadcastAsync(_actor.ToString(), resource.Notification, SendAsync);
        var progress = services.GetRequiredService<WorkspaceProgressDeliveryService>();
        return transport == "package"
            ? progress.SendGroupAsync(ProgressResourceKind.Package, $"package:{resource.PackageId}", "PackageStatusChanged",
                [new { packageId = resource.PackageId, status = "Generating" }], SendAsync)
            : progress.SendGroupAsync(ProgressResourceKind.Import, $"import:{resource.ImportId}", "ImportProgress",
                [new { jobId = resource.ImportId, status = "Processing" }], SendAsync);
    }

    private Task SendAsync(string connection, string method, object?[] payload, CancellationToken ct)
    {
        _sent.Add(connection);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("notifications")]
    [InlineData("package")]
    [InlineData("import-progress")]
    public async Task CapturedSupportCookie_HttpExitStopsExistingDelivery_WithoutRevokingOtherSessions(string transport)
    {
        // Arrange
        var resource = await SeedAsync();
        using var administrator = Administrator();
        var captured = await EnterSupportAsync(administrator);
        var independent = await EnterSupportAsync(administrator);
        independent.Should().NotBe(captured);
        await using var hub = HubServices(resource);
        hub.GetRequiredService<ITenantImpersonationService>().Should()
            .NotBeSameAs(factory.Services.GetRequiredService<ITenantImpersonationService>());
        hub.GetRequiredService<ITenantSupportSessionStore>().Should()
            .NotBeSameAs(factory.Services.GetRequiredService<ITenantSupportSessionStore>());
        await SubscribeAsync(hub, resource, transport, "revoked-support", captured);
        await SubscribeAsync(hub, resource, transport, "other-support", independent);
        await SubscribeAsync(hub, resource, transport, "ordinary", captured, "ordinary");
        await PublishAsync(hub, resource, transport);
        _sent.Should().BeEquivalentTo(["revoked-support", "other-support", "ordinary"]);
        _sent.Clear();

        // Act
        await ExitSupportAsync(administrator, captured);
        await PublishAsync(hub, resource, transport);

        // Assert
        _sent.Should().BeEquivalentTo(["other-support", "ordinary"]);
        _aborted.Should().Contain("revoked-support").And.NotContain("other-support").And.NotContain("ordinary");
    }

    [Fact]
    public async Task CapturedSupportCookie_HttpExitDeniesOperationsAndFreshRegistryReplay()
    {
        // Arrange
        var resource = await SeedAsync();
        using var administrator = Administrator();
        var captured = await EnterSupportAsync(administrator);
        await using var connected = HubServices(resource);
        await SubscribeAsync(connected, resource, "notifications", "established", captured);
        await connected.GetRequiredService<NotificationConnectionRegistry>().RequireAsync("established");

        // Act
        await ExitSupportAsync(administrator, captured);
        await using var fresh = HubServices(resource);
        var operation = () => connected.GetRequiredService<NotificationConnectionRegistry>().RequireAsync("established");
        var replay = () => fresh.GetRequiredService<NotificationConnectionRegistry>()
            .ConnectAsync("replay", CapturedRequest("notifications", captured), () => _aborted.Add("replay"));

        // Assert
        fresh.GetRequiredService<ITenantImpersonationService>().Should().NotBeSameAs(connected.GetRequiredService<ITenantImpersonationService>());
        fresh.GetRequiredService<ITenantSupportSessionStore>().Should().NotBeSameAs(connected.GetRequiredService<ITenantSupportSessionStore>());
        await operation.Should().ThrowAsync<HubException>();
        await replay.Should().ThrowAsync<HubException>();
    }

    private sealed record ReplayResource(
        string ConnectionString, string SystemId, AlertNotification Notification, Guid PackageId, Guid ImportId);
}
