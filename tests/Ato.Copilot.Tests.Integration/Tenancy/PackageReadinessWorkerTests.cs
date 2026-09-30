using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class PackageReadinessWorkerTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, true)]
    public async Task Worker_ValidatesEmittedBytes_AndCannotCompleteAfterSchemaFailure(
        bool validBytes, bool sourceDrift, bool missingEvidence, bool realSchemas)
    {
        // Arrange
        using var owner = new MultiTenantWebApplicationFactory<McpProgram>();
        using var output = new TestExportDirectory();
        owner.ResetLegacyTenantContext(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
        var schema = new Mock<IOscalSchemaValidationService>();
        schema.Setup(x => x.ValidateForSystemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalSchemaValidationResult { IsValid = true });
        schema.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalSchemaValidationResult { IsValid = validBytes, Violations = validBytes ? [] : [new() { Message = "Synthetic emitted-byte violation" }] });
        var export = new Mock<IEmassExportService>();
        Func<Task>? mutateSource = null;
        export.Setup(x => x.ExportOscalAsync(It.IsAny<string>(), It.IsAny<OscalModelType>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, OscalModelType model, CancellationToken _) =>
            {
                if (model == OscalModelType.Ssp && mutateSource != null) await mutateSource();
                return "{\"synthetic-source\":\"retained bytes\"}";
            });
        var sap = new Mock<IOscalSapExportService>();
        sap.Setup(x => x.ExportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("{\"synthetic-plan\":true}");
        if (realSchemas)
        {
            var resources = typeof(OscalSchemaValidationService).Assembly.GetManifestResourceNames();
            foreach (var model in new[] { "ssp", "poam", "assessment-results", "assessment-plan" })
                resources.Should().Contain(x => x.EndsWith($"oscal_{model}_schema.json", StringComparison.Ordinal));
            var realValidator = new OscalSchemaValidationService(export.Object, sap.Object, NullLogger<OscalSchemaValidationService>.Instance);
            schema.Setup(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string json, string model, CancellationToken ct) => realValidator.ValidateAsync(json, model, ct));
        }
        var sar = new Mock<ISecurityAssessmentReportService>();
        sar.Setup(x => x.ExportToWordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var stream = new MemoryStream();
                using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    using var text = new StreamWriter(archive.CreateEntry("word/document.xml").Open());
                    text.Write("<document/>");
                }
                stream.Position = 0;
                return stream;
            });
        using var app = owner.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<ExportSettings>(settings => settings.DataPath = output.Path);
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
            services.RemoveAll<IOscalSchemaValidationService>(); services.AddSingleton(schema.Object);
            services.RemoveAll<IEmassExportService>(); services.AddSingleton(export.Object);
            services.RemoveAll<IOscalSapExportService>(); services.AddSingleton(sap.Object);
            services.RemoveAll<ISecurityAssessmentReportService>(); services.AddSingleton(sar.Object);
        }));
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        using var tenantScope = app.Services.GetRequiredService<Ato.Copilot.Core.Interfaces.Tenancy.ITenantContextAccessor>()
            .Push(owner.GetActiveContext());
        string systemId, packageId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic byte test", CreatedBy = "test" };
            db.RegisteredSystems.Add(system); systemId = system.Id;
            db.AuthorizationBoundaryDefinitions.Add(new() { TenantId = tenant, RegisteredSystemId = systemId, Name = "Synthetic boundary", CreatedBy = "test" });
            db.PrivacyThresholdAnalyses.Add(new() { TenantId = tenant, RegisteredSystemId = systemId, Determination = PtaDetermination.PiaNotRequired });
            db.SecurityAssessmentPlans.Add(new() { TenantId = tenant, RegisteredSystemId = systemId, Title = "SAP", Status = SapStatus.Finalized, BaselineLevel = "Low" });
            foreach (var number in Enumerable.Range(1, 13))
                db.SspSections.Add(new() { TenantId = tenant, RegisteredSystemId = systemId, SectionNumber = number,
                    SectionTitle = $"Synthetic section {number}", Status = SspSectionStatus.Approved });
            db.SecurityAssessmentReports.Add(new() { TenantId = tenant, RegisteredSystemId = systemId, Title = "SAR", Status = SarStatus.Approved, CreatedBy = "test" });
            if (missingEvidence)
            {
                var implementation = new ControlImplementation { TenantId = tenant, RegisteredSystemId = systemId, ControlId = "AC-1" };
                db.ControlImplementations.Add(implementation);
                db.EvidenceArtifacts.Add(new()
                {
                    TenantId = tenant, RegisteredSystemId = systemId, ControlImplementationId = implementation.Id,
                    FileName = "synthetic-missing.txt", ContentType = "text/plain", ContentHash = new string('a', 64),
                    StoragePath = $"synthetic-unavailable/{Guid.NewGuid()}", UploadedBy = "test"
                });
            }
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IInventoryService>().AddItemAsync(systemId, new()
            {
                ItemName = "Synthetic package software", Type = InventoryItemType.Software,
                SoftwareFunction = SoftwareFunction.Application, Vendor = "Synthetic vendor", Version = "1.0"
            }, "test");
            var evaluation = await scope.ServiceProvider.GetRequiredService<PackageReadinessService>()
                .ValidateAsync(systemId, new(PackagePurpose.InitialSubmission), "test", default);
            evaluation.Outcome.Should().Be(missingEvidence ? "Blocked" : "Ready", evaluation.FailureJson);
            schema.Invocations.Clear();
            var source = await scope.ServiceProvider.GetRequiredService<PackageReadinessService>()
                .ReadSourceAsync(systemId, new(PackagePurpose.InitialSubmission), "test", default);
            source.State.Should().Be("Available");
            var run = missingEvidence ? new PackageReadinessRun
            {
                TenantId = tenant, RegisteredSystemId = systemId, Purpose = PackagePurpose.InitialSubmission,
                SelectionHash = PackageReadinessService.SelectionHash(new(PackagePurpose.InitialSubmission)),
                SourceHash = source.Hash, SourceHashAfter = source.Hash, Outcome = "Ready", RuleVersion = PackageReadinessService.RuleVersion,
                StartedAt = DateTime.UtcNow, EvaluatedAt = DateTime.UtcNow, EvaluatedBy = "test",
                ChecksJson = JsonSerializer.Serialize(new[] { new PackageReadinessCheck("synthetic", "synthetic", "Synthetic evaluator", "Passed",
                    "fixture", true, "Applicable", "Synthetic worker fixture only", null, [], [], null, null, new(false, false, null, null, "Fixture")) },
                    PackageReadinessService.Json)
            } : evaluation;
            if (missingEvidence) db.PackageReadinessRuns.Add(run);
            var package = new AuthorizationPackage
            {
                TenantId = tenant, RegisteredSystemId = systemId, Purpose = PackagePurpose.InitialSubmission,
                ReadinessRunId = run.Id, ReadinessSourceHash = run.SourceHash, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), GeneratedBy = "test"
            };
            db.AuthorizationPackages.Add(package); packageId = package.Id;
            await db.SaveChangesAsync();
        }
        if (sourceDrift)
            mutateSource = async () =>
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                (await db.RegisteredSystems.SingleAsync(x => x.Id == systemId)).Name = "Changed during artifact emission";
                await db.SaveChangesAsync();
            };
        var worker = new PackageBackgroundService(Channel.CreateUnbounded<PackageExportJob>(),
            app.Services.GetRequiredService<IServiceScopeFactory>(), Mock.Of<IPackageExportNotifier>(),
            NullLogger<PackageBackgroundService>.Instance);

        // Act
        var process = typeof(PackageBackgroundService).GetMethod("ProcessJobAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)process.Invoke(worker, [new PackageExportJob(packageId, systemId, EvidenceMode.ManifestOnly, "test", PackagePurpose.InitialSubmission, tenant), CancellationToken.None])!;

        // Assert
        await using var verify = app.Services.CreateAsyncScope();
        var saved = await verify.ServiceProvider.GetRequiredService<AtoCopilotContext>().AuthorizationPackages.SingleAsync(x => x.Id == packageId);
        var complete = validBytes && !sourceDrift && !missingEvidence;
        saved.Status.Should().Be(complete ? PackageStatus.Completed : PackageStatus.Failed);
        schema.Verify(x => x.ValidateAsync("{\"synthetic-source\":\"retained bytes\"}", "ssp", It.IsAny<CancellationToken>()),
            missingEvidence ? Times.Never() : Times.Once());
        schema.Verify(x => x.ValidateForSystemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        if (complete)
        {
            var artifacts = await verify.ServiceProvider.GetRequiredService<AtoCopilotContext>().PackageArtifacts
                .Where(x => x.AuthorizationPackageId == packageId).ToListAsync();
            artifacts.Should().HaveCount(7).And.OnlyContain(x => x.TenantId == tenant);
            var inventoryArtifact = artifacts.Single(x => x.FileName == "hardware-software-inventory.xlsx");
            inventoryArtifact.Format.Should().Be("xlsx");
            inventoryArtifact.ContentHash.Should().NotBeNullOrWhiteSpace();
            artifacts.Where(x => x.ArtifactType is PackageArtifactType.OscalSsp or PackageArtifactType.OscalPoam
                or PackageArtifactType.OscalAssessmentPlan or PackageArtifactType.OscalAssessmentResults)
                .Should().OnlyContain(x => x.SchemaValid == true && x.ContentHash != null);
            using var archive = System.IO.Compression.ZipFile.OpenRead(saved.FilePath!);
            using var inventoryContent = new MemoryStream();
            await archive.GetEntry("hardware-software-inventory.xlsx")!.Open().CopyToAsync(inventoryContent);
            inventoryContent.Position = 0;
            using var workbook = new ClosedXML.Excel.XLWorkbook(inventoryContent);
            workbook.Worksheet("Software").Cell(2, 2).GetString().Should().Be("Synthetic package software");
            using var json = JsonDocument.Parse(archive.GetEntry("package-metadata.json")!.Open());
            json.RootElement.GetProperty("readiness").GetProperty("runId").GetString().Should().Be(saved.ReadinessRunId);
            json.RootElement.GetProperty("readiness").GetProperty("sourceHash").GetString().Should().Be(saved.ReadinessSourceHash);
            using var text = new StreamReader(archive.GetEntry("oscal-ssp.json")!.Open());
            (await text.ReadToEndAsync()).Should().Be("{\"synthetic-source\":\"retained bytes\"}");
        }
        else
        {
            saved.FilePath.Should().BeNull();
            saved.ValidationPassed.Should().BeFalse();
        }
    }

    private sealed class TestExportDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("readiness-worker-");
        public string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(recursive: true);
    }
}
