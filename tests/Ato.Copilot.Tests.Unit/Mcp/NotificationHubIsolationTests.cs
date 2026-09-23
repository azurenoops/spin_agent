using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Hubs.Notifications;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public partial class NotificationHubIsolationTests : IAsyncDisposable
{
    private readonly Guid _directory = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly ServiceProvider _services;
    private readonly IServiceCollection _registrations;
    private readonly Mock<ITenantImpersonationService> _support = new();
    private readonly List<string> _aborted = [];
    private readonly List<(string Connection, string Method, object?[] Args)> _sent = [];
    private readonly Dictionary<Guid, Guid> _alerts = new();
    private NotificationConnectionRegistry Registry => _services.GetRequiredService<NotificationConnectionRegistry>();
    private NotificationDeliveryService Delivery => _services.GetRequiredService<NotificationDeliveryService>();

    public NotificationHubIsolationTests()
    {
        var services = new ServiceCollection();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var accessor = new TenantContextAccessor();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(options, accessor));
        factory.Setup(f => f.CreateDbContext()).Returns(() => new AtoCopilotContext(options, accessor));
        services.AddSingleton(factory.Object);
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContext());
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddSingleton(Mock.Of<ICspProfileService>());
        _support.SetupGet(s => s.CookieName).Returns("ato-impersonate");
        services.AddSingleton(_support.Object);
        services.AddOptions<RoleClaimMappingsOptions>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddAuthorization(options => options.AddPolicy(OnboardingAdministratorRequirement.PolicyName,
            policy => policy.Requirements.Add(new OnboardingAdministratorRequirement())));
        services.AddScoped<IAuthorizationHandler, OnboardingAdministratorHandler>();
        services.AddScoped<ISystemWorkspaceAccessService, SystemWorkspaceAccessService>();
        services.AddSingleton<ScanImportStatusTracker>();
        services.AddSingleton<ScanImportQueue>();
        services.AddLogging();
        services.AddSignalR();
        services.AddWorkspaceNotifications();
        _registrations = services;
        _services = services.BuildServiceProvider();
    }

    private Task<AtoCopilotContext> Db() =>
        _services.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContextAsync();

    private async Task SeedAsync()
    {
        await using var db = await Db();
        foreach (var tenant in new[] { _tenantA, _tenantB })
        {
            db.Tenants.Add(new Tenant { Id = tenant, DisplayName = $"Organization {tenant}", Status = TenantStatus.Active });
            var person = new Person { TenantId = tenant, DisplayName = "Member", Email = "not-authority@example.invalid" };
            db.Persons.Add(person);
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = tenant, DirectoryTenantId = _directory, ObjectId = _actor,
                PersonId = person.Id, GrantedBy = "test",
            });
            var system = new RegisteredSystem { TenantId = tenant, Name = "Visible system", IsActive = true };
            db.RegisteredSystems.Add(system);
            db.SystemRoleAssignments.Add(new SystemRoleAssignment { TenantId = tenant,
                RegisteredSystemId = system.Id, PersonId = person.Id, Role = OrganizationRole.MissionOwner });
            var alert = new ComplianceAlert { Id = Guid.NewGuid(), TenantId = tenant, RegisteredSystemId = system.Id };
            db.ComplianceAlerts.Add(alert);
            _alerts.Add(tenant, alert.Id);
        }
        await db.SaveChangesAsync();
    }

    private DefaultHttpContext Http(Guid tenant, string mode = "ordinary", Guid? directory = null,
        Guid? actor = null, bool csp = false, string? cookie = null)
    {
        var claims = new List<Claim>
        {
            new("tid", (directory ?? _directory).ToString()), new("oid", (actor ?? _actor).ToString()),
        };
        if (csp) claims.Add(new(ClaimTypes.Role, "CSP.Admin"));
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity(claims, "test")) };
        http.Request.Path = "/hubs/notifications";
        http.Request.QueryString = new($"?workspaceKind=organization&workspaceTenantId={tenant:D}&workspaceMode={mode}");
        if (cookie is not null) http.Request.Headers.Cookie = $"ato-impersonate={cookie}";
        return http;
    }

    private async Task ConnectAsync(string id, Guid tenant, DefaultHttpContext? http = null)
    {
        await Registry.ConnectAsync(id, http ?? Http(tenant), () => _aborted.Add(id));
        await Registry.RegisterAsync(id, _actor.ToString());
    }

    private Task Send(string connection, string method, object?[] args, CancellationToken ct)
    {
        _sent.Add((connection, method, args));
        return Task.CompletedTask;
    }

    private AlertNotification Notification(Guid tenant) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, UserId = _actor.ToString(),
        Recipient = _actor.ToString(), Channel = NotificationChannel.Chat, Subject = "Tenant-private",
        AlertId = _alerts.GetValueOrDefault(tenant),
    };

    [Fact]
    public async Task RegisterUser_RejectsForgedIdentity()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("connection", _tenantA);
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("connection");
        var hub = new NotificationHub(NullLogger<NotificationHub>.Instance, Registry, Delivery)
        {
            Context = context.Object,
            Groups = Mock.Of<IGroupManager>(),
        };

        // Act
        var action = () => hub.RegisterUser(Guid.NewGuid().ToString());

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task TwoOrdinaryTabs_OnlyReceiveTheirSelectedTenant()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        await ConnectAsync("b", _tenantB);

        // Act
        await Delivery.BroadcastAsync(_actor.ToString(), Notification(_tenantA), Send);

        // Assert
        _sent.Select(s => s.Connection).Should().Equal("a");
        var a = await Registry.RequireAsync("a");
        var b = await Registry.RequireAsync("b");
        a.Connection.GroupName.Should().NotBe(b.Connection.GroupName);
        a.Connection.GroupName.Should().Contain(_directory.ToString("N")).And.Contain(_actor.ToString("N"));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("directory")]
    [InlineData("tenant")]
    public async Task WrongActorDirectoryOrTenant_CannotConnect(string mismatch)
    {
        // Arrange
        await SeedAsync();
        var http = Http(mismatch == "tenant" ? Guid.NewGuid() : _tenantA,
            directory: mismatch == "directory" ? Guid.NewGuid() : null,
            actor: mismatch == "actor" ? Guid.NewGuid() : null);

        // Act
        var action = () => Registry.ConnectAsync("denied", http, () => _aborted.Add("denied"));

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    [Theory]
    [InlineData("?workspaceKind=organization&workspaceTenantId=not-a-guid")]
    [InlineData("?workspaceKind=organization&workspaceKind=csp")]
    [InlineData("?workspaceKind=csp&workspaceMode=support")]
    [InlineData("?workspaceMode=ordinary")]
    [InlineData("?workspaceKind=organization&workspaceMode=invalid")]
    [InlineData("?workspaceKind=organization&workspaceTenantId=00000000-0000-0000-0000-000000000000")]
    public async Task InvalidSelectors_AreRejected(string query)
    {
        // Arrange
        await SeedAsync();
        var http = Http(_tenantA);
        http.Request.QueryString = new(query);

        // Act
        var action = () => Registry.ConnectAsync("invalid", http, () => { });

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task OrdinaryScope_DoesNotReadSupportCookie()
    {
        // Arrange
        await SeedAsync();

        // Act
        await ConnectAsync("ordinary", _tenantA, Http(_tenantA, cookie: "ignored", csp: true));

        // Assert
        (await Registry.RequireAsync("ordinary")).Context.EffectiveTenantId.Should().Be(_tenantA);
        _support.Verify(s => s.Validate(It.IsAny<string>()), Times.Never);
        _support.Verify(s => s.ValidateWorkspaceTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("directory")]
    [InlineData("tenant")]
    [InlineData("expired")]
    public async Task SupportScope_RejectsInvalidSession(string mismatch)
    {
        // Arrange
        await SeedAsync();
        _support.Setup(s => s.ValidateWorkspaceTokenAsync("support", It.IsAny<CancellationToken>())).ReturnsAsync(new ImpersonationCookiePayload(
            (mismatch == "actor" ? Guid.NewGuid() : _actor).ToString(), Guid.Empty,
            mismatch == "tenant" ? _tenantB : _tenantA, DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddMinutes(mismatch == "expired" ? -1 : 30),
            mismatch == "directory" ? Guid.NewGuid() : _directory));

        // Act
        var action = () => Registry.ConnectAsync("support", Http(_tenantA, "support", csp: true, cookie: "support"), () => { });

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task Revocation_StopsDeliveryAndOperationsWithoutReconnect()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        await ConnectAsync("b", _tenantB);
        await using (var db = await Db())
        {
            (await db.OrganizationMemberships.SingleAsync(m => m.TenantId == _tenantA)).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Act
        await Delivery.BroadcastAsync(_actor.ToString(), Notification(_tenantA), Send);
        await Delivery.BroadcastAsync(_actor.ToString(), Notification(_tenantB), Send);
        var operation = () => Registry.RegisterAsync("a", _actor.ToString());

        // Assert
        _sent.Select(s => s.Connection).Should().Equal("b");
        _aborted.Should().Contain("a");
        await operation.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task SupportExpiration_AfterConnect_StopsOperations()
    {
        // Arrange
        await SeedAsync();
        var payload = new ImpersonationCookiePayload(_actor.ToString(), Guid.Empty, _tenantA,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), _directory);
        _support.Setup(s => s.ValidateWorkspaceTokenAsync("support", It.IsAny<CancellationToken>())).ReturnsAsync(() => payload);
        await ConnectAsync("support", _tenantA, Http(_tenantA, "support", csp: true, cookie: "support"));
        await Delivery.BroadcastAsync(_actor.ToString(), Notification(_tenantA), Send);
        payload = payload with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };

        // Act
        await Delivery.BroadcastAsync(_actor.ToString(), Notification(_tenantA), Send);
        var operation = () => Registry.RequireAsync("support");

        // Assert
        await operation.Should().ThrowAsync<HubException>();
        _aborted.Should().Contain("support");
        _sent.Select(s => s.Connection).Should().Equal("support");
    }

    [Fact]
    public async Task MarkRead_CannotSynchronizeForeignNotification()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        var foreign = Notification(_tenantB);
        await using (var db = await Db())
        {
            db.AlertNotifications.Add(foreign);
            await db.SaveChangesAsync();
        }

        // Act
        var action = () => Delivery.MarkReadAsync("a", foreign.Id, Send);

        // Assert
        await action.Should().ThrowAsync<HubException>();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task NotificationWithoutTrustedTenant_IsNotDelivered()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);

        // Act
        var action = () => Delivery.BroadcastAsync(_actor.ToString(), Notification(Guid.Empty), Send);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>();
        _sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousObjectIdAcrossDirectories_DoesNotReceiveLegacyAddressedNotification(bool historicalGrant)
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        await using (var db = await Db())
        {
            var person = new Person { TenantId = _tenantA, DisplayName = "Different directory" };
            db.Persons.Add(person);
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                TenantId = _tenantA, DirectoryTenantId = Guid.NewGuid(), ObjectId = _actor,
                PersonId = person.Id, GrantedBy = "test",
                RevokedAt = historicalGrant ? DateTimeOffset.UtcNow : null,
            });
            await db.SaveChangesAsync();
        }

        // Act
        await Delivery.BroadcastAsync(_actor.ToString(), Notification(_tenantA), Send);

        // Assert
        _sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SystemRoleRevocation_StopsDelivery_EvenForOrdinaryCspAdmin(bool csp)
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA, Http(_tenantA, csp: csp));
        var notification = Notification(_tenantA);
        Guid roleId;
        await using (var db = await Db())
        {
            var person = await db.OrganizationMemberships.Where(m => m.TenantId == _tenantA).Select(m => m.PersonId).SingleAsync();
            var system = new RegisteredSystem { TenantId = _tenantA, Name = "Assigned", IsActive = true };
            db.RegisteredSystems.Add(system);
            var role = new SystemRoleAssignment { TenantId = _tenantA, PersonId = person,
                RegisteredSystemId = system.Id, Role = OrganizationRole.MissionOwner };
            db.SystemRoleAssignments.Add(role);
            roleId = role.Id;
            var alert = new ComplianceAlert { Id = Guid.NewGuid(), TenantId = _tenantA, RegisteredSystemId = system.Id };
            db.ComplianceAlerts.Add(alert);
            notification.AlertId = alert.Id;
            await db.SaveChangesAsync();
        }

        // Act
        await Delivery.BroadcastAsync(_actor.ToString(), notification, Send);
        await using (var db = await Db())
        {
            (await db.SystemRoleAssignments.SingleAsync(r => r.Id == roleId)).RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await Delivery.BroadcastAsync(_actor.ToString(), notification, Send);

        // Assert
        _sent.Select(s => s.Connection).Should().Equal("a");
    }

    [Fact]
    public async Task WizardSubscription_IsTenantBound_AndRechecksAdministratorOnDelivery()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        var job = new WizardJobStatus { TenantId = _tenantA, EnqueuedBy = _actor };
        Guid roleId;
        await using (var db = await Db())
        {
            var person = await db.OrganizationMemberships.Where(m => m.TenantId == _tenantA).Select(m => m.PersonId).SingleAsync();
            var role = new OrganizationRoleAssignment { TenantId = _tenantA, PersonId = person, Role = OrganizationRole.Administrator };
            db.OrganizationRoleAssignments.Add(role);
            roleId = role.Id;
            db.WizardJobStatuses.Add(job);
            await db.SaveChangesAsync();
        }
        await Delivery.SubscribeWizardAsync("a", job.Id);
        object?[] payload = [new { tenantId = _tenantA, jobId = job.Id }];

        // Act
        await Delivery.SendLegacyGroupAsync($"wizard-{_tenantA}", "WizardJobStatus", payload, Send);
        await Delivery.SendLegacyGroupAsync($"wizard-{_tenantA}-job-{job.Id}", "WizardJobStatus", payload, Send);
        await using (var db = await Db())
        {
            (await db.OrganizationRoleAssignments.SingleAsync(r => r.Id == roleId)).RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await Delivery.SendLegacyGroupAsync($"wizard-{_tenantA}-job-{job.Id}", "WizardJobStatus", payload, Send);

        // Assert
        _sent.Select(s => s.Connection).Should().Equal("a");
    }

    [Fact]
    public async Task WizardSubscription_CannotSelectAnotherOrganizationsJob()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        var job = new WizardJobStatus { TenantId = _tenantB, EnqueuedBy = _actor };
        await using (var db = await Db())
        {
            db.WizardJobStatuses.Add(job);
            await db.SaveChangesAsync();
        }

        // Act
        var action = () => Delivery.SubscribeWizardAsync("a", job.Id);

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task UnreadCounts_AreRecomputedIndependentlyForEachWorkspace()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        await ConnectAsync("b", _tenantB);
        await using (var db = await Db())
        {
            db.AlertNotifications.AddRange(Notification(_tenantA), Notification(_tenantB), Notification(_tenantB));
            await db.SaveChangesAsync();
        }

        // Act
        await Delivery.UnreadCountAsync(_actor.ToString(), Send);

        // Assert
        _sent.Should().HaveCount(2);
        System.Text.Json.JsonSerializer.SerializeToElement(_sent.Single(s => s.Connection == "a").Args[0])
            .GetProperty("unreadCount").GetInt32().Should().Be(1);
        System.Text.Json.JsonSerializer.SerializeToElement(_sent.Single(s => s.Connection == "b").Args[0])
            .GetProperty("unreadCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task MarkRead_SynchronizesOnlySameTenantAndIdentity()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        await ConnectAsync("a2", _tenantA);
        await ConnectAsync("b", _tenantB);
        var notification = Notification(_tenantA);
        await using (var db = await Db())
        {
            db.AlertNotifications.Add(notification);
            await db.SaveChangesAsync();
        }

        // Act
        await Delivery.MarkReadAsync("a", notification.Id, Send);

        // Assert
        _sent.Select(s => s.Connection).Should().BeEquivalentTo(["a", "a2"]);
    }

    [Fact]
    public async Task UnlinkedLegacyNotification_CannotBypassSystemVisibility()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        var notification = Notification(_tenantA);
        notification.AlertId = Guid.Empty;

        // Act
        await Delivery.BroadcastAsync(_actor.ToString(), notification, Send);

        // Assert
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task DisabledTenant_IsRecheckedBeforeDelivery()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        await using (var db = await Db())
        {
            (await db.Tenants.SingleAsync(t => t.Id == _tenantA)).Status = TenantStatus.Disabled;
            await db.SaveChangesAsync();
        }

        // Act
        await Delivery.BroadcastAsync(_actor.ToString(), Notification(_tenantA), Send);

        // Assert
        _sent.Should().BeEmpty();
        _aborted.Should().Contain("a");
    }

    [Fact]
    public async Task NoSelectors_OnlyAutoSelectsOneAuthorizedWorkspace()
    {
        // Arrange
        await SeedAsync();
        var http = Http(_tenantA);
        http.Request.QueryString = QueryString.Empty;
        var ambiguous = () => Registry.ConnectAsync("ambiguous", http, () => { });
        await ambiguous.Should().ThrowAsync<HubException>();
        await using (var db = await Db())
        {
            (await db.OrganizationMemberships.SingleAsync(m => m.TenantId == _tenantB)).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Act
        await Registry.ConnectAsync("legacy", http, () => { });
        var group = await Registry.RegisterAsync("legacy", null);

        // Assert
        (await Registry.RequireAsync("legacy")).Workspace.TenantId.Should().Be(_tenantA);
        group.Should().NotStartWith("user:");
    }

    [Fact]
    public async Task AnonymousOrExpiredAuthentication_CannotConnect()
    {
        // Arrange
        await SeedAsync();
        var anonymous = Http(_tenantA);
        anonymous.User = new ClaimsPrincipal(new ClaimsIdentity());
        var expired = Http(_tenantA);
        ((ClaimsIdentity)expired.User.Identity!).AddClaim(new("exp", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString()));

        // Act
        var anonymousAction = () => Registry.ConnectAsync("anonymous", anonymous, () => { });
        var expiredAction = () => Registry.ConnectAsync("expired", expired, () => { });

        // Assert
        await anonymousAction.Should().ThrowAsync<HubException>();
        await expiredAction.Should().ThrowAsync<HubException>();
        typeof(NotificationHub).GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Broadcaster_UsesValidatedConnections_NotUnscopedUserGroups()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        await ConnectAsync("b", _tenantB);
        var client = new Mock<ISingleClientProxy>(MockBehavior.Strict);
        client.Setup(c => c.SendCoreAsync("NewNotification", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        clients.Setup(c => c.Client("a")).Returns(client.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var broadcaster = new SignalRNotificationBroadcaster(hub.Object,
            NullLogger<SignalRNotificationBroadcaster>.Instance, Delivery);

        // Act
        await broadcaster.BroadcastToUserAsync(_actor.ToString(), Notification(_tenantA));

        // Assert
        clients.Verify(c => c.Client("a"), Times.Once);
        clients.Verify(c => c.Group(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LegacySspExport_UsesOwningSystemAndRevalidatesRoles()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        await ConnectAsync("b", _tenantB);
        Guid exportId;
        await using (var db = await Db())
        {
            var alert = await db.ComplianceAlerts.SingleAsync(a => a.Id == _alerts[_tenantA]);
            var export = new SspExport { SystemId = alert.RegisteredSystemId!, GeneratedBy = _actor.ToString(), Format = "pdf" };
            db.SspExports.Add(export);
            exportId = export.Id;
            await db.SaveChangesAsync();
        }
        object?[] payload = [new { exportId, step = "Ready", percentage = 100 }];

        // Act
        await Delivery.SendLegacyGroupAsync($"user:{_actor}", "SspExportProgress", payload, Send);
        await using (var db = await Db())
        {
            (await db.SystemRoleAssignments.SingleAsync(r => r.TenantId == _tenantA)).RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        await Delivery.SendLegacyGroupAsync($"user:{_actor}", "SspExportProgress", payload, Send);

        // Assert
        _sent.Select(s => s.Connection).Should().Equal("a");
    }

    [Fact]
    public async Task RegisteredLifetimeManager_RevalidatesLegacyWizardDelivery()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        var job = new WizardJobStatus { TenantId = _tenantA };
        await using (var db = await Db())
        {
            var person = await db.OrganizationMemberships.Where(m => m.TenantId == _tenantA).Select(m => m.PersonId).SingleAsync();
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
                { TenantId = _tenantA, PersonId = person, Role = OrganizationRole.Administrator });
            db.WizardJobStatuses.Add(job);
            await db.SaveChangesAsync();
        }
        await Delivery.SubscribeWizardAsync("a", job.Id);
        var manager = _services.GetRequiredService<HubLifetimeManager<NotificationHub>>();
        await manager.SendGroupAsync($"wizard-{_tenantA}-job-{job.Id}", "WizardJobStatus", [new { tenantId = _tenantA, jobId = job.Id }]);
        await using (var db = await Db())
        {
            (await db.OrganizationMemberships.SingleAsync(m => m.TenantId == _tenantA)).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Act
        await manager.SendGroupAsync($"wizard-{_tenantA}-job-{job.Id}", "WizardJobStatus", [new { tenantId = _tenantA, jobId = job.Id }]);

        // Assert
        manager.Should().BeOfType<NotificationHubLifetimeManager>();
        _aborted.Should().Contain("a");
    }

    [Fact]
    public async Task HubLifecycle_BindsScopeAndCleansUpSubscriptions()
    {
        // Arrange
        await SeedAsync();
        var http = Http(_tenantA);
        var features = new FeatureCollection();
        var httpFeature = new Mock<IHttpContextFeature>();
        httpFeature.SetupGet(f => f.HttpContext).Returns(http);
        features.Set(httpFeature.Object);
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("hub");
        context.SetupGet(c => c.Features).Returns(features);
        var groups = new Mock<IGroupManager>();
        var client = new Mock<ISingleClientProxy>();
        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.Client("hub")).Returns(client.Object);
        var hub = new NotificationHub(NullLogger<NotificationHub>.Instance, Registry, Delivery)
            { Context = context.Object, Groups = groups.Object, Clients = clients.Object };
        var notification = Notification(_tenantA);
        var job = new WizardJobStatus { TenantId = _tenantA };
        await using (var db = await Db())
        {
            db.AlertNotifications.Add(notification);
            db.WizardJobStatuses.Add(job);
            var person = await db.OrganizationMemberships.Where(m => m.TenantId == _tenantA).Select(m => m.PersonId).SingleAsync();
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
                { TenantId = _tenantA, PersonId = person, Role = OrganizationRole.Administrator });
            await db.SaveChangesAsync();
        }

        // Act
        await hub.OnConnectedAsync();
        await hub.RegisterUser(null);
        await hub.MarkRead(notification.Id.ToString());
        await hub.SubscribeToWizardJob(job.Id.ToString());
        await hub.OnDisconnectedAsync(null);

        // Assert
        Registry.Connections.Should().BeEmpty();
        groups.Verify(g => g.AddToGroupAsync("hub", It.Is<string>(s => s.StartsWith("notifications:")), It.IsAny<CancellationToken>()), Times.Exactly(2));
        client.Verify(c => c.SendCoreAsync("NotificationRead", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("wizard")]
    public async Task HubMethods_RejectMalformedResourceIdentifiers(string method)
    {
        // Arrange
        var hub = new NotificationHub(NullLogger<NotificationHub>.Instance, Registry, Delivery);

        // Act
        var action = () => method == "read" ? hub.MarkRead("forged") : hub.SubscribeToWizardJob("forged");

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task ExistingConnection_CannotBeRetargetedOrReboundToAnotherPerson()
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        var duplicate = () => Registry.ConnectAsync("a", Http(_tenantB), () => { });
        await duplicate.Should().ThrowAsync<HubException>();
        await using (var db = await Db())
        {
            var person = new Person { TenantId = _tenantA, DisplayName = "Replacement" };
            db.Persons.Add(person);
            (await db.OrganizationMemberships.SingleAsync(m => m.TenantId == _tenantA)).PersonId = person.Id;
            await db.SaveChangesAsync();
        }

        // Act
        var action = () => Registry.RequireAsync("a");

        // Assert
        await action.Should().ThrowAsync<HubException>();
        _aborted.Should().Contain("a");
    }

    [Theory]
    [InlineData("recipient")]
    [InlineData("tenant")]
    [InlineData("unread")]
    public async Task Publishers_MustSupplyConsistentTrustedIdentityAndTenant(string invalid)
    {
        // Arrange
        await SeedAsync();
        await ConnectAsync("a", _tenantA);
        var notification = Notification(_tenantA);
        if (invalid == "recipient") notification.UserId = Guid.NewGuid().ToString();
        if (invalid == "tenant") notification.TenantId = _tenantB;

        // Act
        var action = () => invalid == "unread" ? Delivery.UnreadCountAsync("not-an-oid", Send)
            : Delivery.BroadcastAsync(_actor.ToString(), notification, Send);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkRead_RequiresNotificationRegistration()
    {
        // Arrange
        await SeedAsync();
        await Registry.ConnectAsync("unregistered", Http(_tenantA), () => { });

        // Act
        var action = () => Delivery.MarkReadAsync("unregistered", Guid.NewGuid(), Send);

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
