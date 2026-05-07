using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClosedXML.Excel;
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
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration tests for <see cref="EmassImportEndpoints"/> (T073 / FR-030..FR-038).
/// </summary>
public class EmassImportEndpointsTests : IAsyncLifetime
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

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        var dbName = $"EmassEndpoints_{Guid.NewGuid():N}";
        builder.Services.AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddSingleton(_storage.Object);
        builder.Services.AddSingleton(_audit.Object);
        builder.Services.AddSingleton(_runner.Object);
        builder.Services.Configure<OnboardingOptions>(o =>
        {
            o.Limits.MaxEmassImportBytes = 1024 * 1024; // 1 MB for tests
        });
        builder.Services.AddScoped<IEmassImportService, EmassImportService>();
        builder.Services.AddSingleton<ILogger<EmassImportService>>(NullLogger<EmassImportService>.Instance);

        _runner
            .Setup(r => r.EnqueueAsync(
                It.IsAny<WizardJobType>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<EmassParseJobPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WizardJobType jt, Guid t, Guid u, EmassParseJobPayload _, CancellationToken __) =>
                new WizardJobStatus { Id = Guid.NewGuid(), TenantId = t, JobType = jt, EnqueuedBy = u });
        _runner
            .Setup(r => r.EnqueueAsync(
                It.IsAny<WizardJobType>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<EmassCommitJobPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WizardJobType jt, Guid t, Guid u, EmassCommitJobPayload _, CancellationToken __) =>
                new WizardJobStatus { Id = Guid.NewGuid(), TenantId = t, JobType = jt, EnqueuedBy = u });

        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization(o =>
            o.AddPolicy(OnboardingAdministratorRequirement.PolicyName, p => p.RequireAssertion(_ => true)));

        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapEmassImportEndpoints();

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
    public async Task Upload_HappyPath_Returns202WithJobId()
    {
        var bytes = BuildFixtureBytes();
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "fixture.xlsx");

        var response = await _client.PostAsync("/api/onboarding/imports/emass/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("parseJobId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("data").GetProperty("status").GetString().Should().Be("Parsing");
    }

    [Fact]
    public async Task Upload_OversizedFile_Returns413()
    {
        var bytes = new byte[2 * 1024 * 1024]; // 2 MB > 1 MB limit
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", "huge.xlsx");

        var response = await _client.PostAsync("/api/onboarding/imports/emass/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("errorCode").GetString().Should().Be("WIZARD_EMASS_TOO_LARGE");
    }

    [Fact]
    public async Task Upload_UnsupportedExtension_Returns415()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "wrong.txt");

        var response = await _client.PostAsync("/api/onboarding/imports/emass/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("errorCode").GetString().Should().Be("WIZARD_EMASS_INVALID_FORMAT");
    }

    [Fact]
    public async Task Preview_Returns200_WhenSessionIsParsed()
    {
        var sessionId = await SeedParsedSessionAsync();
        var response = await _client.GetAsync($"/api/onboarding/imports/emass/{sessionId}/preview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("systems").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Commit_Returns202_WhenSessionIsParsed()
    {
        var sessionId = await SeedParsedSessionAsync();
        var response = await _client.PostAsJsonAsync(
            $"/api/onboarding/imports/emass/{sessionId}/commit",
            new
            {
                instructions = new[]
                {
                    new { systemIdentifier = "S1", decision = "Merge" },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("commitJobId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Log_Returns200_WhenCommitJobHasResult()
    {
        var sessionId = Guid.NewGuid();
        var commitJobId = Guid.NewGuid();
        var entries = new[] { new EmassImportLogEntry("S1", "System One", "Merged", "rs-1", null) };

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
            await using var db = await factory.CreateDbContextAsync();
            db.EmassImportSessions.Add(new EmassImportSession
            {
                Id = sessionId,
                TenantId = AdminTenantId,
                OriginalFileName = "x.xlsx",
                StorageBlobKey = "key",
                ContentChecksumSha256 = "sha",
                Status = EmassImportStatus.Imported,
                CommitJobId = commitJobId,
            });
            db.WizardJobStatuses.Add(new WizardJobStatus
            {
                Id = commitJobId,
                TenantId = AdminTenantId,
                JobType = WizardJobType.EmassCommit,
                Status = WizardJobState.Succeeded,
                Result = JsonSerializer.Serialize(entries),
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/onboarding/imports/emass/{sessionId}/log");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("entries").GetArrayLength().Should().Be(1);
    }

    private async Task<Guid> SeedParsedSessionAsync()
    {
        var sessionId = Guid.NewGuid();
        var preview = new EmassParseResult(
            new[] { new EmassParsedSystem("S1", "System One", 10, 0, null) },
            "Xlsx");
        await using var scope = _app.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.EmassImportSessions.Add(new EmassImportSession
        {
            Id = sessionId,
            TenantId = AdminTenantId,
            OriginalFileName = "x.xlsx",
            StorageBlobKey = "key",
            ContentChecksumSha256 = "sha",
            Status = EmassImportStatus.Parsed,
            Preview = JsonSerializer.Serialize(preview),
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static byte[] BuildFixtureBytes()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Systems");
        ws.Cell(1, 1).Value = "system_identifier";
        ws.Cell(1, 2).Value = "system_name";
        ws.Cell(2, 1).Value = "S1";
        ws.Cell(2, 2).Value = "System One";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
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
