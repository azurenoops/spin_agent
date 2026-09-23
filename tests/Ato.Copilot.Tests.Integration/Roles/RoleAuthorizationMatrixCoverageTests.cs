using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Core.Services.Roles;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Roles;

/// <summary>
/// T018 [US1] — Failing integration test pinning HTTP-level enforcement of the
/// FR-027 role-tiered authorization matrix.
///
/// <para>
/// Iterates every disallowed (caller, target) cell. The caller is seeded with a
/// single tenant role; the request is a POST to <c>/api/onboarding/role-assignments</c>
/// asking to assign <paramref>target</paramref>. Asserts HTTP 403 with envelope
/// <c>{ ok:false, errorCode:"RBAC_ROLE_ASSIGN_DENIED", callerEffectiveRole, targetRole }</c>.
/// </para>
///
/// <para>Drives SC-009.</para>
/// </summary>
public class RoleAuthorizationMatrixCoverageTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _dbName = null!;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        _dbName = $"AuthzMatrix_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(_dbName));
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddScoped<IWizardAuditService>(_ => Mock.Of<IWizardAuditService>());
        builder.Services.AddScoped<IOrganizationRoleAssignmentService, OrganizationRoleAssignmentService>();
        builder.Services.AddSingleton(NullLogger<OrganizationRoleAssignmentService>.Instance);

        // Feature 049 services
        builder.Services.AddSingleton<IRoleAuthorizationService, RoleAuthorizationService>();
        builder.Services.AddScoped<ICallerEffectiveRoleResolver, CallerEffectiveRoleResolver>();
        builder.Services.AddScoped<ISoDConflictDetector, SoDConflictDetector>();
        builder.Services.AddScoped<IUnifiedRoleReader, UnifiedRoleReader>();

        // Onboarding state — minimum stub so the "Roles" step-complete try block does not throw.
        builder.Services.AddScoped<IOnboardingStateService>(_ => Mock.Of<IOnboardingStateService>());

        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                OnboardingAdministratorRequirement.PolicyName,
                p => p.RequireAssertion(_ => true));
        });

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapRoleAssignmentEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    public static IEnumerable<object[]> Disallowed_RmfRole_cells()
    {
        // Lower-tier callers (AO, Sca, SystemOwner, MissionOwner) may assign nothing.
        foreach (var caller in new[] { OrganizationRole.AuthorizingOfficial, OrganizationRole.Assessor, OrganizationRole.SystemOwner, OrganizationRole.MissionOwner })
        {
            foreach (var target in new[] { OrganizationRole.MissionOwner, OrganizationRole.SystemOwner, OrganizationRole.Issm, OrganizationRole.Isso, OrganizationRole.Assessor, OrganizationRole.AuthorizingOfficial })
            {
                yield return new object[] { caller, target };
            }
        }

        // Isso may assign only MissionOwner + SystemOwner.
        foreach (var target in new[] { OrganizationRole.Issm, OrganizationRole.Isso, OrganizationRole.Assessor, OrganizationRole.AuthorizingOfficial })
        {
            yield return new object[] { OrganizationRole.Isso, target };
        }

        // Issm may not assign AuthorizingOfficial.
        yield return new object[] { OrganizationRole.Issm, OrganizationRole.AuthorizingOfficial };
    }

    [Theory]
    [MemberData(nameof(Disallowed_RmfRole_cells))]
    public async Task Disallowed_cells_return_403_with_RBAC_envelope(
        OrganizationRole callerRole,
        OrganizationRole targetRole)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var callerPersonId = Guid.NewGuid();
        var targetPersonId = Guid.NewGuid();

        using var scope = _app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using (var db = factory.CreateDbContext())
        {
            db.Persons.Add(new Person { Id = callerPersonId, TenantId = tenantId, DisplayName = "Caller", Email = "caller@x.mil" });
            db.Persons.Add(new Person { Id = targetPersonId, TenantId = tenantId, DisplayName = "Assignee", Email = "assignee@x.mil" });
            // Caller's seed role (Org-scoped)
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
            {
                TenantId = tenantId,
                Role = callerRole,
                PersonId = callerPersonId,
                IsPrimary = true,
            });
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("X-Test-User");
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{tenantId}|{callerPersonId}");

        var body = new { Role = targetRole.ToString(), PersonId = targetPersonId };

        // Act
        var response = await _client.PostAsJsonAsync("/api/onboarding/role-assignments/", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "FR-027: disallowed cell ({0} → {1}) MUST yield HTTP 403", callerRole, targetRole);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        payload.GetProperty("ok").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString()
            .Should().Be("RBAC_ROLE_ASSIGN_DENIED",
                "every FR-027 denial MUST use the closed error code RBAC_ROLE_ASSIGN_DENIED");
        payload.GetProperty("callerEffectiveRole").GetString().Should()
            .Be(OrganizationRoleToRmfRoleMap.TryMap(callerRole)!.Value.ToString());
        payload.GetProperty("targetRole").GetString().Should()
            .Be(OrganizationRoleToRmfRoleMap.TryMap(targetRole)!.Value.ToString());
        await using var verificationDb = await factory.CreateDbContextAsync();
        var assignments = await verificationDb.OrganizationRoleAssignments.ToListAsync();
        assignments.Should().ContainSingle();
        assignments[0].PersonId.Should().Be(callerPersonId);
        assignments[0].Role.Should().Be(callerRole);
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var header))
                return Task.FromResult(AuthenticateResult.NoResult());
            var parts = header.ToString().Split('|', 2);
            var claims = new List<Claim>();
            if (!string.IsNullOrEmpty(parts[0])) claims.Add(new Claim("tid", parts[0]));
            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1])) claims.Add(new Claim("oid", parts[1]));
            claims.Add(new Claim(ClaimTypes.Name, "test-user"));
            var identity = new ClaimsIdentity(claims, AuthScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, AuthScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
