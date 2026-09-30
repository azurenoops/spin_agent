using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Moq;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class AssessmentResultsWorkspaceHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid Directory = Guid.NewGuid();
    private readonly WebApplicationFactory<McpProgram> _factory;
    private readonly Mock<IAssessmentEnvironmentService> _environment = new();
    private readonly Mock<IAtoComplianceEngine> _engine = new();
    private readonly Mock<ISystemEnvironmentScopeResolver> _scopes = new();
    public AssessmentResultsWorkspaceHttpTests(WorkspaceMembershipFactory factory)
    {
        _scopes.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<EnvironmentScopePurpose>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, EnvironmentScopePurpose _, CancellationToken _) => new ResolvedSystemEnvironmentScopes(id, 0, [], []));
        _environment.Setup(x => x.GetReadinessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new AssessmentReadinessResponse(id, false,
                "ASSESSMENT_AZURE_ENVIRONMENT_REQUIRED", "Configure Azure first.", null, "", null, null, [], DateTimeOffset.UtcNow));
        _factory = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType != typeof(TenancySeedHostedService)).ToArray()) services.Remove(d);
            services.AddSingleton(_environment.Object);
            services.AddSingleton(_engine.Object);
            services.AddSingleton(_scopes.Object);
            services.AddTransient<IStartupFilter, AssessmentWriterClaimsFilter>();
        }));
    }

    [Fact]
    public async Task Collection_CanonicalWithdrawalCannotUseStaleReadyResponse_AndHistoryRemainsReadable()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        var subscription = Guid.NewGuid();
        _environment.Setup(x => x.GetReadinessAsync(f.System, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AssessmentReadinessResponse(f.System, true, null, "Previously ready", null, "", "Government", "Government",
                [new(subscription.ToString(), "Synthetic")], DateTimeOffset.UtcNow));
        var source = new ResolvedSystemEnvironmentScope(Guid.NewGuid(), 2, Guid.NewGuid(), 1,
            new(Guid.NewGuid(), WorkspaceMembershipFactory.TenantAId, subscription, Directory, "Government",
                "Synthetic", "Selected", DateTimeOffset.UtcNow), "ProviderAllocation", Guid.NewGuid(), 2,
            false, "Allocation withdrawn", [$"/subscriptions/{subscription}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/a"],
            [], [], new("Test", null, null, "Reconciled", null, DateTimeOffset.UtcNow));
        _scopes.Setup(x => x.ResolveAsync(f.System, EnvironmentScopePurpose.Assessment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedSystemEnvironmentScopes(f.System, 2, [source], []));

        // Act
        var response = await client.PostAsJsonAsync(f.Root + "/collect", new { planId = (string?)null,
            expectedPlanHash = (string?)null, requestId = Guid.NewGuid().ToString() });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _engine.Verify(x => x.RunRetainedAssessmentAsync(It.IsAny<ComplianceAssessment>(),
            It.IsAny<IProgress<AssessmentProgress>?>(), It.IsAny<CancellationToken>()), Times.Never);
        var retained = await ReadAsync(client, f.Root + "/results");
        retained.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task LegacyResults_ArePreliminary_NotHumanReviewed_AndScopeUsesObservations()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        // Act
        var response = await client.GetAsync(f.Root + "/results?planId=" + f.Plan);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = body.GetProperty("items")[0];
        item.GetProperty("id").GetString().Should().Be("assessment:" + f.Assessment);
        item.GetProperty("reviewedControlCount").GetInt32().Should().Be(0);
        item.GetProperty("observedControlCount").GetInt32().Should().Be(1);
        item.GetProperty("planId").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("sarReadiness").GetProperty("missingControlIds").EnumerateArray()
            .Select(x => x.GetString()).Should().Contain("AC-2");
    }

    [Fact]
    public async Task SourceAndPlanMustBelongToExactSystem_AndIssmCannotReview()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        var foreign = await SeedAsync();
        using var client = Client(f.Actor);
        // Act
        var response = await client.GetAsync(f.Root + "/results/assessment:" + foreign.Assessment);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(f.Root + "/results?planId=" + foreign.Plan)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var detail = await (await client.GetAsync(f.Root + "/results/assessment:" + f.Assessment))
            .Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("permissions").GetProperty("canReview").GetBoolean().Should().BeFalse();
        detail.GetProperty("permissions").GetProperty("canRemediate").GetBoolean().Should().BeTrue();
        detail.GetProperty("permissions").GetProperty("canRequestDeviation").GetBoolean().Should().BeFalse();
        var catalog = await ReadAsync(client, f.Root + "/results");
        catalog.GetProperty("permissions").GetProperty("canRemediate").GetBoolean().Should().BeTrue();
        catalog.GetProperty("permissions").GetProperty("canRequestDeviation").GetBoolean().Should().BeFalse();
        (await client.PostAsJsonAsync(f.Root + "/results/assessment:" + f.Assessment + "/review",
            new { expectedResultRevision = detail.GetProperty("item").GetProperty("revision").GetString(),
                controlId = "AC-1", determination = "Satisfied", method = "Examine", notes = "No authority", evidenceIds = Array.Empty<string>() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Fixture> SeedAsync(OrganizationRole role = OrganizationRole.Assessor)
    {
        using var initialized = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenant = WorkspaceMembershipFactory.TenantAId;
        if (!await db.NistControls.AnyAsync(x => x.Id == "AC-1"))
            db.NistControls.Add(new() { Id = "AC-1", Family = "AC", Title = "Policy" });
        var actor = Guid.NewGuid();
        var person = new Person { TenantId = tenant, DisplayName = "Results assessor", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = tenant, Name = "Results system", IsActive = true };
        var plan = new SecurityAssessmentPlan { TenantId = tenant, RegisteredSystemId = system.Id, Title = "Draft scope", Content = "Saved scope", GeneratedBy = "fixture", BaselineLevel = "Low" };
        foreach (var id in new[] { "AC-1", "AC-2" }) plan.ControlEntries.Add(new() { TenantId = tenant, ControlId = id, ControlTitle = id, ControlFamily = "AC" });
        var assessment = new ComplianceAssessment { TenantId = tenant, RegisteredSystemId = system.Id, Status = AssessmentStatus.Completed, InitiatedBy = "scanner", TotalControls = 100, PassedControls = 99 };
        assessment.Findings.Add(new() { TenantId = tenant, AssessmentId = assessment.Id, ControlId = "AC-1", ControlFamily = "AC", Title = "Observed gap" });
        db.AddRange(person, system, plan, assessment);
        db.OrganizationMemberships.Add(new() { TenantId = tenant, DirectoryTenantId = Directory, ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
        db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person.Id, RegisteredSystemId = system.Id, Role = role });
        await db.SaveChangesAsync();
        return new(actor, system.Id, plan.Id, assessment.Id);
    }

    [Fact]
    public async Task HumanReview_RejectsForeignEvidence_RequiresSeverity_AndRetainsImmutableOriginalPlan()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var plan = await db.SecurityAssessmentPlans.Include(x => x.ControlEntries).SingleAsync(x => x.Id == f.Plan);
            var assessment = await db.Assessments.SingleAsync(x => x.Id == f.Assessment);
            assessment.ResultProvenanceJson = new AssessmentResultProvenance { Plan = AssessmentResultProvenance.Pin(plan) }.Serialize();
            await db.SaveChangesAsync();
        }
        var url = f.Root + "/results/assessment:" + f.Assessment;
        var detail = await ReadAsync(client, url);
        var revision = detail.GetProperty("item").GetProperty("revision").GetString();
        // Act
        var invalid = await client.PostAsJsonAsync(url + "/review", new { expectedResultRevision = revision,
            controlId = "AC-1", determination = "Satisfied", method = "Examine", notes = "Checked", evidenceIds = new[] { "foreign" } });
        // Assert
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(url + "/review", new { expectedResultRevision = revision,
            controlId = "AC-1", determination = "OtherThanSatisfied", method = "Test", notes = "Failed", evidenceIds = Array.Empty<string>() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var reviewed = await client.PostAsJsonAsync(url + "/review", new { expectedResultRevision = revision,
            controlId = "AC-1", determination = "OtherThanSatisfied", method = "Test", catSeverity = "CatII", notes = "Explicit SCA test", evidenceIds = Array.Empty<string>() });
        reviewed.StatusCode.Should().Be(HttpStatusCode.OK, await reviewed.Content.ReadAsStringAsync());
        (await reviewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("item").GetProperty("reviewedControlCount").GetInt32().Should().Be(1);
        (await client.PostAsJsonAsync(url + "/review", new { expectedResultRevision = revision,
            controlId = "AC-1", determination = "Satisfied", method = "Examine", notes = "Stale", evidenceIds = Array.Empty<string>() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var plan = await db.SecurityAssessmentPlans.SingleAsync(x => x.Id == f.Plan);
            plan.Revision++;
            plan.Content = "Changed selected plan";
            await db.SaveChangesAsync();
            (await db.ControlEffectivenessRecords.SingleAsync(x => x.AssessmentId == f.Assessment)).Notes.Should().Be("Explicit SCA test");
            (await db.ComplianceSnapshots.Where(x => x.IsImmutable).ToListAsync()).Should().Contain(x => x.ControlFamilyBreakdown!.Contains("Explicit SCA test"));
        }
        var changed = await ReadAsync(client, url + "?planId=" + f.Plan);
        changed.GetProperty("item").GetProperty("planRevision").GetInt64().Should().Be(1);
        changed.GetProperty("item").GetProperty("requiresReconciliation").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DraftSar_IsIdempotent_SelectedSourcesOnly_RetainedAndSystemScoped()
    {
        // Arrange
        var f = await SeedAsync();
        var foreign = await SeedAsync();
        using var client = Client(f.Actor);
        await using (var setup = _factory.Services.CreateAsyncScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var unselected = new ComplianceAssessment { TenantId = WorkspaceMembershipFactory.TenantAId,
                RegisteredSystemId = f.System, Status = AssessmentStatus.Completed, InitiatedBy = "other scanner" };
            unselected.Findings.Add(new() { TenantId = WorkspaceMembershipFactory.TenantAId, AssessmentId = unselected.Id,
                ControlId = "AC-1", Title = "Unselected finding must not appear" });
            setupDb.Assessments.Add(unselected);
            await setupDb.SaveChangesAsync();
        }
        var result = (await ReadAsync(client, f.Root + "/results/assessment:" + f.Assessment)).GetProperty("item");
        var request = new { planId = f.Plan, expectedPlanHash = AssessmentResultProvenance.Hash("Saved scope"),
            resultIds = new[] { "assessment:" + f.Assessment },
            expectedResultRevisions = new Dictionary<string, string> { ["assessment:" + f.Assessment] = result.GetProperty("revision").GetString()! },
            requestId = Guid.NewGuid().ToString(), title = "Retained selected observations" };
        // Act
        var response = await client.PostAsJsonAsync(f.Root + "/reports", request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        var retry = await client.PostAsJsonAsync(f.Root + "/reports", request);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString().Should().Be(report.GetProperty("id").GetString());
        report.GetProperty("sections").ToString().Should().Contain("pending").And.NotContain("100 controls");
        report.GetProperty("sections").ToString().Should().NotContain("Unselected finding must not appear");
        (await client.GetAsync(foreign.Root + "/reports/" + report.GetProperty("id").GetString())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/systems/{foreign.System}/sar/{report.GetProperty("id").GetString()}/export")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var retained = await ReadAsync(client, f.Root + "/reports/" + report.GetProperty("id").GetString());
        retained.GetProperty("sections").ToString().Should().Be(report.GetProperty("sections").ToString());
        var exported = await client.GetAsync(report.GetProperty("downloadUrl").GetString());
        exported.StatusCode.Should().Be(HttpStatusCode.OK, await exported.Content.ReadAsStringAsync());
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync())))
        using (var document = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
        {
            var text = await document.ReadToEndAsync();
            text.Should().Contain("Observed gap").And.NotContain("Unselected finding must not appear");
        }
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var stored = await db.SecurityAssessmentReports.SingleAsync(x => x.Id == report.GetProperty("id").GetString());
        stored.SatisfiedCount.Should().Be(0);
        stored.TotalControlsAssessed.Should().Be(0);
        stored.TotalControlsPending.Should().Be(2);
    }

    [Fact]
    public async Task ImportUpload_PinsBeforeQueue_DeduplicatesAndRejectsStalePlan()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        var requestId = Guid.NewGuid().ToString();
        MultipartFormDataContent Upload(string hash)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent("<CHECKLIST><STIGS/></CHECKLIST>"), "file", "scan.ckl");
            form.Add(new StringContent(f.Plan), "planId");
            form.Add(new StringContent(hash), "expectedPlanHash");
            form.Add(new StringContent(requestId), "requestId");
            return form;
        }
        // Act
        using var stale = Upload("stale");
        var rejected = await client.PostAsync($"/api/dashboard/systems/{f.System}/scans/import", stale);
        // Assert
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict, await rejected.Content.ReadAsStringAsync());
        using var file = Upload(AssessmentResultProvenance.Hash("Saved scope"));
        var first = await client.PostAsync($"/api/dashboard/systems/{f.System}/scans/import", file);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync());
        var job = await first.Content.ReadFromJsonAsync<JsonElement>();
        using var repeated = Upload(AssessmentResultProvenance.Hash("Saved scope"));
        var retry = await client.PostAsync($"/api/dashboard/systems/{f.System}/scans/import", repeated);
        (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("importJobId").GetString().Should().Be(job.GetProperty("importJobId").GetString());
        using var changedIntent = Upload("different-plan-hash");
        (await client.PostAsync($"/api/dashboard/systems/{f.System}/scans/import", changedIntent)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var record = await db.ScanImportRecords.SingleAsync(x => x.RegisteredSystemId == f.System);
        AssessmentResultProvenance.Read(record.ResultProvenanceJson).Plan!.Id.Should().Be(f.Plan);
        record.ImportStatus.Should().Be(ScanImportStatus.Queued);
        var page = await ReadAsync(client, f.Root + "/results?planId=" + f.Plan);
        page.GetProperty("items").GetArrayLength().Should().Be(2, "the existing unrelated assessment and individual import are distinct");
        page.GetProperty("items").EnumerateArray().Should().NotContain(x => x.GetProperty("id").GetString() == "assessment:" + record.AssessmentId);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectedSources_WarnOnDuplicates_AndKeepConflictingCurrentReviewsPending(bool conflictingCurrentReviews)
    {
        // Arrange
        var f = await SeedAsync();
        var secondId = Guid.NewGuid().ToString();
        await using (var setup = _factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var second = new ComplianceAssessment { Id = secondId, TenantId = WorkspaceMembershipFactory.TenantAId,
                RegisteredSystemId = f.System, Status = AssessmentStatus.Completed, InitiatedBy = "second collector" };
            second.Findings.Add(new() { TenantId = WorkspaceMembershipFactory.TenantAId,
                AssessmentId = secondId, ControlId = "AC-1", Title = "Another observation of the same control" });
            db.Assessments.Add(second);
            await db.SaveChangesAsync();
        }
        using var client = Client(f.Actor);
        async Task Review(string sourceId, string determination)
        {
            var path = f.Root + "/results/assessment:" + sourceId;
            var detail = await ReadAsync(client, path);
            var response = await client.PostAsJsonAsync(path + "/review", new
            {
                expectedResultRevision = detail.GetProperty("item").GetProperty("revision").GetString(),
                controlId = "AC-1", determination, method = "Test", notes = "Explicit source-specific SCA review",
                evidenceIds = Array.Empty<string>(), catSeverity = determination == "OtherThanSatisfied" ? "CatII" : null
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
        await Review(secondId, conflictingCurrentReviews ? "Satisfied" : "OtherThanSatisfied");
        await Review(secondId, conflictingCurrentReviews ? "OtherThanSatisfied" : "Satisfied");
        await Review(f.Assessment, "OtherThanSatisfied");
        await Review(f.Assessment, "Satisfied");
        var ids = new[] { "assessment:" + f.Assessment, "assessment:" + secondId };
        var revisions = new Dictionary<string, string>();
        foreach (var id in ids)
            revisions[id] = (await ReadAsync(client, f.Root + "/results/" + id)).GetProperty("item").GetProperty("revision").GetString()!;

        // Act
        var catalog = await ReadAsync(client, f.Root + "/results?planId=" + f.Plan + "&selectedResultIds=" + string.Join(",", ids));
        var response = await client.PostAsJsonAsync(f.Root + "/reports", new
        {
            planId = f.Plan, expectedPlanHash = AssessmentResultProvenance.Hash("Saved scope"),
            resultIds = ids, expectedResultRevisions = revisions, requestId = Guid.NewGuid().ToString(),
            title = "Selected source review reconciliation"
        });

        // Assert
        var readiness = catalog.GetProperty("sarReadiness");
        readiness.GetProperty("canPrepareDraft").GetBoolean().Should().BeTrue("conflicts are advisory, not a new hard gate");
        readiness.GetProperty("observedControlCount").GetInt32().Should().Be(1);
        readiness.GetProperty("reviewedControlCount").GetInt32().Should().Be(conflictingCurrentReviews ? 0 : 1);
        var warnings = readiness.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()).ToArray();
        warnings.Should().Contain(x => x!.Contains("Duplicate observations") && x.Contains("AC-1"));
        warnings.Any(x => x!.Contains("Conflicting current determinations")).Should().Be(conflictingCurrentReviews);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        var reportWarnings = report.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()).ToArray();
        reportWarnings.Should().Contain(x => x!.Contains("Duplicate observations"));
        reportWarnings.Any(x => x!.Contains("Conflicting current determinations")).Should().Be(conflictingCurrentReviews);
        if (conflictingCurrentReviews)
            reportWarnings.Should().Contain(x => x!.Contains(ids[0]) && x.Contains(ids[1]) && x.Contains("pending"));
        await using var verification = _factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sar = await context.SecurityAssessmentReports.SingleAsync(x => x.Id == report.GetProperty("id").GetString());
        sar.SatisfiedCount.Should().Be(conflictingCurrentReviews ? 0 : 1);
        sar.NotSatisfiedCount.Should().Be(0);
        sar.TotalControlsAssessed.Should().Be(conflictingCurrentReviews ? 0 : 1);
        sar.TotalControlsPending.Should().Be(conflictingCurrentReviews ? 2 : 1);
        var retained = JsonSerializer.Deserialize<ScopedSarInput>(sar.SourceSnapshotJson!)!;
        retained.Sources.Should().HaveCount(2);
        retained.Sources.Should().OnlyContain(x => x.Reviews.Length == 2, "all review history is retained even though only current reviews count");
        foreach (var id in new[] { f.Assessment, secondId })
        {
            var source = await context.Assessments.SingleAsync(x => x.Id == id);
            AssessmentResultProvenance.Read(source.ResultProvenanceJson).Reviews.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Results_ProjectsSelectedNamesAndRevisionsAcrossPaginationAndSearch()
    {
        // Arrange
        var f = await SeedAsync();
        var otherId = Guid.NewGuid().ToString();
        await using (var setup = _factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Assessments.Add(new ComplianceAssessment { Id = otherId, TenantId = WorkspaceMembershipFactory.TenantAId,
                RegisteredSystemId = f.System, AssessedAt = DateTime.UtcNow.AddDays(-1),
                Status = AssessmentStatus.Completed, InitiatedBy = "retained collector" });
            await db.SaveChangesAsync();
        }
        using var client = Client(f.Actor);
        var selectedIds = new[] { "assessment:" + f.Assessment, "assessment:" + otherId };
        var first = (await ReadAsync(client, f.Root + "/results/" + selectedIds[0])).GetProperty("item");
        var second = (await ReadAsync(client, f.Root + "/results/" + selectedIds[1])).GetProperty("item");
        // Act
        var page = await ReadAsync(client, f.Root + "/results?page=2&pageSize=1&selectedResultIds=" + string.Join(",", selectedIds));
        var filtered = await ReadAsync(client, f.Root + "/results?search=does-not-match&selectedResultIds=" + string.Join(",", selectedIds));
        // Assert
        page.GetProperty("items").GetArrayLength().Should().Be(1);
        filtered.GetProperty("items").GetArrayLength().Should().Be(0);
        foreach (var catalog in new[] { page, filtered })
        {
            catalog.GetProperty("selectedResults").GetArrayLength().Should().Be(2);
            foreach (var detail in new[] { first, second })
            {
                var item = catalog.GetProperty("selectedResults").EnumerateArray()
                    .Single(x => x.GetProperty("id").GetString() == detail.GetProperty("id").GetString());
                item.GetProperty("name").GetString().Should().Be(detail.GetProperty("name").GetString());
                item.GetProperty("revision").GetString().Should().Be(detail.GetProperty("revision").GetString());
            }
            catalog.GetProperty("reports").ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task Results_ReportHistoryIncludesLegacyRetainedReports_IndependentOfSearch_WithoutRegeneration()
    {
        // Arrange
        var f = await SeedAsync();
        var foreign = await SeedAsync();
        var reportId = Guid.NewGuid().ToString();
        var foreignReportId = Guid.NewGuid().ToString();
        var createdAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        await using (var setup = _factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.SecurityAssessmentReports.AddRange(
                new SecurityAssessmentReport { Id = reportId, TenantId = WorkspaceMembershipFactory.TenantAId,
                    RegisteredSystemId = f.System, Title = "Existing legacy SAR", Status = SarStatus.Draft,
                    CreatedAt = createdAt, CreatedBy = "original assessor",
                    Sections = [new SarSection { TenantId = WorkspaceMembershipFactory.TenantAId,
                        SectionType = SarSectionType.ExecutiveSummary, Title = "Retained summary", Content = "Original legacy report text" }] },
                new SecurityAssessmentReport { Id = foreignReportId, TenantId = WorkspaceMembershipFactory.TenantAId,
                    RegisteredSystemId = foreign.System, Title = "Foreign SAR", Status = SarStatus.Draft, CreatedBy = "fixture" });
            await db.SaveChangesAsync();
        }
        using var client = Client(f.Actor);
        // Act
        var catalog = await ReadAsync(client, f.Root + "/results?search=not-a-result&selectedResultIds=");
        // Assert
        catalog.GetProperty("items").GetArrayLength().Should().Be(0);
        catalog.GetProperty("reports").GetArrayLength().Should().Be(1);
        var summary = catalog.GetProperty("reports")[0];
        summary.GetProperty("id").GetString().Should().Be(reportId);
        summary.GetProperty("title").GetString().Should().Be("Existing legacy SAR");
        summary.GetProperty("status").GetString().Should().Be("Draft");
        summary.GetProperty("createdAt").GetDateTime().Should().Be(createdAt);
        var retained = await ReadAsync(client, f.Root + "/reports/" + reportId);
        retained.GetProperty("sections")[0].GetProperty("content").GetString().Should().Be("Original legacy report text");
        retained.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()).Should()
            .Contain("Legacy report: selected-source provenance unknown.");
        retained.GetProperty("sourceResultIds").GetArrayLength().Should().Be(0);
        await using var verification = _factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await context.SecurityAssessmentReports.CountAsync(x => x.RegisteredSystemId == f.System)).Should().Be(1);
    }

    [Fact]
    public async Task Collection_UsesEverySubscription_RetainsPartialWork_AndConcurrentRetryCannotDuplicate()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        var subscriptions = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        _environment.Setup(x => x.GetReadinessAsync(f.System, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AssessmentReadinessResponse(f.System, true, null, "Ready", null, "", "Government", "Government",
                subscriptions.Select(id => new AssessmentSubscriptionResponse(id, "Configured subscription")).ToArray(), DateTimeOffset.UtcNow));
        var attempts = new Dictionary<string, int>();
        _engine.Setup(x => x.RunRetainedAssessmentAsync(It.IsAny<ComplianceAssessment>(), null, It.IsAny<CancellationToken>()))
            .Returns(async (ComplianceAssessment assessment, IProgress<AssessmentProgress>? _, CancellationToken ct) =>
            {
                attempts[assessment.SubscriptionId] = attempts.GetValueOrDefault(assessment.SubscriptionId) + 1;
                if (assessment.SubscriptionId == subscriptions[1] && attempts[assessment.SubscriptionId] == 1)
                    throw new InvalidOperationException("Scanner failed");
                assessment.Status = AssessmentStatus.Completed;
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                var saved = await db.Assessments.SingleAsync(x => x.Id == assessment.Id, ct);
                saved.Status = AssessmentStatus.Completed;
                await db.SaveChangesAsync(ct);
                return assessment;
            });
        var request = new { planId = f.Plan, expectedPlanHash = AssessmentResultProvenance.Hash("Saved scope"), requestId = Guid.NewGuid().ToString() };
        // Act
        var first = await client.PostAsJsonAsync(f.Root + "/collect", request);
        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var partial = await first.Content.ReadFromJsonAsync<JsonElement>();
        partial.GetProperty("status").GetString().Should().Be("Partial");
        var retries = await Task.WhenAll(client.PostAsJsonAsync(f.Root + "/collect", request), client.PostAsJsonAsync(f.Root + "/collect", request));
        foreach (var response in retries)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var completed = await response.Content.ReadFromJsonAsync<JsonElement>();
            completed.GetProperty("status").GetString().Should().Be("Completed");
            completed.GetProperty("resultIds").EnumerateArray().Select(x => x.GetString()).Should()
                .BeEquivalentTo(partial.GetProperty("resultIds").EnumerateArray().Select(x => x.GetString()));
        }
        attempts[subscriptions[0]].Should().Be(1, "successful subscription work is not repeated");
        attempts[subscriptions[1]].Should().Be(2);
        await using var verification = _factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await context.Assessments.Where(x => x.RegisteredSystemId == f.System && x.WorkspaceOperationKey != null).ToListAsync()).Should().HaveCount(2);
        (await context.ControlEffectivenessRecords.CountAsync(x => x.RegisteredSystemId == f.System)).Should().Be(0);
    }

    [Fact]
    public async Task Collection_ReadinessDenied_DoesNotCreateRun_AndUnavailableProbePreservesResults()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        // Act
        var denied = await client.PostAsJsonAsync(f.Root + "/collect", new { planId = (string?)null,
            expectedPlanHash = (string?)null, requestId = Guid.NewGuid().ToString() });
        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _engine.Verify(x => x.RunRetainedAssessmentAsync(It.IsAny<ComplianceAssessment>(), It.IsAny<IProgress<AssessmentProgress>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _environment.Setup(x => x.GetReadinessAsync(f.System, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Readiness source failed"));
        var historical = await ReadAsync(client, f.Root + "/results");
        historical.GetProperty("items").GetArrayLength().Should().Be(1);
        historical.GetProperty("collection").GetProperty("canRunAzure").GetBoolean().Should().BeFalse();
        historical.GetProperty("collection").GetProperty("azure").GetProperty("state").GetString().Should().Be("Unavailable");
    }

    [Theory]
    [InlineData(OrganizationRole.Issm, false)]
    [InlineData(OrganizationRole.Assessor, true)]
    public async Task ConfigurationPermission_UsesExistingWriterPolicy_NotManagementRole(OrganizationRole role, bool writer)
    {
        // Arrange
        var f = await SeedAsync(role);
        using var client = Client(f.Actor);
        if (writer) client.DefaultRequestHeaders.Add("X-Test-Assessment-Writer", "true");
        var configuration = new AssessmentEnvironmentResponse(f.System, "Government", null, [], []);
        _environment.Setup(x => x.GetConfigurationAsync(f.System, It.IsAny<CancellationToken>())).ReturnsAsync(configuration);
        _environment.Setup(x => x.ConfigureAsync(f.System, It.IsAny<UpdateAssessmentEnvironmentRequest>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(configuration);
        // Act
        var catalog = await ReadAsync(client, f.Root + "/results");
        var read = await client.GetAsync($"/api/dashboard/systems/{f.System}/assessment-environment");
        var write = await client.PutAsJsonAsync($"/api/dashboard/systems/{f.System}/assessment-environment",
            new { cloudEnvironment = "Government", subscriptionIds = Array.Empty<string>() });
        // Assert
        catalog.GetProperty("collection").GetProperty("canConfigureAzure").GetBoolean().Should().Be(writer);
        read.StatusCode.Should().Be(writer ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await read.Content.ReadAsStringAsync());
        write.StatusCode.Should().Be(writer ? HttpStatusCode.OK : HttpStatusCode.Forbidden, await write.Content.ReadAsStringAsync());
        _environment.Verify(x => x.ConfigureAsync(f.System, It.IsAny<UpdateAssessmentEnvironmentRequest>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), writer ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task ViewOnlyHistory_DoesNotProbeAzure_AndActualSourceFailuresRemainExplicit()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        _environment.Setup(x => x.GetReadinessAsync(f.System, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider unavailable"));
        // Act
        var history = await ReadAsync(client, f.Root + "/results");
        // Assert
        history.GetProperty("items").GetArrayLength().Should().Be(1);
        history.GetProperty("collection").GetProperty("azure").GetProperty("state").GetString().Should().Be("NotChecked");
        history.GetProperty("collection").GetProperty("azure").GetProperty("checkedAt").ValueKind.Should().Be(JsonValueKind.Null);
        _environment.Verify(x => x.GetReadinessAsync(f.System, It.IsAny<CancellationToken>()), Times.Never);
        await using (var setup = _factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var assessment = await db.Assessments.SingleAsync(x => x.Id == f.Assessment);
            assessment.ResultProvenanceJson = "{invalid source";
            await db.SaveChangesAsync();
        }
        var failedRead = await client.GetAsync(f.Root + "/results");
        failedRead.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(OrganizationRole.Issm, false)]
    [InlineData(OrganizationRole.Assessor, true)]
    public async Task EnvironmentAccess_UsesWriterPolicy_WithoutProbesOrHistoricalResultReads(OrganizationRole role, bool writer)
    {
        // Arrange
        var f = await SeedAsync(role);
        using var client = Client(f.Actor);
        if (writer) client.DefaultRequestHeaders.Add("X-Test-Assessment-Writer", "true");
        _environment.Setup(x => x.GetReadinessAsync(f.System, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Must not probe Azure for permission"));
        _environment.Setup(x => x.GetConfigurationAsync(f.System, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Must not expose subscriptions for permission"));
        await using (var setup = _factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var result = await db.Assessments.SingleAsync(x => x.Id == f.Assessment);
            result.ResultProvenanceJson = "{invalid unrelated historical result";
            await db.SaveChangesAsync();
        }
        // Act
        var access = await ReadAsync(client, $"/api/dashboard/systems/{f.System}/assessment-environment/access");
        // Assert
        access.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo("systemId", "canConfigure", "reason");
        access.GetProperty("systemId").GetString().Should().Be(f.System);
        access.GetProperty("canConfigure").GetBoolean().Should().Be(writer);
        access.GetProperty("reason").ValueKind.Should().Be(writer ? JsonValueKind.Null : JsonValueKind.String);
        _environment.Verify(x => x.GetReadinessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _environment.Verify(x => x.GetConfigurationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnvironmentAccess_WriterCannotReadForeignSystemDecision()
    {
        // Arrange
        var f = await SeedAsync();
        var foreign = await SeedAsync();
        using var client = Client(f.Actor);
        client.DefaultRequestHeaders.Add("X-Test-Assessment-Writer", "true");
        // Act
        var response = await client.GetAsync($"/api/dashboard/systems/{foreign.System}/assessment-environment/access");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _environment.Verify(x => x.GetReadinessAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _environment.Verify(x => x.GetConfigurationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Collection_ResourceRestrictedBoundary_RejectsRatherThanScanningWholeSubscription()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        _environment.Setup(x => x.GetReadinessAsync(f.System, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AssessmentReadinessResponse(f.System, true, null, "Ready", null, "", "Government", "Government",
                [new(Guid.NewGuid().ToString(), "Configured subscription")], DateTimeOffset.UtcNow));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AuthorizationBoundaries.Add(new() { TenantId = WorkspaceMembershipFactory.TenantAId,
                RegisteredSystemId = f.System, ResourceId = "/subscriptions/s/resourceGroups/restricted/providers/Microsoft.Storage/storageAccounts/example",
                ResourceType = "Microsoft.Storage/storageAccounts" });
            await db.SaveChangesAsync();
        }
        // Act
        var response = await client.PostAsJsonAsync(f.Root + "/collect", new { planId = (string?)null,
            expectedPlanHash = (string?)null, requestId = Guid.NewGuid().ToString() });
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _engine.Verify(x => x.RunRetainedAssessmentAsync(It.IsAny<ComplianceAssessment>(),
            It.IsAny<IProgress<AssessmentProgress>?>(), It.IsAny<CancellationToken>()), Times.Never);
        var result = await ReadAsync(client, f.Root + "/results");
        result.GetProperty("collection").GetProperty("canRunAzure").GetBoolean().Should().BeFalse();
        result.GetProperty("collection").GetProperty("canImport").GetBoolean().Should().BeTrue();
    }

    private HttpClient Client(Guid actor)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", WorkspaceMembershipFactory.TenantAId.ToString());
        return client;
    }
    private sealed class AssessmentWriterClaimsFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (http, run) =>
            {
                if (http.Request.Headers["X-Test-Assessment-Writer"] == "true"
                    && http.User.Identity is ClaimsIdentity identity)
                    identity.AddClaim(new Claim(ClaimTypes.Role, Ato.Copilot.Core.Constants.ComplianceRoles.Analyst));
                await run();
            });
            next(app);
        };
    }

    private sealed record Fixture(Guid Actor, string System, string Plan, string Assessment)
    {
        public string Root => $"/api/dashboard/systems/{System}/assessment-workspace";
    }
}
