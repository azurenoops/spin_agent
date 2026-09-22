using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Hubs.Notifications;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public partial class NotificationHubIsolationTests
{
    private WorkspaceProgressDeliveryService Progress => _services.GetRequiredService<WorkspaceProgressDeliveryService>();
    private async Task<Guid> ProgressResourceAsync(ProgressResourceKind kind, Guid tenant)
    {
        var id = Guid.NewGuid();
        await using var db = await Db();
        var systemId = (await db.ComplianceAlerts.SingleAsync(a => a.Id == _alerts[tenant])).RegisteredSystemId!;
        if (kind == ProgressResourceKind.Package)
        {
            db.AuthorizationPackages.Add(new AuthorizationPackage
                { Id = id.ToString(), TenantId = tenant, RegisteredSystemId = systemId, GeneratedBy = _actor.ToString() });
            await db.SaveChangesAsync();
        }
        else
            _services.GetRequiredService<ScanImportStatusTracker>().Register(id.ToString(), systemId);
        return id;
    }

    private Task ConnectProgressAsync(string id, ProgressResourceKind kind, Guid tenant, DefaultHttpContext? http = null)
    {
        http ??= Http(tenant);
        http.Request.Path = kind == ProgressResourceKind.Package ? "/hubs/package" : "/hubs/import-progress";
        return Registry.ConnectAsync(id, http, () => _aborted.Add(id));
    }

    private Task PublishProgressAsync(ProgressResourceKind kind, Guid id) =>
        kind == ProgressResourceKind.Package
            ? Progress.SendGroupAsync(kind, $"package:{id}", "PackageStatusChanged", [new { packageId = id, status = "Processing" }], Send)
            : Progress.SendGroupAsync(kind, $"import:{id}", "ImportProgress", [new { jobId = id, status = "Processing" }], Send);
    [Fact]
    public async Task PackageProgress_RequiresValidatedWorkspace()
    {
        // Arrange
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("unknown");
        var hub = new PackageHub(NullLogger<PackageHub>.Instance, Registry, Progress)
            { Context = context.Object, Groups = Mock.Of<IGroupManager>() };

        // Act
        var action = () => hub.SubscribeToPackage(Guid.NewGuid().ToString());

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task ImportProgress_RequiresValidatedWorkspace()
    {
        // Arrange
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("unknown");
        var hub = new ImportProgressHub(Registry, Progress) { Context = context.Object, Groups = Mock.Of<IGroupManager>() };

        // Act
        var action = () => hub.JoinImportGroup(Guid.NewGuid().ToString());

        // Assert
        await action.Should().ThrowAsync<HubException>();
    }

    [Theory]
    [InlineData(ProgressResourceKind.Package)]
    [InlineData(ProgressResourceKind.Import)]
    public async Task Progress_TwoOrdinaryContexts_CannotSubscribeOrReceiveAcrossOrganizations(ProgressResourceKind kind)
    {
        // Arrange
        await SeedAsync();
        var a = await ProgressResourceAsync(kind, _tenantA);
        var b = await ProgressResourceAsync(kind, _tenantB);
        await ConnectProgressAsync("a", kind, _tenantA);
        await ConnectProgressAsync("b", kind, _tenantB);
        var group = await Progress.SubscribeAsync("a", kind, a.ToString());
        await Progress.SubscribeAsync("b", kind, b.ToString());

        // Act
        var crossOrganization = () => Progress.SubscribeAsync("b", kind, a.ToString());
        await PublishProgressAsync(kind, a);

        // Assert
        await crossOrganization.Should().ThrowAsync<HubException>();
        group.Should().Contain(_tenantA.ToString("N")).And.Contain(_directory.ToString("N")).And.Contain(_actor.ToString("N"));
        _sent.Select(s => s.Connection).Should().Equal("a");
    }

    [Theory]
    [InlineData(ProgressResourceKind.Package)]
    [InlineData(ProgressResourceKind.Import)]
    public async Task Progress_MembershipRevocation_AbortsSubscriptionAndOperations(ProgressResourceKind kind)
    {
        // Arrange
        await SeedAsync();
        var resource = await ProgressResourceAsync(kind, _tenantA);
        await ConnectProgressAsync("a", kind, _tenantA);
        await Progress.SubscribeAsync("a", kind, resource.ToString());
        await using (var db = await Db())
        {
            (await db.OrganizationMemberships.SingleAsync(m => m.TenantId == _tenantA)).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Act
        await PublishProgressAsync(kind, resource);
        var operation = () => Progress.UnsubscribeAsync("a", kind, resource.ToString());

        // Assert
        _sent.Should().BeEmpty();
        _aborted.Should().Contain("a");
        await operation.Should().ThrowAsync<HubException>();
    }

    [Theory]
    [InlineData(ProgressResourceKind.Package)]
    [InlineData(ProgressResourceKind.Import)]
    public async Task Progress_RoleRevocation_DropsDeliveryForOrdinaryCspAdmin(ProgressResourceKind kind)
    {
        // Arrange
        await SeedAsync();
        var resource = await ProgressResourceAsync(kind, _tenantA);
        await ConnectProgressAsync("a", kind, _tenantA, Http(_tenantA, csp: true));
        await Progress.SubscribeAsync("a", kind, resource.ToString());
        await using (var db = await Db())
        {
            (await db.SystemRoleAssignments.SingleAsync(r => r.TenantId == _tenantA)).RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Act
        await PublishProgressAsync(kind, resource);
        var resubscribe = () => Progress.SubscribeAsync("a", kind, resource.ToString());

        // Assert
        _sent.Should().BeEmpty();
        await resubscribe.Should().ThrowAsync<HubException>();
    }

    [Theory]
    [InlineData(ProgressResourceKind.Package)]
    [InlineData(ProgressResourceKind.Import)]
    public async Task Progress_SupportExpiry_RevalidatesBeforeDelivery(ProgressResourceKind kind)
    {
        // Arrange
        await SeedAsync();
        var resource = await ProgressResourceAsync(kind, _tenantA);
        var session = new ImpersonationCookiePayload(_actor.ToString(), Guid.Empty, _tenantA,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), _directory);
        _support.Setup(s => s.ValidateWorkspaceTokenAsync("progress-support", It.IsAny<CancellationToken>())).ReturnsAsync(() => session);
        await ConnectProgressAsync("support", kind, _tenantA, Http(_tenantA, "support", csp: true, cookie: "progress-support"));
        await Progress.SubscribeAsync("support", kind, resource.ToString());
        await PublishProgressAsync(kind, resource);
        session = session with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };

        // Act
        await PublishProgressAsync(kind, resource);

        // Assert
        _sent.Select(s => s.Connection).Should().Equal("support");
        _aborted.Should().Contain("support");
    }

    [Theory]
    [InlineData(ProgressResourceKind.Package)]
    [InlineData(ProgressResourceKind.Import)]
    public async Task Progress_Unsubscribe_StopsDeliveryAndWrongTransportCannotSubscribe(ProgressResourceKind kind)
    {
        // Arrange
        await SeedAsync();
        var resource = await ProgressResourceAsync(kind, _tenantA);
        await ConnectProgressAsync("progress", kind, _tenantA);
        await ConnectAsync("notifications", _tenantA);
        await Progress.SubscribeAsync("progress", kind, resource.ToString());

        // Act
        await Progress.UnsubscribeAsync("progress", kind, resource.ToString());
        await PublishProgressAsync(kind, resource);
        var wrongTransport = () => Progress.SubscribeAsync("notifications", kind, resource.ToString());

        // Assert
        _sent.Should().BeEmpty();
        await wrongTransport.Should().ThrowAsync<HubException>();
    }

    [Theory]
    [InlineData(ProgressResourceKind.Package)]
    [InlineData(ProgressResourceKind.Import)]
    public async Task Progress_RejectsRetargetedPayloadAndUnknownResources(ProgressResourceKind kind)
    {
        // Arrange
        await SeedAsync();
        await ConnectProgressAsync("a", kind, _tenantA);
        var resource = await ProgressResourceAsync(kind, _tenantA);
        await Progress.SubscribeAsync("a", kind, resource.ToString());
        var prefix = kind == ProgressResourceKind.Package ? "package:" : "import:";
        var method = kind == ProgressResourceKind.Package ? "PackageStatusChanged" : "ImportProgress";

        // Act
        var unknown = () => Progress.SubscribeAsync("a", kind, Guid.NewGuid().ToString());
        var malformed = () => Progress.SubscribeAsync("a", kind, "not-a-guid");
        var mismatched = () => Progress.SendGroupAsync(kind, $"{prefix}{resource}", method,
            [new { packageId = Guid.NewGuid(), jobId = Guid.NewGuid() }], Send);

        // Assert
        await unknown.Should().ThrowAsync<HubException>();
        await malformed.Should().ThrowAsync<HubException>();
        await mismatched.Should().ThrowAsync<InvalidOperationException>();
        _sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ProgressResourceKind.Package)]
    [InlineData(ProgressResourceKind.Import)]
    public async Task Progress_HubLifecycle_UsesScopedGroupsAndRevocationSafePublisher(ProgressResourceKind kind)
    {
        // Arrange
        await SeedAsync();
        var id = await ProgressResourceAsync(kind, _tenantA);
        var http = Http(_tenantA);
        http.Request.Path = kind == ProgressResourceKind.Package ? "/hubs/package" : "/hubs/import-progress";
        var features = new FeatureCollection();
        var feature = new Mock<IHttpContextFeature>();
        feature.SetupGet(f => f.HttpContext).Returns(http);
        features.Set(feature.Object);
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("lifecycle");
        context.SetupGet(c => c.Features).Returns(features);
        var groups = new Mock<IGroupManager>();
        Hub hub = kind == ProgressResourceKind.Package
            ? new PackageHub(NullLogger<PackageHub>.Instance, Registry, Progress)
            : new ImportProgressHub(Registry, Progress);
        hub.Context = context.Object;
        hub.Groups = groups.Object;

        // Act
        await hub.OnConnectedAsync();
        if (hub is PackageHub package)
        {
            await package.SubscribeToPackage(id.ToString());
            await _services.GetRequiredService<HubLifetimeManager<PackageHub>>()
                .SendGroupAsync($"package:{id}", "PackageComplete", [new { packageId = id, downloadUrl = "/authenticated-download" }]);
            await package.UnsubscribeFromPackage(id.ToString());
        }
        else if (hub is ImportProgressHub import)
        {
            await import.JoinImportGroup(id.ToString());
            await _services.GetRequiredService<HubLifetimeManager<ImportProgressHub>>()
                .SendGroupAsync($"import:{id}", "ImportProgress", [new { jobId = id, status = "Completed" }]);
            await import.LeaveImportGroup(id.ToString());
        }
        await hub.OnDisconnectedAsync(null);

        // Assert
        Registry.Connections.Should().BeEmpty();
        groups.Verify(g => g.AddToGroupAsync("lifecycle", It.Is<string>(s => s.Contains(_tenantA.ToString("N"))), It.IsAny<CancellationToken>()), Times.Once);
        groups.Verify(g => g.RemoveFromGroupAsync("lifecycle", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportPollingFallback_CannotReadAnotherOrganizationsJob()
    {
        // Arrange
        await SeedAsync();
        await using var db = await Db();
        var foreignSystem = (await db.ComplianceAlerts.SingleAsync(a => a.Id == _alerts[_tenantB])).RegisteredSystemId!;
        using var server = HttpServer();
        var id = Guid.NewGuid().ToString();
        server.Services.GetRequiredService<ScanImportStatusTracker>().Register(id, foreignSystem);
        using var client = HttpClient(server, _tenantA);

        // Act
        var response = await client.GetAsync($"/api/dashboard/systems/{foreignSystem}/scans/import/{id}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ImportPollingFallback_ReadPermissionDoesNotGrantCancellationOrUpload()
    {
        // Arrange
        await SeedAsync();
        await using var db = await Db();
        var system = (await db.ComplianceAlerts.SingleAsync(a => a.Id == _alerts[_tenantA])).RegisteredSystemId!;
        using var server = HttpServer();
        var id = Guid.NewGuid().ToString();
        var state = server.Services.GetRequiredService<ScanImportStatusTracker>().Register(id, system);
        using var client = HttpClient(server, _tenantA);
        var root = $"/api/dashboard/systems/{system}/scans/import";

        // Act
        var read = await client.GetAsync($"{root}/{id}/status");
        var cancel = await client.DeleteAsync($"{root}/{id}");
        var upload = await client.PostAsync(root, null);

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        cancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        state.CancelRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Progress_TransportFailure_IsNotReportedAsAuthorizationRevocation()
    {
        // Arrange
        await SeedAsync();
        var id = await ProgressResourceAsync(ProgressResourceKind.Package, _tenantA);
        await ConnectProgressAsync("a", ProgressResourceKind.Package, _tenantA);
        await Progress.SubscribeAsync("a", ProgressResourceKind.Package, id.ToString());

        // Act
        var action = () => Progress.SendGroupAsync(ProgressResourceKind.Package, $"package:{id}",
            "PackageStatusChanged", [new { packageId = id }],
            (_, _, _, _) => throw new HubException("Transport failed"));

        // Assert
        await action.Should().ThrowAsync<HubException>().WithMessage("Transport failed");
    }
}
