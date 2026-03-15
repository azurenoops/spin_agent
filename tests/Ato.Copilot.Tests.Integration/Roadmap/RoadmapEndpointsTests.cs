using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text.Json;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Roadmap;
using Ato.Copilot.Core.Models.Roadmap;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Ato.Copilot.Tests.Integration.Roadmap;

/// <summary>
/// Integration tests for roadmap dashboard endpoints.
/// Tests GET roadmap, progress, and export endpoints using TestServer.
/// </summary>
public class RoadmapEndpointsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private AtoCopilotContext _context = null!;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        var dbName = $"RoadmapEndpoints_{Guid.NewGuid():N}";

        builder.Services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));

        // Register services needed by dashboard endpoints
        builder.Services.AddScoped<NarrativeTemplateService>();
        builder.Services.AddScoped<CapabilityService>();
        builder.Services.AddScoped<DashboardService>();
        builder.Services.AddScoped<IRoadmapService>(sp =>
            new RoadmapService(
                sp.GetRequiredService<AtoCopilotContext>(),
                Mock.Of<IKanbanService>(),
                sp.GetRequiredService<CapabilityService>(),
                sp.GetRequiredService<ILogger<RoadmapService>>()));
        builder.Services.AddLogging();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        // Register only the roadmap-specific endpoints to avoid pulling in
        // services for portfolio/components/trends/etc.
        var group = _app.MapGroup("/api/dashboard");

        group.MapGet("/systems/{systemId}/roadmap", async (
                string systemId,
                bool? includeItems,
                IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                var roadmap = await roadmapService.GetRoadmapAsync(
                    systemId, includeItems ?? true, ct);

                if (roadmap is null)
                    return Results.NotFound(new { error = "No active roadmap found" });

                var allItems = roadmap.Phases.SelectMany(p => p.Items).ToList();
                var completedItems = allItems.Count(i => i.Status == ItemStatus.Complete);
                var overallCompletion = allItems.Count > 0 ? (double)completedItems / allItems.Count * 100 : 0;

                return Results.Ok(new
                {
                    roadmapId = roadmap.Id,
                    systemId = roadmap.SystemId,
                    systemName = roadmap.Name,
                    status = roadmap.Status.ToString(),
                    totalGaps = roadmap.TotalGaps,
                    overallCompletionPercent = overallCompletion,
                    phases = roadmap.Phases.OrderBy(p => p.DisplayOrder).Select(p => new
                    {
                        phaseId = p.Id,
                        name = p.Name,
                        items = p.Items.Select(i => new { controlId = i.ControlId }),
                    }),
                });
            });

        group.MapGet("/systems/{systemId}/roadmap/progress", async (
                string systemId,
                IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                var progress = await roadmapService.GetRoadmapProgressAsync(systemId, ct);
                return progress is null
                    ? Results.NotFound(new { error = "No active roadmap found" })
                    : Results.Ok(progress);
            });

        group.MapGet("/systems/{systemId}/roadmap/export", async (
                string systemId,
                IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                try
                {
                    var pdfBytes = await roadmapService.ExportRoadmapPdfAsync(systemId, ct);
                    return Results.File(pdfBytes, "application/pdf", $"roadmap-{systemId}.pdf");
                }
                catch (InvalidOperationException)
                {
                    return Results.NotFound(new { error = "No active roadmap" });
                }
            });

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _context = _app.Services.CreateScope().ServiceProvider.GetRequiredService<AtoCopilotContext>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        _context.Dispose();
    }

    [Fact]
    public async Task GetRoadmap_NoRoadmapExists_Returns404()
    {
        var response = await _client.GetAsync("/api/dashboard/systems/nonexistent/roadmap");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRoadmap_ActiveRoadmapExists_Returns200()
    {
        // Arrange
        var roadmap = new ImplementationRoadmap
        {
            SystemId = "sys-int-1",
            Name = "Integration Test Roadmap",
            Status = RoadmapStatus.Active,
            TotalGaps = 3,
            TotalEstimatedEffort = 10,
            TotalRiskPoints = 15,
            ProjectedRiskReduction = 100,
            BaselineLevel = "Moderate",
        };
        roadmap.Phases.Add(new RoadmapPhase
        {
            RoadmapId = roadmap.Id,
            Name = "Phase 1",
            DisplayOrder = 1,
            TotalItemCount = 3,
        });
        _context.ImplementationRoadmaps.Add(roadmap);
        await _context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/dashboard/systems/sys-int-1/roadmap");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("sys-int-1");
        content.Should().Contain("Integration Test Roadmap");
    }

    [Fact]
    public async Task GetRoadmap_WithIncludeItems_ReturnsItems()
    {
        // Arrange
        var roadmap = new ImplementationRoadmap
        {
            SystemId = "sys-int-2",
            Name = "Item Test Roadmap",
            Status = RoadmapStatus.Active,
            TotalGaps = 1,
            TotalRiskPoints = 10,
            BaselineLevel = "Low",
        };
        var phase = new RoadmapPhase
        {
            RoadmapId = roadmap.Id,
            Name = "P1",
            DisplayOrder = 1,
            TotalItemCount = 1,
        };
        phase.Items.Add(new RoadmapItem
        {
            PhaseId = phase.Id,
            RoadmapId = roadmap.Id,
            ControlId = "AC-2",
            Severity = ItemSeverity.Critical,
            RiskPoints = 10,
            GapType = GapType.Unmapped,
        });
        roadmap.Phases.Add(phase);
        _context.ImplementationRoadmaps.Add(roadmap);
        await _context.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/dashboard/systems/sys-int-2/roadmap?includeItems=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("AC-2");
    }

    [Fact]
    public async Task GetProgress_NoRoadmap_Returns404()
    {
        var response = await _client.GetAsync("/api/dashboard/systems/nonexistent/roadmap/progress");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetExport_NoRoadmap_Returns404Or501()
    {
        var response = await _client.GetAsync("/api/dashboard/systems/nonexistent/roadmap/export");

        // Either 404 (no roadmap) or 501 (not implemented) is acceptable
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.NotImplemented);
    }
}
