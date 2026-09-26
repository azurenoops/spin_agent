using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using Ato.Copilot.Mcp.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class CspPackageImportContractTests : IClassFixture<PackageImportFactory>
{
    private readonly PackageImportFactory _factory;
    private readonly HttpClient _client;

    public CspPackageImportContractTests(PackageImportFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.GetActiveContext().IsCspAdmin = true;
        factory.GetActiveContext().ImpersonatedTenantId = null;
    }

    [Fact]
    public async Task OnboardingReceipt_CanBePolledBeforeActivation_AndSubmitLeavesDraftsUnpublished()
    {
        // Arrange
        await _factory.ResetCspProfileAsync();
        using var upload = Upload("/api/csp/onboarding/atos/upload", Guid.NewGuid().ToString(), "Onboarding source", true);
        var receipt = await _client.SendAsync(upload);
        receipt.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var location = receipt.Headers.Location!;
        var packageId = (await receipt.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("packageId").GetGuid();
        // Act
        var status = await _client.GetAsync(location);
        // Assert
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        var review = await _client.GetAsync(location + "/review-state");
        review.StatusCode.Should().Be(HttpStatusCode.OK);
        var recovered = (await review.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        recovered.GetProperty("packageId").GetGuid().Should().Be(packageId);
        recovered.GetProperty("preview").ValueKind.Should().Be(JsonValueKind.Null);
        recovered.GetProperty("publication").ValueKind.Should().Be(JsonValueKind.Null);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var entries = await _client.GetAsync(location + "/entries");
        entries.StatusCode.Should().Be(HttpStatusCode.OK);
        var original = await db.CspPackageEntries.SingleAsync(x => x.PackageId == packageId);
        var source = await _client.GetAsync(location + $"/artifacts/{original.Id:D}/content");
        source.StatusCode.Should().Be(HttpStatusCode.OK);
        (await source.Content.ReadAsStringAsync()).Should().Be("Onboarding source");
        var package = await db.CspPackages.SingleAsync(x => x.Id == packageId);
        package.ProcessingState = "Failed";
        package.LastError = "Synthetic interrupted analysis";
        package.Version++;
        await db.SaveChangesAsync();
        using var retry = new HttpRequestMessage(HttpMethod.Post, location + "/retry");
        retry.Headers.Add("Idempotency-Key", "inactive-provider-retry");
        (await _client.SendAsync(retry)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var profile = await db.CspProfiles.SingleAsync();
        profile.LegalEntityName = "Synthetic provider";
        profile.DisplayName = "Synthetic";
        profile.PrimarySupportEmail = "synthetic@example.test";
        profile.IdentityCompletedAt = DateTimeOffset.UtcNow;
        profile.SupportCompletedAt = DateTimeOffset.UtcNow;
        profile.ClassificationCompletedAt = DateTimeOffset.UtcNow;
        var draft = new CspInheritedComponent { CspProfileId = profile.Id, Name = "Unrelated onboarding draft" };
        db.CspInheritedComponents.Add(draft);
        await db.SaveChangesAsync();
        var submit = await _client.PostAsJsonAsync("/api/csp/onboarding/submit", new { });
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        await db.Entry(draft).ReloadAsync();
        draft.Status.Should().Be(CspInheritedComponentStatus.Draft);
    }

    [Fact]
    public async Task AsyncIngress_RetainsBytesBeforeReceipt_AndDoesNotPublishUnrelatedDraft()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var profile = await db.CspProfiles.SingleAsync();
        var unrelated = new CspInheritedComponent { CspProfileId = profile.Id, Name = "Unrelated draft", Description = "Synthetic" };
        db.CspInheritedComponents.Add(unrelated);
        await db.SaveChangesAsync();
        using var request = Upload("/api/csp/inherited-components/import", Guid.NewGuid().ToString(), "Synthetic source", true);
        // Act
        var response = await _client.SendAsync(request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
        var receipt = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        receipt.GetProperty("processingState").GetString().Should().Be("Received");
        var id = receipt.GetProperty("packageId").GetGuid();
        var entry = await db.CspPackageEntries.SingleAsync(x => x.PackageId == id);
        _factory.Files[entry.StorageKey!].Should().Equal(Encoding.UTF8.GetBytes("Synthetic source"));
        await db.Entry(unrelated).ReloadAsync();
        unrelated.Status.Should().Be(CspInheritedComponentStatus.Draft);
        var bytes = await _client.GetAsync($"/api/csp/package-imports/{id}/artifacts/{entry.Id}/content");
        bytes.StatusCode.Should().Be(HttpStatusCode.OK);
        bytes.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        bytes.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await bytes.Content.ReadAsStringAsync()).Should().Be("Synthetic source");
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    public async Task EveryPrivateReadAndMutation_RejectsNonProviderContext(
        bool admin, bool impersonated, bool inactive, bool authenticated)
    {
        // Arrange
        if (inactive) await _factory.ResetCspProfileAsync();
        else await _factory.EnsureActiveCspProfileAsync();
        if (!authenticated) _client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        _factory.GetActiveContext().IsCspAdmin = admin;
        _factory.GetActiveContext().ImpersonatedTenantId = impersonated ? MultiTenantWebApplicationFactory<McpProgram>.TenantBId : null;
        var id = Guid.NewGuid();
        // Act
        var paths = new[] { "", $"/{id}", $"/{id}/review-state", $"/{id}/entries", $"/{id}/candidates", $"/{id}/artifacts/{id}/content" };
        var responses = new List<HttpResponseMessage>();
        foreach (var path in paths) responses.Add(await _client.GetAsync("/api/csp/package-imports" + path));
        responses.Add(await _client.GetAsync($"/api/csp/package-imports/{id}/candidates?type=AuthorizationReference&reviewState=Reviewed"));
        responses.Add(await _client.PatchAsJsonAsync($"/api/csp/package-imports/{id}/candidates/{id}",
            new EditPackageCandidateRequest(1, "Synthetic reference", "Source-stated only", "Service",
                "", "", new Dictionary<string, string>(), [], "Rejected", "Synthetic rejection", null,
                new PackageAuthorizationReference("Synthetic reference", null, null, null))));
        responses.Add(await _client.PostAsJsonAsync($"/api/csp/package-imports/{id}/retry", new { }));
        // Assert
        var expected = authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        responses.Should().OnlyContain(x => x.StatusCode == expected);
    }

    [Fact]
    public async Task StableUploadKey_ConflictingBytesReturn409()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        var key = Guid.NewGuid().ToString();
        using var first = Upload("/api/csp/inherited-components/import", key, "one", true);
        (await _client.SendAsync(first)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var second = Upload("/api/csp/inherited-components/import", key, "two", true);
        // Act
        var response = await _client.SendAsync(second);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        error.GetProperty("errorCode").GetString().Should().Be("PACKAGE_CONFLICT");
        error.GetProperty("suggestion").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SourceStorageFailure_ReturnsStructured503_WithoutAcknowledgingReceipt()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        var storage = new Mock<IFileStorageProvider>();
        storage.Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Synthetic unavailable source storage"));
        using var failingHost = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton(storage.Object)));
        using var client = failingHost.CreateClient();
        var key = Guid.NewGuid().ToString();
        using var request = Upload("/api/csp/inherited-components/import", key, "Not acknowledged", true);
        // Act
        var response = await client.SendAsync(request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("errorCode")
            .GetString().Should().Be("PACKAGE_STORAGE_UNAVAILABLE");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.CspPackages.AnyAsync(x => x.IdempotencyKey == key)).Should().BeFalse();
    }

    [Fact]
    public async Task LegacyIngress_RetainsTallyEnvelope_WithoutPublishing()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        using var request = Upload("/api/csp/inherited-components/import", Guid.NewGuid().ToString(), "source", false);
        // Act
        var response = await _client.SendAsync(request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        data.GetProperty("documentsAccepted").GetInt32().Should().Be(1);
        data.GetProperty("componentsExtracted").GetInt32().Should().Be(0);
        data.GetProperty("capabilitiesMapped").GetInt32().Should().Be(0);
        data.GetProperty("files").GetArrayLength().Should().Be(1);
    }

    private static HttpRequestMessage Upload(string url, string key, string contents, bool async)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(contents));
        file.Headers.ContentType = new("text/plain");
        form.Add(file, "files", "synthetic.txt");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Add("Idempotency-Key", key);
        if (async) request.Headers.Add("Prefer", "respond-async");
        return request;
    }
}

public sealed class PackageImportFactory : MultiTenantWebApplicationFactory<McpProgram>
{
    public Dictionary<string, byte[]> Files { get; } = [];
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService)
                && x.ImplementationType == typeof(CspPackageWorker)).ToArray()) services.Remove(descriptor);
            var storage = new Mock<IFileStorageProvider>();
            storage.Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string key, Stream stream, string _, CancellationToken ct) =>
                {
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, ct);
                    Files[key] = buffer.ToArray();
                });
            storage.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken _) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var value) ? new MemoryStream(value) : null));
            services.AddSingleton(storage.Object);
            var analyzer = new Mock<ICspPackageAnalyzer>();
            analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
                {
                    var input = inputs[0];
                    return Task.FromResult(new CspPackageAnalysisResult(
                        [new("root", null, input.ArtifactId, input.FileName, input.MediaType, input.Content.Length,
                            input.Content, CspPackageEntryStatus.Processed, null, null, true)],
                        [], [], new(1, 1, 0, 0, 0, 0, input.Content.Length, 0, true, true)));
                });
            services.AddSingleton(analyzer.Object);
        });
    }
}
