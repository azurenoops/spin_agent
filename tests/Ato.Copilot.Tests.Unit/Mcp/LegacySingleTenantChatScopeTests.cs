using System.Security.Claims;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Mcp.Server;
using Ato.Copilot.Mcp.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public class LegacySingleTenantChatScopeTests
{
    [Fact]
    public void ValidatedSingleTenantContext_KeepsActorIsolatedHistoryWithoutWorkspaceSelectors()
    {
        // Arrange
        var tenant = new TenantContext(Guid.NewGuid()) { Status = TenantStatus.Active };
        var accessor = new TenantContextAccessor();
        using var services = Services(tenant, accessor, DeploymentMode.SingleTenant);
        using var scope = accessor.Push(tenant);
        var http = Request(services);

        // Act
        var first = WorkspaceChatScope.Resolve(http);
        var second = WorkspaceChatScope.Resolve(http);

        // Assert
        first.Kind.Should().Be("legacy");
        first.TenantId.Should().Be(tenant.EffectiveTenantId);
        first.StorageKey("conversation").Should().Be(second.StorageKey("conversation"));
        http.User = Principal(Guid.NewGuid());
        WorkspaceChatScope.Resolve(http).StorageKey("conversation").Should().NotBe(first.StorageKey("conversation"));
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("multiple-tenants")]
    [InlineData("explicit-selector")]
    [InlineData("support")]
    [InlineData("disabled")]
    public void MissingCanonicalWorkspace_DoesNotUseAnUnauthorizedCompatibilityFallback(string invalid)
    {
        // Arrange
        var tenant = new TenantContext(Guid.NewGuid())
        {
            Status = invalid == "disabled" ? TenantStatus.Disabled : TenantStatus.Active,
            ImpersonatedTenantId = invalid == "support" ? Guid.NewGuid() : null,
        };
        var accessor = new TenantContextAccessor();
        using var services = Services(tenant, accessor,
            invalid == "multiple-tenants" ? DeploymentMode.MultiTenant : DeploymentMode.SingleTenant);
        using var scope = invalid == "unbound" ? null : accessor.Push(tenant);
        var http = Request(services);
        if (invalid == "explicit-selector") http.Request.Headers["X-Workspace-Kind"] = "organization";

        // Act
        var resolve = () => WorkspaceChatScope.Resolve(http);

        // Assert
        resolve.Should().Throw<WorkspaceException>().Which.StatusCode.Should().Be(409);
    }

    private static ServiceProvider Services(TenantContext tenant, TenantContextAccessor accessor, DeploymentMode mode) =>
        new ServiceCollection()
            .AddSingleton<ITenantContext>(tenant)
            .AddSingleton<ITenantContextAccessor>(accessor)
            .AddSingleton<IOptions<DeploymentOptions>>(Options.Create(new DeploymentOptions { Mode = mode }))
            .BuildServiceProvider();

    private static DefaultHttpContext Request(IServiceProvider services) =>
        new() { RequestServices = services, User = Principal(Guid.NewGuid()) };

    private static ClaimsPrincipal Principal(Guid subject) => new(new ClaimsIdentity(
        [new Claim("tid", "11111111-1111-1111-1111-111111111111"), new Claim("oid", subject.ToString())], "Synthetic"));
}
