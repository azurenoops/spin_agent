using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration tests for <see cref="RoleAssignmentEndpoints"/>
/// (T060 / FR-002 last-Administrator invariant).
/// </summary>
public class RoleAssignmentEndpointsTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid AdminTenantId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _dbName = null!;
    private readonly Mock<IWizardAuditService> _auditMock = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        _dbName = $"RoleAssignmentEndpoints_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(_dbName));
        builder.Services.AddScoped<IWizardAuditService>(_ => _auditMock.Object);
        builder.Services.AddScoped<IOrganizationRoleAssignmentService, OrganizationRoleAssignmentService>();
        builder.Services.AddSingleton<ILogger<OrganizationRoleAssignmentService>>(
            NullLogger<OrganizationRoleAssignmentService>.Instance);

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
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{AdminTenantId}|{AdminUserId}");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    private async Task<Guid> SeedPersonAsync(string name = "Sample")
    {
        using var scope = _app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = factory.CreateDbContext();
        var person = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = AdminTenantId,
            DisplayName = name,
            Email = $"{name.ToLowerInvariant()}@example.mil",
        };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        return person.Id;
    }

    [Fact]
    public async Task Post_AddIsso_ReturnsOkWithIsPrimaryTrue()
    {
        var personId = await SeedPersonAsync("Alice");

        var response = await _client.PostAsJsonAsync("/api/onboarding/role-assignments", new
        {
            role = "Isso",
            personId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("isPrimary").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Get_ListsActiveAssignments()
    {
        var p1 = await SeedPersonAsync("Alice");
        var p2 = await SeedPersonAsync("Bob");
        await _client.PostAsJsonAsync("/api/onboarding/role-assignments",
            new { role = "Isso", personId = p1 });
        await _client.PostAsJsonAsync("/api/onboarding/role-assignments",
            new { role = "Assessor", personId = p2 });

        var response = await _client.GetAsync("/api/onboarding/role-assignments");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Delete_LastAdmin_Returns409WithLastAdminProtected()
    {
        var personId = await SeedPersonAsync("OnlyAdmin");
        var add = await _client.PostAsJsonAsync("/api/onboarding/role-assignments", new
        {
            role = "Administrator",
            personId,
        });
        var addBody = await add.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var assignmentId = addBody.GetProperty("data").GetProperty("id").GetGuid();

        var delete = await _client.DeleteAsync($"/api/onboarding/role-assignments/{assignmentId}");

        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await delete.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeFalse();
        body.GetProperty("errorCode").GetString().Should().Be("WIZARD_LAST_ADMIN_PROTECTED");
    }

    [Fact]
    public async Task Delete_AdminWithReplacement_Succeeds()
    {
        var p1 = await SeedPersonAsync("Primary");
        var p2 = await SeedPersonAsync("Backup");

        var add1 = await _client.PostAsJsonAsync("/api/onboarding/role-assignments",
            new { role = "Administrator", personId = p1 });
        var add1Body = await add1.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var firstId = add1Body.GetProperty("data").GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync("/api/onboarding/role-assignments",
            new { role = "Administrator", personId = p2 });

        var delete = await _client.DeleteAsync($"/api/onboarding/role-assignments/{firstId}");
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
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
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
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
