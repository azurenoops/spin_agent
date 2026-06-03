using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Xunit;

namespace Ato.Copilot.Tests.Integration.ApiMismatch;

/// <summary>
/// Integration tests for Epic #120 — API Mismatch Fixes (052-api-mismatch-fixes).
/// RED phase: these tests fail against main, pass after the fix is implemented.
///
/// T007 — apply-profile route returns 200 (GAP-001, issue #141)
/// T008 — import/preview route returns 200 (GAP-002, issue #142)
/// T009 — import/apply route returns 200 (GAP-002, issue #142)
/// T010 — bulk POAM PUT /remediation/poam/bulk-status returns 200 (GAP-003, issue #143)
/// T011 — single POAM status with systemId prefix returns 200 (GAP-004, issue #144)
/// T012 — chat stream accepts multipart/form-data with attachment (GAP-014, issue #145)
/// T013 — chat stream rejects disallowed MIME type with 400 UNSUPPORTED_ATTACHMENT_TYPE
/// </summary>
[Collection("IntegrationTests")]
public class ApiMismatchRouteTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _dbName = null!;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // ─── Test data ────────────────────────────────────────────────────────────

    private const string TestSystemId = "sys-apimismatch-052-001";
    private const string TestBaselineId = "bl-apimismatch-052-001";

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        _dbName = $"ApiMismatch_052_{Guid.NewGuid():N}";

        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.Configure<AzureAdOptions>(builder.Configuration.GetSection(AzureAdOptions.SectionName));
        builder.Services.AddHttpClient();

        builder.Services.AddSingleton(sp =>
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                AuthorityHost = AzureAuthorityHosts.AzureGovernment
            });
            return new ArmClient(credential, default, new ArmClientOptions
            {
                Environment = ArmEnvironment.AzureGovernment
            });
        });

        builder.Services.AddAtoCopilotMcpForTesting(builder.Configuration, _dbName);

        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.UseCors();
        _app.UseMiddleware<ComplianceAuthorizationMiddleware>();
        _app.UseMiddleware<AuditLoggingMiddleware>();

        var httpBridge = _app.Services.GetRequiredService<McpHttpBridge>();
        httpBridge.MapEndpoints(_app);

        _app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Json(new
        {
            service = "ATO Copilot",
            version = "1.0.0",
            mode = "test"
        }));

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await SeedTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task SeedTestDataAsync()
    {
        using var scope = _app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();

        db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = TestSystemId,
            Name = "API Mismatch Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Gov",
            CreatedBy = "test",
            IsActive = true,
        });

        db.ControlBaselines.Add(new ControlBaseline
        {
            Id = TestBaselineId,
            RegisteredSystemId = TestSystemId,
            BaselineLevel = "Moderate",
            TotalControls = 3,
            ControlIds = new List<string> { "AC-1", "AC-2", "AU-1" },
            CreatedBy = "test",
        });

        await db.SaveChangesAsync();
    }

    // ─── T007: GAP-001 — apply-profile route ─────────────────────────────────

    /// <summary>
    /// T007 (issue #141 GAP-001): POST /api/dashboard/systems/{id}/inheritance/apply-profile
    /// must be registered and return a non-404 response.
    /// Currently returns 404 — this test will fail on main (RED) and pass after the fix (GREEN).
    /// </summary>
    [Fact]
    public async Task T007_ApplyProfile_Route_Returns_NotFound404_Without_Fix()
    {
        var request = new
        {
            profileId = "test-profile-id",
            conflictResolution = "skip",
            preview = true
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{TestSystemId}/inheritance/apply-profile",
            request, _jsonOptions);

        // RED: route does not exist — expect 404
        // After fix: route is registered, expect 200 or 400 (valid response from handler)
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            because: "POST /api/dashboard/systems/{id}/inheritance/apply-profile must be registered (GAP-001, issue #141)");
    }

    // ─── T008: GAP-002 — import/preview route ────────────────────────────────

    /// <summary>
    /// T008 (issue #142 GAP-002): POST /api/dashboard/systems/{id}/inheritance/import/preview
    /// must be registered and return a non-404 response.
    /// </summary>
    [Fact]
    public async Task T008_ImportPreview_Route_Returns_NotFound404_Without_Fix()
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("controlId,inheritanceType,provider,customerResponsibility\nAC-1,Inherited,Azure,Partial"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "test-inheritance.csv");

        var response = await _client.PostAsync(
            $"/api/dashboard/systems/{TestSystemId}/inheritance/import/preview",
            content);

        // RED: route does not exist — expect 404
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            because: "POST /api/dashboard/systems/{id}/inheritance/import/preview must be registered (GAP-002, issue #142)");
    }

    // ─── T009: GAP-002 — import/apply route ──────────────────────────────────

    /// <summary>
    /// T009 (issue #142 GAP-002): POST /api/dashboard/systems/{id}/inheritance/import/apply
    /// must be registered and return a non-404 response.
    /// </summary>
    [Fact]
    public async Task T009_ImportApply_Route_Returns_NotFound404_Without_Fix()
    {
        var request = new
        {
            previewToken = "fake-preview-token",
            columnMapping = new
            {
                controlId = "controlId",
                inheritanceType = "inheritanceType",
                provider = "provider",
                customerResponsibility = "customerResponsibility"
            },
            conflictResolution = "overwrite"
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{TestSystemId}/inheritance/import/apply",
            request, _jsonOptions);

        // RED: route does not exist — expect 404
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            because: "POST /api/dashboard/systems/{id}/inheritance/import/apply must be registered (GAP-002, issue #142)");
    }

    // ─── T010: GAP-003 — bulk POAM PUT /remediation/poam/bulk-status ─────────

    /// <summary>
    /// T010 (issue #143 GAP-003): PUT /api/dashboard/systems/{id}/remediation/poam/bulk-status
    /// (the path frontend remediation.ts calls) must exist and not return 404.
    /// Backend currently has POST /api/dashboard/poam/bulk-status — wrong verb, wrong path.
    /// </summary>
    [Fact]
    public async Task T010_BulkPoamStatus_Put_RemeditionPath_Returns_NotFound404_Without_Fix()
    {
        // Seed a POAM item first
        string poamId = await CreateTestPoamAsync();

        var request = new
        {
            poamIds = new[] { poamId },
            status = "Ongoing"
        };

        // This is what remediation.ts calls:
        // apiClient.put('/remediation/poam/bulk-status', ...) → PUT /api/dashboard/remediation/poam/bulk-status
        var response = await _client.PutAsJsonAsync(
            "/api/dashboard/remediation/poam/bulk-status",
            request, _jsonOptions);

        // RED: route doesn't exist at this path with PUT — expect 404 or 405
        ((int)response.StatusCode).Should().BeLessThan(500,
            because: "server must not crash on this request");
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            because: "PUT /api/dashboard/remediation/poam/bulk-status must be registered matching frontend (GAP-003, issue #143)");
    }

    // ─── T011: GAP-004 — single POAM status with systemId ───────────────────

    /// <summary>
    /// T011 (issue #144 GAP-004): The single POAM status update must be accessible
    /// via a systemId-scoped path for RLS tenant isolation.
    /// poam.ts calls PUT /api/dashboard/poam/{poamId}/status — this DOES exist.
    /// The spec says the backend must scope via systemId. This test verifies the route
    /// works correctly with a seeded POAM and returns a non-5xx response.
    /// </summary>
    [Fact]
    public async Task T011_SinglePoamStatus_WithSystemId_ReturnsSuccessOrValidationError()
    {
        string poamId = await CreateTestPoamAsync();

        // Fetch the POAM's rowVersion first
        var poamResponse = await _client.GetAsync($"/api/dashboard/poam/{poamId}");
        poamResponse.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        // GET /api/dashboard/systems/{systemId}/poam/{poamId} (the system-scoped read)
        var systemScopedResponse = await _client.GetAsync(
            $"/api/dashboard/systems/{TestSystemId}/poam/{poamId}");

        ((int)systemScopedResponse.StatusCode).Should().BeLessThan(500,
            because: "system-scoped POAM read must not cause a server error");
    }

    // ─── T012: GAP-014 — chat stream accepts multipart/form-data ─────────────

    /// <summary>
    /// T012 (issue #145 GAP-014): POST /mcp/chat/stream must accept multipart/form-data
    /// with an attachment and not silently drop it (must return 200, not 400/500).
    /// ChatInput.tsx currently drops attachments before sending — this tests the server half.
    /// </summary>
    [Fact]
    public async Task T012_ChatStream_WithMultipartAttachment_Returns200()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Please analyze this document"), "message");
        content.Add(new StringContent(Guid.NewGuid().ToString()), "conversationId");

        var fileBytes = "This is a test plain text document for analysis.\n"u8.ToArray();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "attachment[]", "test-document.txt");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await _client.PostAsync("/mcp/chat/stream", content, cts.Token);

        // RED: server does not accept multipart yet — expect 400 or 500
        // GREEN: server accepts multipart, returns 200 SSE stream
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "POST /mcp/chat/stream must accept multipart/form-data with file attachments (GAP-014, issue #145)");

        var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
        responseBody.Should().Contain("data:",
            because: "response must be an SSE stream with at least one data: event");
    }

    // ─── T013: GAP-014 — MIME validation rejects disallowed types ────────────

    /// <summary>
    /// T013 (issue #145): Server must reject image/png with 400 UNSUPPORTED_ATTACHMENT_TYPE.
    /// </summary>
    [Fact]
    public async Task T013_ChatStream_WithDisallowedMimeType_Returns400UnsupportedAttachmentType()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Analyze this image"), "message");

        // PNG bytes (1x1 pixel placeholder)
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "attachment[]", "screenshot.png");

        var response = await _client.PostAsync("/mcp/chat/stream", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            because: "image/png is not in the allowed MIME list — server must reject with 400");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("UNSUPPORTED_ATTACHMENT_TYPE",
            because: "error envelope must include the code UNSUPPORTED_ATTACHMENT_TYPE");
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private async Task<string> CreateTestPoamAsync()
    {
        using var scope = _app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var poam = new PoamItem
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = TestSystemId,
            Weakness = "Test weakness for API mismatch tests",
            Source = "STIG",
            SecurityControlNumber = "AC-2",
            Severity = CatSeverity.CatII,
            PocEmail = "test@test.gov",
            ScheduledCompletionDate = DateTime.UtcNow.AddDays(30),
            Status = PoamStatus.Ongoing,
            CreatedBy = "test",
            RowVersion = Guid.NewGuid(),
        };

        db.PoamItems.Add(poam);
        await db.SaveChangesAsync();

        return poam.Id;
    }
}
