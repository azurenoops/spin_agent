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
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration.Boundary;

public class BoundaryEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private AtoCopilotContext _context = null!;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private const string SystemId = "sys-001";
    private const string PrimaryBoundaryId = "bnd-primary";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });

        var dbName = $"BoundaryEndpoints_{Guid.NewGuid():N}";

        builder.Services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<BoundaryDefinitionService>();
        builder.Services.AddLogging();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        // Register only boundary endpoints to avoid requiring all dashboard services
        var group = _app.MapGroup("/api/dashboard");

        group.MapGet("/systems/{systemId}/boundary-definitions", async (
                string systemId,
                BoundaryDefinitionService boundaryService,
                CancellationToken ct) =>
            {
                var items = await boundaryService.ListAsync(systemId, ct);
                return Results.Ok(new { items, totalCount = items.Count });
            });

        group.MapPost("/systems/{systemId}/boundary-definitions", async (
                string systemId,
                CreateBoundaryDefinitionRequest request,
                BoundaryDefinitionService boundaryService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await boundaryService.CreateAsync(systemId, request, "system", ct);
                    return Results.Created(
                        $"/api/dashboard/boundary-definitions/{result.Id}", result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
                {
                    return Results.Conflict(new { error = ex.Message });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new { error = ex.Message });
                }
            });

        group.MapPut("/boundary-definitions/{id}", async (
                string id,
                CreateBoundaryDefinitionRequest request,
                BoundaryDefinitionService boundaryService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await boundaryService.UpdateAsync(id, request, ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
                {
                    return Results.Conflict(new { error = ex.Message });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new { error = ex.Message });
                }
            });

        group.MapDelete("/boundary-definitions/{id}", async (
                string id,
                BoundaryDefinitionService boundaryService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await boundaryService.DeleteAsync(id, "system", ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Primary"))
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new { error = ex.Message });
                }
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
        _context.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId,
            Name = "Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });

        _context.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = PrimaryBoundaryId,
            RegisteredSystemId = SystemId,
            Name = "Test System — Primary",
            BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true,
            CreatedBy = "migration",
        });

        _context.SaveChanges();
    }

    [Fact]
    public async Task GetBoundaryDefinitions_ReturnsListForSystem()
    {
        var response = await _client.GetAsync($"/api/dashboard/systems/{SystemId}/boundary-definitions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        body.GetProperty("totalCount").GetInt32().Should().Be(1);
        body.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task CreateBoundaryDefinition_Returns201()
    {
        var request = new CreateBoundaryDefinitionRequest("Dev/Test", "Logical", "Dev env");

        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{SystemId}/boundary-definitions", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<BoundaryDefinitionDto>(_jsonOptions);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Dev/Test");
        dto.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public async Task CreateBoundaryDefinition_DuplicateName_Returns409()
    {
        var request = new CreateBoundaryDefinitionRequest("Test System — Primary", "Logical", null);

        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{SystemId}/boundary-definitions", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateBoundaryDefinition_Returns200()
    {
        // Create first
        var createReq = new CreateBoundaryDefinitionRequest("ToUpdate", "Logical", null);
        var createRes = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{SystemId}/boundary-definitions", createReq);
        var created = await createRes.Content.ReadFromJsonAsync<BoundaryDefinitionDto>(_jsonOptions);

        // Update
        var updateReq = new CreateBoundaryDefinitionRequest("Updated", "Physical", "Updated desc");
        var response = await _client.PutAsJsonAsync(
            $"/api/dashboard/boundary-definitions/{created!.Id}", updateReq);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<BoundaryDefinitionDto>(_jsonOptions);
        dto!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteBoundaryDefinition_Returns200WithReassignment()
    {
        // Create a temp boundary
        var createReq = new CreateBoundaryDefinitionRequest("ToDelete", "Logical", null);
        var createRes = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{SystemId}/boundary-definitions", createReq);
        var created = await createRes.Content.ReadFromJsonAsync<BoundaryDefinitionDto>(_jsonOptions);

        var response = await _client.DeleteAsync(
            $"/api/dashboard/boundary-definitions/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        body.GetProperty("deletedId").GetString().Should().Be(created.Id);
        body.GetProperty("primaryBoundaryId").GetString().Should().Be(PrimaryBoundaryId);
    }

    [Fact]
    public async Task DeleteBoundaryDefinition_Primary_Returns400()
    {
        var response = await _client.DeleteAsync(
            $"/api/dashboard/boundary-definitions/{PrimaryBoundaryId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
