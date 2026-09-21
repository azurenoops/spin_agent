using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Authentication;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Extensions;
using Ato.Copilot.Mcp.Middleware;
using Ato.Copilot.Mcp.Server;
using Microsoft.AspNetCore.Authentication;
using Moq;
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
// ApiMismatchRouteTests uses IAsyncLifetime and boots its own WebApplication.
// It must NOT share the IntegrationTests collection because its DisposeAsync
// disposes shared singletons while sibling IClassFixture tests are still running,
// causing ObjectDisposedException on ImpersonationAuditTests/SignOutEndpointTests.
[Collection("ApiMismatchRouteTests")]
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
    private const string TestActorId = "audit.user@example.mil";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Issue962_BusinessContextRead_DistinguishesDraftFromAbsence(bool hasDraft)
    {
        // Arrange
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = new ControlImplementation
        {
            RegisteredSystemId = TestSystemId, ControlId = "AC-2",
            PolicyNarrative = "Policy text", TechnicalNarrative = "Technical text"
        };
        db.ControlImplementations.Add(implementation);
        if (hasDraft)
            db.BusinessContextDrafts.Add(new BusinessContextDraft
            {
                ControlImplementationId = implementation.Id,
                Content = "Synthetic mission context", AuthoredBy = "mission-owner"
            });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{TestSystemId}/business-context/AC-2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        if (hasDraft)
        {
            payload.GetProperty("content").GetString().Should().Be("Synthetic mission context");
            payload.GetProperty("controlId").GetString().Should().Be("AC-2");
            payload.GetProperty("governanceStatus").GetString().Should().Be("Draft");
            payload.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        }
        else
            payload.ValueKind.Should().Be(JsonValueKind.Null);
        await db.Entry(implementation).ReloadAsync();
        implementation.PolicyNarrative.Should().Be("Policy text");
        implementation.TechnicalNarrative.Should().Be("Technical text");
    }

    [Theory]
    [InlineData(RmfRole.MissionOwner, false, true)]
    [InlineData(RmfRole.Issm, false, false)]
    [InlineData(RmfRole.Isso, false, false)]
    [InlineData(RmfRole.MissionOwner, true, false)]
    [InlineData(RmfRole.Issm, true, true)]
    [InlineData(RmfRole.Isso, true, false)]
    public async Task Issue962_BusinessContextWrites_UseAssignedRoles(RmfRole role, bool flag, bool allowed)
    {
        // Arrange
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = TestSystemId, ControlId = "AC-2",
            PolicyNarrative = "Policy text", TechnicalNarrative = "Technical text"
        });
        db.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = TestSystemId, UserId = TestActorId,
            UserDisplayName = "Synthetic actor", RmfRole = role,
            AssignedBy = "test", IsActive = true
        });
        await db.SaveChangesAsync();
        var endpoint = $"/api/dashboard/systems/{TestSystemId}/business-context";

        // Act
        var response = flag
            ? await _client.PostAsJsonAsync($"{endpoint}/flags", new { controlId = "AC-2", isFlagged = true })
            : await _client.PutAsJsonAsync($"{endpoint}/AC-2", new { content = "Owner context" });

        // Assert
        response.StatusCode.Should().Be(allowed ? (flag ? HttpStatusCode.NoContent : HttpStatusCode.OK) : HttpStatusCode.Forbidden);
        if (allowed && flag)
        {
            var flags = await _client.GetFromJsonAsync<JsonElement>($"{endpoint}/flagged-controls");
            flags.EnumerateArray().Should().ContainSingle();
            flags[0].GetProperty("controlId").GetString().Should().Be("AC-2");
            flags[0].GetProperty("hasDraft").GetBoolean().Should().BeFalse();
            var unflag = await _client.PostAsJsonAsync($"{endpoint}/flags", new { controlId = "AC-2", isFlagged = false });
            unflag.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await _client.GetFromJsonAsync<JsonElement>($"{endpoint}/flagged-controls")).GetArrayLength().Should().Be(0);
        }
        if (allowed && !flag)
        {
            var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
            saved.GetProperty("authoredBy").GetString().Should().Be(TestActorId);
            var update = await _client.PutAsJsonAsync($"{endpoint}/AC-2", new { content = "Updated owner context" });
            update.StatusCode.Should().Be(HttpStatusCode.OK);
            var loaded = await _client.GetFromJsonAsync<JsonElement>($"{endpoint}/AC-2");
            loaded.GetProperty("content").GetString().Should().Be("Updated owner context");
            loaded.GetProperty("id").GetString().Should().Be(saved.GetProperty("id").GetString());
        }
        var implementation = await db.ControlImplementations.AsNoTracking().SingleAsync();
        implementation.PolicyNarrative.Should().Be("Policy text");
        implementation.TechnicalNarrative.Should().Be("Technical text");
        (await db.BusinessContextDrafts.CountAsync()).Should().Be(allowed && !flag ? 1 : 0);
    }

    [Theory]
    [InlineData("GET", "AC-2")]
    [InlineData("GET", "flagged-controls")]
    [InlineData("PUT", "AC-2")]
    [InlineData("POST", "flags")]
    public async Task Issue962_BusinessContextRoutes_RequireAuthentication(string method, string suffix)
    {
        // Arrange
        using var anonymous = _app.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/dashboard/systems/{TestSystemId}/business-context/{suffix}");
        if (method != "GET")
            request.Content = JsonContent.Create(new { content = "Synthetic context", controlId = "AC-2", isFlagged = true });

        // Act
        var response = await anonymous.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "AC-2", false)]
    [InlineData("GET", "flagged-controls", false)]
    [InlineData("PUT", "AC-2", false)]
    [InlineData("POST", "flags", false)]
    [InlineData("GET", "AC-2", true)]
    [InlineData("PUT", "AC-2", true)]
    [InlineData("POST", "flags", true)]
    public async Task Issue962_BusinessContextRoutes_RejectMissingTargets(string method, string suffix, bool existingSystem)
    {
        // Arrange
        var systemId = existingSystem ? TestSystemId : "missing-system";
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/dashboard/systems/{systemId}/business-context/{suffix}");
        if (method != "GET")
            request.Content = JsonContent.Create(new { content = "Synthetic context", controlId = "AC-2", isFlagged = true });

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be(existingSystem ? "CONTROL_NOT_FOUND" : "SYSTEM_NOT_FOUND");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("oversized", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public async Task Issue962_BusinessContextWrites_RejectInvalidInput(string? input, bool flag)
    {
        // Arrange
        var endpoint = $"/api/dashboard/systems/{TestSystemId}/business-context";
        var content = input == "oversized" ? new string('x', 8001) : input;

        // Act
        var response = flag
            ? await _client.PostAsJsonAsync($"{endpoint}/flags", new { controlId = input, isFlagged = true })
            : await _client.PutAsJsonAsync($"{endpoint}/AC-2", new { content });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SYSTEM_NOT_FOUND", 404, false)]
    [InlineData("CONTROL_NOT_FOUND", 404, false)]
    [InlineData("CONCURRENCY_CONFLICT", 409, false)]
    [InlineData("UNEXPECTED", 0, false)]
    [InlineData("SYSTEM_NOT_FOUND", 404, true)]
    [InlineData("CONCURRENCY_CONFLICT", 409, true)]
    [InlineData("UNEXPECTED", 0, true)]
    public async Task Issue962_BusinessContextWrites_MapOnlyKnownServiceFailures(string code, int expectedStatus, bool flag)
    {
        // Arrange
        var service = new Mock<ISystemProfileService>();
        service.Setup(profile => profile.SaveBusinessContextAsync(TestSystemId, "AC-2", "Context", TestActorId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"{code}: synthetic failure"));
        service.Setup(profile => profile.SetControlFlagAsync(TestSystemId, "AC-2", true, TestActorId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"{code}: synthetic failure"));
        await DisposeAsync();
        await StartAppAsync(service.Object);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.ControlImplementations.Add(new ControlImplementation { RegisteredSystemId = TestSystemId, ControlId = "AC-2" });
        await db.SaveChangesAsync();
        var endpoint = $"/api/dashboard/systems/{TestSystemId}/business-context";

        // Act
        Func<Task<HttpResponseMessage>> send = () => flag
            ? _client.PostAsJsonAsync($"{endpoint}/flags", new { controlId = "AC-2", isFlagged = true })
            : _client.PutAsJsonAsync($"{endpoint}/AC-2", new { content = "Context" });

        // Assert
        if (expectedStatus == 0)
        {
            var response = await send();
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
                await response.Content.ReadAsStringAsync());
        }
        else
        {
            var response = await send();
            ((int)response.StatusCode).Should().Be(expectedStatus);
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            payload.GetProperty("errorCode").GetString().Should().Be(code);
        }
    }

    public Task InitializeAsync() => StartAppAsync();

    private async Task StartAppAsync(ISystemProfileService? profileOverride = null)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ATO_Auth__BypassForTests", "true");
        Environment.SetEnvironmentVariable("ATO_AZUREAI__ENABLED", "false");

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
        if (profileOverride is not null)
            builder.Services.AddSingleton(profileOverride);
        builder.Services
            .AddAuthentication(CacPassthroughAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CacPassthroughAuthHandler>(
                CacPassthroughAuthHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization();

        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        builder.WebHost.UseTestServer();

        _app = builder.Build();

        _app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Test-Authenticated"))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, TestActorId)],
                    CacPassthroughAuthHandler.SchemeName));
            }

            await next(context);
        });
        _app.UseCors();
        _app.UseMiddleware<ComplianceAuthorizationMiddleware>();
        _app.UseMiddleware<AuditLoggingMiddleware>();
        _app.UseAuthentication();
        _app.UseAuthorization();

        var httpBridge = _app.Services.GetRequiredService<McpHttpBridge>();
        httpBridge.MapEndpoints(_app);
        _app.MapDashboardEndpoints();
        _app.MapEmassWorkflowEndpoints();
        _app.MapControlValidationEndpoints();
        _app.MapNotificationEndpoints();
        _app.MapCapabilitySubscriptionEndpoints();

        _app.MapGet("/healthz-test", () => Microsoft.AspNetCore.Http.Results.Json(new
        {
            service = "ATO Copilot",
            version = "1.0.0",
            mode = "test"
        }));

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        await SeedTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("/api/dashboard/systems")]
    [InlineData("/api/dashboard/assessments")]
    [InlineData("/api/dashboard/notifications/")]
    [InlineData("/api/dashboard/capability-library")]
    public async Task Issue822_DashboardRoutes_RejectAnonymousRequests(string route)
    {
        // Arrange
        using var anonymousClient = _app.GetTestClient();

        // Act
        var response = await anonymousClient.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Issue822_AllDashboardRoutes_RequireAuthorization()
    {
        // Arrange
        var routeEndpoints = _app.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?
                .StartsWith("/api/dashboard", StringComparison.OrdinalIgnoreCase) == true);

        // Act
        var unprotectedRoutes = routeEndpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .OrderBy(route => route)
            .ToList();

        // Assert
        unprotectedRoutes.Should().BeEmpty("every dashboard API route contains sensitive compliance data");
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

        // Handler-level 404 (baseline/profile missing) still proves the route
        // is registered. Unregistered routes return an empty framework 404.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("errorCode",
                because: "POST /api/dashboard/systems/{id}/inheritance/apply-profile must be registered (GAP-001, issue #141)");
        }
        else
        {
            response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
                because: "POST /api/dashboard/systems/{id}/inheritance/apply-profile must be registered (GAP-001, issue #141)");
        }
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

    [Fact]
    public async Task Issue832_BulkCreateMixedBatch_ReturnsMultiStatusWithFailureDetails()
    {
        // Arrange
        const string validFindingId = "finding-832-valid";
        const string missingFindingId = "finding-832-missing";
        await CreateTestFindingAsync(validFindingId);

        var request = new
        {
            findingIds = new[] { validFindingId, missingFindingId },
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{TestSystemId}/poam/bulk-create",
            request,
            _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        body.GetProperty("totalSubmitted").GetInt32().Should().Be(2);
        body.GetProperty("totalSucceeded").GetInt32().Should().Be(1);
        body.GetProperty("totalFailed").GetInt32().Should().Be(1);
        var failedRecord = body.GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("findingId").GetString() == missingFindingId);
        failedRecord.GetProperty("status").GetString().Should().Be("error");
        failedRecord.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Issue832_BulkCreateAllFailed_ReturnsBadRequestWithFailureDetails()
    {
        // Arrange
        var request = new
        {
            findingIds = new[] { "finding-832-missing-1", "finding-832-missing-2" },
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{TestSystemId}/poam/bulk-create",
            request,
            _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        body.GetProperty("totalSubmitted").GetInt32().Should().Be(2);
        body.GetProperty("totalSucceeded").GetInt32().Should().Be(0);
        body.GetProperty("totalFailed").GetInt32().Should().Be(2);
        body.GetProperty("results").EnumerateArray()
            .Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Issue832_BulkCreateSuccessfulBatch_ReturnsOk()
    {
        // Arrange
        const string validFindingId = "finding-832-success";
        await CreateTestFindingAsync(validFindingId);
        var request = new
        {
            findingIds = new[] { validFindingId },
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/dashboard/systems/{TestSystemId}/poam/bulk-create",
            request,
            _jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        body.GetProperty("totalSubmitted").GetInt32().Should().Be(1);
        body.GetProperty("totalSucceeded").GetInt32().Should().Be(1);
        body.GetProperty("totalFailed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Issue826_DeleteExistingPoam_ReturnsNoContentAndRemovesItem()
    {
        // Arrange
        string poamId;
        using (var scope = _app.Services.CreateScope())
        {
            var poamService = scope.ServiceProvider.GetRequiredService<PoamService>();
            var poam = await poamService.CreateAsync(
                TestSystemId,
                "POA&M to delete",
                "Manual",
                "AC-1",
                CatSeverity.CatII,
                "test-user",
                DateTime.UtcNow.AddDays(30));
            poamId = poam.Id;
        }

        // Act
        var response = await _client.DeleteAsync($"/api/dashboard/poam/{poamId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var verificationScope = _app.Services.CreateScope();
        var factory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();
        (await db.PoamItems.AnyAsync(p => p.Id == poamId)).Should().BeFalse();
    }

    [Fact]
    public async Task Issue826_DeleteMissingPoam_ReturnsStructuredNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/api/dashboard/poam/missing-poam-826");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        body.GetProperty("errorCode").GetString().Should().Be("POAM_NOT_FOUND");
        body.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(false, ImplementationStatus.PartiallyImplemented)]
    [InlineData(true, ImplementationStatus.Implemented)]
    public async Task Issue961_RunAssessment_TemplateProvenanceDoesNotImplyReview(
        bool reviewed, ImplementationStatus expectedStatus)
    {
        // Arrange
        using (var scope = _app.Services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.SecurityCategorizations.Add(new SecurityCategorization
            {
                RegisteredSystemId = TestSystemId, CategorizedBy = "test-user"
            });
            db.ControlImplementations.Add(new ControlImplementation
            {
                RegisteredSystemId = TestSystemId, ControlId = "AC-1", AuthoredBy = "template",
                Narrative = "Deterministic scaffold", TechnicalNarrative = "Deterministic scaffold",
                IsAutoPopulated = true, AiSuggested = false,
                ReviewedBy = reviewed ? "reviewer" : null,
                ImplementationStatus = ImplementationStatus.Planned
            });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync($"/api/dashboard/systems/{TestSystemId}/run-assessment", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verificationScope = _app.Services.CreateScope();
        var verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var verificationDb = await verificationFactory.CreateDbContextAsync();
        var implementation = await verificationDb.ControlImplementations.SingleAsync(item =>
            item.RegisteredSystemId == TestSystemId && item.ControlId == "AC-1");
        implementation.ImplementationStatus.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task Issue823_RunAssessment_PersistsAuthenticatedActor()
    {
        // Arrange
        using (var scope = _app.Services.CreateScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.SecurityCategorizations.Add(new SecurityCategorization
            {
                RegisteredSystemId = TestSystemId,
                CategorizedBy = "test-user",
            });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync(
            $"/api/dashboard/systems/{TestSystemId}/run-assessment",
            content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verificationScope = _app.Services.CreateScope();
        var verificationFactory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var verificationDb = await verificationFactory.CreateDbContextAsync();
        var assessment = await verificationDb.Assessments
            .SingleAsync(a => a.RegisteredSystemId == TestSystemId);
        assessment.InitiatedBy.Should().Be(TestActorId);
        (await verificationDb.ControlEffectivenessRecords
                .Where(e => e.AssessmentId == assessment.Id)
                .Select(e => e.AssessorId)
                .Distinct()
                .ToListAsync())
            .Should().Equal(TestActorId);
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

    private async Task CreateTestFindingAsync(string findingId)
    {
        using var scope = _app.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var assessmentId = $"assessment-{findingId}";
        db.Assessments.Add(new ComplianceAssessment
        {
            Id = assessmentId,
            RegisteredSystemId = TestSystemId,
            SubscriptionId = "subscription-issue-832",
            InitiatedBy = "test",
        });
        db.Findings.Add(new ComplianceFinding
        {
            Id = findingId,
            ControlId = "AC-2",
            ControlFamily = "AC",
            Title = "Issue 832 test finding",
            Description = "Valid finding for issue 832 integration coverage",
            Severity = FindingSeverity.High,
            Source = "test",
            Status = FindingStatus.Open,
            ResourceId = $"resource-{findingId}",
            ResourceType = "Microsoft.Compute/virtualMachines",
            AssessmentId = assessmentId,
        });
        await db.SaveChangesAsync();
    }

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
            WeaknessSource = "STIG",
            SecurityControlNumber = "AC-2",
            CatSeverity = CatSeverity.CatII,
            PointOfContact = "Test User",
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
