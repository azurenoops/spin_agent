using System.Net;
using Ato.Copilot.Mcp.Endpoints;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.ScanImport;

public sealed class ScanImportEndpointIntegrationTests : IAsyncLifetime
{
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

        _app = builder.Build();
        _app.MapScanImportEndpoints();
        await _app.StartAsync();

        _client = _app.GetTestClient();
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

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}
