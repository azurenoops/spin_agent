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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratedOscal_EmbedsOnlyApprovedSummaryBytes_AndPinsTheirContentHash(bool fromPreview)
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
            .Callback(() =>
            {
                accessor.Current!.EffectiveTenantId.Should().Be(share.TargetTenantId);
                accessor.Current.PersonId.Should().Be(person);
            })
            .ReturnsAsync(new PagedResult<ProviderEvidenceShareResponse>([share], 1, 100, 1));
        service.Setup(s => s.SummaryContentAsync("mission", share.ShareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(content));
        service.Setup(s => s.VerifyForExportAsync(share.TargetTenantId, person, "mission", share.ShareId, hash, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var responsibilities = new Mock<ICapabilityResponsibilityService>();
        var workspaceAccess = new Mock<ISystemWorkspaceAccessService>();
        workspaceAccess.Setup(s => s.CanReadAsync(share.TargetTenantId, person, "mission", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var dbName = Guid.NewGuid().ToString();
        using var services = new ServiceCollection().AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName))
            .AddSingleton<ITenantContextAccessor>(accessor).AddScoped<ITenantContext, TenantContext>()
            .AddSingleton(service.Object).AddSingleton(responsibilities.Object)
            .AddSingleton(workspaceAccess.Object).BuildServiceProvider();
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
        responsibilities.Setup(s => s.PreviewAsync("mission", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityResponsibilityResponse("mission", "baseline-v1", null, false,
            [
                new("subscription", Guid.NewGuid(), null, null, null, "AU-2", "source-v1", "review-v1", "Shared",
                    "source-v1", "issm", DateTimeOffset.UtcNow,
                    new("AU-2", "Shared", "DEMO provider", "Review events and retain customer evidence"),
                    "Shared", "CspSubscription", true, "PRIVATE RAW SOURCE MUST NOT LEAK", null)
            ], []));
        // Exercise retained approved-source evidence exports, not promotion of working profile previews.
        var approvedProducer = new OscalSspExportService(factory, NullLogger<OscalSspExportService>.Instance);
        var legacyApprovedPreview = new Mock<IOscalSspExportService>();
        legacyApprovedPreview.Setup(s => s.PreviewAsync("mission", true, true, It.IsAny<CancellationToken>()))
            .Returns((string system, bool backMatter, bool pretty, CancellationToken ct) =>
                approvedProducer.ExportAsync(system, backMatter, pretty, ct));
        legacyApprovedPreview.Setup(s => s.ExportAsync("mission", true, true, It.IsAny<CancellationToken>()))
            .Returns((string system, bool backMatter, bool pretty, CancellationToken ct) =>
                approvedProducer.ExportAsync(system, backMatter, pretty, ct));
        var exporter = new SspExportService(factory, Mock.Of<ISspService>(), Mock.Of<IDocumentTemplateService>(),
            legacyApprovedPreview.Object,
            schema.Object, Mock.Of<ISspExportNotifier>(),
            NullLogger<SspExportService>.Instance, Options.Create(new ExportSettings { DataPath = output.Path }),
            channel, service.Object, responsibilities.Object);
        var result = await exporter.CreatePreviewAsync("mission", "reviewer");
        var export = fromPreview
            ? await exporter.EnqueueFromPreviewAsync("mission", result.PreviewId!.Value, "reviewer")
            : await exporter.EnqueueExportAsync("mission", "json", null, "reviewer");
        channel.Reader.TryRead(out var job).Should().BeTrue();
        await ProcessWithoutRequestContextAsync(exporter, job!);

        // Assert
        var detail = await exporter.GetExportAsync(export.Id);
        detail!.Status.Should().Be("Completed",
            (await db.SspExports.AsNoTracking().SingleAsync(e => e.Id == export.Id)).ErrorMessage);
        var final = await exporter.GetExportFileStreamAsync(export.Id);
        using var reader = new StreamReader(final!.Value.Stream!);
        var finalContent = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(finalContent);
        var resources = document.RootElement.GetProperty("system-security-plan").GetProperty("back-matter").GetProperty("resources");
        var resource = resources.EnumerateArray().Single(r => r.GetProperty("uuid").GetString() == share.ShareId.ToString());
        resource.GetProperty("description").GetString().Should().Be(share.Summary);
        var encoded = resource.GetProperty("base64").GetProperty("value").GetString()!;
        Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Should().Be(content);
        finalContent.Should().NotContain("storageKey").And.NotContain("private/source");
        finalContent.Should().Contain("Review events and retain customer evidence").And.NotContain("PRIVATE RAW SOURCE MUST NOT LEAK");
        detail.SourceManifest!.HasWorkingProfileSources.Should().BeFalse();
        detail.SourceManifest.Responsibilities.Single().ReviewedSourceRevision.Should().Be("source-v1");
        detail.SourceManifest.Evidence.Single().ShareId.Should().Be(share.ShareId);
        detail.SourceManifest.Evidence.Single().ContentHash.Should().Be(hash);
        service.Verify(s => s.VerifyForExportAsync(share.TargetTenantId, person, "mission", share.ShareId, hash, It.IsAny<CancellationToken>()), Times.Once);
        (await exporter.GetExportAsync(export.Id))!.Status.Should().Be("Completed");
        var stored = await db.SspExports.AsNoTracking().SingleAsync(e => e.Id == export.Id);
        stored.SourceTenantId.Should().Be(share.TargetTenantId);
        stored.RequestedPersonId.Should().Be(person);
        var filePath = Path.Combine(output.Path, "exports", stored.FilePath!);
        File.Exists(filePath).Should().BeTrue();
        var pending = fromPreview
            ? await exporter.EnqueueFromPreviewAsync("mission", result.PreviewId!.Value, "reviewer")
            : await exporter.EnqueueExportAsync("mission", "json", null, "reviewer");
        channel.Reader.TryRead(out var revokedJob).Should().BeTrue();
        service.Setup(s => s.VerifyForExportAsync(share.TargetTenantId, person, "mission", share.ShareId, hash, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Requester lost evidence access after queueing."));
        await ProcessWithoutRequestContextAsync(exporter, revokedJob!);
        (await exporter.GetExportAsync(pending.Id))!.Status.Should().Be("Failed");
        (await db.SspExports.AsNoTracking().SingleAsync(e => e.Id == pending.Id)).FilePath.Should().BeNull();
        service.Setup(s => s.SummaryContentAsync("mission", share.ShareId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Revoked sharing grant."));
        var download = () => exporter.GetExportFileStreamAsync(export.Id);
        await download.Should().ThrowAsync<KeyNotFoundException>();
        File.Exists(filePath).Should().BeTrue("revocation denies current access without deleting retained historical output");
        (await exporter.GetExportAsync(export.Id))!.Status.Should().Be("Completed");
        if (!fromPreview)
        {
            service.Setup(s => s.VerifyForExportAsync(share.TargetTenantId, person, "mission", share.ShareId, hash, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var unavailable = await exporter.EnqueueExportAsync("mission", "json", null, "reviewer");
            channel.Reader.TryRead(out var unavailableJob).Should().BeTrue();
            await ProcessWithoutRequestContextAsync(exporter, unavailableJob!);
            var failure = await exporter.GetExportAsync(unavailable.Id);
            failure!.Status.Should().Be("Failed");
            var unavailableRecord = await db.SspExports.AsNoTracking().SingleAsync(e => e.Id == unavailable.Id);
            unavailableRecord.ErrorMessage.Should().Contain("Approved provider evidence summaries could not be verified");
            unavailableRecord.FilePath.Should().BeNull();

            service.Setup(s => s.SummaryContentAsync("mission", share.ShareId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Encoding.UTF8.GetBytes(content));
            foreach (var deniedContext in new[]
            {
                new TenantContext(),
                new TenantContext(share.TargetTenantId, isCspAdmin: true) { PersonId = person },
                new TenantContext(Guid.NewGuid(), impersonatedTenantId: share.TargetTenantId) { PersonId = person }
            })
            {
                using var deniedScope = accessor.Push(deniedContext);
                var noIdentity = await exporter.EnqueueExportAsync("mission", "json", null, "reviewer");
                channel.Reader.TryRead(out var noIdentityJob).Should().BeTrue();
                await ProcessWithoutRequestContextAsync(exporter, noIdentityJob!);
                var identityFailure = await exporter.GetExportAsync(noIdentity.Id);
                identityFailure!.Status.Should().Be("Failed");
                var noIdentityRecord = await db.SspExports.AsNoTracking().SingleAsync(e => e.Id == noIdentity.Id);
                noIdentityRecord.ErrorMessage.Should().Contain("captured mission tenant and requester");
                noIdentityRecord.FilePath.Should().BeNull();
            }
        }
    }

    private static async Task ProcessWithoutRequestContextAsync(SspExportService exporter, SspExportJob job)
    {
        Task worker;
        using (ExecutionContext.SuppressFlow())
            worker = Task.Run(() => exporter.ProcessExportAsync(job));
        await worker;
    }

    private sealed class ExportDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(Directory.GetCurrentDirectory(), $"evidence-document-tests-{Guid.NewGuid():N}");
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
