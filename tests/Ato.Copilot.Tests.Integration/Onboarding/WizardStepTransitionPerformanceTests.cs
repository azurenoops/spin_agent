using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// T141 — wizard step-transition spot-check. Confirms that the
/// <c>GET /api/onboarding/state</c> read path used for every step transition
/// completes in well under one second on the in-memory test harness.
/// </summary>
public class WizardStepTransitionPerformanceTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        var dbName = $"Tx_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddSingleton<IOnboardingStateService, StubStateService>();
        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization(o =>
            o.AddPolicy(OnboardingAdministratorRequirement.PolicyName, p => p.RequireAssertion(_ => true)));

        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapOnboardingStateEndpoints();

        await _app.StartAsync();

        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{TenantId}|{AdminId}");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task GetState_RespondsUnder1Second()
    {
        // Warm-up.
        var warm = await _client.GetAsync("/api/onboarding/state");
        warm.StatusCode.Should().Be(HttpStatusCode.OK);

        var sw = Stopwatch.StartNew();
        var resp = await _client.GetAsync("/api/onboarding/state");
        sw.Stop();
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(1),
            "Wizard step transitions read /api/onboarding/state and must feel instant (T141).");
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
            var claims = new List<Claim>
            {
                new("tid", parts[0]),
                new("oid", parts[1]),
                new(ClaimTypes.Name, "test"),
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, AuthScheme)),
                    AuthScheme)));
        }
    }

    /// <summary>
    /// Lightweight stub so the perf test measures only the request pipeline +
    /// JSON serialization cost, not EF setup. The real service is exercised by
    /// the unit + integration suites.
    /// </summary>
    private sealed class StubStateService : IOnboardingStateService
    {
        private readonly TenantOnboardingState _state = new()
        {
            TenantId = TenantId,
            Status = TenantOnboardingStatus.InProgress,
            LastStep = "OrganizationContext",
        };

        public Task<TenantOnboardingState> GetAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(_state);

        public Task<TenantOnboardingState> StartAsync(Guid tenantId, Guid actorUserId,
            string? actorDisplayName, string? actorEmail, Guid correlationId, CancellationToken ct = default)
            => Task.FromResult(_state);

        public Task MarkStepSkippedAsync(Guid tenantId, string stepName, long durationMs,
            Guid actorUserId, Guid correlationId, CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkStepCompletedAsync(Guid tenantId, string stepName, long durationMs,
            Guid actorUserId, Guid correlationId, CancellationToken ct = default) => Task.CompletedTask;

        public Task CompleteOnboardingAsync(Guid tenantId, Guid actorUserId,
            Guid correlationId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
