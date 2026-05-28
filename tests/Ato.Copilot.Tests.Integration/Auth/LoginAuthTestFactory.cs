using System.Security.Claims;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Feature 051 — derived test factory that adds an
/// <see cref="IStartupFilter"/> which synthesises a
/// <see cref="ClaimsPrincipal"/> for the request from the
/// <c>X-Test-Oid</c> / <c>X-Test-Tid</c> / <c>X-Test-DisplayName</c> /
/// <c>X-Test-Roles</c> headers. The production
/// <see cref="Ato.Copilot.Mcp.Middleware.CacAuthenticationMiddleware"/>
/// silently no-ops in the "Testing" environment without a Bearer header,
/// so the endpoint sees an unauthenticated principal by default — this
/// filter lets a single test opt-in to a synthetic identity by adding
/// the headers to its request.
/// </summary>
/// <remarks>
/// Inherits <see cref="MultiTenantWebApplicationFactory{TStartup}"/> so we
/// also get the SQLite per-fixture DB and seeded Tenants A &amp; B.
/// </remarks>
public sealed class LoginAuthTestFactory
    : MultiTenantWebApplicationFactory<McpProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, TestClaimsInjectorStartupFilter>();
        });
    }
}

/// <summary>
/// Inserts a middleware at the very start of the pipeline that converts
/// <c>X-Test-*</c> headers into a synthesised
/// <see cref="ClaimsPrincipal"/>. No-op when the headers are absent.
/// </summary>
internal sealed class TestClaimsInjectorStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (ctx, n) =>
            {
                if (ctx.Request.Headers.TryGetValue("X-Test-Oid", out var oid) &&
                    !string.IsNullOrWhiteSpace(oid.ToString()))
                {
                    var claims = new List<Claim>
                    {
                        new("oid", oid.ToString()),
                    };

                    if (ctx.Request.Headers.TryGetValue("X-Test-Tid", out var tid) &&
                        !string.IsNullOrWhiteSpace(tid.ToString()))
                    {
                        claims.Add(new Claim("tid", tid.ToString()));
                    }

                    if (ctx.Request.Headers.TryGetValue("X-Test-DisplayName", out var dn) &&
                        !string.IsNullOrWhiteSpace(dn.ToString()))
                    {
                        claims.Add(new Claim(ClaimTypes.Name, dn.ToString()));
                        claims.Add(new Claim("name", dn.ToString()));
                    }

                    if (ctx.Request.Headers.TryGetValue("X-Test-Roles", out var rolesHeader))
                    {
                        foreach (var role in rolesHeader.ToString()
                                     .Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
                        }
                    }

                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestSynth"));
                }

                await n();
            });

            next(app);
        };
}
