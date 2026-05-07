using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
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
using Ato.Copilot.Core.Models.Compliance;
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

        builder.Services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName));
        builder.Services.AddDbContextFactory<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), lifetime: ServiceLifetime.Singleton);
        builder.Services.AddSingleton<IFileStorageProvider>(storageProvider.Object);
        builder.Services.AddSingleton<IEvidenceArtifactService, EvidenceArtifactService>();
        builder.Services.AddSingleton(Mock.Of<IEvidenceStorageService>());
        builder.Services.AddLogging();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        // Map only the evidence-related endpoints
        var group = _app.MapGroup("/api/dashboard");
        group.MapDashboardEndpoints();

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
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ─── Upload Evidence ─────────────────────────────────────────────────

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
        var response = await _client.GetFromJsonAsync<JsonElement>(
            "/api/dashboard/evidence/settings", _json);

        response.GetProperty("storageProvider").GetString().Should().Be("Local");
        response.GetProperty("retentionDays").GetInt32().Should().BeGreaterThan(0);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> UploadTestEvidence(string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("test-file-content-123"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent("ScanResult"), "category");
        form.Add(new StringContent("ci-1"), "controlImplementationId");
        form.Add(new StringContent("test@integration.com"), "uploadedBy");

        return await _client.PostAsync(
            $"/api/dashboard/systems/{_systemId}/evidence", form);
    }
}
