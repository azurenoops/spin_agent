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
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Agents.Compliance.Services;

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
    private readonly Mock<IProviderEvidenceSharingService> _provider = new();
    private readonly Mock<IFileStorageProvider> _storage = new();
    private readonly Mock<IAzurePolicyComplianceService> _policy = new();
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

        var storageProvider = _storage;
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
        _provider.Setup(s => s.ListMissionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProviderEvidenceShareResponse>([], 1, 100, 0));
        builder.Services.AddSingleton(_provider.Object);
        builder.Services.AddSingleton(_policy.Object);
        builder.Services.AddSingleton(Mock.Of<IDefenderForCloudService>());
        builder.Services.AddSingleton<IEvidenceStorageService, EvidenceStorageService>();
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
    public async Task Catalog_GloballyPagesScopedSourcesAndReturnsKnownLinks()
    {
        // Arrange
        await UploadTestEvidence("older.pdf", "application/pdf");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Assessments.Add(new ComplianceAssessment { Id = "catalog-assessment", RegisteredSystemId = _systemId });
            db.Evidence.AddRange(
                new ComplianceEvidence { Id = "scoped", AssessmentId = "catalog-assessment", ControlId = "AC-1", Description = "New scan", CollectedAt = DateTime.UtcNow.AddMinutes(1) },
                new ComplianceEvidence { Id = "unscoped", ControlId = "AC-1", Description = "Another system scan", CollectedAt = DateTime.UtcNow.AddMinutes(2) });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{_systemId}/evidence-catalog?pageSize=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalCount").GetInt32().Should().Be(2);
        json.GetProperty("counts").GetProperty("system").GetInt32().Should().Be(2);
        json.GetProperty("items").GetArrayLength().Should().Be(1);
        json.GetProperty("items")[0].GetProperty("id").GetString().Should().Be("automated:scoped");
        var filtered = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence-catalog?source=Manual&family=AC");
        filtered.GetProperty("totalCount").GetInt32().Should().Be(1);
        filtered.GetProperty("items")[0].GetProperty("controls")[0].GetProperty("kind").GetString().Should().Be("direct");
    }

    [Fact]
    public async Task Catalog_ProjectsCapabilityValidationTitlesAndFilteredMissingLinks()
    {
        // Arrange
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.NistControls.Add(new NistControl { Id = "AC-1", Family = "AC", Title = "Access control policy" });
            db.SecurityCapabilities.Add(new SecurityCapability { Id = "catalog-capability", Name = "Capability" });
            db.CapabilityControlMappings.Add(new CapabilityControlMapping
                { SecurityCapabilityId = "catalog-capability", RegisteredSystemId = _systemId, ControlId = "AC-1" });
            db.EvidenceArtifacts.AddRange(
                new EvidenceArtifact { Id = "capability-artifact", RegisteredSystemId = _systemId,
                    SecurityCapabilityId = "catalog-capability", FileName = "capability.txt", ArtifactCategory = ArtifactCategory.Other },
                new EvidenceArtifact { Id = "validation-artifact", RegisteredSystemId = _systemId,
                    FileName = "validation.txt", ArtifactCategory = ArtifactCategory.Other },
                new EvidenceArtifact { Id = "missing-link", RegisteredSystemId = _systemId,
                    FileName = "unlinked.txt", ArtifactCategory = ArtifactCategory.Other });
            db.ControlValidationLinks.AddRange(
                new ControlValidationLink { ControlImplementationId = "ci-1", LinkType = ControlValidationLinkType.EvidenceArtifact,
                    LinkTarget = "evidence://validation-artifact" },
                new ControlValidationLink { ControlImplementationId = "ci-1", LinkType = ControlValidationLinkType.EvidenceArtifact,
                    LinkTarget = "validation-artifact" });
            await db.SaveChangesAsync();
        }

        // Act
        var all = await _client.GetFromJsonAsync<JsonElement>($"/api/dashboard/systems/{_systemId}/evidence-catalog");
        var filtered = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence-catalog?family=AC&category=Other");

        // Assert
        all.GetProperty("totalCount").GetInt32().Should().Be(3);
        all.GetProperty("counts").GetProperty("missingLinks").GetInt32().Should().Be(1);
        filtered.GetProperty("totalCount").GetInt32().Should().Be(2);
        filtered.GetProperty("counts").GetProperty("missingLinks").GetInt32().Should().Be(0);
        var controls = filtered.GetProperty("items").EnumerateArray()
            .SelectMany(i => i.GetProperty("controls").EnumerateArray()).ToArray();
        controls.Should().HaveCount(2, "duplicate validation references represent one linked control");
        controls.Select(c => c.GetProperty("kind").GetString()).Should().BeEquivalentTo("capability", "validation");
        controls.Should().OnlyContain(c => c.GetProperty("title").GetString() == "Access control policy");
    }

    [Fact]
    public async Task Catalog_ProviderFailure_IsPartialNotEmpty()
    {
        // Arrange
        await UploadTestEvidence("local.pdf", "application/pdf");
        _provider.Setup(s => s.ListMissionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("private infrastructure detail"));

        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/{_systemId}/evidence-catalog");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("totalCount").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("availableCount").GetInt32().Should().Be(1);
        json.GetProperty("counts").GetProperty("system").GetInt32().Should().Be(1);
        json.GetProperty("counts").GetProperty("provider").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("sources")[1].GetProperty("state").GetString().Should().Be("unavailable");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("private infrastructure");
    }

    [Fact]
    public async Task Catalog_FilterExcludingFailedSourceHasCompleteSelectedCount()
    {
        // Arrange
        await UploadTestEvidence("local.pdf", "application/pdf");
        _provider.Setup(s => s.ListMissionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("unavailable"));

        // Act
        var json = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence-catalog?source=Manual");

        // Assert
        json.GetProperty("totalCount").GetInt32().Should().Be(1);
        json.GetProperty("counts").GetProperty("all").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Catalog_ProviderPaginationAndDateFilteringHappenBeforeGlobalPaging()
    {
        // Arrange
        var rows = Enumerable.Range(1, 105).Select(i => new ProviderEvidenceShareResponse(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _systemId,
            1, null, $"Summary {i:D3}", "same-source-hash", "same-summary-hash", "approver",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i), 1, null)).ToArray();
        _provider.Setup(s => s.ListMissionAsync(_systemId, It.IsAny<int>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, int page, int size, CancellationToken _) =>
                new PagedResult<ProviderEvidenceShareResponse>(rows.Skip((page - 1) * size).Take(size).ToArray(), page, size, rows.Length));

        // Act
        var json = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence-catalog?view=provider&sortBy=name&sortOrder=asc&page=2&pageSize=100");
        var filtered = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence-catalog?dateFrom=2026-04-15T00:00:00Z&search=Summary");

        // Assert
        json.GetProperty("totalCount").GetInt32().Should().Be(105, "equal hashes must not collapse separate grants");
        json.GetProperty("items").GetArrayLength().Should().Be(5);
        json.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Summary 101");
        filtered.GetProperty("totalCount").GetInt32().Should().Be(2);
        filtered.GetProperty("counts").GetProperty("missingLinks").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Catalog_FileCheckFailureIsUnavailableAndMetadataRemainsUnknown()
    {
        // Arrange
        var response = await UploadTestEvidence("local.pdf", "application/pdf");
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        _storage.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("private disk path"));

        // Act
        var detail = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence-catalog/artifact:{id}");

        // Assert
        detail.GetProperty("availability").GetString().Should().Be("Unavailable");
        detail.GetProperty("permissions").GetProperty("canDownload").GetBoolean().Should().BeFalse();
        detail.GetProperty("owner").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("currency").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("relevance").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetRawText().Should().NotContain("private disk path");
    }

    [Fact]
    public async Task Catalog_ProviderDeniedDoesNotReportZeroOrExposeDetails()
    {
        // Arrange
        _provider.Setup(s => s.ListMissionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("private policy detail"));

        // Act
        var json = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence-catalog?view=provider");

        // Assert
        json.GetProperty("totalCount").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("sources")[1].GetProperty("state").GetString().Should().Be("denied");
        json.GetRawText().Should().NotContain("private policy detail");
    }

    [Fact]
    public async Task Collect_OperationalFailureDoesNotPersistSuccessShapedErrorEvidence()
    {
        // Arrange
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var system = await db.RegisteredSystems.SingleAsync(s => s.Id == _systemId);
            system.AzureProfile = new AzureEnvironmentProfile { SubscriptionIds = ["sub-test"] };
            db.Assessments.Add(new ComplianceAssessment { Id = "collect-assessment", RegisteredSystemId = _systemId });
            await db.SaveChangesAsync();
        }
        _policy.Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("private infrastructure failure"));

        // Act
        var response = await _client.PostAsync(
            $"/api/dashboard/systems/{_systemId}/controls/AC-1/collect-evidence", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        using var check = _app.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<AtoCopilotContext>().Evidence.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Collect_SuccessPersistsExactAssessmentAndAppearsInCatalog()
    {
        // Arrange
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var system = await db.RegisteredSystems.SingleAsync(s => s.Id == _systemId);
            system.AzureProfile = new AzureEnvironmentProfile { SubscriptionIds = ["sub-test"] };
            db.Assessments.Add(new ComplianceAssessment { Id = "collect-assessment", RegisteredSystemId = _systemId });
            await db.SaveChangesAsync();
        }
        _policy.Setup(p => p.GetComplianceSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"compliant":1}""");

        // Act
        var response = await _client.PostAsync(
            $"/api/dashboard/systems/{_systemId}/controls/AC-1/collect-evidence", null);
        var catalog = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{_systemId}/evidence-catalog?view=system");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        catalog.GetProperty("totalCount").GetInt32().Should().Be(1);
        using var check = _app.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<AtoCopilotContext>().Evidence.SingleAsync())
            .AssessmentId.Should().Be("collect-assessment");
    }

    [Fact]
    public async Task Catalog_SystemDatabaseFailureKeepsProviderRecordsAndUnknownTotals()
    {
        // Arrange
        await using var unavailableDb = new AtoCopilotContext(
            new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite("Data Source=:memory:").Options);
        var share = new ProviderEvidenceShareResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _systemId, 1, null, "Approved summary",
            "source-hash", "summary-hash", "approver", DateTimeOffset.UtcNow, 1, null);
        _provider.Setup(p => p.ListMissionAsync(_systemId, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProviderEvidenceShareResponse>([share], 1, 100, 1));
        var service = new EvidenceCatalogService(unavailableDb, _provider.Object, _storage.Object,
            Mock.Of<ILogger<EvidenceCatalogService>>());

        // Act
        var result = await service.ListAsync(_systemId, new(),
            new(false, "Denied", false, "Denied", false, "Denied"), CancellationToken.None);

        // Assert
        result.Items.Should().ContainSingle(r => r.Source == "Provider");
        result.Sources[0].State.Should().Be("unavailable");
        result.Counts.System.Should().BeNull();
        result.Counts.Provider.Should().Be(1);
        result.TotalCount.Should().BeNull();
        result.AvailableCount.Should().Be(1);
    }

    [Fact]
    public async Task Catalog_CancellationIsNotConvertedIntoPartialSuccess()
    {
        // Arrange
        _provider.Setup(p => p.ListMissionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var scope = _app.Services.CreateScope();
        var service = new EvidenceCatalogService(scope.ServiceProvider.GetRequiredService<AtoCopilotContext>(),
            _provider.Object, _storage.Object, Mock.Of<ILogger<EvidenceCatalogService>>());

        // Act
        var act = () => service.ListAsync(_systemId, new(),
            new(false, "Denied", false, "Denied", false, "Denied"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReplaceSameFilename_RetainsDistinctProtectedVersionAndChecksHash()
    {
        // Arrange
        var upload = await UploadTestEvidence("same.pdf", "application/pdf");
        var uploaded = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var id = uploaded.GetProperty("id").GetString();
        var hash = uploaded.GetProperty("contentHash").GetString();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("new content"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "same.pdf");
        form.Add(new StringContent(hash!), "expectedHash");

        // Act
        var response = await _client.PutAsync($"/api/dashboard/systems/{_systemId}/evidence/{id}", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var artifact = await db.EvidenceArtifacts.SingleAsync(a => a.Id == id);
        var version = await db.EvidenceVersions.SingleAsync(v => v.EvidenceArtifactId == id);
        artifact.StoragePath.Should().NotBe(version.StoragePath);
        version.ContentHash.Should().Be(hash);
        (await _client.GetAsync($"/api/dashboard/systems/{_systemId}/evidence/{id}/versions/{version.Id}/download"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.DeleteAsync($"/api/dashboard/systems/{_systemId}/evidence/{id}?expectedHash={hash}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/download")]
    [InlineData("/versions")]
    public async Task EvidenceReads_RejectOtherSystem(string suffix)
    {
        // Arrange
        var upload = await UploadTestEvidence("scope.pdf", "application/pdf");
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        // Act
        var response = await _client.GetAsync($"/api/dashboard/systems/another-system/evidence/{id}{suffix}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RejectsOtherSystemWithoutChangingArtifact()
    {
        // Arrange
        var upload = await UploadTestEvidence("scope.pdf", "application/pdf");
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        // Act
        var response = await _client.DeleteAsync($"/api/dashboard/systems/another-system/evidence/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/dashboard/systems/{_systemId}/evidence/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Upload_ResolvesControlIdWithinRouteSystem()
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("scoped evidence"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "control.pdf");
        form.Add(new StringContent("AC-1"), "controlId");
        form.Add(new StringContent("Other"), "category");

        // Act
        var response = await _client.PostAsync($"/api/dashboard/systems/{_systemId}/evidence", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

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
