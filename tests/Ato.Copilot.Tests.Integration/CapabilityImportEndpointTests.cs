using System.Net;
using System.Net.Http.Json;
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
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for POST /capabilities/import/csp-profile and GET /capabilities/csp-profiles.
/// Uses TestServer with in-memory DB and maps only the endpoints under test.
/// </summary>
public class CapabilityImportEndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _tempDir = null!;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"import-integration-{Guid.NewGuid()}");
        var profileDir = Path.Combine(_tempDir, "src", "seed-data", "csp-profiles");
        Directory.CreateDirectory(profileDir);

        var profile = new
        {
            profileId = "int-test-azure-high",
            name = "Integration Test Azure Gov FedRAMP High",
            provider = "Microsoft Azure Government",
            baselineLevel = "high",
            description = "Integration test profile",
            version = "1.0",
            services = new[]
            {
                new
                {
                    name = "Microsoft Entra ID",
                    category = "Identity & Access Management",
                    description = "Identity services",
                    controls = new[]
                    {
                        new { controlId = "ac-2", inheritanceType = "Inherited", customerResponsibility = "" },
                        new { controlId = "ac-3", inheritanceType = "Shared", customerResponsibility = "Configure RBAC" },
                    }
                },
                new
                {
                    name = "Azure Monitor",
                    category = "Audit & Logging",
                    description = "Logging",
                    controls = new[]
                    {
                        new { controlId = "au-2", inheritanceType = "Inherited", customerResponsibility = "" },
                    }
                }
            }
        };
        File.WriteAllText(
            Path.Combine(profileDir, "test-profile.json"),
            JsonSerializer.Serialize(profile));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var dbName = $"CapImportEndpoints_{Guid.NewGuid():N}";

        var orgServiceMock = new Mock<IOrgInheritanceService>();
        orgServiceMock
            .Setup(o => o.DeriveOrgDefaultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgDerivationResult(3, 2, 1, 0, 1, DateTime.UtcNow));

        builder.Services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName));

        builder.Services.AddSingleton<CspProfileService>(sp =>
        {
            var envMock = new Mock<IWebHostEnvironment>();
            envMock.Setup(e => e.ContentRootPath).Returns(Path.Combine(_tempDir, "non-existent"));
            var origDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(_tempDir);
            var svc = new CspProfileService(
                sp.GetRequiredService<ILogger<CspProfileService>>(), envMock.Object);
            Directory.SetCurrentDirectory(origDir);
            return svc;
        });

        builder.Services.AddSingleton<NarrativeTemplateService>();
        builder.Services.AddSingleton<IOrgInheritanceService>(orgServiceMock.Object);
        builder.Services.AddScoped<CapabilityImportService>();
        builder.Services.AddSingleton<CrmExportService>();
        builder.Services.AddLogging();

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        // Map ONLY the endpoints under test to avoid 40+ service dependency registrations
        var group = _app.MapGroup("/api/dashboard");
        MapCapabilityImportEndpoints(group);

        // Seed NIST controls
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.NistControls.AddRange(
            new NistControl { Id = "ac-2", Title = "Account Management", Family = "AC", Description = "Test" },
            new NistControl { Id = "ac-3", Title = "Access Enforcement", Family = "AC", Description = "Test" },
            new NistControl { Id = "au-2", Title = "Event Logging", Family = "AU", Description = "Test" }
        );
        await db.SaveChangesAsync();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    private static void MapCapabilityImportEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/capabilities/import/csp-profile", async (
                CspProfileImportRequest request,
                CapabilityImportService importService,
                CancellationToken ct) =>
            {
                try
                {
                    if (request.DryRun == true)
                    {
                        var preview = await importService.ImportCspProfilePreviewAsync(
                            request.ProfileId, request.ConflictResolution ?? "skip", ct);
                        return Results.Ok(preview);
                    }
                    var result = await importService.ImportCspProfileAsync(
                        request.ProfileId, request.ConflictResolution ?? "skip", ct);
                    return Results.Ok(result);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = $"CSP profile '{request.ProfileId}' not found" });
                }
            });

        group.MapGet("/capabilities/csp-profiles", (
                CspProfileService cspProfileService) =>
            {
                var profiles = cspProfileService.GetProfiles();
                return Results.Ok(new
                {
                    profiles = profiles.Select(p => new
                    {
                        profileId = p.ProfileId,
                        name = p.Name,
                        provider = p.Provider,
                        baselineLevel = p.BaselineLevel,
                        description = p.Description,
                        controlCount = p.Controls.Count,
                        serviceCount = p.Services?.Count ?? 0,
                        version = p.Version
                    })
                });
            });

        group.MapGet("/capabilities/coverage", async (
                bool? includePerSystem,
                bool? includePerFamily,
                CapabilityImportService importService,
                CancellationToken ct) =>
            {
                var result = await importService.ComputeCoverageAsync(
                    includePerSystem ?? false, includePerFamily ?? true, ct);
                return Results.Ok(result);
            });

        group.MapPost("/capabilities/import/crm", async (
                HttpRequest httpRequest,
                CapabilityImportService importService,
                CrmExportService crmExportService,
                CancellationToken ct) =>
            {
                var form = await httpRequest.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { error = "No file uploaded" });

                var columnMappingJson = form["columnMapping"].ToString();
                var conflictResolution = form["conflictResolution"].ToString();
                var dryRunStr = form["dryRun"].ToString();
                var dryRun = dryRunStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(conflictResolution)) conflictResolution = "skip";

                CrmExportService.ImportParseResult parsed;
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                using var stream = file.OpenReadStream();
                if (ext is ".xlsx" or ".xls")
                    parsed = crmExportService.ParseExcel(stream);
                else
                    parsed = crmExportService.ParseCsv(stream);

                Dictionary<string, string> mapping;
                if (!string.IsNullOrEmpty(columnMappingJson))
                    mapping = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(columnMappingJson) ?? new();
                else
                    mapping = new Dictionary<string, string>
                    {
                        ["controlId"] = parsed.Columns.FirstOrDefault(c => c.Contains("control", StringComparison.OrdinalIgnoreCase)) ?? "controlId",
                        ["inheritanceType"] = parsed.Columns.FirstOrDefault(c => c.Contains("inheritance", StringComparison.OrdinalIgnoreCase)) ?? "inheritanceType",
                        ["provider"] = parsed.Columns.FirstOrDefault(c => c.Contains("provider", StringComparison.OrdinalIgnoreCase)) ?? "provider",
                        ["customerResponsibility"] = parsed.Columns.FirstOrDefault(c => c.Contains("responsibility", StringComparison.OrdinalIgnoreCase)) ?? "customerResponsibility",
                    };

                var rows = parsed.Rows.Select(row => new CrmImportRow
                {
                    ControlId = mapping.TryGetValue("controlId", out var cidCol) && row.TryGetValue(cidCol, out var cid) ? cid : "",
                    InheritanceType = mapping.TryGetValue("inheritanceType", out var itCol) && row.TryGetValue(itCol, out var it) ? it : "",
                    Provider = mapping.TryGetValue("provider", out var pCol) && row.TryGetValue(pCol, out var p) ? p : null,
                    CustomerResponsibility = mapping.TryGetValue("customerResponsibility", out var crCol) && row.TryGetValue(crCol, out var cr) ? cr : null,
                }).Where(r => !string.IsNullOrWhiteSpace(r.ControlId)).ToList();

                if (dryRun)
                {
                    var preview = await importService.ImportCrmPreviewAsync(file.FileName, rows, conflictResolution, ct);
                    preview.DetectedColumns = parsed.Columns;
                    preview.SampleRows = parsed.SampleRows;
                    return Results.Ok(preview);
                }

                var result = await importService.ImportCrmAsync(file.FileName, rows, conflictResolution, ct);
                return Results.Ok(result);
            }).DisableAntiforgery();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── POST /capabilities/import/csp-profile — dryRun ────────────────────

    [Fact]
    public async Task ImportCspProfile_DryRunTrue_ReturnsPreviewWithoutPersisting()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = true },
            _json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        body.GetProperty("componentsToCreate").GetInt32().Should().Be(2);
        body.GetProperty("capabilitiesToCreate").GetInt32().Should().Be(2);
        body.GetProperty("controlMappingsToCreate").GetInt32().Should().Be(3);

        // Nothing persisted
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemComponents.CountAsync()).Should().Be(0);
        (await db.SecurityCapabilities.CountAsync()).Should().Be(0);
    }

    // ─── POST /capabilities/import/csp-profile — apply ─────────────────────

    [Fact]
    public async Task ImportCspProfile_DryRunFalse_CreatesFullPipeline()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = false },
            _json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("dryRun").GetBoolean().Should().BeFalse();
        body.GetProperty("componentsCreated").GetInt32().Should().Be(2);
        body.GetProperty("capabilitiesCreated").GetInt32().Should().Be(2);
        body.GetProperty("controlMappingsCreated").GetInt32().Should().Be(3);

        // Verify persisted
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemComponents.CountAsync()).Should().Be(2);
        (await db.SecurityCapabilities.CountAsync()).Should().Be(2);
        (await db.CapabilityControlMappings.CountAsync()).Should().Be(3);
        (await db.ComponentCapabilityLinks.CountAsync()).Should().Be(2);
    }

    // ─── POST /capabilities/import/csp-profile — duplicate ──────────────────

    [Fact]
    public async Task ImportCspProfile_DuplicateRun_ReusesExistingRecords()
    {
        // First import
        await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = false },
            _json);

        // Second import
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = false },
            _json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("componentsCreated").GetInt32().Should().Be(0);
        body.GetProperty("componentsReused").GetInt32().Should().Be(2);
        body.GetProperty("capabilitiesCreated").GetInt32().Should().Be(0);
        body.GetProperty("capabilitiesReused").GetInt32().Should().Be(2);
        body.GetProperty("controlMappingsCreated").GetInt32().Should().Be(0);
    }

    // ─── POST /capabilities/import/csp-profile — not found ──────────────────

    [Fact]
    public async Task ImportCspProfile_InvalidProfileId_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "nonexistent", conflictResolution = "skip", dryRun = false },
            _json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── GET /capabilities/csp-profiles ─────────────────────────────────────

    [Fact]
    public async Task ListCspProfiles_ReturnsAvailableProfiles()
    {
        var response = await _client.GetAsync("/api/dashboard/capabilities/csp-profiles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        var profiles = body.GetProperty("profiles");
        profiles.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        var first = profiles[0];
        first.GetProperty("profileId").GetString().Should().Be("int-test-azure-high");
        first.GetProperty("name").GetString().Should().Contain("Integration Test");
        first.GetProperty("controlCount").GetInt32().Should().Be(3);
        first.GetProperty("serviceCount").GetInt32().Should().Be(2);
    }

    // ─── GET /capabilities/coverage ─────────────────────────────────────────

    [Fact]
    public async Task GetCoverage_WithImportedData_ReturnsCorrectCounts()
    {
        // Import a profile first
        await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = false },
            _json);

        var response = await _client.GetAsync("/api/dashboard/capabilities/coverage?includePerFamily=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        var orgWide = body.GetProperty("orgWide");
        orgWide.GetProperty("totalCapabilities").GetInt32().Should().BeGreaterThan(0);
        orgWide.GetProperty("mappedControls").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GetCoverage_WithNoData_ReturnsZeroCounts()
    {
        var response = await _client.GetAsync("/api/dashboard/capabilities/coverage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        var orgWide = body.GetProperty("orgWide");
        orgWide.GetProperty("totalCapabilities").GetInt32().Should().Be(0);
        orgWide.GetProperty("mappedControls").GetInt32().Should().Be(0);
    }

    // ─── POST /capabilities/import/crm ────────────────────────────────────

    private MultipartFormDataContent CreateCrmFormData(string csv, string? columnMapping = null, string conflictResolution = "skip", bool dryRun = false)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "test-crm.csv");
        if (columnMapping is not null) content.Add(new StringContent(columnMapping), "columnMapping");
        content.Add(new StringContent(conflictResolution), "conflictResolution");
        content.Add(new StringContent(dryRun.ToString().ToLower()), "dryRun");
        return content;
    }

    [Fact]
    public async Task ImportCrm_DryRunTrue_ReturnsPreviewWithDetectedColumns()
    {
        var csv = "controlId,inheritanceType,provider,customerResponsibility\nac-2,Inherited,Zscaler,\nac-3,Shared,Zscaler,Configure policies";
        var mapping = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["controlId"] = "controlId",
            ["inheritanceType"] = "inheritanceType",
            ["provider"] = "provider",
            ["customerResponsibility"] = "customerResponsibility",
        });

        var response = await _client.PostAsync(
            "/api/dashboard/capabilities/import/crm",
            CreateCrmFormData(csv, mapping, dryRun: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        body.GetProperty("rowsParsed").GetInt32().Should().Be(2);
        body.GetProperty("detectedColumns").GetArrayLength().Should().Be(4);

        // Nothing persisted
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemComponents.CountAsync()).Should().Be(0);
        (await db.SecurityCapabilities.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportCrm_Apply_CreatesFullPipeline()
    {
        var csv = "controlId,inheritanceType,provider,customerResponsibility\nac-2,Inherited,Zscaler,\nau-2,Inherited,Zscaler,";
        var mapping = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["controlId"] = "controlId",
            ["inheritanceType"] = "inheritanceType",
            ["provider"] = "provider",
            ["customerResponsibility"] = "customerResponsibility",
        });

        var response = await _client.PostAsync(
            "/api/dashboard/capabilities/import/crm",
            CreateCrmFormData(csv, mapping, dryRun: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("dryRun").GetBoolean().Should().BeFalse();
        body.GetProperty("componentsCreated").GetInt32().Should().Be(1); // Zscaler
        body.GetProperty("capabilitiesCreated").GetInt32().Should().Be(2); // Zscaler / AC, Zscaler / AU
        body.GetProperty("controlMappingsCreated").GetInt32().Should().Be(2);

        // Verify persisted
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemComponents.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
        (await db.SecurityCapabilities.CountAsync()).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ImportCrm_DuplicateRun_ReusesExistingRecords()
    {
        var csv = "controlId,inheritanceType,provider,customerResponsibility\nac-2,Inherited,Zscaler,";
        var mapping = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["controlId"] = "controlId",
            ["inheritanceType"] = "inheritanceType",
            ["provider"] = "provider",
            ["customerResponsibility"] = "customerResponsibility",
        });

        // First import
        await _client.PostAsync(
            "/api/dashboard/capabilities/import/crm",
            CreateCrmFormData(csv, mapping, dryRun: false));

        // Second import
        var response = await _client.PostAsync(
            "/api/dashboard/capabilities/import/crm",
            CreateCrmFormData(csv, mapping, dryRun: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("componentsCreated").GetInt32().Should().Be(0);
        body.GetProperty("componentsReused").GetInt32().Should().Be(1);
        body.GetProperty("capabilitiesCreated").GetInt32().Should().Be(0);
        body.GetProperty("capabilitiesReused").GetInt32().Should().Be(1);
    }

    // ─── Performance Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task PT001_CspProfileImport_CompletesWithinPerformanceBudget()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = false },
            _json);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(30000, "CSP profile import should complete within 30s");
    }

    [Fact]
    public async Task PT002_CoverageEndpoint_ReturnsWithinBudget()
    {
        // Import data first
        await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = false },
            _json);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.GetAsync("/api/dashboard/capabilities/coverage?includePerFamily=true&includePerSystem=true");
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, "Coverage endpoint should return within 2s");
    }

    [Fact]
    public async Task PT003_CspProfileImport_DuplicateRun_NoPerformanceDegradation()
    {
        // First import
        await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = false },
            _json);

        // Duplicate import — should still be fast
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.PostAsJsonAsync(
            "/api/dashboard/capabilities/import/csp-profile",
            new { profileId = "int-test-azure-high", conflictResolution = "skip", dryRun = false },
            _json);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(30000, "Duplicate CSP import should not degrade");
    }
}
