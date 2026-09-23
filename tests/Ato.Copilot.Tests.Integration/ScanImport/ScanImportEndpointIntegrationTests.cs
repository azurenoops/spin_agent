using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.ScanImport;

public sealed class ScanImportEndpointIntegrationTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
    private static readonly Guid TenantId = Guid.Parse("74dfc91c-6a22-4182-8e99-7c2ecc37d010");
    private static readonly Guid DirectoryId = Guid.Parse("74dfc91c-6a22-4182-8e99-7c2ecc37d011");
    private static readonly Guid ActorId = Guid.Parse("74dfc91c-6a22-4182-8e99-7c2ecc37d012");
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private ScanImportQueue _queue = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ScanImportQueue>();
        builder.Services.AddSingleton<ScanImportStatusTracker>();
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContextFactory<AtoCopilotContext>(options => options.UseInMemoryDatabase(database));
        builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
        builder.Services.AddScoped<ISystemWorkspaceAccessService, SystemWorkspaceAccessService>();
        builder.Services.AddSingleton(Mock.Of<ICspProfileService>());
        builder.Services.AddSingleton(Mock.Of<ITenantImpersonationService>());
        builder.Services.AddOptions<RoleClaimMappingsOptions>();
        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapScanImportEndpoints();
        await _app.StartAsync();
        await using (var db = await _app.Services.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContextAsync())
        {
            var person = new Person { TenantId = TenantId, DisplayName = "Scan operator", Email = "scan@example.invalid" };
            db.Tenants.Add(new Tenant { Id = TenantId, DisplayName = "Scan organization" });
            db.Persons.Add(person);
            db.OrganizationMemberships.Add(new OrganizationMembership { TenantId = TenantId, PersonId = person.Id,
                DirectoryTenantId = DirectoryId, ObjectId = ActorId, GrantedBy = "test" });
            db.OrganizationRoleAssignments.Add(new OrganizationRoleAssignment
                { TenantId = TenantId, PersonId = person.Id, Role = OrganizationRole.Isso });
            db.RegisteredSystems.AddRange(new[] { "system-1", "system-a", "system-b" }
                .Select(id => new RegisteredSystem { Id = id, TenantId = TenantId, Name = id }));
            await db.SaveChangesAsync();
        }

        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", "scan-import-user");
        _client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        _client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", TenantId.ToString());
        _client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        _queue = _app.Services.GetRequiredService<ScanImportQueue>();
    }

    [Fact]
    public async Task UploadScanImport_QueuesNessusInBoundedTemporaryStorage()
    {
        // Arrange
        const string NessusXml = "<NessusClientData_v2><Report name=\"test\" /></NessusClientData_v2>";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(NessusXml), "file", "scan.nessus");

        // Act
        using var response = await _client.PostAsync("/api/dashboard/systems/system-1/scans/import", form);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var job = await _queue.Reader.ReadAsync(timeout.Token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        job.ImportType.Should().Be("Nessus");
        job.ImportedBy.Should().Be(ActorId.ToString());
        job.TemporaryFilePath.Should().NotBeNullOrWhiteSpace();
        File.Exists(job.TemporaryFilePath).Should().BeTrue();
        (await File.ReadAllTextAsync(job.TemporaryFilePath, timeout.Token)).Should().Be(NessusXml);

        File.Delete(job.TemporaryFilePath);
    }

    [Fact]
    public async Task UploadScanImport_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        using var anonymousClient = _app.GetTestClient();
        using var form = CreateNessusForm();

        // Act
        using var response = await anonymousClient.PostAsync(
            "/api/dashboard/systems/system-1/scans/import",
            form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "/api/dashboard/systems/system-1/scans/import/import-1/status")]
    [InlineData("DELETE", "/api/dashboard/systems/system-1/scans/import/import-1")]
    public async Task ScanImportStatusAndCancel_WithoutAuthentication_ReturnUnauthorized(
        string method,
        string requestUri)
    {
        // Arrange
        using var anonymousClient = _app.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), requestUri);

        // Act
        using var response = await anonymousClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ScanImportStatusAndCancel_FromDifferentSystem_ReturnNotFound()
    {
        // Arrange
        using var form = CreateNessusForm();
        using var uploadResponse = await _client.PostAsync(
            "/api/dashboard/systems/system-a/scans/import",
            form);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var upload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var importId = upload.GetProperty("importJobId").GetString();
        importId.Should().NotBeNullOrWhiteSpace();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var job = await _queue.Reader.ReadAsync(timeout.Token);

        // Act
        using var statusResponse = await _client.GetAsync(
            $"/api/dashboard/systems/system-b/scans/import/{importId}/status");
        using var cancelResponse = await _client.DeleteAsync(
            $"/api/dashboard/systems/system-b/scans/import/{importId}");

        // Assert
        statusResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var state = _app.Services.GetRequiredService<ScanImportStatusTracker>().TryGet(importId!);
        state.Should().NotBeNull();
        state!.CancelRequested.Should().BeFalse();
        state.Status.Should().Be(ImportJobStatus.Queued);
        File.Delete(job.TemporaryFilePath);
    }

    private static MultipartFormDataContent CreateNessusForm()
    {
        const string NessusXml = "<NessusClientData_v2><Report name=\"test\" /></NessusClientData_v2>";
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(NessusXml), "file", "scan.nessus");
        return form;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
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
            if (!Request.Headers.ContainsKey("X-Test-User"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "scan-import-user"),
                 new Claim("oid", ActorId.ToString()), new Claim("tid", DirectoryId.ToString())],
                AuthScheme));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, AuthScheme)));
        }
    }
}
