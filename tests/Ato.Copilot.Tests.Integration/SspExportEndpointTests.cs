using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for SSP export and template management endpoints.
/// Tests the full HTTP pipeline: request → endpoint → service → database → response.
/// </summary>
public class SspExportEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
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

        var dbName = $"SspExportEndpoints_{Guid.NewGuid():N}";
        var settings = new ExportSettings
        {
            DataPath = Path.Combine(Path.GetTempPath(), $"ssptest_{Guid.NewGuid()}"),
            RetentionDays = 30,
            MaxExportSizeBytes = 52_428_800,
            MaxTemplateSizeBytes = 10_485_760,
        };

        builder.Services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName));
        builder.Services.AddSingleton(Options.Create(settings));
        builder.Services.AddSingleton(Channel.CreateBounded<SspExportJob>(100));
        builder.Services.AddScoped<ISspExportService, SspExportService>();
        builder.Services.AddSingleton(Mock.Of<ISspService>());
        builder.Services.AddSingleton(Mock.Of<IDocumentTemplateService>());
        builder.Services.AddSingleton(Mock.Of<IOscalSspExportService>());
        builder.Services.AddSingleton(Mock.Of<ISspExportNotifier>());
        builder.Services.AddLogging();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        MapExportEndpoints(_app);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ─── POST /systems/{systemId}/exports ───────────────────────────────

    [Fact]
    public async Task PostExport_Returns202WithExportId()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/systems/sys-001/exports?format=docx",
            new { },
            _json);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("exportId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task PostExport_BadFormat_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/systems/sys-001/exports?format=xlsx",
            new { },
            _json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── GET /systems/{systemId}/exports ────────────────────────────────

    [Fact]
    public async Task ListExports_ReturnsItems()
    {
        // Create some exports first
        await _client.PostAsJsonAsync("/api/dashboard/systems/sys-001/exports?format=docx", new { }, _json);
        await _client.PostAsJsonAsync("/api/dashboard/systems/sys-001/exports?format=pdf", new { }, _json);

        var response = await _client.GetFromJsonAsync<JsonElement>(
            "/api/dashboard/systems/sys-001/exports", _json);

        response.GetProperty("items").GetArrayLength().Should().BeGreaterOrEqualTo(2);
    }

    // ─── GET /systems/{systemId}/exports/{exportId} ─────────────────────

    [Fact]
    public async Task GetExport_ReturnsDetail()
    {
        var post = await _client.PostAsJsonAsync(
            "/api/dashboard/systems/sys-001/exports?format=docx", new { }, _json);
        var postBody = await post.Content.ReadFromJsonAsync<JsonElement>(_json);
        var exportId = postBody.GetProperty("exportId").GetString();

        var detail = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/sys-001/exports/{exportId}", _json);

        detail.GetProperty("exportId").GetString().Should().Be(exportId);
        detail.GetProperty("format").GetString().Should().Be("docx");
    }

    [Fact]
    public async Task GetExport_NotFound_Returns404()
    {
        var response = await _client.GetAsync(
            $"/api/dashboard/systems/sys-001/exports/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── GET /templates ─────────────────────────────────────────────────

    [Fact]
    public async Task ListTemplates_ReturnsEmptyInitially()
    {
        var response = await _client.GetFromJsonAsync<JsonElement>(
            "/api/dashboard/templates", _json);

        response.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    // ─── DELETE /templates/{templateId} ─────────────────────────────────

    [Fact]
    public async Task DeleteTemplate_NotFound_Returns404()
    {
        var response = await _client.DeleteAsync(
            $"/api/dashboard/templates/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── PUT /templates/{templateId} ────────────────────────────────────

    [Fact]
    public async Task UpdateTemplate_NotFound_Returns404()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/dashboard/templates/{Guid.NewGuid()}",
            new { name = "New Name" },
            _json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Endpoint wiring ────────────────────────────────────────────────

    private static void MapExportEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard");

        group.MapPost("/systems/{systemId}/exports", async (
                string systemId,
                string format,
                HttpContext httpContext,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var userId = httpContext.User?.Identity?.Name ?? "test-user";
                try
                {
                    var export = await exportService.EnqueueExportAsync(systemId, format, null, userId, ct);
                    return Results.Accepted($"/api/dashboard/systems/{systemId}/exports/{export.Id}", new
                    {
                        exportId = export.Id,
                        status = export.Status,
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

        group.MapGet("/systems/{systemId}/exports", async (
                string systemId,
                int? limit,
                int? offset,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var exports = await exportService.ListExportsAsync(
                    systemId, limit: limit ?? 25, offset: offset ?? 0, cancellationToken: ct);
                return Results.Ok(new { items = exports, totalCount = exports.Count });
            });

        group.MapGet("/systems/{systemId}/exports/{exportId:guid}", async (
                string systemId,
                Guid exportId,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var export = await exportService.GetExportAsync(exportId, ct);
                return export is not null ? Results.Ok(export) : Results.NotFound(new { error = "Export not found" });
            });

        group.MapGet("/templates", async (
                int? limit,
                int? offset,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var templates = await exportService.ListTemplatesAsync(
                    limit ?? 25, offset ?? 0, ct);
                return Results.Ok(new { items = templates, totalCount = templates.Count });
            });

        group.MapDelete("/templates/{templateId:guid}", async (
                Guid templateId,
                HttpContext httpContext,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var userId = httpContext.User?.Identity?.Name ?? "test-user";
                var deleted = await exportService.DeleteTemplateAsync(templateId, userId, ct);
                return deleted ? Results.NoContent() : Results.NotFound(new { error = "Template not found" });
            });

        group.MapPut("/templates/{templateId:guid}", async (
                Guid templateId,
                UpdateTemplateRequest body,
                HttpContext httpContext,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                try
                {
                    var userId = httpContext.User?.Identity?.Name ?? "test-user";
                    var result = await exportService.UpdateTemplateAsync(
                        templateId, body.Name, body.Description, userId, ct);
                    return result is not null ? Results.Ok(result) : Results.NotFound(new { error = "Template not found" });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });
    }
}
