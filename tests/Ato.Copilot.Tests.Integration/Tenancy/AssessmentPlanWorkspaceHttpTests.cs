using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class AssessmentPlanWorkspaceHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid Directory = Guid.NewGuid();
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private readonly WebApplicationFactory<McpProgram> _factory;

    public AssessmentPlanWorkspaceHttpTests(WorkspaceMembershipFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType != typeof(TenancySeedHostedService)).ToArray())
                services.Remove(descriptor);
        }));
    }

    [Fact]
    public async Task GeneratedEvidenceCounts_RequireAssessmentSystemOwnership_AndRemainRetainedOnReads()
    {
        // Arrange
        var first = await SeedAsync();
        var second = await SeedAsync();
        var subscription = Guid.NewGuid().ToString();
        string firstAssessmentId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.SecurityAssessmentPlans.RemoveRange(await db.SecurityAssessmentPlans
                .Where(p => p.Id == first.Plan || p.Id == second.Plan).ToListAsync());
            var owned = new ComplianceAssessment { TenantId = Tenant, RegisteredSystemId = first.System, SubscriptionId = subscription };
            var otherSystem = new ComplianceAssessment { TenantId = Tenant, RegisteredSystemId = second.System, SubscriptionId = subscription };
            var unowned = new ComplianceAssessment { TenantId = Tenant, SubscriptionId = subscription };
            var foreignSystem = new RegisteredSystem { TenantId = WorkspaceMembershipFactory.TenantBId, Name = "Foreign evidence system" };
            var foreignAssessment = new ComplianceAssessment { TenantId = WorkspaceMembershipFactory.TenantBId,
                RegisteredSystemId = foreignSystem.Id, SubscriptionId = subscription };
            db.AddRange(owned, otherSystem, unowned, foreignSystem, foreignAssessment);
            firstAssessmentId = owned.Id;
            db.Evidence.Add(new() { TenantId = Tenant, AssessmentId = owned.Id, ControlId = "AC-1", SubscriptionId = subscription });
            foreach (var control in new[] { "AC-1", "AC-2" })
            {
                for (var i = 0; i < 3; i++)
                    db.Evidence.Add(new() { TenantId = Tenant, AssessmentId = otherSystem.Id, ControlId = control, SubscriptionId = subscription });
                db.Evidence.Add(new() { TenantId = Tenant, AssessmentId = unowned.Id, ControlId = control, SubscriptionId = subscription });
                db.Evidence.Add(new() { TenantId = Tenant, ControlId = control, SubscriptionId = subscription });
                db.Evidence.Add(new() { TenantId = WorkspaceMembershipFactory.TenantBId, AssessmentId = foreignAssessment.Id,
                    ControlId = control, SubscriptionId = subscription });
                // Same-tenant evidence with a foreign-tenant provenance link is not owned by either visible system.
                db.Evidence.Add(new() { TenantId = Tenant, AssessmentId = foreignAssessment.Id, ControlId = control, SubscriptionId = subscription });
            }
            await db.SaveChangesAsync();
        }
        using var firstClient = Client(first.Actor);
        using var secondClient = Client(second.Actor);

        // Act
        var firstResponse = await firstClient.PostAsJsonAsync(first.Root + "/plans", new { requestId = Guid.NewGuid().ToString() });
        var secondResponse = await secondClient.PostAsJsonAsync(second.Root + "/plans", new { requestId = Guid.NewGuid().ToString() });

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK, await firstResponse.Content.ReadAsStringAsync());
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK, await secondResponse.Content.ReadAsStringAsync());
        var firstPlan = (await firstResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan").GetProperty("id").GetString()!;
        var secondPlan = (await secondResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan").GetProperty("id").GetString()!;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var firstCounts = await db.SapControlEntries.Where(e => e.SecurityAssessmentPlanId == firstPlan)
                .ToDictionaryAsync(e => e.ControlId, e => e.EvidenceCollected);
            firstCounts.Should().BeEquivalentTo(new Dictionary<string, int> { ["AC-1"] = 1, ["AC-2"] = 0 });
            var secondCounts = await db.SapControlEntries.Where(e => e.SecurityAssessmentPlanId == secondPlan)
                .ToDictionaryAsync(e => e.ControlId, e => e.EvidenceCollected);
            secondCounts.Should().BeEquivalentTo(new Dictionary<string, int> { ["AC-1"] = 3, ["AC-2"] = 3 });
            db.Evidence.Add(new() { TenantId = Tenant, AssessmentId = firstAssessmentId, ControlId = "AC-2", SubscriptionId = subscription });
            await db.SaveChangesAsync();
        }
        var retained = await SnapshotAsync(firstPlan);
        await GetAsync(firstClient, first.Root + "/plan?planId=" + firstPlan);
        await GetAsync(firstClient, first.Root + $"/plans/{firstPlan}/preview");
        await using var verify = _factory.Services.CreateAsyncScope();
        var service = verify.ServiceProvider.GetRequiredService<ISapService>();
        (await service.GetSapAsync(sapId: firstPlan)).EvidenceGaps.Should().Be(2);
        (await service.GetWorkingSapAsync(first.System))!.ControlEntries.Single(e => e.ControlId == "AC-2").EvidenceCollected.Should().Be(0);
        (await SnapshotAsync(firstPlan)).Should().Be(retained);
    }

    [Fact]
    public async Task SavedTasks_PreserveStructure_RenderContent_AndPreviewNeverWrites()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var workspace = await GetAsync(client, f.Root + "/plan");
        var plan = workspace.GetProperty("plan");
        var original = await SnapshotAsync(f.Plan);

        // Act
        var preview = await GetAsync(client, f.Root + $"/plans/{f.Plan}/preview");

        // Assert
        preview.GetProperty("content").GetString().Should().Be(original.Content);
        (await SnapshotAsync(f.Plan)).Should().Be(original);
        workspace.GetProperty("leadOptions").EnumerateArray().Should()
            .Contain(x => x.GetProperty("id").GetString() == f.Person.ToString());
        var changed = await SaveAsync(client, f, plan, new { task = "lead", assessmentLeadId = f.Person.ToString() });
        changed.GetProperty("assessmentLead").GetString().Should().Be("Named assessor");
        changed.GetProperty("teamMembers").GetArrayLength().Should().Be(1);
        changed.GetProperty("controls")[0].GetProperty("objectives")[0].GetString().Should().Be("Retained objective");
        changed = await SaveAsync(client, f, changed, new { task = "scope",
            scopeNotes = "Exact mission scope", includedControlIds = new[] { "AC-1" },
            exclusionReasons = new Dictionary<string, string> { ["AC-2"] = "Provider testing retained separately" } });
        changed.GetProperty("scopeCount").GetInt32().Should().Be(1);
        changed.GetProperty("controls").GetArrayLength().Should().Be(2);
        changed = await SaveAsync(client, f, changed, new { task = "approach",
            assessmentApproach = "Interview owners then examine evidence", rulesOfEngagement = "Approved maintenance window" });
        changed = await SaveAsync(client, f, changed, new { task = "procedures",
            methodOverrides = new[] { new { controlId = "AC-1", methods = new[] { "Examine", "Test" }, rationale = "Test the selected mission boundary" } } });
        changed.GetProperty("controls")[0].GetProperty("methods").GetArrayLength().Should().Be(2);
        changed = await SaveAsync(client, f, changed, new { task = "schedule", scheduleStart = (string?)null, scheduleEnd = (string?)null });
        changed.GetProperty("scheduleStart").ValueKind.Should().Be(JsonValueKind.Null);
        preview = await GetAsync(client, f.Root + $"/plans/{f.Plan}/preview");
        preview.GetProperty("content").GetString().Should().Contain("Named assessor")
            .And.Contain("Exact mission scope").And.Contain("Provider testing retained separately")
            .And.Contain("Interview owners then examine evidence").And.Contain("Retained objective")
            .And.Contain("Test the selected mission boundary");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemRoleAssignments.CountAsync(x => x.RegisteredSystemId == f.System)).Should().Be(1);
        (await db.RmfRoleAssignments.CountAsync(x => x.RegisteredSystemId == f.System)).Should().Be(0);
    }

    [Fact]
    public async Task AdvisoryWarnings_FinalizeAndRevise_PreserveHistory_AndRetryUsesSameDraft()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var workspace = await GetAsync(client, f.Root + "/plan");
        var plan = workspace.GetProperty("plan");

        // Act
        plan = await SaveAsync(client, f, plan, new { task = "team", teamMembers = Array.Empty<object>() });
        workspace = await GetAsync(client, f.Root + "/plan");

        // Assert
        workspace.GetProperty("warnings").GetArrayLength().Should().BeGreaterThan(0);
        workspace.GetProperty("finalizationBlockers").GetArrayLength().Should().Be(0);
        var finalize = await client.PostAsJsonAsync(f.Root + $"/plans/{f.Plan}/finalize", Version(plan));
        finalize.StatusCode.Should().Be(HttpStatusCode.OK, await finalize.Content.ReadAsStringAsync());
        var finalized = (await finalize.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan");
        var before = await SnapshotAsync(f.Plan);
        var request = new { requestId = Guid.NewGuid().ToString(), previousPlanId = f.Plan,
            expectedContentHash = finalized.GetProperty("contentHash").GetString() };
        var first = await client.PostAsJsonAsync(f.Root + "/plans", request);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var draft = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan");
        draft.GetProperty("controls")[0].GetProperty("objectives")[0].GetString().Should().Be("Retained objective");
        draft.GetProperty("assessmentLead").GetString().Should().Be("Legacy lead");
        var second = await client.PostAsJsonAsync(f.Root + "/plans", request);
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan").GetProperty("id").GetString()
            .Should().Be(draft.GetProperty("id").GetString()).And.NotBe(f.Plan);
        (await SnapshotAsync(f.Plan)).Should().Be(before);
        var selected = (await GetAsync(client, f.Root + "/plan")).GetProperty("plan");
        selected.GetProperty("id").GetString().Should().Be(draft.GetProperty("id").GetString());
        var immutable = await client.PutAsJsonAsync(f.Root + $"/plans/{f.Plan}",
            new { task = "title", title = "Forbidden", expectedRevision = finalized.GetProperty("revision").GetInt64(),
                expectedContentHash = finalized.GetProperty("contentHash").GetString() });
        immutable.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task StaleUpdatesAndFinalize_AndForeignSources_AreRejected()
    {
        // Arrange
        var f = await SeedAsync();
        var other = await SeedAsync();
        using var client = Client(f.Actor);
        var plan = (await GetAsync(client, f.Root + "/plan")).GetProperty("plan");

        // Act
        await SaveAsync(client, f, plan, new { task = "title", title = "Saved title" });

        // Assert
        (await client.PostAsJsonAsync(f.Root + $"/plans/{f.Plan}/finalize", Version(plan)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var stale = await client.PutAsJsonAsync(f.Root + $"/plans/{f.Plan}",
            new { task = "title", title = "Stale", expectedRevision = plan.GetProperty("revision").GetInt64(),
                expectedContentHash = plan.GetProperty("contentHash").GetString() });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetAsync(f.Root + "/plan?planId=" + other.Plan)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(f.Root + $"/plans/{other.Plan}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        plan = (await GetAsync(client, f.Root + "/plan")).GetProperty("plan");
        foreach (var fields in new object[] {
            new { task = "lead", assessmentLeadId = other.Person.ToString() },
            new { task = "scope", includedControlIds = new[] { "FOREIGN-CONTROL" } } })
        {
            var body = JsonSerializer.SerializeToElement(fields).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            body["expectedRevision"] = plan.GetProperty("revision").GetInt64();
            body["expectedContentHash"] = plan.GetProperty("contentHash").GetString();
            (await client.PutAsJsonAsync(f.Root + $"/plans/{f.Plan}", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task BinaryExports_UseExactSelectedPlan_IncludeSavedFields_AndNeverWrite()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var plan = (await GetAsync(client, f.Root + "/plan")).GetProperty("plan");
        plan = await SaveAsync(client, f, plan, new { task = "lead", assessmentLeadId = f.Person.ToString() });
        plan = await SaveAsync(client, f, plan, new { task = "scope", scopeNotes = "SCOPE-SENTINEL",
            includedControlIds = new[] { "AC-1" }, exclusionReasons = new Dictionary<string, string> { ["AC-2"] = "EXCLUSION-SENTINEL" } });
        plan = await SaveAsync(client, f, plan, new { task = "approach", assessmentApproach = "APPROACH-SENTINEL", rulesOfEngagement = "RULES-SENTINEL" });
        plan = await SaveAsync(client, f, plan, new { task = "schedule", scheduleStart = "2026-10-01", scheduleEnd = "2026-10-15" });
        plan = await SaveAsync(client, f, plan, new { task = "team", teamMembers = new[] {
            new { name = "TEAM-SENTINEL", organization = "ORG-SENTINEL", role = "Observer", contactInfo = "team@example.invalid" } } });
        string historyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var history = new SecurityAssessmentPlan { TenantId = Tenant, RegisteredSystemId = f.System,
                Status = SapStatus.Finalized, Title = "HISTORICAL-ONLY", Content = "Historical retained Markdown",
                BaselineLevel = "Low", GeneratedBy = "fixture", AssessmentLead = "HISTORICAL-LEAD" };
            db.Add(history);
            await db.SaveChangesAsync();
            historyId = history.Id;
        }
        var snapshot = await SnapshotAsync(f.Plan);
        var historicalSnapshot = await SnapshotAsync(historyId);

        // Act / Assert
        byte[]? docx = null;
        foreach (var format in new[] { "docx", "pdf" })
        {
            var response = await client.GetAsync(f.Root + $"/plans/{f.Plan}/export?format={format}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var content = ExportText(bytes, format);
            content.Should().Contain("Named assessor").And.Contain("SCOPE-SENTINEL").And.Contain("EXCLUSION-SENTINEL")
                .And.Contain("APPROACH-SENTINEL").And.Contain("RULES-SENTINEL").And.Contain("TEAM-SENTINEL")
                .And.Contain("ORG-SENTINEL").And.Contain("Observer").And.Contain("team@example.invalid")
                .And.Contain("2026-10-01").And.Contain("2026-10-15").And.Contain("Retained objective")
                .And.Contain(f.Plan).And.NotContain("HISTORICAL-ONLY");
            if (format == "docx") docx = bytes;
            var history = await client.GetAsync(f.Root + $"/plans/{historyId}/export?format={format}");
            history.StatusCode.Should().Be(HttpStatusCode.OK, await history.Content.ReadAsStringAsync());
            ExportText(await history.Content.ReadAsByteArrayAsync(), format)
                .Should().Contain("HISTORICAL-ONLY").And.Contain("HISTORICAL-LEAD").And.NotContain("SCOPE-SENTINEL");
        }
        (await SnapshotAsync(f.Plan)).Should().Be(snapshot);
        await using var serviceScope = _factory.Services.CreateAsyncScope();
        var renderer = serviceScope.ServiceProvider.GetRequiredService<IDocumentTemplateService>();
        ExportText(await renderer.RenderDocxAsync(f.System, "sap"), "docx")
            .Should().Contain("SCOPE-SENTINEL").And.NotContain("HISTORICAL-ONLY");
        ExportText(await renderer.RenderPdfAsync(f.System, "sap"), "pdf")
            .Should().Contain("SCOPE-SENTINEL").And.NotContain("HISTORICAL-ONLY");

        // An older custom template must not silently discard newly retained plan fields.
        var templateBytes = TemplateWithSystemNameOnly(docx!);
        var template = await renderer.UploadTemplateAsync("Legacy SAP layout", "sap", templateBytes, "fixture");
        var custom = await renderer.RenderDocxAsync(f.System, "sap", template.TemplateId, default, f.Plan);
        ExportText(custom, "docx").Should()
            .Contain("Named assessor").And.Contain("APPROACH-SENTINEL").And.Contain("EXCLUSION-SENTINEL")
            .And.Contain("TEAM-SENTINEL").And.Contain("Retained objective");
        (await SnapshotAsync(f.Plan)).Should().Be(snapshot);
        (await SnapshotAsync(historyId)).Should().Be(historicalSnapshot);
        var dbAfterExport = serviceScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await dbAfterExport.SecurityAssessmentPlans.CountAsync(p => p.RegisteredSystemId == f.System)).Should().Be(2);
        (await client.GetAsync(f.Root + $"/plans/{f.Plan}/export?format=xlsx")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static string ExportText(byte[] bytes, string format)
    {
        if (format == "pdf")
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
            return string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
        }
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var document = archive.GetEntry("word/document.xml")!.Open();
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Join("\n", XDocument.Load(document).Descendants(w + "t").Select(t => t.Value));
    }

    private static byte[] TemplateWithSystemNameOnly(byte[] docx)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("word/document.xml")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open(), Encoding.UTF8);
            writer.Write("""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>{{SystemName}}</w:t></w:r></w:p></w:body>
                </w:document>
                """);
        }
        return stream.ToArray();
    }

    [Fact]
    public async Task EmptyScopeIsAdvisory_AndInvalidFocusedInputsDoNotChangeTheDraft()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var plan = (await GetAsync(client, f.Root + "/plan")).GetProperty("plan");
        var snapshot = await SnapshotAsync(f.Plan);

        // Act / Assert
        foreach (var fields in new object[]
        {
            new { task = "title", title = "" },
            new { task = "unknown" },
            new { task = "scope" },
            new { task = "procedures", methodOverrides = new[] { new { controlId = "AC-1", methods = new[] { "Invented" } } } },
            new { task = "procedures", methodOverrides = new[] { new { controlId = "FOREIGN", methods = new[] { "Test" } } } },
            new { task = "schedule", scheduleStart = "2026-09-30", scheduleEnd = "2026-09-01" },
            new { task = "team", teamMembers = new[] { new { name = "", organization = "Mission", role = "Observer" } } }
        })
        {
            var body = JsonSerializer.SerializeToElement(fields).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            body["expectedRevision"] = plan.GetProperty("revision").GetInt64();
            body["expectedContentHash"] = plan.GetProperty("contentHash").GetString();
            (await client.PutAsJsonAsync(f.Root + $"/plans/{f.Plan}", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await SnapshotAsync(f.Plan)).Should().Be(snapshot);
        }
        plan = await SaveAsync(client, f, plan, new { task = "scope", includedControlIds = Array.Empty<string>() });
        plan.GetProperty("scopeCount").GetInt32().Should().Be(0);
        plan.GetProperty("controls").GetArrayLength().Should().Be(2);
        var workspace = await GetAsync(client, f.Root + "/plan");
        workspace.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain("No controls are included in the assessment scope.");
        workspace.GetProperty("finalizationBlockers").GetArrayLength().Should().Be(0);
        (await client.PostAsJsonAsync(f.Root + $"/plans/{f.Plan}/finalize", Version(plan)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForeignTenantSourceAndMissingVersions_AreRejectedWithoutWrites()
    {
        // Arrange
        var f = await SeedAsync();
        string foreignPlan;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var system = new RegisteredSystem { TenantId = WorkspaceMembershipFactory.TenantBId, Name = "Foreign system" };
            var plan = new SecurityAssessmentPlan { TenantId = WorkspaceMembershipFactory.TenantBId,
                RegisteredSystemId = system.Id, Title = "Foreign plan", BaselineLevel = "Low", GeneratedBy = "fixture" };
            db.AddRange(system, plan);
            await db.SaveChangesAsync();
            foreignPlan = plan.Id;
        }
        using var client = Client(f.Actor);
        var snapshot = await SnapshotAsync(f.Plan);

        // Act / Assert
        (await client.GetAsync(f.Root + "/plan?planId=" + foreignPlan)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(f.Root + $"/plans/{foreignPlan}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(f.Root + $"/plans/{foreignPlan}/export?format=docx")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync(f.Root + "/plans", new { requestId = Guid.NewGuid().ToString(), previousPlanId = foreignPlan }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync(f.Root + $"/plans/{f.Plan}", new { task = "title", title = "No version" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(f.Root + $"/plans/{f.Plan}/finalize", new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SnapshotAsync(f.Plan)).Should().Be(snapshot);
        await using var rendererScope = _factory.Services.CreateAsyncScope();
        var renderer = rendererScope.ServiceProvider.GetRequiredService<IDocumentTemplateService>();
        await FluentActions.Awaiting(() => renderer.RenderDocxAsync(f.System, "sap", null, default, foreignPlan))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ConcurrentEditAndFinalize_HaveOneWinner_AndFinalizedChildrenStayImmutable()
    {
        // Arrange
        var f = await SeedAsync();
        using var client = Client(f.Actor);
        var plan = (await GetAsync(client, f.Root + "/plan")).GetProperty("plan");

        // Act
        var outcomes = await Task.WhenAll(
            client.PutAsJsonAsync(f.Root + $"/plans/{f.Plan}", new { task = "title", title = "Concurrent edit",
                expectedRevision = plan.GetProperty("revision").GetInt64(), expectedContentHash = plan.GetProperty("contentHash").GetString() }),
            client.PostAsJsonAsync(f.Root + $"/plans/{f.Plan}/finalize", Version(plan)));

        // Assert
        outcomes.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        plan = (await GetAsync(client, f.Root + "/plan")).GetProperty("plan");
        if (plan.GetProperty("status").GetString() == "Draft")
            (await client.PostAsJsonAsync(f.Root + $"/plans/{f.Plan}/finalize", Version(plan)))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var child = await db.SapControlEntries.FirstAsync(c => c.SecurityAssessmentPlanId == f.Plan);
        child.AssessmentObjectives = ["Tampered after finalization"];
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Clear();
        var finalized = await db.SecurityAssessmentPlans.SingleAsync(p => p.Id == f.Plan);
        finalized.ContentHash = "tampered";
        FluentActions.Invoking(() => db.SaveChanges()).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => db.SaveChangesAsync(true)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ConcurrentCreateAndRetries_RetainOneDraftAndOperation()
    {
        // Arrange
        var f = await SeedAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.SecurityAssessmentPlans.Remove(await db.SecurityAssessmentPlans.SingleAsync(p => p.Id == f.Plan));
            await db.SaveChangesAsync();
        }
        using var client = Client(f.Actor);
        var request = new { requestId = Guid.NewGuid().ToString() };

        // Act
        var outcomes = await Task.WhenAll(client.PostAsJsonAsync(f.Root + "/plans", request),
            client.PostAsJsonAsync(f.Root + "/plans", request));

        // Assert
        outcomes.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        outcomes.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        var retry = await client.PostAsJsonAsync(f.Root + "/plans", request);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        var plan = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan");
        var id = plan.GetProperty("id").GetString();
        (await client.PostAsJsonAsync(f.Root + $"/plans/{id}/finalize", Version(plan))).StatusCode.Should().Be(HttpStatusCode.OK);
        var lateRetry = await client.PostAsJsonAsync(f.Root + "/plans", request);
        lateRetry.StatusCode.Should().Be(HttpStatusCode.OK, await lateRetry.Content.ReadAsStringAsync());
        (await lateRetry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan").GetProperty("id").GetString().Should().Be(id);
        await using var verify = _factory.Services.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<AtoCopilotContext>().SecurityAssessmentPlans
            .CountAsync(p => p.RegisteredSystemId == f.System)).Should().Be(1);
    }

    [Fact]
    public async Task ReadOnlyRole_ProjectsAndEnforcesWriteDenial()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.MissionOwner);
        using var client = Client(f.Actor);

        // Act
        var workspace = await GetAsync(client, f.Root + "/plan");

        // Assert
        workspace.GetProperty("permissions").GetProperty("canEditPlan").GetBoolean().Should().BeFalse();
        (await client.PostAsJsonAsync(f.Root + "/plans", new { requestId = Guid.NewGuid().ToString() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync(f.Root + $"/plans/{f.Plan}/finalize", Version(workspace.GetProperty("plan"))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static object Version(JsonElement plan) => new {
        expectedRevision = plan.GetProperty("revision").GetInt64(),
        expectedContentHash = plan.GetProperty("contentHash").GetString() };

    private static async Task<JsonElement> SaveAsync(HttpClient client, Fixture f, JsonElement plan, object fields)
    {
        var body = JsonSerializer.SerializeToElement(fields).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        body["expectedRevision"] = plan.GetProperty("revision").GetInt64();
        body["expectedContentHash"] = plan.GetProperty("contentHash").GetString();
        var response = await client.PutAsJsonAsync(f.Root + $"/plans/{f.Plan}", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plan");
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(string Content, string? Hash, SapStatus Status, int Controls, int Team, long Revision, DateTime? UpdatedAt)> SnapshotAsync(string id)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var sap = await db.SecurityAssessmentPlans.Include(x => x.ControlEntries).Include(x => x.TeamMembers).SingleAsync(x => x.Id == id);
        return (sap.Content, sap.ContentHash, sap.Status, sap.ControlEntries.Count, sap.TeamMembers.Count, sap.Revision, sap.UpdatedAt);
    }

    private HttpClient Client(Guid actor)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return client;
    }

    private async Task<Fixture> SeedAsync(OrganizationRole role = OrganizationRole.Assessor)
    {
        using var initialized = _factory.CreateClient();
        var actor = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var person = new Person { TenantId = Tenant, DisplayName = "Named assessor", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = Tenant, Name = "Assessment system " + actor, IsActive = true };
        var sap = new SecurityAssessmentPlan { TenantId = Tenant, RegisteredSystemId = system.Id, Title = "Retained plan",
            BaselineLevel = "Low", GeneratedBy = "fixture", AssessmentLead = "Legacy lead", Content = "Exact retained content",
            TotalControls = 2, CustomerControls = 2, ScheduleStart = DateTime.UtcNow, ScheduleEnd = DateTime.UtcNow.AddDays(1) };
        sap.ControlEntries.Add(new() { TenantId = Tenant, ControlId = "AC-1", ControlTitle = "Policy", ControlFamily = "AC",
            AssessmentObjectives = ["Retained objective"], AssessmentMethods = ["Examine"] });
        sap.ControlEntries.Add(new() { TenantId = Tenant, ControlId = "AC-2", ControlTitle = "Accounts", ControlFamily = "AC" });
        sap.TeamMembers.Add(new() { TenantId = Tenant, Name = "Existing member", Organization = "Mission", Role = "Observer" });
        db.AddRange(person, system, sap);
        db.ControlBaselines.Add(new() { TenantId = Tenant, RegisteredSystemId = system.Id,
            BaselineLevel = "Low", ControlIds = ["AC-1", "AC-2"], TotalControls = 2 });
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = Directory,
            ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
        db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = system.Id, Role = role });
        await db.SaveChangesAsync();
        return new(actor, person.Id, system.Id, sap.Id);
    }

    private sealed record Fixture(Guid Actor, Guid Person, string System, string Plan)
    {
        public string Root => $"/api/dashboard/systems/{System}/assessment-workspace";
    }
}
