using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.ScanImport;

public sealed class ScanImportEndpointIntegrationTests : IAsyncLifetime
{
    private const string AuthScheme = "TestAuth";
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
        builder.Services.AddAuthentication(AuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(AuthScheme, _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapScanImportEndpoints();
        await _app.StartAsync();

        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add("X-Test-User", "scan-import-user");
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
                [new Claim(ClaimTypes.NameIdentifier, "scan-import-user")],
                AuthScheme));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, AuthScheme)));
        }
    }
}
