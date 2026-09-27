using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ProviderEvidenceDocumentTests
{
    [Fact]
    public async Task GeneratedOscal_EmbedsOnlyApprovedSummaryBytes_AndPinsTheirContentHash()
    {
        // Arrange
        const string content = "{\"summary\":\"DEMO approved customer-visible evidence summary\"}";
        var hash = ProviderAuthorizationStore.Hash(content);
        var share = new ProviderEvidenceShareResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "mission", 2, Guid.NewGuid(), "DEMO approved customer-visible evidence summary",
            new string('A', 64), hash, "reviewer", DateTimeOffset.UtcNow, 1, null);
        var service = new Mock<IProviderEvidenceSharingService>();
        var person = Guid.NewGuid();
        var accessor = new TenantContextAccessor();
        using var tenantScope = accessor.Push(new TenantContext(share.TargetTenantId) { PersonId = person });
        service.Setup(s => s.ListMissionAsync("mission", 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ProviderEvidenceShareResponse>([share], 1, 100, 1));
        service.Setup(s => s.SummaryContentAsync("mission", share.ShareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(content));
        service.Setup(s => s.VerifyForExportAsync(share.TargetTenantId, person, "mission", share.ShareId, hash, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var dbName = Guid.NewGuid().ToString();
        using var services = new ServiceCollection().AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName))
            .AddSingleton<ITenantContextAccessor>(accessor).AddSingleton(service.Object).BuildServiceProvider();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Add(new RegisteredSystem { Id = "mission", Name = "DEMO mission", TenantId = share.TargetTenantId });
        await db.SaveChangesAsync();

        // Act
        using var output = new ExportDirectory();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        var schema = new Mock<IOscalSchemaValidationService>();
        schema.Setup(s => s.ValidateAsync(It.IsAny<string>(), "ssp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalSchemaValidationResult { IsValid = true });
        var channel = Channel.CreateUnbounded<SspExportJob>();
        var responsibilities = new Mock<ICapabilityResponsibilityService>();
        responsibilities.Setup(s => s.PreviewAsync("mission", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityResponsibilityResponse("mission", "baseline-v1", false,
            [
                new("subscription", Guid.NewGuid(), null, null, "AU-2", "source-v1", "review-v1", "Shared",
                    "source-v1", "issm", DateTimeOffset.UtcNow,
                    new("AU-2", "Shared", "DEMO provider", "Review events and retain customer evidence"),
                    "Shared", "CspSubscription", true, "PRIVATE RAW SOURCE MUST NOT LEAK", null)
            ], []));
        var exporter = new SspExportService(factory, Mock.Of<ISspService>(), Mock.Of<IDocumentTemplateService>(),
            new OscalSspExportService(factory, NullLogger<OscalSspExportService>.Instance),
            schema.Object, Mock.Of<ISspExportNotifier>(),
            NullLogger<SspExportService>.Instance, Options.Create(new ExportSettings { DataPath = output.Path }),
            channel, service.Object, responsibilities.Object);
        var result = await exporter.CreatePreviewAsync("mission", "reviewer");
        var export = await exporter.EnqueueFromPreviewAsync("mission", result.PreviewId!.Value, "reviewer");
        channel.Reader.TryRead(out var job).Should().BeTrue();
        await exporter.ProcessExportAsync(job!);

        // Assert
        using var document = JsonDocument.Parse(result.Content);
        var resources = document.RootElement.GetProperty("system-security-plan").GetProperty("back-matter").GetProperty("resources");
        var resource = resources.EnumerateArray().Single(r => r.GetProperty("uuid").GetString() == share.ShareId.ToString());
        resource.GetProperty("description").GetString().Should().Be(share.Summary);
        var encoded = resource.GetProperty("base64").GetProperty("value").GetString()!;
        Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Should().Be(content);
        result.Content.Should().NotContain("storageKey").And.NotContain("private/source");
        result.Content.Should().Contain("Review events and retain customer evidence").And.NotContain("PRIVATE RAW SOURCE MUST NOT LEAK");
        result.SourceManifest!.Responsibilities.Single().ReviewedSourceRevision.Should().Be("source-v1");
        result.SourceManifest!.Evidence.Single().ShareId.Should().Be(share.ShareId);
        result.SourceManifest.Evidence.Single().ContentHash.Should().Be(hash);
        service.Verify(s => s.VerifyForExportAsync(share.TargetTenantId, person, "mission", share.ShareId, hash, It.IsAny<CancellationToken>()), Times.Once);
        (await exporter.GetExportAsync(export.Id))!.Status.Should().Be("Completed");
        var stored = await db.SspExports.AsNoTracking().SingleAsync(e => e.Id == export.Id);
        var filePath = Path.Combine(output.Path, "exports", stored.FilePath!);
        File.Exists(filePath).Should().BeTrue();
        var pending = await exporter.EnqueueFromPreviewAsync("mission", result.PreviewId!.Value, "reviewer");
        channel.Reader.TryRead(out var revokedJob).Should().BeTrue();
        service.Setup(s => s.VerifyForExportAsync(share.TargetTenantId, person, "mission", share.ShareId, hash, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Requester lost evidence access after queueing."));
        await exporter.ProcessExportAsync(revokedJob!);
        (await exporter.GetExportAsync(pending.Id))!.Status.Should().Be("Failed");
        (await db.SspExports.AsNoTracking().SingleAsync(e => e.Id == pending.Id)).FilePath.Should().BeNull();
        service.Setup(s => s.SummaryContentAsync("mission", share.ShareId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Revoked sharing grant."));
        var download = () => exporter.GetExportFileStreamAsync(export.Id);
        await download.Should().ThrowAsync<KeyNotFoundException>();
        File.Exists(filePath).Should().BeTrue("revocation denies current access without deleting retained historical output");
        (await exporter.GetExportAsync(export.Id))!.Status.Should().Be("Completed");
    }

    private sealed class ExportDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(Directory.GetCurrentDirectory(), $"evidence-document-tests-{Guid.NewGuid():N}");
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
