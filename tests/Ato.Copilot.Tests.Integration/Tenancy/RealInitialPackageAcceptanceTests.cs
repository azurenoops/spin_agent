using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using ClosedXML.Excel;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed partial class RealInitialPackageAcceptanceTests(ITestOutputHelper output)
{
    private const string OriginalMarker = "SYNTHETIC-REFERENCE-MISSION-REVISION-ONE";
    private const string RevisedMarker = "SYNTHETIC-REFERENCE-MISSION-REVISION-TWO";
    private const string EvidenceMarker = "SYNTHETIC-REFERENCE-LOCAL-EVIDENCE";

    [Fact]
    public async Task RealSchemaLoader_ResolvesAllFourBundledSchemas()
    {
        // Arrange
        using var fixture = new ReferenceSystem(output);
        var validator = fixture.Services.GetRequiredService<IOscalSchemaValidationService>();

        // Act
        var results = new List<OscalSchemaValidationResult>();
        foreach (var model in new[] { "ssp", "poam", "assessment-results", "assessment-plan" })
            results.Add(await validator.ValidateAsync("{}", model));

        // Assert
        results.Should().OnlyContain(x => !x.IsValid && x.Violations.Count > 0);
        results.SelectMany(x => x.Violations).Should().NotContain(x => x.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CanonicalAuthorshipReviewAndRealSchemas_ProduceInitialPackagesWithoutAo_AndPreservePreviousBytes()
    {
        // Arrange
        using var fixture = new ReferenceSystem(output);
        await fixture.PrepareAsync();
        var readiness = fixture.Services.GetRequiredService<PackageReadinessService>();
        var packages = fixture.Services.GetRequiredService<IAuthorizationPackageService>();
        fixture.Services.GetRequiredService<IEmassExportService>().Should().BeOfType<EmassExportService>();
        fixture.Services.GetRequiredService<IOscalSapExportService>().Should().BeOfType<OscalSapExportService>();
        fixture.Services.GetRequiredService<IOscalSchemaValidationService>().Should().BeOfType<OscalSchemaValidationService>();
        fixture.Services.GetRequiredService<ISecurityAssessmentReportService>().Should().BeOfType<SecurityAssessmentReportService>();
        await using var initialRole = fixture.As(fixture.Reviewer);
        var firstRun = await readiness.ValidateAsync(fixture.SystemId, new(PackagePurpose.InitialSubmission), fixture.Reviewer.ToString(), default);
        AssertReady(firstRun);
        var worker = ActivatorUtilities.CreateInstance<PackageBackgroundService>(fixture.Services);
        await worker.StartAsync(default);
        try
        {
            // Act
            var first = await ((AuthorizationPackageService)packages).EnqueueFromReadinessAsync(fixture.SystemId,
                new(PackagePurpose.InitialSubmission), firstRun.Id, firstRun.SourceHash!, EvidenceMode.Embedded,
                fixture.Reviewer.ToString(), default);
            first = await fixture.WaitForPackageAsync(first.Id);
            var firstBytes = await File.ReadAllBytesAsync(first.FilePath!);
            var firstHash = Hash(firstBytes);
            await InspectPackageAsync(fixture, first, OriginalMarker);
            await initialRole.DisposeAsync();
            await fixture.SaveMissionAsync(RevisedMarker, approve: false);
            await using (fixture.As(fixture.Reviewer))
            {
                var source = await readiness.ReadSourceAsync(fixture.SystemId, new(PackagePurpose.InitialSubmission), fixture.Reviewer.ToString(), default);
                PackageReadinessService.Freshness(firstRun, source).State.Should().Be("Stale");
                var denied = () => ((AuthorizationPackageService)packages).EnqueueFromReadinessAsync(fixture.SystemId,
                    new(PackagePurpose.InitialSubmission), firstRun.Id, firstRun.SourceHash!, EvidenceMode.Embedded,
                    fixture.Reviewer.ToString(), default);
                await denied.Should().ThrowAsync<DbUpdateConcurrencyException>();
                var approvedExport = await fixture.Services.GetRequiredService<IEmassExportService>()
                    .ExportOscalAsync(fixture.SystemId, OscalModelType.Ssp);
                approvedExport.Should().Contain(OriginalMarker).And.NotContain(RevisedMarker);
            }
            await fixture.ReviewMissionAsync();
            await using var finalRole = fixture.As(fixture.Reviewer);
            var secondRun = await readiness.ValidateAsync(fixture.SystemId, new(PackagePurpose.InitialSubmission), fixture.Reviewer.ToString(), default);
            AssertReady(secondRun);
            var second = await ((AuthorizationPackageService)packages).EnqueueFromReadinessAsync(fixture.SystemId,
                new(PackagePurpose.InitialSubmission), secondRun.Id, secondRun.SourceHash!, EvidenceMode.Embedded,
                fixture.Reviewer.ToString(), default);
            second = await fixture.WaitForPackageAsync(second.Id);
            await InspectPackageAsync(fixture, second, RevisedMarker);

            // Assert
            Hash(await File.ReadAllBytesAsync(first.FilePath!)).Should().Be(firstHash);
            firstRun.Id.Should().NotBe(secondRun.Id);
            firstRun.SourceHash.Should().NotBe(secondRun.SourceHash);
            await using var verify = fixture.Services.CreateAsyncScope();
            var db = verify.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AuthorizationDecisions.CountAsync(x => x.RegisteredSystemId == fixture.SystemId)).Should().Be(0);
            (await db.PackageReadinessRuns.CountAsync(x => x.RegisteredSystemId == fixture.SystemId)).Should().Be(2);
            (await db.SspSections.Where(x => x.RegisteredSystemId == fixture.SystemId).ToListAsync())
                .Should().HaveCount(13).And.OnlyContain(x => x.Status == SspSectionStatus.Approved
                    && x.AuthoredBy == fixture.Author.ToString() && x.ReviewedBy == fixture.Reviewer.ToString());
            await fixture.WriteReportAsync(first, second, firstRun, secondRun);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await worker.StopAsync(stop.Token);
            worker.Dispose();
        }
    }

    private static void AssertReady(PackageReadinessRun run)
    {
        var checks = PackageReadinessService.Checks(run);
        using var assertions = new AssertionScope();
        run.Outcome.Should().Be("Ready", string.Join("\n", checks.Where(x => x.Required && x.Outcome is "Blocking" or "Unavailable")
            .Select(x => $"{x.Id}: {x.Why}")));
        checks.Should().Contain(x => x.Id == "authorization-decision" && x.Outcome == "NotApplicable");
        checks.Where(x => x.Category == "schema").Should().HaveCount(4).And.OnlyContain(x => x.Outcome == "Passed");
    }

    private static async Task InspectPackageAsync(ReferenceSystem fixture, AuthorizationPackage package, string marker)
    {
        package.Status.Should().Be(PackageStatus.Completed, package.FailureReason);
        package.ContentHash.Should().Be(Hash(await File.ReadAllBytesAsync(package.FilePath!)));
        using var zip = ZipFile.OpenRead(package.FilePath!);
        var validator = fixture.Services.GetRequiredService<IOscalSchemaValidationService>();
        using var assertions = new AssertionScope();
        foreach (var model in new[] { "ssp", "poam", "assessment-results", "assessment-plan" })
        {
            var entry = zip.GetEntry($"oscal-{model}.json");
            entry.Should().NotBeNull(model);
            using var reader = new StreamReader(entry!.Open());
            var text = await reader.ReadToEndAsync();
            var result = await validator.ValidateAsync(text, model);
            result.IsValid.Should().BeTrue($"{model}: {string.Join("\n", result.Violations.Select(x => $"{x.JsonPath}: {x.Message}"))}");
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement.EnumerateObject().Single().Value;
            if (model == "ssp") text.Should().Contain(marker);
            if (model is "poam" or "assessment-plan")
                root.GetProperty("import-ssp").GetProperty("href").GetString().Should().Be("oscal-ssp.json");
            if (model == "assessment-results")
                root.GetProperty("import-ap").GetProperty("href").GetString().Should().Be("oscal-assessment-plan.json");
            if (model == "poam")
                text.Should().Contain("SYNTHETIC-OPEN-POAM").And.NotContain("related-findings");
        }
        using var metadata = JsonDocument.Parse(zip.GetEntry("package-metadata.json")!.Open());
        metadata.RootElement.GetProperty("purpose").GetString().Should().Be("InitialSubmission");
        metadata.RootElement.GetProperty("readiness").GetProperty("runId").GetString().Should().Be(package.ReadinessRunId);
        metadata.RootElement.GetProperty("readiness").GetProperty("sourceHash").GetString().Should().Be(package.ReadinessSourceHash);
        var inventoryEntry = zip.GetEntry("hardware-software-inventory.xlsx");
        inventoryEntry.Should().NotBeNull("the canonical inventory workbook is a supporting package artifact");
        using var inventoryBytes = new MemoryStream();
        await inventoryEntry!.Open().CopyToAsync(inventoryBytes);
        inventoryBytes.Position = 0;
        using var inventory = new XLWorkbook(inventoryBytes);
        var software = inventory.Worksheet("Software");
        software.Cell(2, 2).GetString().Should().Be("SYNTHETIC Reference Application");
        software.Cell(2, 3).GetString().Should().Be("Synthetic laboratory");
        software.Cell(2, 4).GetString().Should().Be("1.0");
        software.Cell(2, 6).GetString().Should().Be("Application");
        using var manifest = JsonDocument.Parse(zip.GetEntry("evidence-manifest.json")!.Open());
        manifest.RootElement.GetProperty("totalArtifacts").GetInt32().Should().Be(fixture.Controls.Count);
        manifest.RootElement.GetProperty("artifacts").GetArrayLength().Should().Be(fixture.Controls.Count);
        manifest.RootElement.GetProperty("artifacts").EnumerateArray().Select(x => x.GetProperty("controlId").GetString())
            .Should().BeEquivalentTo(fixture.Controls);
        foreach (var evidence in manifest.RootElement.GetProperty("artifacts").EnumerateArray())
        {
            var path = evidence.GetProperty("path").GetString()!;
            await using var content = zip.GetEntry(path)!.Open();
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes);
            Hash(bytes.ToArray()).Should().Be(evidence.GetProperty("contentHash").GetString());
            Encoding.UTF8.GetString(bytes.ToArray()).Should().Contain(EvidenceMarker);
        }
        using var sarEntry = zip.GetEntry("security-assessment-report.docx")!.Open();
        using var sarBytes = new MemoryStream();
        await sarEntry.CopyToAsync(sarBytes);
        sarBytes.Position = 0;
        using var word = new ZipArchive(sarBytes, ZipArchiveMode.Read);
        using var wordText = new StreamReader(word.GetEntry("word/document.xml")!.Open());
        (await wordText.ReadToEndAsync()).Should().Contain("SYNTHETIC-SCA-RECOMMENDATION");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class ReferenceSystem : IDisposable
    {
        private readonly MultiTenantWebApplicationFactory<McpProgram> _owner = new();
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<McpProgram> _app;
        private readonly ITestOutputHelper _output;
        private readonly bool _retain;
        private readonly string _directory;
        public Guid Author { get; } = Guid.NewGuid();
        public Guid Reviewer { get; } = Guid.NewGuid();
        public Guid Assessor { get; } = Guid.NewGuid();
        public string SystemId { get; private set; } = "";
        public IReadOnlyList<string> Controls { get; private set; } = [];
        public IServiceProvider Services => _app.Services;

        public ReferenceSystem(ITestOutputHelper output)
        {
            _output = output;
            var artifactRoot = Environment.GetEnvironmentVariable("ATO_REAL_PACKAGE_ARTIFACT_DIR");
            _retain = !string.IsNullOrWhiteSpace(artifactRoot);
            _directory = _retain ? Directory.CreateDirectory(Path.Combine(Path.GetFullPath(artifactRoot!), Guid.NewGuid().ToString("N"))).FullName
                : Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(),
                    ".test-artifacts", $"real-initial-package-{Guid.NewGuid():N}")).FullName;
            _owner.ResetLegacyTenantContext(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
            _app = _owner.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IHostedService, TenancySeedHostedService>();
                services.Configure<ExportSettings>(settings => settings.DataPath = _directory);
                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = new OfflineTransport()));
            }));
        }

        public IAsyncDisposable As(Guid person)
        {
            var context = _owner.GetActiveContext();
            context.PersonId = person; context.IsWorkspaceRequest = true;
            return new RoleScope(Services.GetRequiredService<ITenantContextAccessor>().Push(context));
        }

        public async Task PrepareAsync()
        {
            await using (As(Author))
            await using (var scope = Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
                foreach (var (id, name) in new[] { (Author, "SYNTHETIC Author"), (Reviewer, "SYNTHETIC Reviewer"), (Assessor, "SYNTHETIC Assessor") })
                {
                    db.Persons.Add(new() { Id = id, TenantId = tenant, DisplayName = name, Email = $"{id}@example.invalid" });
                    db.OrganizationMemberships.Add(new() { TenantId = tenant, PersonId = id, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "synthetic-fixture" });
                }
                foreach (var (person, role) in new[] { (Author, OrganizationRole.Administrator), (Author, OrganizationRole.MissionOwner),
                    (Author, OrganizationRole.Isso), (Reviewer, OrganizationRole.Issm), (Assessor, OrganizationRole.Assessor) })
                    db.OrganizationRoleAssignments.Add(new() { TenantId = tenant, PersonId = person, Role = role });
                await db.SaveChangesAsync();
                var registered = await Services.GetRequiredService<IRmfLifecycleService>().RegisterSystemAsync(
                    "SYNTHETIC REFERENCE SYSTEM", SystemType.MajorApplication, MissionCriticality.MissionSupport,
                    "Isolated on-premises test laboratory", Author.ToString(), "SYNREF", "Synthetic acceptance fixture, not a production authorization.");
                SystemId = registered.Id;
            }
            await using (As(Reviewer))
            await using (var scope = Services.CreateAsyncScope())
            {
                await Services.GetRequiredService<IRmfLifecycleService>().UpdateOperationalStatusAsync(
                    SystemId, OperationalStatus.UnderDevelopment, Reviewer.ToString());
                await scope.ServiceProvider.GetRequiredService<BoundaryDefinitionService>().CreateAsync(SystemId,
                    new("Synthetic logical boundary", "Logical", "Isolated software test scope"), Reviewer.ToString());
                await Services.GetRequiredService<IBoundaryService>().DefineBoundaryAsync(SystemId,
                    [new() { ResourceId = "urn:synthetic:reference:application", ResourceName = "SYNTHETIC Reference Application", ResourceType = "Software" }], Reviewer.ToString());
                await Services.GetRequiredService<ICategorizationService>().CategorizeSystemAsync(SystemId,
                    [new() { Sp80060Id = "D.1.1", Name = "Synthetic public training records", ConfidentialityImpact = "Low",
                        IntegrityImpact = "Low", AvailabilityImpact = "Low" }], Reviewer.ToString());
                var baseline = await Services.GetRequiredService<IBaselineService>().SelectBaselineAsync(SystemId, false, selectedBy: Reviewer.ToString());
                Controls = baseline.ControlIds;
                await Services.GetRequiredService<IPrivacyService>().CreatePtaAsync(SystemId, Reviewer.ToString(), manualMode: true);
                await Services.GetRequiredService<IInterconnectionService>().CertifyNoInterconnectionsAsync(SystemId, true);
                await Services.GetRequiredService<IInventoryService>().AddItemAsync(SystemId, new()
                {
                    Type = InventoryItemType.Software, ItemName = "SYNTHETIC Reference Application",
                    SoftwareFunction = SoftwareFunction.Application,
                    Vendor = "Synthetic laboratory", Version = "1.0", PatchLevel = "test-1",
                    LicenseType = "Synthetic test fixture", Location = "Isolated test environment"
                }, Reviewer.ToString());
            }
            await SaveProfilesAsync();
            var evidenceIds = new Dictionary<string, string>();
            await using (As(Author))
            {
                for (var section = 1; section <= 13; section++)
                    await Services.GetRequiredService<ISspService>().WriteSspSectionAsync(SystemId, section,
                        $"SYNTHETIC authored section {section}: laboratory documentation only.", Author.ToString(), submitForReview: true);
                foreach (var control in Controls)
                {
                    await Services.GetRequiredService<IDualNarrativeService>().UpdateAsync(SystemId, control,
                        $"SYNTHETIC {control} policy: laboratory-only approved procedure.", true,
                        $"SYNTHETIC {control} technical: isolated reference implementation.", true, "Isso", Author.ToString());
                    await Services.GetRequiredService<INarrativeGovernanceService>().SubmitNarrativeAsync(SystemId, control, Author.ToString());
                    await using var scope = Services.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                    var implementation = await db.ControlImplementations.SingleAsync(x => x.RegisteredSystemId == SystemId && x.ControlId == control);
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"{EvidenceMarker}: {control}"));
                    var evidence = await Services.GetRequiredService<IEvidenceArtifactService>().UploadAsync(SystemId,
                        $"synthetic-{control.Replace('(', '-').Replace(")", "")}.txt", "text/plain", stream,
                        ArtifactCategory.TestResult, Author.ToString(), controlImplementationId: implementation.Id,
                        narrativeType: EvidenceNarrativeType.Technical);
                    evidenceIds[control] = evidence.Id;
                }
            }
            await using (As(Reviewer))
            {
                for (var section = 1; section <= 13; section++)
                    await Services.GetRequiredService<ISspService>().ReviewSspSectionAsync(SystemId, section, "approve", Reviewer.ToString(), "Independent synthetic review.");
                foreach (var control in Controls)
                    await Services.GetRequiredService<INarrativeGovernanceService>().ReviewNarrativeAsync(SystemId, control, ReviewDecision.Approve, Reviewer.ToString());
            }
            await using (As(Assessor))
            {
                var assessment = new ComplianceAssessment
                {
                    Id = Guid.NewGuid().ToString(), RegisteredSystemId = SystemId,
                    TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                    SubscriptionId = Guid.Empty.ToString(), InitiatedBy = Assessor.ToString(),
                    Status = AssessmentStatus.Pending, Framework = "NIST80053", Baseline = "Low", ScanType = "manual"
                };
                await Services.GetRequiredService<IAssessmentPersistenceService>().SaveAssessmentAsync(assessment);
                foreach (var control in Controls)
                    await Services.GetRequiredService<IAssessmentArtifactService>().AssessControlAsync(assessment.Id, control,
                        "Satisfied", "Examine", evidenceIds: [evidenceIds[control]], notes: $"SYNTHETIC SCA examined {control} fixture evidence.", assessorId: Assessor.ToString());
                assessment.Status = AssessmentStatus.Completed;
                assessment.CompletedAt = DateTime.UtcNow;
                assessment.TotalControls = Controls.Count; assessment.PassedControls = Controls.Count; assessment.ComplianceScore = 100;
                await Services.GetRequiredService<IAssessmentPersistenceService>().SaveAssessmentAsync(assessment);
                var plan = await Services.GetRequiredService<ISapService>().GenerateSapAsync(new(SystemId, assessment.Id,
                    DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1), "Synthetic full baseline assessment",
                    "No production systems or external connections.", [new("SYNTHETIC Assessor", "Synthetic laboratory", "SCA")]), Assessor.ToString());
                await Services.GetRequiredService<ISapService>().FinalizeSapAsync(plan.SapId, Assessor.ToString());
                var sar = await Services.GetRequiredService<ISecurityAssessmentReportService>().CreateSarAsync(SystemId,
                    new() { Title = "SYNTHETIC Security Assessment Report", SapId = plan.SapId }, Assessor.ToString());
                await Services.GetRequiredService<ISecurityAssessmentReportService>().EditSectionAsync(sar.Id, SarSectionType.Recommendations,
                    new() { Content = "SYNTHETIC-SCA-RECOMMENDATION: exercise is confined to synthetic reference data; no production security determination." }, Assessor.ToString());
                await Services.GetRequiredService<ISecurityAssessmentReportService>().SubmitForReviewAsync(sar.Id, Assessor.ToString());
                await using (As(Reviewer))
                    await Services.GetRequiredService<ISecurityAssessmentReportService>().ReviewSarAsync(sar.Id,
                        new() { Decision = "approve", Comments = "Independent review of synthetic report." }, Reviewer.ToString());
            }
            await using (As(Reviewer))
            await using (var scope = Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<PoamService>().CreateAsync(SystemId, "SYNTHETIC-OPEN-POAM",
                    "Synthetic follow-up planning exercise", Controls[0], CatSeverity.CatIII, "Synthetic laboratory",
                    DateTime.UtcNow.AddDays(30), comments: "Open planning item; no fabricated finding linkage.", createdBy: Reviewer.ToString(),
                    milestones: [("Synthetic follow-up review", DateTime.UtcNow.AddDays(15))]);
        }

        private async Task SaveProfilesAsync()
        {
            await SaveMissionAsync(OriginalMarker, false);
            var profile = Services.GetRequiredService<ISystemProfileService>();
            await using (As(Author))
            {
                await profile.SaveDraftWithChildrenAsync(SystemId, ProfileSectionType.UsersAndAccess,
                    """{"accessOverview":"Synthetic test operators only"}""",
                    [JsonSerializer.SerializeToElement(new { categoryName = "Synthetic operators", approximateCount = 3, accessMethod = "Local test console", dataSensitivityLevel = "Public" })], Author.ToString());
                await profile.SaveDraftAsync(SystemId, ProfileSectionType.EnvironmentAndDeployment,
                    """{"hostingModel":"Isolated laboratory","networkZones":"Local only","disasterRecoveryPosture":"Rebuild synthetic fixtures"}""", Author.ToString());
                await profile.SaveDraftWithChildrenAsync(SystemId, ProfileSectionType.DataTypes, """{"dataOverview":"Synthetic public records only"}""",
                    [JsonSerializer.SerializeToElement(new { dataTypeName = "Synthetic public training", sensitivityClassification = "Public", source = "Test fixture", destination = "Test archive" })], Author.ToString());
                await profile.SaveDraftWithChildrenAsync(SystemId, ProfileSectionType.PortsProtocolsAndServices, """{"ppsOverview":"No external connections"}""",
                    [JsonSerializer.SerializeToElement(new { portOrRange = "8080", protocol = "TCP", serviceName = "Synthetic local test", direction = "Inbound", justification = "Loopback-only acceptance fixture" })], Author.ToString());
                await profile.SubmitForReviewAsync(SystemId, null, Author.ToString());
            }
            await using (As(Reviewer))
                foreach (var type in Enum.GetValues<ProfileSectionType>().Where(x => x != ProfileSectionType.LeveragedAuthorizations))
                    await profile.ReviewSectionAsync(SystemId, type, ReviewDecision.Approve, Reviewer.ToString());
            await using (As(Author))
            {
                var users = await profile.GetSectionDetailAsync(SystemId, ProfileSectionType.UsersAndAccess);
                var category = users!.UserCategories.Single();
                await profile.ReviewUserCategoryAsync(SystemId, category.Id, "submit", category.Revision, Author.ToString());
            }
            await using (As(Reviewer))
            {
                var users = await profile.GetSectionDetailAsync(SystemId, ProfileSectionType.UsersAndAccess);
                var category = users!.UserCategories.Single();
                await profile.ReviewUserCategoryAsync(SystemId, category.Id, "approve", category.Revision, Reviewer.ToString(), "Independent synthetic category review.");
            }
        }

        public async Task SaveMissionAsync(string marker, bool approve)
        {
            await using (As(Author))
                await Services.GetRequiredService<ISystemProfileService>().SaveDraftAsync(SystemId, ProfileSectionType.MissionAndPurpose,
                    JsonSerializer.Serialize(new { missionStatement = marker, businessPurpose = "Synthetic pipeline acceptance only" }), Author.ToString());
            if (approve) await ReviewMissionAsync();
        }

        public async Task ReviewMissionAsync()
        {
            await using (As(Author))
                await Services.GetRequiredService<ISystemProfileService>().SubmitForReviewAsync(SystemId, [ProfileSectionType.MissionAndPurpose], Author.ToString());
            await using (As(Reviewer))
                await Services.GetRequiredService<ISystemProfileService>().ReviewSectionAsync(SystemId, ProfileSectionType.MissionAndPurpose,
                    ReviewDecision.Approve, Reviewer.ToString(), "Independent review of changed synthetic mission.");
        }

        public async Task<AuthorizationPackage> WaitForPackageAsync(string id)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            while (true)
            {
                var package = await Services.GetRequiredService<IAuthorizationPackageService>().GetPackageAsync(id, timeout.Token);
                if (package!.Status is PackageStatus.Completed or PackageStatus.Failed)
                {
                    package.Status.Should().Be(PackageStatus.Completed, package.FailureReason);
                    return package;
                }
                await Task.Delay(50, timeout.Token);
            }
        }

        public async Task WriteReportAsync(AuthorizationPackage first, AuthorizationPackage second, PackageReadinessRun before, PackageReadinessRun after)
        {
            var report = Path.Combine(_directory, "acceptance-report.json");
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
            {
                systemId = SystemId, synthetic = true, controlCount = Controls.Count, baseline = "Low",
                author = Author, reviewer = Reviewer, assessor = Assessor,
                initial = new { first.Id, first.FilePath, first.ContentHash, runId = before.Id, before.SourceHash },
                subsequent = new { second.Id, second.FilePath, second.ContentHash, runId = after.Id, after.SourceHash },
                realExporters = true, realSchemas = true, aoDecisionCreated = false,
                receivingSystemAcceptance = "Not performed; external eMASS gate remains open"
            }, new JsonSerializerOptions { WriteIndented = true }));
            _output.WriteLine($"Synthetic acceptance report: {report}");
        }

        public async Task WriteDesignArtifactsAsync(IReadOnlyDictionary<string, byte[]> artifacts, object report)
        {
            var directory = Directory.CreateDirectory(Path.Combine(_directory, "system-design-acceptance")).FullName;
            foreach (var (name, content) in artifacts)
                await File.WriteAllBytesAsync(Path.Combine(directory, name), content);
            await File.WriteAllTextAsync(Path.Combine(directory, "verification.json"), JsonSerializer.Serialize(new
            {
                capturedAt = DateTimeOffset.UtcNow, systemId = SystemId, synthetic = true,
                actualSupportedSourceAndDesignApproval = true, realExporters = true, realSchemas = true,
                artifacts = artifacts.Select(a => new { file = a.Key, bytes = a.Value.Length, sha256 = Hash(a.Value) }),
                verification = report,
                receivingSystemAcceptance = "Not performed; external eMASS gate remains open"
            }, new JsonSerializerOptions { WriteIndented = true }));
            _output.WriteLine($"System Design acceptance artifacts: {directory}");
        }

        public void Dispose()
        {
            _app.Dispose(); _owner.Dispose();
            if (!_retain) Directory.Delete(_directory, true);
        }

        private sealed class RoleScope(IDisposable scope) : IAsyncDisposable
        {
            private bool _disposed;
            public ValueTask DisposeAsync()
            {
                if (!_disposed) { scope.Dispose(); _disposed = true; }
                return ValueTask.CompletedTask;
            }
        }

        private sealed class OfflineTransport : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri?.Host == "raw.githubusercontent.com")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); // Exercise the genuine bundled-catalog fallback.
                throw new InvalidOperationException($"Acceptance fixture forbids external HTTP transport: {request.RequestUri?.Host}");
            }
        }
    }
}
