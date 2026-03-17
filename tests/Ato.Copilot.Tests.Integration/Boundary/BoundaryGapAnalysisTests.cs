using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration.Boundary;

public class BoundaryGapAnalysisTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private AtoCopilotContext _context = null!;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private const string SystemId = "sys-gap-001";
    private const string PrimaryBoundaryId = "bnd-gap-primary";
    private const string DevBoundaryId = "bnd-gap-dev";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        var dbName = $"BoundaryGapAnalysis_{Guid.NewGuid():N}";

        builder.Services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<CapabilityService>();
        builder.Services.AddScoped<NarrativeTemplateService>();
        builder.Services.AddLogging();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        var group = _app.MapGroup("/api/dashboard");

        group.MapGet("/systems/{systemId}/gaps", async (
                string systemId,
                string? boundaryDefinitionId,
                CapabilityService capService,
                CancellationToken ct) =>
            {
                var result = await capService.GetGapAnalysisAsync(systemId, boundaryDefinitionId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new { error = "System or baseline not found" });
            });

        await _app.StartAsync();
        _client = _app.GetTestClient();

        _context = _app.Services.CreateScope().ServiceProvider
            .GetRequiredService<AtoCopilotContext>();

        SeedData();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private void SeedData()
    {
        var system = new RegisteredSystem
        {
            Id = SystemId,
            Name = "Gap Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        };
        _context.RegisteredSystems.Add(system);

        // Baseline with 3 controls: AC-2, IA-2, SC-7
        _context.ControlBaselines.Add(new ControlBaseline
        {
            RegisteredSystemId = SystemId,
            BaselineLevel = "Moderate",
            TotalControls = 3,
            ControlIds = ["AC-2", "IA-2", "SC-7"],
            CreatedBy = "test",
        });

        // Add NistControls so unmapped lookups succeed
        _context.NistControls.AddRange(
            new NistControl { Id = "AC-2", Family = "AC", Title = "Account Management" },
            new NistControl { Id = "IA-2", Family = "IA", Title = "Identification and Authentication" },
            new NistControl { Id = "SC-7", Family = "SC", Title = "Boundary Protection" }
        );

        // Primary boundary
        _context.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = PrimaryBoundaryId,
            RegisteredSystemId = SystemId,
            Name = "Primary",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "test",
        });

        // Dev boundary
        _context.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = DevBoundaryId,
            RegisteredSystemId = SystemId,
            Name = "Dev/Test",
            BoundaryType = BoundaryDefinitionType.Physical,
            IsPrimary = false,
            CreatedBy = "test",
        });

        // Capability
        var cap = new SecurityCapability
        {
            Id = "cap-001",
            Name = "MFA",
            Provider = "Entra ID",
            Description = "Multi-factor authentication",
        };
        _context.SecurityCapabilities.Add(cap);

        // Mapping 1: AC-2 scoped to Primary boundary
        _context.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = "cap-001",
            ControlId = "AC-2",
            RegisteredSystemId = SystemId,
            AuthorizationBoundaryDefinitionId = PrimaryBoundaryId,
        });

        // Mapping 2: IA-2 org-wide (null boundary FK) — applies to all boundaries
        _context.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = "cap-001",
            ControlId = "IA-2",
            RegisteredSystemId = SystemId,
            AuthorizationBoundaryDefinitionId = null,
        });

        // Mapping 3: SC-7 scoped to Dev boundary only
        _context.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = "cap-001",
            ControlId = "SC-7",
            RegisteredSystemId = SystemId,
            AuthorizationBoundaryDefinitionId = DevBoundaryId,
        });

        _context.SaveChanges();
    }

    [Fact]
    public async Task GapAnalysis_NoFilter_ReturnsAllCovered()
    {
        var response = await _client.GetAsync($"/api/dashboard/systems/{SystemId}/gaps");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        // All 3 controls should be covered (AC-2 + IA-2 + SC-7)
        body.GetProperty("totalBaselineControls").GetInt32().Should().Be(3);
        body.GetProperty("coveredControls").GetInt32().Should().Be(3);
        body.GetProperty("gapCount").GetInt32().Should().Be(0);
        body.GetProperty("coveragePercent").GetDouble().Should().Be(100.0);
    }

    [Fact]
    public async Task GapAnalysis_PrimaryBoundary_IncludesOrgWide()
    {
        var response = await _client.GetAsync(
            $"/api/dashboard/systems/{SystemId}/gaps?boundaryDefinitionId={PrimaryBoundaryId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        // Primary boundary: AC-2 (scoped to Primary) + IA-2 (org-wide) = 2 covered
        body.GetProperty("coveredControls").GetInt32().Should().Be(2);
        body.GetProperty("gapCount").GetInt32().Should().Be(1); // SC-7 is Dev-only
    }

    [Fact]
    public async Task GapAnalysis_DevBoundary_IncludesOrgWide()
    {
        var response = await _client.GetAsync(
            $"/api/dashboard/systems/{SystemId}/gaps?boundaryDefinitionId={DevBoundaryId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        // Dev boundary: SC-7 (scoped to Dev) + IA-2 (org-wide) = 2 covered
        body.GetProperty("coveredControls").GetInt32().Should().Be(2);
        body.GetProperty("gapCount").GetInt32().Should().Be(1); // AC-2 is Primary-only
    }

    [Fact]
    public async Task GapAnalysis_NoFilter_IncludesBoundaryComparison()
    {
        var response = await _client.GetAsync($"/api/dashboard/systems/{SystemId}/gaps");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        // Should include boundary comparison when no filter and >1 boundary
        var comparison = body.GetProperty("boundaryComparison");
        comparison.ValueKind.Should().Be(JsonValueKind.Array);
        comparison.GetArrayLength().Should().Be(2);

        // Primary should be first (sorted by IsPrimary desc)
        var primary = comparison[0];
        primary.GetProperty("boundaryName").GetString().Should().Be("Primary");
        primary.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        primary.GetProperty("coveredControls").GetInt32().Should().Be(2); // AC-2 + IA-2

        var dev = comparison[1];
        dev.GetProperty("boundaryName").GetString().Should().Be("Dev/Test");
        dev.GetProperty("isPrimary").GetBoolean().Should().BeFalse();
        dev.GetProperty("coveredControls").GetInt32().Should().Be(2); // SC-7 + IA-2
    }

    [Fact]
    public async Task GapAnalysis_WithFilter_ExcludesBoundaryComparison()
    {
        var response = await _client.GetAsync(
            $"/api/dashboard/systems/{SystemId}/gaps?boundaryDefinitionId={PrimaryBoundaryId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        // When filtering by boundary, comparison should be null
        body.TryGetProperty("boundaryComparison", out var comp).Should().BeTrue();
        comp.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GapAnalysis_InvalidSystem_Returns404()
    {
        var response = await _client.GetAsync("/api/dashboard/systems/non-existent/gaps");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
