using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Feature 051 T142 [Phase 13.1] — integration coverage for
/// <c>LoginThrottleMiddleware</c>. Drives 21+ requests through the real
/// HTTP pipeline and asserts the 21st failed sign-in attempt from the
/// same source IP returns <c>429 TOO_MANY_LOGINS</c> with a
/// <c>Retry-After</c> header AND writes a <c>LoginFailure</c> audit row
/// stamped <c>MetadataJson.throttled = true</c>.
/// </summary>
/// <remarks>
/// <para>
/// Test factory overrides the Production identity threshold to 100 so
/// the per-IP cap of 20 trips first (the anonymous-identity default of
/// 10 would otherwise trip at attempt 11). All requests share one IP
/// via the <c>X-Forwarded-For</c> header so the per-IP counter
/// accumulates.
/// </para>
/// <para>
/// Per analysis C11, any non-Development environment selects the
/// Production bucket — <c>Testing</c> (pinned by the base factory) and
/// <c>Staging</c> both hit the same code path. The Staging assertion is
/// covered as a unit test in <c>LoginThrottleServiceTests</c> to avoid
/// process-global <c>ASPNETCORE_ENVIRONMENT</c> contention.
/// </para>
/// <para>
/// Per analysis C17 the counter MUST increment ONLY on <c>401</c> and
/// <c>403 NO_TENANT_ASSIGNMENT</c> responses — never on a <c>2xx</c>,
/// <c>4xx-validation</c>, or <c>5xx</c>. The "success / 400 do not
/// increment" assertions exercise that contract end-to-end.
/// </para>
/// </remarks>
[Collection("Tenancy")]
public class LoginThrottleMiddlewareTests
    : IClassFixture<LoginThrottleMiddlewareTests.ThrottleFactory>
{
    private readonly ThrottleFactory _factory;

    public LoginThrottleMiddlewareTests(ThrottleFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TwentyFirstFailedAttempt_FromSameIp_Returns429_WithRetryAfter_AndAuditRow()
    {
        // Arrange — distinct IP per test method so the bucket does not
        // collide with sibling cases. The IP is sent via X-Forwarded-For
        // which the production LoginAuditContextAccessor honors.
        var ip = "203.0.113.142";
        var client = _factory.CreateClient();

        // Act — send 20 unauthenticated GETs (each returns 401 because
        // /api/auth/me requires an authenticated principal). After these
        // 20 the per-IP counter has hit the cap; the 21st must be 429.
        for (int i = 0; i < 20; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            req.Headers.Add("X-Forwarded-For", ip);
            var r = await client.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"attempt {i + 1} of 20 must reach the endpoint and fail auth");
        }

        var twentyFirstReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        twentyFirstReq.Headers.Add("X-Forwarded-For", ip);
        var twentyFirst = await client.SendAsync(twentyFirstReq);

        // Assert — status, header, envelope.
        twentyFirst.StatusCode.Should().Be((HttpStatusCode)429,
            "the 21st failed-login attempt from the same IP must short-circuit");
        twentyFirst.Headers.RetryAfter.Should().NotBeNull(
            "FR-035 mandates a Retry-After header on the 429");

        var body = await twentyFirst.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("TOO_MANY_LOGINS");

        // Audit row stamped with MetadataJson.throttled = true.
        using var scope = _factory.Services.CreateScope();
        var dbFactory = Microsoft.Extensions.DependencyInjection
            .ServiceProviderServiceExtensions
            .GetRequiredService<IDbContextFactory<AtoCopilotContext>>(scope.ServiceProvider);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.EventType == LoginAuditEventType.LoginFailure
                        && e.SourceIp == ip
                        && e.MetadataJson != null
                        && e.MetadataJson.Contains("\"throttled\":true"))
            .ToListAsync();
        rows.Should().NotBeEmpty(
            "the throttle middleware must write a LoginFailure audit row " +
            "with MetadataJson.throttled=true when it short-circuits with 429");
        rows.Last().MetadataJson.Should().Contain("\"retryAfterSeconds\":");
    }

    [Fact]
    public async Task SuccessfulLoginConfigResponse_DoesNotIncrementCounter()
    {
        // Per analysis C17 a 200 response MUST NOT increment the throttle
        // counter. The public /api/auth/login-config endpoint always
        // returns 200; hitting it 50 times must NEVER produce a 429.
        var client = _factory.CreateClient();
        var ip = "203.0.113.231";

        for (int i = 0; i < 25; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/login-config");
            req.Headers.Add("X-Forwarded-For", ip);
            var resp = await client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.OK,
                $"login-config is anonymous and must remain 200 (iteration {i + 1})");
        }
    }

    [Fact]
    public async Task ValidationFailedResponse_DoesNotIncrementCounter()
    {
        // /api/auth/select-tenant returns 400 VALIDATION_FAILED when the
        // body is missing/malformed but the caller IS authenticated.
        // Per analysis C17 the 400 MUST NOT increment the throttle
        // counter — only 401 and 403 NO_TENANT_ASSIGNMENT do.
        var client = _factory.CreateClient();
        var ip = "203.0.113.55";

        for (int i = 0; i < 25; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/select-tenant");
            req.Headers.Add("X-Forwarded-For", ip);
            req.Headers.Add("X-Test-Oid", "validation-test-oid");
            req.Headers.Add("X-Test-Tid", Guid.NewGuid().ToString());
            // No body → 400 VALIDATION_FAILED.
            var resp = await client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                $"validation failures MUST stay 400 across the throttle window " +
                $"(iteration {i + 1})");
        }
    }

    /// <summary>
    /// Throttle-tuned factory: pins the Production identity threshold
    /// high (100) so only the per-IP cap (20) trips. Also installs the
    /// X-Test-* claims injector from <see cref="LoginAuthTestFactory"/>.
    /// </summary>
    public sealed class ThrottleFactory : MultiTenantWebApplicationFactory<McpProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration(cfg =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Production block — analysis C11 routes any
                    // non-Development env (including Testing) here.
                    // PerIdentityPerMinute=100 keeps the anonymous-identity
                    // counter from tripping at 11 so the test isolates
                    // the per-IP (20) cap.
                    ["Auth:Throttle:Production:PerIpPerMinute"] = "20",
                    ["Auth:Throttle:Production:PerIdentityPerMinute"] = "100",
                });
            });

            builder.ConfigureServices(services =>
            {
                services.AddTransient<
                    Microsoft.AspNetCore.Hosting.IStartupFilter,
                    TestClaimsInjectorStartupFilter>();
            });
        }
    }
}
