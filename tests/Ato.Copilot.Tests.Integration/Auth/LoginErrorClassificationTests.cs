using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Feature 051 T078 [US4] — end-to-end wiring coverage for
/// <see cref="LoginErrorClass"/> classification on the live request
/// pipeline. The full 10-class taxonomy is unit-tested in
/// <c>LoginErrorClassifierTests</c> (Unit project); this file verifies
/// the ONE class the dashboard server-side flow can deterministically
/// induce today: <see cref="LoginErrorClass.NoTenantAssignment"/> via
/// the <c>/api/auth/me</c> endpoint when the bearer's <c>tid</c> does
/// not map to a known tenant.
/// </summary>
/// <remarks>
/// <para>
/// Other classes (<c>CertExpired</c>, <c>CertRevoked</c>,
/// <c>ConditionalAccessBlock</c>, etc.) cannot be induced through
/// <see cref="LoginAuthTestFactory"/> without standing up real
/// certificate validation, OCSP responders, or Entra MFA — they're
/// wired-only and deferred to live verification (quickstart.md § 13 /
/// Phase 13).
/// </para>
/// <para>
/// FR-033 privacy is asserted directly: the audit row's
/// <c>MetadataJson</c> MUST NOT contain cert thumbprints, serials,
/// subjects, or issuer DNs.
/// </para>
/// </remarks>
public sealed class LoginErrorClassificationTests : IClassFixture<LoginAuthTestFactory>
{
    private readonly LoginAuthTestFactory _factory;

    public LoginErrorClassificationTests(LoginAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Me_UnknownTid_WritesLoginFailure_With_NoTenantAssignment_AndPrivacySafeMetadata()
    {
        // Arrange — authenticated principal but a `tid` not in Tenants.
        var client = _factory.CreateClient();
        var oid = $"oid-errclass-{Guid.NewGuid():N}";
        var unknownTid = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Test-Oid", oid);
        client.DefaultRequestHeaders.Add("X-Test-Tid", unknownTid.ToString());
        client.DefaultRequestHeaders.Add("X-Test-DisplayName", "Phase 6 US4");

        // Act
        var resp = await client.GetAsync("/api/auth/me");

        // Assert — envelope shape per http-api.md § 2.6.
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("NO_TENANT_ASSIGNMENT");

        // Assert — exactly one audit row with ErrorClass set.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var rows = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.Oid == oid)
            .ToListAsync();

        rows.Should().HaveCount(1, "FR-014/FR-015 mandate exactly one failure row per attempt");
        var row = rows[0];
        row.EventType.Should().Be(LoginAuditEventType.LoginFailure);
        row.ErrorClass.Should().Be(LoginErrorClass.NoTenantAssignment);
        row.EffectiveTenantId.Should().Be(Guid.Empty,
            "FR-015 stamps SYSTEM_TENANT_ID for tenant-less failures");

        // Assert — FR-033 privacy: metadata, if present, contains NONE
        // of the forbidden cert/PII identifiers.
        if (!string.IsNullOrEmpty(row.MetadataJson))
        {
            row.MetadataJson.Should().NotContain("thumbprint",
                "FR-033: audit metadata MUST NOT contain cert thumbprints");
            row.MetadataJson.Should().NotContain("serial",
                "FR-033: audit metadata MUST NOT contain cert serial numbers");
            row.MetadataJson.Should().NotContain("subject",
                "FR-033: audit metadata MUST NOT contain cert subject DNs");
            row.MetadataJson.Should().NotContain("issuer",
                "FR-033: audit metadata MUST NOT contain cert issuer DNs");
        }
    }
}
