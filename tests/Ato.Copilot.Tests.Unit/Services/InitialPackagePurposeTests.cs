using System.Threading.Channels;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class InitialPackagePurposeTests
{
    [Fact]
    public async Task CurrentMappingChanges_BlockPreparation_WhilePreviewRetainsReviewedResponse()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.SeedReviewedArtifactsAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = await db.ControlImplementations.SingleAsync();
        var snapshot = RequirementCoverageService.Deserialize(implementation.RequirementCoverageJson)!;
        implementation.RequirementCoverageJson = JsonSerializer.Serialize(snapshot with
        {
            Responses = [snapshot.Responses[0] with { Response = "CHANGED draft response" }]
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await db.SaveChangesAsync();
        var exporter = new OscalSspExportService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspExportService>.Instance);

        // Act
        var validation = await fixture.Validator.ValidateAsync("mission", PackagePurpose.InitialSubmission);
        var preview = await exporter.PreviewAsync("mission");

        // Assert
        validation.Findings.Should().Contain(x => x.Category == "requirement-coverage"
            && x.Description.Contains("differ from the retained review"));
        preview.OscalJson.Should().Contain("DEMO reviewed response").And.NotContain("CHANGED draft response");
    }

    [Fact]
    public async Task RequirementEvidenceWithoutControlWideLink_IsByteValidated()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.SeedReviewedArtifactsAsync();
        fixture.Evidence.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<(Stream Content, string FileName, string ContentType)?>(
                (new MemoryStream([1, 2, 3]), "demo.txt", "text/plain")));

        // Act
        var validation = await fixture.Validator.ValidateAsync("mission", PackagePurpose.InitialSubmission);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(x => x.Category == "evidence" && x.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task InitialSubmission_WithoutRequirementSource_BlocksPreparationNotWorkingPreview()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var exporter = new OscalSspExportService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspExportService>.Instance);

        // Act
        var result = await fixture.Validator.ValidateAsync("mission", PackagePurpose.InitialSubmission);
        var preview = await exporter.PreviewAsync("mission");

        // Assert
        result.Findings.Should().Contain(x => x.Category == "requirement-coverage" && x.Severity == ValidationSeverity.Error);
        preview.Warnings.Should().Contain(x => x.Contains("Catalog source"));
        preview.OscalJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReadinessSourceHash_IncludesImmutableCatalogContent()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = await db.RegisteredSystems.SingleAsync();
        var baseline = new ControlBaseline { RegisteredSystemId = "mission", RequirementCatalogBindingId = "binding" };
        var binding = new BaselineCatalogBinding { Id = "binding", ControlBaselineId = baseline.Id,
            CatalogJson = "original bytes", ContentHash = "original hash" };
        db.AddRange(baseline, binding);
        await db.SaveChangesAsync();
        var selection = new PackageReadinessSelection(PackagePurpose.InitialSubmission);
        var before = await PackageReadinessSources.CaptureAsync(db, scope.ServiceProvider, system, selection, "tester", default);
        binding.CatalogJson = "tampered bytes";
        await db.SaveChangesAsync();

        // Act
        var after = await PackageReadinessSources.CaptureAsync(db, scope.ServiceProvider, system, selection, "tester", default);

        // Assert
        after.Hash.Should().NotBe(before.Hash);
        after.Sources.Should().Contain(x => x.Kind == "requirement-catalog");
    }

    [Fact]
    public async Task CompleteInitialSubmission_CanQueueWithoutAoDecision()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.SeedReviewedArtifactsAsync();
        var channel = Channel.CreateUnbounded<PackageExportJob>();
        var service = new AuthorizationPackageService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Evidence.Object, Mock.Of<IFileStorageProvider>(), fixture.Validator, channel,
            NullLogger<AuthorizationPackageService>.Instance);

        // Act
        var package = await service.EnqueuePackageAsync("mission", PackagePurpose.InitialSubmission);

        // Assert
        package.Purpose.Should().Be(PackagePurpose.InitialSubmission);
        package.Status.Should().Be(PackageStatus.Pending);
        using var scope = fixture.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().AuthorizationDecisions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InitialSubmission_MissingRequiredSspSection_BlocksEvenWhenPresentSectionsApproved()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.SeedReviewedArtifactsAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Remove(await db.SspSections.SingleAsync(x => x.SectionNumber == 13));
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Validator.ValidateAsync("mission", PackagePurpose.InitialSubmission);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Findings.Should().Contain(f => f.Category == "ssp" && f.Description.Contains("13"));
    }

    [Fact]
    public async Task PurposeSchema_IsIdempotent_AndPreservesHistoricalRowsAsLegacy()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE AuthorizationPackages (Id TEXT PRIMARY KEY, ContentHash TEXT)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO AuthorizationPackages VALUES ('historical', 'DEMO-retained-hash')");

        // Act
        await PackagePurposeSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await PackagePurposeSchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        (await db.Database.SqlQueryRaw<int>("SELECT Purpose AS Value FROM AuthorizationPackages").SingleAsync())
            .Should().Be((int)PackagePurpose.Legacy);
        (await db.Database.SqlQueryRaw<string>("SELECT ContentHash AS Value FROM AuthorizationPackages").SingleAsync())
            .Should().Be("DEMO-retained-hash");
    }

    [Fact]
    public async Task InitialSubmission_DoesNotRequireOrCreateAoDecision_ButKeepsOtherGates()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();

        // Act
        var initial = await fixture.Validator.ValidateAsync("mission", PackagePurpose.InitialSubmission);
        var legacy = await fixture.Validator.ValidateAsync("mission");

        // Assert
        initial.Findings.Should().NotContain(f => f.Category == "authorization-decision");
        legacy.Findings.Should().Contain(f => f.Category == "authorization-decision");
        initial.Findings.Should().Contain(f => f.Category == "boundary");
        initial.Findings.Should().Contain(f => f.Category == "ssp");
        initial.Findings.Should().Contain(f => f.Category == "sar");
        initial.Findings.Should().Contain(f => f.Category == "sap");
        initial.IsValid.Should().BeFalse();
        using var scope = fixture.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().AuthorizationDecisions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InitialSubmission_UnavailableSchemaCheck_IsBlocking()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Schema.Setup(x => x.ValidateForSystemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DEMO validator unavailable"));

        // Act
        var result = await fixture.Validator.ValidateAsync("mission", PackagePurpose.InitialSubmission);

        // Assert
        result.Findings.Should().Contain(f => f.Category == "schema" && f.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task Enqueue_PersistsExplicitPurpose_AndCarriesItIntoJob()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.SeedReviewedArtifactsAsync();
        var channel = Channel.CreateUnbounded<PackageExportJob>();
        var service = new AuthorizationPackageService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Evidence.Object, Mock.Of<IFileStorageProvider>(), fixture.Validator, channel,
            NullLogger<AuthorizationPackageService>.Instance);

        // Act
        var package = await service.EnqueuePackageAsync("mission", PackagePurpose.InitialSubmission,
            EvidenceMode.ManifestOnly, "demo-user");
        var persisted = await service.GetPackageAsync(package.Id);
        var job = await channel.Reader.ReadAsync();

        // Assert
        persisted!.Purpose.Should().Be(PackagePurpose.InitialSubmission);
        job.Purpose.Should().Be(PackagePurpose.InitialSubmission);
        persisted.Status.Should().Be(PackageStatus.Pending);
        persisted.ReadinessRunId.Should().NotBeNullOrWhiteSpace();
        persisted.ReadinessSourceHash.Should().NotBeNullOrWhiteSpace();
        using var scope = fixture.Services.CreateScope();
        var run = await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().PackageReadinessRuns.SingleAsync();
        run.Id.Should().Be(persisted.ReadinessRunId);
        run.Purpose.Should().Be(PackagePurpose.InitialSubmission);
        run.EvaluatedBy.Should().Be("demo-user");
        run.Outcome.Should().Be("Ready");
        run.SourceHash.Should().Be(persisted.ReadinessSourceHash);
        PackageReadinessService.Checks(run).Should().Contain(x => x.Id == "inventory" && x.Outcome == "Passed");
    }

    [Fact]
    public async Task UnknownPurpose_IsRejected()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var action = () => fixture.Validator.ValidateAsync("mission", (PackagePurpose)99);

        // Assert
        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private sealed class Fixture : IDisposable
    {
        public ServiceProvider Services { get; }
        public Mock<IOscalSchemaValidationService> Schema { get; } = new();
        public Mock<IEvidenceArtifactService> Evidence { get; } = new();
        public PackageValidationService Validator { get; }

        public Fixture()
        {
            var name = Guid.NewGuid().ToString();
            Schema.Setup(x => x.ValidateForSystemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OscalSchemaValidationResult { IsValid = true });
            Evidence.Setup(x => x.GetSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EvidenceSummary { CoveragePercentage = 100 });
            Services = new ServiceCollection()
                .AddLogging()
                .AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(name))
                .AddSingleton(Schema.Object)
                .AddSingleton(Evidence.Object)
                .AddSingleton<IInventoryService, InventoryService>()
                .AddSingleton<IInterconnectionService, InterconnectionService>()
                .AddSingleton<IPrivacyService, PrivacyService>()
                .AddSingleton<IPackageValidationService, PackageValidationService>()
                .AddSingleton<PackageReadinessService>()
                .BuildServiceProvider();
            Validator = (PackageValidationService)Services.GetRequiredService<IPackageValidationService>();
        }

        public async Task SeedAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Add(new RegisteredSystem { Id = "mission", Name = "DEMO mission", Acronym = "DEMO" });
            await db.SaveChangesAsync();
            await Services.GetRequiredService<IPrivacyService>().CreatePtaAsync("mission", "demo-user", manualMode: true);
            await Services.GetRequiredService<IInventoryService>().AddItemAsync("mission", new InventoryItemInput
            {
                Type = InventoryItemType.Software, SoftwareFunction = SoftwareFunction.Application,
                ItemName = "DEMO standalone application", Vendor = "Synthetic test vendor", Version = "1.0"
            }, "demo-user");
        }

        public async Task SeedReviewedArtifactsAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AddRange(new AuthorizationBoundaryDefinition { RegisteredSystemId = "mission", Name = "DEMO boundary" },
                new SecurityAssessmentReport { RegisteredSystemId = "mission", Status = SarStatus.Approved, Title = "DEMO SAR" },
                new SecurityAssessmentPlan { RegisteredSystemId = "mission", Status = SapStatus.Finalized, Title = "DEMO SAP" });
            foreach (var number in Enumerable.Range(1, 13))
                db.Add(new SspSection { RegisteredSystemId = "mission", SectionNumber = number,
                    SectionTitle = $"DEMO section {number}", Status = SspSectionStatus.Approved, Content = "DEMO reviewed content" });
            var raw = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Requirements", "export-audit-catalog.json"));
            var baseline = new ControlBaseline { RegisteredSystemId = "mission", ControlIds = ["AU-2"] };
            var binding = new BaselineCatalogBinding { ControlBaselineId = baseline.Id, CatalogJson = raw,
                ContentHash = RequirementCoverageService.Hash(raw), CatalogVersion = "test-1", SourceUri = "https://example.invalid/catalog.json" };
            baseline.RequirementCatalogBindingId = binding.Id;
            var implementation = new ControlImplementation { RegisteredSystemId = "mission", ControlId = "AU-2",
                PolicyNarrative = "DEMO approved policy", TechnicalNarrative = "DEMO approved technical" };
            var evidenceBytes = System.Text.Encoding.UTF8.GetBytes("DEMO evidence bytes");
            var evidence = new EvidenceArtifact { RegisteredSystemId = "mission", FileName = "demo.txt",
                ContentHash = RequirementCoverageService.Hash("DEMO evidence bytes").ToLowerInvariant() };
            Evidence.Setup(x => x.DownloadAsync(evidence.Id, It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<(Stream Content, string FileName, string ContentType)?>(
                    (new MemoryStream(evidenceBytes), "demo.txt", "text/plain")));
            var snapshot = new RequirementCoverageSnapshot(binding.Id, binding.ContentHash,
                [new("audit-statement", "Technical", "DEMO reviewed response", [new(evidence.Id, evidence.ContentHash)])],
                new Dictionary<string, string>(), RequirementCoverageService.NarrativeHash(implementation),
                Guid.NewGuid(), "author", DateTime.UtcNow, Guid.NewGuid(), "reviewer", DateTime.UtcNow);
            implementation.ApprovedRequirementCoverageJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            implementation.RequirementCoverageJson = implementation.ApprovedRequirementCoverageJson;
            db.AddRange(baseline, binding, implementation, evidence);
            await db.SaveChangesAsync();
        }

        public void Dispose() => Services.Dispose();
    }
}
