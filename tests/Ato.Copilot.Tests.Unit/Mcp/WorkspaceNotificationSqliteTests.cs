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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public sealed class WorkspaceNotificationSqliteTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _directory = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _newest = Guid.NewGuid();
    private ServiceProvider _services = null!;
    private DbContextOptions<AtoCopilotContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options;
        await using var db = new AtoCopilotContext(_options);
        await db.Database.EnsureCreatedAsync();
        var person = new Person { TenantId = _tenant, DisplayName = "Member", Email = "member@example.invalid" };
        var system = new RegisteredSystem { TenantId = _tenant, Name = "Visible system" };
        var alert = new ComplianceAlert { Id = Guid.NewGuid(), TenantId = _tenant, AlertId = "ALT-2026092100001",
            Title = "Visible alert", RegisteredSystemId = system.Id };
        db.Tenants.Add(new Tenant { Id = _tenant, DisplayName = "Organization" });
        db.Persons.Add(person);
        db.RegisteredSystems.Add(system);
        db.OrganizationMemberships.Add(new OrganizationMembership { TenantId = _tenant, PersonId = person.Id,
            ObjectId = _actor, DirectoryTenantId = _directory, GrantedBy = "test" });
        db.SystemRoleAssignments.Add(new SystemRoleAssignment { TenantId = _tenant, PersonId = person.Id,
            RegisteredSystemId = system.Id, Role = OrganizationRole.MissionOwner });
        db.ComplianceAlerts.Add(alert);
        db.AlertNotifications.AddRange(
            new AlertNotification { Id = Guid.NewGuid(), TenantId = _tenant, AlertId = alert.Id,
                UserId = _actor.ToString(), SentAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new AlertNotification { Id = _newest, TenantId = _tenant, AlertId = alert.Id,
                UserId = _actor.ToString(), SentAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        _services = CreateProvider();
    }

    private ServiceProvider CreateProvider()
    {
        var accessor = new TenantContextAccessor();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new AtoCopilotContext(_options, accessor));
        var services = new ServiceCollection();
        services.AddSingleton(factory.Object);
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddSingleton(Mock.Of<ICspProfileService>());
        services.AddLogging();
        services.AddSingleton<ITenantSupportSessionStore, TenantSupportSessionStore>();
        services.AddSingleton<ITenantImpersonationService>(provider => new TenantImpersonationService(
            "synthetic-support-replay-test-signing-key-only",
            provider.GetRequiredService<ITenantSupportSessionStore>(),
            provider.GetRequiredService<ILogger<TenantImpersonationService>>()));
        services.AddOptions<RoleClaimMappingsOptions>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<ISystemWorkspaceAccessService, SystemWorkspaceAccessService>();
        services.AddScoped<INotificationAccessService, NotificationAccessService>();
        services.AddScoped<IWorkspaceNotificationService, WorkspaceNotificationService>();
        services.AddSingleton<NotificationConnectionRegistry>();
        services.AddSingleton<NotificationDeliveryService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Sqlite_ListSummaryAndRead_ApplySharedAuthorizationAndNewestFirstPagination()
    {
        // Arrange
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkspaceNotificationService>();
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity(
            [new Claim("tid", _directory.ToString()), new Claim("oid", _actor.ToString())], "test")) };
        http.Request.Headers["X-Workspace-Kind"] = "organization";
        http.Request.Headers["X-Workspace-Tenant-Id"] = _tenant.ToString();
        http.Request.Headers["X-Workspace-Mode"] = "ordinary";
        await service.InitializeAsync(http, CancellationToken.None);

        // Act
        var listed = await service.ListAsync(true, 1, CancellationToken.None);
        var before = await service.SummaryAsync(CancellationToken.None);
        var read = await service.MarkReadAsync([_newest], CancellationToken.None);
        var after = await service.SummaryAsync(CancellationToken.None);

        // Assert
        listed.Items.Select(n => n.Id).Should().Equal(_newest);
        before.UnreadCount.Should().Be(2);
        read.MarkedCount.Should().Be(1);
        after.UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task Sqlite_IndependentStoreRevocationStopsCapturedHubDeliveryAndReplay()
    {
        // Arrange
        await using var exitProvider = CreateProvider();
        var sessions = exitProvider.GetRequiredService<ITenantImpersonationService>();
        var captured = await sessions.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _tenant, CancellationToken.None);
        var separate = await sessions.IssueWorkspaceTokenAsync(_actor.ToString(), _directory, _tenant, CancellationToken.None);
        var registry = _services.GetRequiredService<NotificationConnectionRegistry>();
        var aborted = false;
        await registry.ConnectAsync("existing", HubRequest(captured.value), () => aborted = true);
        await registry.RegisterAsync("existing", _actor.ToString());
        await using var db = new AtoCopilotContext(_options);
        var notification = await db.AlertNotifications.AsNoTracking().SingleAsync(n => n.Id == _newest);
        var sent = new List<string>();
        Task Send(string id, string method, object?[] arguments, CancellationToken ct)
        {
            sent.Add(id);
            return Task.CompletedTask;
        }
        var delivery = _services.GetRequiredService<NotificationDeliveryService>();
        await delivery.BroadcastAsync(_actor.ToString(), notification, Send);
        sent.Should().Equal("existing");

        // Act
        await sessions.RevokeWorkspaceTokenAsync(captured.value, _directory, _actor, "manual", CancellationToken.None);
        await delivery.BroadcastAsync(_actor.ToString(), notification, Send);
        var replay = () => exitProvider.GetRequiredService<NotificationConnectionRegistry>()
            .ConnectAsync("replay", HubRequest(captured.value), () => { });

        // Assert
        _services.GetRequiredService<ITenantSupportSessionStore>().Should()
            .NotBeSameAs(exitProvider.GetRequiredService<ITenantSupportSessionStore>());
        sent.Should().Equal("existing");
        aborted.Should().BeTrue();
        await replay.Should().ThrowAsync<HubException>();
        (await sessions.ValidateWorkspaceTokenAsync(separate.value, CancellationToken.None)).Should().NotBeNull();
        await registry.ConnectAsync("ordinary", HubRequest(captured.value, "ordinary"), () => { });
        (await registry.RequireAsync("ordinary")).Workspace.Mode.Should().Be("ordinary");
    }

    private DefaultHttpContext HubRequest(string cookie, string mode = "support")
    {
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity(
            [new Claim("tid", _directory.ToString()), new Claim("oid", _actor.ToString()),
             new Claim(ClaimTypes.Role, "CSP.Admin")], "TrustedHubTest")) };
        http.Request.Path = "/hubs/notifications";
        http.Request.QueryString = new($"?workspaceKind=organization&workspaceTenantId={_tenant}&workspaceMode={mode}");
        http.Request.Headers.Cookie = $"ato-impersonate={cookie}";
        return http;
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
