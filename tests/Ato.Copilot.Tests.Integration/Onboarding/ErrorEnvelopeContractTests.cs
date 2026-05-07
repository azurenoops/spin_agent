using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// T136 — verifies every documented wizard error code produces a Constitution VII
/// `ProblemEnvelope` shape (`ok=false`, `errorCode`, `message`, `suggestion`).
/// Drives the actual <see cref="Envelope.Failure"/> helper through a real
/// pipeline so any drift in shape is caught.
/// </summary>
public class ErrorEnvelopeContractTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        _app = builder.Build();

        // Wire one synthetic endpoint that emits Envelope.Failure for the
        // ?code=…&message=…&suggestion=… query string. This exercises the
        // shared helper used by every wizard endpoint.
        _app.MapGet("/test/error", (HttpContext ctx) =>
        {
            var code = ctx.Request.Query["code"].ToString();
            var message = ctx.Request.Query["message"].ToString();
            string? suggestion = ctx.Request.Query.TryGetValue("suggestion", out var s) ? s.ToString() : null;
            return Envelope.Failure(code, message, suggestion);
        });

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    /// <summary>
    /// Reflects every <c>public const string</c> on
    /// <see cref="WizardErrorCodes"/> so the contract test grows automatically
    /// as new codes are added.
    /// </summary>
    public static IEnumerable<object[]> AllWizardErrorCodes()
    {
        var codes = typeof(WizardErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .Where(v => v.StartsWith("WIZARD_", StringComparison.Ordinal));
        foreach (var c in codes)
        {
            yield return new object[] { c };
        }
    }

    [Theory]
    [MemberData(nameof(AllWizardErrorCodes))]
    public async Task EveryErrorCode_ReturnsCanonicalEnvelopeShape(string errorCode)
    {
        var resp = await _client.GetAsync(
            $"/test/error?code={errorCode}&message=problem%20description&suggestion=try%20again");

        // Status: AuthForbidden ⇒ 403, every other code ⇒ 400 (per Envelope helper).
        var expected = errorCode == WizardErrorCodes.AuthForbidden ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest;
        resp.StatusCode.Should().Be(expected);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("ok", out var okEl).Should().BeTrue($"envelope must include `ok` ({errorCode})");
        okEl.GetBoolean().Should().BeFalse();

        root.TryGetProperty("errorCode", out var codeEl).Should().BeTrue($"envelope must include `errorCode` ({errorCode})");
        codeEl.GetString().Should().Be(errorCode);

        root.TryGetProperty("message", out var msgEl).Should().BeTrue($"envelope must include `message` ({errorCode})");
        msgEl.GetString().Should().NotBeNullOrEmpty();

        root.TryGetProperty("suggestion", out var sugEl).Should().BeTrue($"envelope must include `suggestion` ({errorCode})");
        sugEl.GetString().Should().NotBeNullOrEmpty();

        // Constitution VII: error envelopes never include a `data` payload.
        root.TryGetProperty("data", out _).Should().BeFalse($"error envelope must not include `data` ({errorCode})");
    }

    [Fact]
    public async Task Envelope_OmitsSuggestionFieldGracefully_WhenNotProvided()
    {
        var resp = await _client.GetAsync(
            $"/test/error?code={WizardErrorCodes.JobFailed}&message=oops");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        // suggestion is allowed to be null when omitted — the property is still serialized.
        doc.RootElement.TryGetProperty("suggestion", out var s).Should().BeTrue();
        (s.ValueKind == JsonValueKind.Null || string.IsNullOrEmpty(s.GetString()))
            .Should().BeTrue();
    }
}
