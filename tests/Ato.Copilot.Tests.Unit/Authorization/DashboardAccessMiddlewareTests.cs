using System.Security.Claims;
using Ato.Copilot.Core.Authorization;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Authorization;

public class DashboardAccessMiddlewareTests
{
    [Fact]
    public async Task Dashboard_access_forwards_authenticated_directory_identity_to_membership_resolution()
    {
        // Arrange
        var directoryId = Guid.NewGuid();
        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = CreateContext(HttpMethods.Get, "/api/dashboard/portfolio");
        ((ClaimsIdentity)context.User.Identity!).AddClaim(new Claim("tid", directoryId.ToString()));
        var service = new Mock<IEffectiveAccessService>();
        service.Setup(x => x.ResolveAsync(It.Is<EffectiveAccessSubject>(subject => subject.DirectoryTenantId == directoryId),
            It.IsAny<CancellationToken>())).ReturnsAsync((EffectiveAccessSubject subject, CancellationToken _) =>
                new EffectiveAccessResult("1", DateTimeOffset.UtcNow, subject, null,
                    [SystemDestination("system-a", [AdminPortalActions.SystemView], "OrganizationRoleAssignment")]));
        // Act
        await middleware.InvokeAsync(context, CreateTenantContext(), service.Object);
        // Assert
        nextCalled.Should().BeTrue();
        service.VerifyAll();
    }

    [Fact]
    public async Task Admin_only_user_cannot_read_dashboard_system_data()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = CreateContext(HttpMethods.Get, "/api/dashboard/portfolio");
        var service = CreateAccessService([]);

        // Act
        await middleware.InvokeAsync(context, CreateTenantContext(), service);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/systems/system-a/oscal/import/runs")]
    [InlineData("/api/v1/systems/system-a/package/download")]
    [InlineData("/api/roles/system/system-a")]
    public async Task Admin_only_user_cannot_read_system_import_export_or_role_data(string path)
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = CreateContext(HttpMethods.Get, path, ("systemId", "system-a"));
        var service = CreateAccessService([]);

        // Act
        await middleware.InvokeAsync(context, CreateTenantContext(), service);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/systems/system-a/oscal/import/runs")]
    [InlineData("/api/v1/systems/system-a/package/download")]
    [InlineData("/api/roles/system/system-a")]
    public async Task Assigned_user_can_read_system_import_export_or_role_data(string path)
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = CreateContext(HttpMethods.Get, path, ("systemId", "system-a"));
        var service = CreateAccessService([
            SystemDestination("system-a", [AdminPortalActions.SystemView], "SystemRoleAssignment"),
        ]);

        // Act
        await middleware.InvokeAsync(context, CreateTenantContext(), service);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Named_system_assignment_cannot_read_another_system()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = CreateContext(
            HttpMethods.Get,
            "/api/dashboard/systems/system-b",
            ("systemId", "system-b"));
        var service = CreateAccessService([
            SystemDestination("system-a", [AdminPortalActions.SystemView], "SystemRoleAssignment"),
        ]);

        // Act
        await middleware.InvokeAsync(context, CreateTenantContext(), service);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Named_system_assignment_can_read_its_system()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = CreateContext(
            HttpMethods.Get,
            "/api/dashboard/systems/system-a",
            ("systemId", "system-a"));
        var service = CreateAccessService([
            SystemDestination("system-a", [AdminPortalActions.SystemView], "SystemRoleAssignment"),
        ]);

        // Act
        await middleware.InvokeAsync(context, CreateTenantContext(), service);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task System_specific_assignment_cannot_read_tenant_aggregate()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = CreateContext(HttpMethods.Get, "/api/dashboard/portfolio");
        var service = CreateAccessService([
            SystemDestination("system-a", [AdminPortalActions.SystemView], "SystemRoleAssignment"),
        ]);

        // Act
        await middleware.InvokeAsync(context, CreateTenantContext(), service);

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Organization_rmf_assignment_can_read_tenant_aggregate()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(() => nextCalled = true);
        var context = CreateContext(HttpMethods.Get, "/api/dashboard/portfolio");
        var service = CreateAccessService([
            SystemDestination("system-a", [AdminPortalActions.SystemView], "OrganizationRoleAssignment"),
        ]);

        // Act
        await middleware.InvokeAsync(context, CreateTenantContext(), service);

        // Assert
        nextCalled.Should().BeTrue();
    }

    private static DashboardAccessMiddleware CreateMiddleware(Action next) =>
        new(
            _ =>
            {
                next();
                return Task.CompletedTask;
            },
            new TestHostEnvironment());

    private static DefaultHttpContext CreateContext(
        string method,
        string path,
        params (string Key, string Value)[] routeValues)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            new Claim(ClaimTypes.Name, "Test User"),
        ], "Test"));
        foreach (var (key, value) in routeValues)
        {
            context.Request.RouteValues[key] = value;
        }
        return context;
    }

    private static ITenantContext CreateTenantContext()
    {
        var tenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(value => value.TenantId).Returns(tenantId);
        tenant.SetupGet(value => value.EffectiveTenantId).Returns(tenantId);
        tenant.SetupGet(value => value.Status).Returns(TenantStatus.Active);
        return tenant.Object;
    }

    private static IEffectiveAccessService CreateAccessService(
        IReadOnlyList<EffectiveAccessDestination> destinations)
    {
        var service = new Mock<IEffectiveAccessService>();
        service.Setup(value => value.ResolveAsync(
                It.IsAny<EffectiveAccessSubject>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EffectiveAccessSubject subject, CancellationToken _) =>
                new EffectiveAccessResult("1", DateTimeOffset.UtcNow, subject, null, destinations));
        return service.Object;
    }

    private static EffectiveAccessDestination SystemDestination(
        string id,
        IReadOnlyList<string> actions,
        string source) =>
        new(
            $"system:{id}",
            WorkspaceKind.System,
            AccessScopeKind.System,
            id,
            id,
            actions,
            [new AccessBadge("ISSO", source)],
            "Available");

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
