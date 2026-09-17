using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Authentication;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;

namespace Ato.Copilot.Tests.Integration.Evidence;

/// <summary>
/// Integration tests for evidence API endpoints.
/// Tests the full HTTP pipeline using TestServer with in-memory DB.
/// </summary>
public class EvidenceEndpointsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly string _systemId = "sys-integration-test";
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var dbName = $"EvidenceEndpoints_{Guid.NewGuid():N}";

        var storageProvider = new Mock<IFileStorageProvider>();
        storageProvider
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storageProvider
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("file-content")));
        storageProvider
            .Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storageProvider
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // IEvidenceArtifactService is Singleton and consumes IDbContextFactory.
        // DbContextOptions must be Singleton too — the default AddDbContext
        // options lifetime is Scoped, which the Singleton factory cannot consume.
        builder.Services.AddDbContext<AtoCopilotContext>(
            opts => opts.UseInMemoryDatabase(dbName),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        builder.Services.AddDbContextFactory<AtoCopilotContext>(
            opts => opts.UseInMemoryDatabase(dbName),
            lifetime: ServiceLifetime.Singleton);
        builder.Services.AddSingleton<IFileStorageProvider>(storageProvider.Object);
        builder.Services.AddSingleton<IEvidenceArtifactService, EvidenceArtifactService>();
        builder.Services.AddSingleton(Mock.Of<IEvidenceStorageService>());
        builder.Services.AddSingleton(Mock.Of<IEvidenceCorrelationEngine>());
        builder.Services.AddSingleton(Mock.Of<IEvidenceFreshnessService>());
        builder.Services.AddSingleton(Mock.Of<IEvidenceAuditService>());
        builder.Services.AddSingleton<ITenantContext>(_ =>
            new TenantContext(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICurrentUserService, CurrentUserService>();
        builder.Services.AddLogging();
        builder.Services
            .AddAuthentication(CacPassthroughAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CacPassthroughAuthHandler>(
                CacPassthroughAuthHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Test-Authenticated"))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "evidence-test-user")],
                    CacPassthroughAuthHandler.SchemeName));
            }

            await next(context);
        });
        _app.UseAuthentication();
        _app.UseAuthorization();

        // MapDashboardEndpoints already prefixes /api/dashboard — do not nest
        // another /api/dashboard group or every route 404s / mismatches.
        _app.MapDashboardEvidenceEndpoints();

        // Seed test data
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = _systemId,
            Name = "Integration Test System",
            SystemType = SystemType.MajorApplication,
        });
        db.ControlImplementations.Add(new ControlImplementation
        {
            Id = "ci-1",
            RegisteredSystemId = _systemId,
            ControlId = "AC-1",
            ImplementationStatus = ImplementationStatus.Implemented,
        });
        await db.SaveChangesAsync();

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ─── Upload Evidence ─────────────────────────────────────────────────

    [Fact]
    public async Task EvidenceRoute_RejectsAnonymousRequest()
    {
        // Arrange
        using var anonymousClient = _app.GetTestClient();

        // Act
        var response = await anonymousClient.GetAsync(
            $"/api/dashboard/systems/{_systemId}/evidence");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_ValidFile_Returns200WithArtifact()
    {
        var response = await UploadTestEvidence("report.pdf", "application/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("fileName").GetString().Should().Be("report.pdf");
        body.GetProperty("artifactCategory").GetString().Should().Be("ScanResult");
    }

    [Fact]
    public async Task Upload_DisallowedExtension_Returns400()
    {
        var response = await UploadTestEvidence("malware.exe", "application/octet-stream");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_WithNarrativeType_ReturnsSelectedClassification()
    {
        // Arrange
        using var form = CreateUploadForm("policy.pdf", "application/pdf");
        form.Add(new StringContent("Policy"), "narrativeType");

        // Act
        var response = await _client.PostAsync(
            $"/api/dashboard/systems/{_systemId}/evidence", form);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("narrativeType").GetString().Should().Be("Policy");
    }

    [Fact]
    public async Task Upload_WithUndefinedNumericNarrativeType_Returns400()
    {
        // Arrange
        using var form = CreateUploadForm("policy.pdf", "application/pdf");
        form.Add(new StringContent("999"), "narrativeType");

        // Act
        var response = await _client.PostAsync(
            $"/api/dashboard/systems/{_systemId}/evidence", form);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("errorCode").GetString().Should().Be("INVALID_NARRATIVE_TYPE");
    }

    // ─── List Evidence ───────────────────────────────────────────────────

    [Fact]
    public async Task ListEvidence_ReturnsPagedResults()
    {
        await UploadTestEvidence("a.pdf", "application/pdf");
        await UploadTestEvidence("b.pdf", "application/pdf");

        var response = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence", _json);

        response.GetProperty("items").GetArrayLength().Should().BeGreaterOrEqualTo(2);
        response.GetProperty("totalCount").GetInt32().Should().BeGreaterOrEqualTo(2);
    }

    // ─── Get Evidence Summary ────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_ReturnsSummaryWithCounts()
    {
        await UploadTestEvidence("summary-test.pdf", "application/pdf");

        var response = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence/summary", _json);

        response.GetProperty("totalCount").GetInt32().Should().BeGreaterOrEqualTo(1);
        response.GetProperty("manualCount").GetInt32().Should().BeGreaterOrEqualTo(1);
    }

    // ─── Download Evidence ───────────────────────────────────────────────

    [Fact]
    public async Task Download_ExistingEvidence_ReturnsFile()
    {
        var uploadResponse = await UploadTestEvidence("download-test.pdf", "application/pdf");
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>(_json);
        var evidenceId = uploaded.GetProperty("id").GetString();

        var response = await _client.GetAsync(
            $"/api/dashboard/systems/{_systemId}/evidence/{evidenceId}/download");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("download-test.pdf");
    }

    // ─── Get Evidence Detail ─────────────────────────────────────────────

    [Fact]
    public async Task GetEvidence_ExistingId_ReturnsDetail()
    {
        var uploadResponse = await UploadTestEvidence("detail-test.pdf", "application/pdf");
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>(_json);
        var evidenceId = uploaded.GetProperty("id").GetString();

        var response = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence/{evidenceId}", _json);

        response.GetProperty("fileName").GetString().Should().Be("detail-test.pdf");
    }

    [Fact]
    public async Task GetEvidence_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync(
            $"/api/dashboard/systems/{_systemId}/evidence/nonexistent-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Delete Evidence ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEvidence_ExistingId_Returns204()
    {
        var uploadResponse = await UploadTestEvidence("delete-test.pdf", "application/pdf");
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>(_json);
        var evidenceId = uploaded.GetProperty("id").GetString();

        var response = await _client.DeleteAsync(
            $"/api/dashboard/systems/{_systemId}/evidence/{evidenceId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ─── Evidence Settings ───────────────────────────────────────────────

    [Fact]
    public async Task GetSettings_ReturnsDefaultConfig()
    {
        var httpResponse = await _client.GetAsync("/api/dashboard/evidence/settings");
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<JsonElement>(_json);

        response.GetProperty("storageProvider").GetString().Should().Be("Local");
        response.GetProperty("retentionDays").GetInt32().Should().BeGreaterThan(0);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> UploadTestEvidence(string fileName, string contentType)
    {
        using var form = CreateUploadForm(fileName, contentType);

        return await _client.PostAsync(
            $"/api/dashboard/systems/{_systemId}/evidence", form);
    }

    private static MultipartFormDataContent CreateUploadForm(string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("test-file-content-123"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent("ScanResult"), "category");
        form.Add(new StringContent("ci-1"), "controlImplementationId");
        form.Add(new StringContent("test@integration.com"), "uploadedBy");

        return form;
    }
}
