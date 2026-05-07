using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.SspPdf;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration tests for SSP-PDF batch endpoints (T085 / FR-040..FR-046).
/// </summary>
public class SspPdfImportEndpointsTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid AdminTenantId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly Mock<IFileStorageProvider> _storage = new();
    private readonly Mock<IWizardJobRunner> _runner = new();
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly Mock<IWizardArtifactDependencyService> _deps = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var dbName = $"SspPdfEndpoints_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddSingleton(_storage.Object);
        builder.Services.AddSingleton(_audit.Object);
        builder.Services.AddSingleton(_runner.Object);
        builder.Services.AddSingleton(_deps.Object);
        builder.Services.Configure<OnboardingOptions>(o =>
        {
            o.Limits.MaxSspPdfImportBytes = 1024 * 1024; // 1 MB
            o.Limits.MaxSspPdfBatchSize = 3;
        });
        builder.Services.AddScoped<ISspPdfImportService, SspPdfImportService>();
        builder.Services.AddSingleton<ILogger<SspPdfImportService>>(NullLogger<SspPdfImportService>.Instance);

        _runner
            .Setup(r => r.EnqueueAsync(
                It.IsAny<WizardJobType>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<SspPdfExtractJobPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WizardJobType jt, Guid t, Guid u, SspPdfExtractJobPayload _, CancellationToken __) =>
                new WizardJobStatus { Id = Guid.NewGuid(), TenantId = t, JobType = jt, EnqueuedBy = u });

        _deps
            .Setup(d => d.LinkAsync(
                It.IsAny<Guid>(), It.IsAny<ArtifactSourceKind>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<ArtifactDependentKind>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WizardArtifactDependency());

        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization(o =>
            o.AddPolicy(OnboardingAdministratorRequirement.PolicyName, p => p.RequireAssertion(_ => true)));

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapSspPdfImportEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", $"{AdminTenantId}|{AdminUserId}");
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task Upload_HappyPath_Returns202WithPerPdfJobs()
    {
        using var content = BuildBatch(("a.pdf", new byte[] { 1, 2, 3 }), ("b.pdf", new byte[] { 4, 5, 6 }));

        var response = await _client.PostAsync("/api/onboarding/imports/ssp-pdf/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("batchId").GetGuid().Should().NotBeEmpty();
        var sessions = body.GetProperty("data").GetProperty("sessions");
        sessions.GetArrayLength().Should().Be(2);
        sessions.EnumerateArray().Should().AllSatisfy(s =>
            s.GetProperty("extractJobId").GetGuid().Should().NotBeEmpty());
    }

    [Fact]
    public async Task Upload_BatchTooLarge_Returns422()
    {
        using var content = BuildBatch(
            ("1.pdf", new byte[] { 1 }),
            ("2.pdf", new byte[] { 2 }),
            ("3.pdf", new byte[] { 3 }),
            ("4.pdf", new byte[] { 4 }));

        var response = await _client.PostAsync("/api/onboarding/imports/ssp-pdf/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("errorCode").GetString().Should().Be("WIZARD_SSP_PDF_UNREADABLE");
    }

    [Fact]
    public async Task Upload_OversizeFile_Returns413()
    {
        using var content = BuildBatch(("huge.pdf", new byte[2 * 1024 * 1024]));

        var response = await _client.PostAsync("/api/onboarding/imports/ssp-pdf/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Upload_NonPdfExtension_Returns415()
    {
        using var content = BuildBatch(("doc.txt", new byte[] { 1 }));

        var response = await _client.PostAsync("/api/onboarding/imports/ssp-pdf/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Summary_ReturnsBatchSessions()
    {
        var batchId = Guid.NewGuid();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.SspPdfImportSessions.Add(new SspPdfImportSession
            {
                Id = Guid.NewGuid(),
                TenantId = AdminTenantId,
                BatchId = batchId,
                OriginalFileName = "x.pdf",
                StorageBlobKey = "k",
                ContentChecksumSha256 = "sha",
                Status = SspPdfStatus.Extracted,
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/onboarding/imports/ssp-pdf/batches/{batchId}/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Corrections_PutPersists()
    {
        var sessionId = await SeedExtractedSessionAsync();
        var response = await _client.PutAsJsonAsync(
            $"/api/onboarding/imports/ssp-pdf/{sessionId}/corrections",
            new
            {
                corrections = new[]
                {
                    new { fieldName = "system_name", value = "Override Name" },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Import_Returns201WithSystemId()
    {
        var sessionId = await SeedExtractedSessionAsync();

        var response = await _client.PostAsync(
            $"/api/onboarding/imports/ssp-pdf/{sessionId}/import", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("systemId").GetGuid().Should().NotBeEmpty();
    }

    private async Task<Guid> SeedExtractedSessionAsync()
    {
        var sessionId = Guid.NewGuid();
        var extraction = new SspPdfExtractionResult(
            IsAccepted: true,
            RejectReason: null,
            RejectMessage: null,
            Fields: new List<SspPdfField>
            {
                new("system_identifier", "ID-1", SspPdfFieldConfidence.High, null),
                new("system_name", "Original", SspPdfFieldConfidence.Medium, null),
            },
            PageCount: 5);

        await using var scope = _app.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.SspPdfImportSessions.Add(new SspPdfImportSession
        {
            Id = sessionId,
            TenantId = AdminTenantId,
            BatchId = Guid.NewGuid(),
            OriginalFileName = "x.pdf",
            StorageBlobKey = "k",
            ContentChecksumSha256 = "sha",
            Status = SspPdfStatus.Extracted,
            ExtractionResult = JsonSerializer.Serialize(extraction),
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static MultipartFormDataContent BuildBatch(params (string fileName, byte[] bytes)[] files)
    {
        var content = new MultipartFormDataContent();
        foreach (var (fileName, bytes) in files)
        {
            var c = new ByteArrayContent(bytes);
            c.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(c, "files", fileName);
        }
        return content;
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var parts = header.ToString().Split('|', 2);
            var claims = new List<Claim>();
            if (!string.IsNullOrEmpty(parts[0])) claims.Add(new Claim("tid", parts[0]));
            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1])) claims.Add(new Claim("oid", parts[1]));
            claims.Add(new Claim(ClaimTypes.Name, "test-user"));
            var identity = new ClaimsIdentity(claims, AuthScheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), AuthScheme)));
        }
    }
}
