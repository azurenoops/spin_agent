using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy.Csp;

/// <summary>
/// T189 [US9]: contract + behavior tests for the wizard-time and post-onboarding
/// ATO-document upload endpoints (FR-099, FR-100, FR-101, FR-102, FR-103).
/// </summary>
/// <remarks>
/// <para>
/// RED until T206 (parsers + endpoints) and T207 (post-onboarding /import) are
/// implemented. Until then every endpoint returns 404 and these tests fail at
/// the status-code assertion. Constitution §VI requires this RED-state to be
/// observed before the implementation lands.
/// </para>
/// <para>
/// Coverage:
/// <list type="bullet">
///   <item>PDF / DOCX / OSCAL JSON / XLSX / ZIP happy path (200 OK).</item>
///   <item><c>400 UNSUPPORTED_ATO_DOCUMENT</c> for an unknown content type.</item>
///   <item><c>400 PARSE_FAILED</c> for a corrupt PDF.</item>
///   <item><c>413 ATO_DOCUMENT_TOO_LARGE</c> for a 60 MB upload.</item>
///   <item><c>403 FORBIDDEN_NOT_CSP_ADMIN</c> for a non-CSP-Admin caller.</item>
///   <item><c>503 CSP_ONBOARDING_INCOMPLETE</c> on the post-onboarding /import
///         endpoint when <c>CspProfile.OnboardingState != Active</c>.</item>
/// </list>
/// </para>
/// </remarks>
[Collection("Tenancy")]
public class CspAtoUploadFlowTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private const string WizardUploadUrl = "/api/csp/onboarding/atos/upload";
    private const string PostOnboardingImportUrl = "/api/csp/inherited-components/import";

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspAtoUploadFlowTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        // The shared fixture pre-seeds an Active CspProfile so the gate
        // (FR-090) lifts for unrelated tests. The wizard upload endpoint
        // (FR-099) is the entrypoint to onboarding and therefore must be
        // exercised against a Pending / InWizard profile — otherwise
        // EnsureCreatedAsync correctly returns 409 because onboarding is
        // already complete. Reset before each method so the wizard path
        // is reachable.
        factory.ResetCspProfileAsync().GetAwaiter().GetResult();

        // Default: an active CSP-Admin in Tenant A. Per-test methods may
        // override IsCspAdmin and/or the CspProfile.OnboardingState.
        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;
    }

    // ──────────────────────────── Happy paths ────────────────────────────

    [Theory]
    [InlineData("ssp.pdf", "application/pdf")]
    [InlineData("ssp.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("ssp.json", "application/json")]
    [InlineData("ssp.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("ssp.zip", "application/zip")]
    public async Task Post_Upload_HappyPath_ReturnsOk_WithUploadResponseShape(
        string fileName,
        string contentType)
    {
        // Arrange — minimal non-empty payload. Parsing implementations are
        // expected to tolerate a small fixture file (or surface a clear parse
        // error per their own contract). The point of THIS test is the
        // success-envelope shape.
        using var content = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("placeholder fixture body — parser-specific");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "files", fileName);

        // Act
        var resp = await _client.PostAsync(WizardUploadUrl, content);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "wizard upload of a supported MIME type must succeed (FR-099)");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");
        data.GetProperty("documentsAccepted").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        data.GetProperty("componentsExtracted").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("capabilitiesMapped").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("capabilitiesNeedsReview").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        data.GetProperty("aiMappingAvailable").GetBoolean().Should().BeTrue();
        data.GetProperty("files").GetArrayLength().Should().Be(1);
    }

    // ──────────────────────────── Negative paths ─────────────────────────

    [Fact]
    public async Task Post_Upload_UnknownContentType_Returns400_UnsupportedAtoDocument()
    {
        // Arrange — text/plain is not in the allow-list (FR-100).
        using var content = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("just a plain text file");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "files", "notes.txt");

        // Act
        var resp = await _client.PostAsync(WizardUploadUrl, content);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("UNSUPPORTED_ATO_DOCUMENT");
    }

    [Fact]
    public async Task Post_Upload_CorruptPdf_Returns400_ParseFailed()
    {
        // Arrange — content-type is application/pdf but the body is not a
        // valid PDF. Parser must surface PARSE_FAILED rather than swallow
        // the corruption (FR-101).
        using var content = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("NOT-A-REAL-PDF-MAGIC");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "files", "broken.pdf");

        // Act
        var resp = await _client.PostAsync(WizardUploadUrl, content);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("PARSE_FAILED");
    }

    [Fact]
    public async Task Post_Upload_FileExceeds50Mb_Returns413_AtoDocumentTooLarge()
    {
        // Arrange — 60 MB body (exceeds the 50 MB per-file cap from the
        // OpenAPI contract). Use a sparse-ish array; the request size limit
        // must reject before any parser runs.
        const int bytes60Mb = 60 * 1024 * 1024;
        var oversized = new byte[bytes60Mb];
        // Just enough variation that compression-aware infrastructure cannot
        // shrink the request by orders of magnitude:
        for (var i = 0; i < oversized.Length; i += 4096)
        {
            oversized[i] = (byte)(i & 0xFF);
        }
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(oversized);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "files", "oversized.pdf");

        // Act
        var resp = await _client.PostAsync(WizardUploadUrl, content);

        // Assert
        resp.StatusCode.Should().Be((HttpStatusCode)StatusCodes.RequestEntityTooLarge);
        // For 413 the server MAY produce an empty body. Only assert the
        // ErrorEnvelope when one is present.
        if (resp.Content.Headers.ContentLength.GetValueOrDefault() > 0
            && resp.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("error").GetProperty("errorCode").GetString()
                .Should().Be("ATO_DOCUMENT_TOO_LARGE");
        }
    }

    [Fact]
    public async Task Post_Upload_NotCspAdmin_Returns403_ForbiddenNotCspAdmin()
    {
        // Arrange — flip caller off CSP-Admin. Wizard upload requires
        // CSP.Admin (FR-099, OpenAPI security spec).
        _factory.GetActiveContext().IsCspAdmin = false;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("body"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "files", "ssp.pdf");

        // Act
        var resp = await _client.PostAsync(WizardUploadUrl, content);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    // ─── Post-onboarding /import: 503 if CspProfile.OnboardingState != Active ─

    [Fact]
    public async Task Post_Import_OnboardingIncomplete_Returns503_CspOnboardingIncomplete()
    {
        // Arrange — flip the singleton CspProfile to OnboardingState = InWizard.
        // The post-onboarding /import endpoint is gated on Active (FR-104).
        await SetCspProfileOnboardingStateAsync(OnboardingState.InWizard);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("body"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "files", "ssp.pdf");

        try
        {
            // Act
            var resp = await _client.PostAsync(PostOnboardingImportUrl, content);

            // Assert
            resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("error").GetProperty("errorCode").GetString()
                .Should().Be("CSP_ONBOARDING_INCOMPLETE");
        }
        finally
        {
            // Restore so subsequent tests in the shared fixture see Active.
            await SetCspProfileOnboardingStateAsync(OnboardingState.Active);
        }
    }

    // ───────────────────────────── Helpers ───────────────────────────────

    private async Task SetCspProfileOnboardingStateAsync(OnboardingState target)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"CspProfiles\" SET \"OnboardingState\" = {(int)target};");
    }

    /// <summary>
    /// Local mirror of the sealed <c>StatusCodes</c> constants from
    /// <c>Microsoft.AspNetCore.Http</c>. The integration test project does
    /// not reference the framework directly, so we re-declare just what we
    /// need.
    /// </summary>
    private static class StatusCodes
    {
        public const int RequestEntityTooLarge = 413;
    }
}
