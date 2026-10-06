using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Server;

public sealed partial class ProfileDraftPersistenceTests
{
    [Fact]
    public async Task UsersDetails_RoundTripIndependentReviewAndRetainedSspWithoutDraftLeak()
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        const string payload = """
            {"content":"original","childItems":[{"id":"existing","revision":1,"categoryName":"Recorded automation",
             "identityType":"WorkloadIdentity","privilegeLevel":"Privileged","affiliation":"Internal","authenticationMethod":"Managed identity",
             "responsibleOwner":"Recorded service owner","userLocations":"CONUS","permittedEnvironments":"Recorded production scope",
             "authorizedDataTypes":"Recorded inventory metadata","accessMethod":"Recorded API access","dataSensitivityLevel":"CUI"}]}
            """;
        // Act
        var saved = await PutAsync("UsersAndAccess", payload);
        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var detail = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var category = detail.GetProperty("userCategories")[0];
        category.GetProperty("identityType").GetString().Should().Be("WorkloadIdentity");
        category.GetProperty("responsibleOwner").GetString().Should().Be("Recorded service owner");
        var revision = category.GetProperty("revision").GetInt32();
        using (var approvalScope = _app.Services.CreateScope())
        {
            var service = approvalScope.ServiceProvider.GetRequiredService<ISystemProfileService>();
            await service.SubmitForReviewAsync("mission", [ProfileSectionType.UsersAndAccess], "owner");
            await service.ReviewSectionAsync("mission", ProfileSectionType.UsersAndAccess, ReviewDecision.Approve, "reviewer");
        }
        // Act
        (await ReviewRowAsync("existing", "submit", revision)).StatusCode.Should().Be(HttpStatusCode.OK);
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        (await ReviewRowAsync("existing", "approve", revision + 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var scopes = _app.Services.GetRequiredService<IServiceScopeFactory>();
        var export = await new OscalSspExportService(scopes, NullLogger<OscalSspExportService>.Instance).ExportAsync("mission");
        var template = new DocumentTemplateService(scopes, NullLogger<DocumentTemplateService>.Instance);
        var docx = await template.RenderDocxAsync("mission", "ssp");
        var pdf = await template.RenderPdfAsync("mission", "ssp");
        // Assert
        export.OscalJson.Should().Contain("Recorded service owner").And.Contain("Recorded inventory metadata").And.Contain("Managed identity");
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(docx)))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
            (await reader.ReadToEndAsync()).Should().Contain("Recorded service owner").And.Contain("Recorded inventory metadata");
        using (var document = UglyToad.PdfPig.PdfDocument.Open(pdf))
            string.Join("\n", document.GetPages().Select(p => p.Text)).Should().Contain("Recorded service owner").And.Contain("Managed identity");
        // Act
        _user.SetupGet(x => x.CurrentUserId).Returns("owner");
        var successor = payload.Replace("\"revision\":1", $"\"revision\":{revision + 2}")
            .Replace("Recorded service owner", "LATER DRAFT owner");
        (await PutAsync("UsersAndAccess", successor)).StatusCode.Should().Be(HttpStatusCode.OK);
        var retained = await new OscalSspExportService(scopes, NullLogger<OscalSspExportService>.Instance).ExportAsync("mission");
        // Assert
        retained.OscalJson.Should().Contain("Recorded service owner").And.NotContain("LATER DRAFT owner");
        var current = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        current.GetProperty("userCategories")[0].GetProperty("governanceStatus").GetString().Should().Be("Draft");
    }
    [Theory]
    [InlineData("identityType", "AutograntedIdentity")]
    [InlineData("privilegeLevel", "AutoApprovedAdmin")]
    [InlineData("affiliation", "UnknownSource")]
    public async Task UsersDetails_RejectInvalidDocumentationEnumsForNewRows(string field, string value)
    {
        // Arrange
        await SeedSectionAsync();
        var payload = $$"""{"content":"original","childItems":[{"id":"existing","revision":1,"categoryName":"Existing"},{"categoryName":"New","{{field}}":"{{value}}"}]}""";
        // Act
        var result = await PutAsync("UsersAndAccess", payload);
        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task UsersDetails_NewFieldsCannotEditUnderReviewAndLegacyOmissionsPreserveSavedDetails()
    {
        // Arrange
        await SeedSectionAsync();
        var first = await PutAsync("UsersAndAccess", """{"content":"original","childItems":[{"id":"existing","revision":1,"categoryName":"Existing","responsibleOwner":"Retained owner"}]}""");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var revision = detail.GetProperty("userCategories")[0].GetProperty("revision").GetInt32();
        // Act
        var legacy = await PutAsync("UsersAndAccess", $$"""{"content":"original","childItems":[{"id":"existing","revision":{{revision}},"categoryName":"Existing"}]}""");
        // Assert
        legacy.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess")).GetProperty("userCategories")[0]
            .GetProperty("responsibleOwner").GetString().Should().Be("Retained owner");
        // Act
        (await ReviewRowAsync("existing", "submit", revision)).StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = await PutAsync("UsersAndAccess", $$"""{"content":"original","childItems":[{"id":"existing","revision":{{revision + 1}},"categoryName":"Existing","responsibleOwner":"Changed owner"}]}""");
        // Assert
        changed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private Task<HttpResponseMessage> ReviewRowAsync(string id, string action, int revision, string? comments = null) =>
        _client.PostAsJsonAsync(Root + $"UsersAndAccess/user-categories/{id}/review",
            new { action, expectedRevision = revision, comments });

    [Fact]
    public async Task RowReview_OnlyApprovesSelectedRow_AndNeverApprovesContext()
    {
        // Arrange
        await SeedSectionAsync();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.UserCategories.Add(new UserCategory { Id = "sibling", TenantId = _tenant,
                SystemProfileSectionId = "section", CategoryName = "Sibling draft" });
            db.RmfRoleAssignments.Add(new RmfRoleAssignment { TenantId = _tenant,
                RegisteredSystemId = "mission", UserId = "reviewer", RmfRole = RmfRole.Issm });
            await db.SaveChangesAsync();
        }

        // Act
        var submitted = await ReviewRowAsync("existing", "submit", 1);
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        var approved = await ReviewRowAsync("existing", "approve", 2);
        var detail = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");

        // Assert
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        detail.GetProperty("governanceStatus").GetString().Should().Be("Draft");
        detail.GetProperty("reviewScope").GetString().Should().Be("AccessContext");
        var rows = detail.GetProperty("userCategories").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!);
        rows["existing"].GetProperty("governanceStatus").GetString().Should().Be("Approved");
        rows["existing"].GetProperty("revision").GetInt32().Should().Be(3);
        rows["sibling"].GetProperty("governanceStatus").GetString().Should().Be("Draft");
        detail.GetProperty("userCategoriesReview").GetProperty("isComplete").GetBoolean().Should().BeFalse();
        var approvedResponse = (await approved.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!;
        approvedResponse["reviewResult"].GetProperty("revision").GetInt32().Should().Be(3);
        approvedResponse["reviewResult"] = JsonSerializer.SerializeToElement<object?>(null);
        JsonSerializer.Serialize(approvedResponse).Should().Be(detail.GetRawText());
        var export = await new OscalSspExportService(_app.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspExportService>.Instance).ExportAsync("mission");
        export.OscalJson.Should().Contain("Existing").And.NotContain("Sibling draft");
        export.ProfileSourceGaps.Should().Contain(g => g.Contains("scalar access context"));
        export.ProfileSourceGaps.Should().Contain(g => g.Contains("sibling") && g.Contains("no independently approved baseline"));
        (await ReviewRowAsync("existing", "approve", 3)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RowReview_EnforcesPermissionTenantAndExpectedRevision()
    {
        // Arrange
        await SeedSectionAsync();

        // Act
        var forbidden = await ReviewRowAsync("existing", "approve", 1);
        var foreign = await ReviewRowAsync("foreign-child", "submit", 1);
        var submitted = await ReviewRowAsync("existing", "submit", 1);
        var stale = await ReviewRowAsync("existing", "withdraw", 1);

        // Assert
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RowReview_UnderReviewCannotBeChangedOrDeleted()
    {
        // Arrange
        await SeedSectionAsync();
        (await ReviewRowAsync("existing", "submit", 1)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var removed = await PutAsync("UsersAndAccess", """{"content":"original","childItems":[]}""");
        var changed = await PutAsync("UsersAndAccess",
            """{"content":"original","childItems":[{"id":"existing","revision":2,"categoryName":"Changed"}]}""");

        // Assert
        removed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        changed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _app.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().UserCategories.SingleAsync())
            .CategoryName.Should().Be("Existing");
    }

    [Fact]
    public async Task RowReview_RevisionRequestsWithdrawalAndFlags_FollowPersistedRoleAndState()
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        var initial = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");

        // Act
        await ReviewRowAsync("existing", "submit", 1);
        var submitted = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        var reviewerView = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var missingComment = await ReviewRowAsync("existing", "request_revision", 2);
        var revised = await ReviewRowAsync("existing", "request_revision", 2, "Explain access method");
        _user.SetupGet(x => x.CurrentUserId).Returns("owner");
        var revisionView = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var resubmitted = await ReviewRowAsync("existing", "submit", 3);
        var withdrawn = await ReviewRowAsync("existing", "withdraw", 4);
        var final = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");

        // Assert
        initial.GetProperty("userCategories")[0].GetProperty("canSubmit").GetBoolean().Should().BeTrue();
        initial.GetProperty("userCategories")[0].GetProperty("canReview").GetBoolean().Should().BeFalse();
        submitted.GetProperty("userCategories")[0].GetProperty("canWithdraw").GetBoolean().Should().BeTrue();
        reviewerView.GetProperty("userCategories")[0].GetProperty("canReview").GetBoolean().Should().BeTrue();
        missingComment.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        revised.StatusCode.Should().Be(HttpStatusCode.OK);
        revisionView.GetProperty("userCategories")[0].GetProperty("reviewerComments").GetString().Should().Be("Explain access method");
        revisionView.GetProperty("userCategories")[0].GetProperty("governanceStatus").GetString().Should().Be("NeedsRevision");
        resubmitted.StatusCode.Should().Be(HttpStatusCode.OK);
        withdrawn.StatusCode.Should().Be(HttpStatusCode.OK);
        final.GetProperty("userCategories")[0].GetProperty("revision").GetInt32().Should().Be(5);
        final.GetProperty("userCategories")[0].GetProperty("governanceStatus").GetString().Should().Be("Draft");
        using var scope = _app.Services.CreateScope();
        var audits = await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().ProfileAuditEntries
            .Where(a => a.UserCategoryId == "existing").ToListAsync();
        audits.Should().HaveCount(4);
        audits.Should().OnlyContain(a => a.SnapshotHash != null && a.SnapshotJson != null);
    }

    [Fact]
    public async Task RowReview_ApprovedEditAndRemovalKeepHistoryUntilRemovalApproval()
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        using var scope = _app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();
        await service.SubmitForReviewAsync("mission", [ProfileSectionType.UsersAndAccess], "owner");
        await service.ReviewSectionAsync("mission", ProfileSectionType.UsersAndAccess, ReviewDecision.Approve, "reviewer");
        await ReviewRowAsync("existing", "submit", 1);
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        await ReviewRowAsync("existing", "approve", 2);
        var before = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var approvedSnapshot = before.GetProperty("userCategories")[0].GetProperty("approvedSnapshotId").GetString();
        _user.SetupGet(x => x.CurrentUserId).Returns("owner");

        // Act
        var edited = await PutAsync("UsersAndAccess",
            """{"content":"original","childItems":[{"id":"existing","revision":3,"categoryName":"Unreviewed draft"}]}""");
        var stale = await PutAsync("UsersAndAccess",
            """{"content":"must not save","childItems":[{"id":"existing","revision":3,"categoryName":"Stale overwrite"}]}""");
        var exporter = new OscalSspExportService(_app.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspExportService>.Instance);
        var afterEdit = await exporter.ExportAsync("mission");
        var removal = await PutAsync("UsersAndAccess", """{"content":"original","childItems":[]}""");
        var pending = await removal.Content.ReadFromJsonAsync<JsonElement>();
        var afterRemovalRequest = await exporter.ExportAsync("mission");
        await ReviewRowAsync("existing", "submit", 5);
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        var removalApproval = await ReviewRowAsync("existing", "approve", 6);
        var afterApproval = await exporter.ExportAsync("mission");

        // Assert
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        pending.GetProperty("userCategories")[0].GetProperty("pendingDeletion").GetBoolean().Should().BeTrue();
        pending.GetProperty("userCategories")[0].GetProperty("revision").GetInt32().Should().Be(5);
        afterEdit.OscalJson.Should().Contain("Existing").And.NotContain("Unreviewed draft");
        afterRemovalRequest.OscalJson.Should().Contain("Existing");
        removalApproval.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removalApproval.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userCategories").GetArrayLength().Should().Be(0);
        afterApproval.OscalJson.Should().NotContain("Existing").And.NotContain("Unreviewed draft");
        using var inspect = _app.Services.CreateScope();
        var db = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        (await db.UserCategories.SingleAsync()).PendingDeletion.Should().BeTrue();
        (await db.UserCategories.SingleAsync()).GovernanceStatus.Should().Be(SspSectionStatus.Approved);
        (await db.ProfileAuditEntries.SingleAsync(a => a.Id == approvedSnapshot)).SnapshotJson.Should().Contain("Existing");
    }

    [Fact]
    public async Task RowReview_SectionAndBatchApprovalCannotApproveSiblingRowsOrMakeReadinessComplete()
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        using var scope = _app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();

        // Act
        await service.SubmitForReviewAsync("mission", [ProfileSectionType.UsersAndAccess], "owner");
        await service.BatchApproveSectionsAsync("mission", "reviewer");
        var detail = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var readiness = await service.GetCompletenessAsync("mission");
        var overview = await service.GetProfileOverviewAsync("mission");
        var exported = await new OscalSspExportService(_app.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspExportService>.Instance).ExportAsync("mission");

        // Assert
        detail.GetProperty("governanceStatus").GetString().Should().Be("Approved");
        detail.GetProperty("userCategories")[0].GetProperty("governanceStatus").GetString().Should().Be("Draft");
        detail.GetProperty("userCategoriesReview").GetProperty("isComplete").GetBoolean().Should().BeFalse();
        readiness.ApprovedPercentage.Should().Be(0);
        overview.Sections.Single(s => s.SectionType == ProfileSectionType.UsersAndAccess)
            .GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        exported.OscalJson.Should().NotContain("Existing");
        exported.ProfileSourceGaps.Should().Contain(g => g.Contains("no independently approved baseline"));
    }

    [Theory]
    [InlineData("unknown", 1, null)]
    [InlineData("submit", 0, null)]
    [InlineData("request_revision", 1, "")]
    public async Task RowReview_InvalidRequestDoesNotMutate(string action, int revision, string? comments)
    {
        // Arrange
        await SeedSectionAsync();

        // Act
        var response = await ReviewRowAsync("existing", action, revision, comments);

        // Assert
        response.IsSuccessStatusCode.Should().BeFalse();
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.UserCategories.SingleAsync()).Revision.Should().Be(1);
        (await db.ProfileAuditEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RowReview_SchemaUpgradeIsIdempotentAndNeverInfersIndividualApproval()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE UserCategories (Id TEXT PRIMARY KEY, CategoryName TEXT);
            CREATE TABLE ProfileAuditEntries (Id TEXT PRIMARY KEY, SnapshotJson TEXT);
            INSERT INTO UserCategories VALUES ('legacy', 'Legacy row');
            INSERT INTO ProfileAuditEntries VALUES ('old-approval', 'retained old snapshot');
            """);

        // Act
        await UserCategoryReviewSchemaAdditions.ApplyAsync(db);
        await UserCategoryReviewSchemaAdditions.ApplyAsync(db);

        // Assert
        (await db.Database.SqlQueryRaw<string>("SELECT GovernanceStatus AS Value FROM UserCategories").SingleAsync()).Should().Be("Draft");
        (await db.Database.SqlQueryRaw<int>("SELECT Revision AS Value FROM UserCategories").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM UserCategories WHERE ApprovedSnapshotId IS NOT NULL").SingleAsync()).Should().Be(0);
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM UserCategories WHERE IdentityType IS NOT NULL OR ResponsibleOwner IS NOT NULL OR AuthorizedDataTypes IS NOT NULL").SingleAsync()).Should().Be(0);
        (await db.Database.SqlQueryRaw<string>("SELECT SnapshotJson AS Value FROM ProfileAuditEntries").SingleAsync()).Should().Be("retained old snapshot");
    }

    private async Task AddReviewerAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.RmfRoleAssignments.Add(new RmfRoleAssignment { TenantId = _tenant,
            RegisteredSystemId = "mission", UserId = "reviewer", RmfRole = RmfRole.Issm });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowReview_ConcurrentDatabaseRevisionChangeRollsBackActionAndAudit(bool approving)
    {
        // Arrange
        await SeedSectionAsync();
        if (approving)
        {
            await AddReviewerAsync();
            await ReviewRowAsync("existing", "submit", 1);
            _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        }
        _saveHook.BeforeSave = async db =>
            await db.Database.ExecuteSqlRawAsync("UPDATE UserCategories SET Revision = Revision + 1 WHERE Id = 'existing'");

        // Act
        var response = await ReviewRowAsync("existing", approving ? "approve" : "submit", approving ? 2 : 1);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var row = await db.UserCategories.SingleAsync();
        row.Revision.Should().Be(approving ? 3 : 2);
        row.GovernanceStatus.Should().Be(approving ? SspSectionStatus.UnderReview : SspSectionStatus.Draft);
        row.ApprovedSnapshotId.Should().BeNull();
        (await db.ProfileAuditEntries.CountAsync()).Should().Be(approving ? 1 : 0);
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        (await db.SystemProfileSections.SingleAsync()).ApprovedSnapshotId.Should().BeNull();
    }

    [Fact]
    public async Task RowReview_LocksOnlySelectedRowAndScalarContext_LeavingSiblingDraftEditable()
    {
        // Arrange
        await SeedSectionAsync(SspSectionStatus.UnderReview);
        await AddReviewerAsync();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.UserCategories.Add(new UserCategory { Id = "sibling", TenantId = _tenant, SortOrder = 1,
                SystemProfileSectionId = "section", CategoryName = "Sibling" });
            await db.SaveChangesAsync();
        }
        await ReviewRowAsync("existing", "submit", 1);

        // Act
        var updated = await PutAsync("UsersAndAccess", """
            {"content":"original","childItems":[
              {"id":"existing","revision":2,"categoryName":"Existing","sortOrder":0},
              {"id":"sibling","revision":1,"categoryName":"Updated sibling","sortOrder":1,"governanceStatus":"Approved"}
            ]}
            """);
        var scalarUpdate = await PutAsync("UsersAndAccess", """{"content":"locked scalar change"}""");
        using var scope2 = _app.Services.CreateScope();
        var service = scope2.ServiceProvider.GetRequiredService<ISystemProfileService>();
        var queue = await service.GetPendingReviewsAsync("reviewer");

        // Assert
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        scalarUpdate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var db2 = scope2.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db2.UserCategories.SingleAsync(c => c.Id == "existing")).GovernanceStatus.Should().Be(SspSectionStatus.UnderReview);
        (await db2.UserCategories.SingleAsync(c => c.Id == "sibling")).GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        (await db2.UserCategories.SingleAsync(c => c.Id == "sibling")).CategoryName.Should().Be("Updated sibling");
        queue.Should().Contain(q => q.UserCategoryId == "existing" && q.ReviewScope == "UserCategory" && q.Revision == 2);
        queue.Should().Contain(q => q.UserCategoryId == null && q.ReviewScope == "AccessContext");
    }

    [Fact]
    public async Task RowReview_DraftRemovalCanBeCancelledWithoutDestroyingApprovedBaseline()
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        await ReviewRowAsync("existing", "submit", 1);
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        await ReviewRowAsync("existing", "approve", 2);
        _user.SetupGet(x => x.CurrentUserId).Returns("owner");
        await PutAsync("UsersAndAccess", """{"content":"original","childItems":[]}""");

        // Act
        var restored = await PutAsync("UsersAndAccess", """
            {"content":"original","childItems":[{"id":"existing","revision":4,"categoryName":"Existing","pendingDeletion":false}]}
            """);

        // Assert
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _app.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().UserCategories.SingleAsync();
        row.PendingDeletion.Should().BeFalse();
        row.Revision.Should().Be(5);
        row.GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        row.ApprovedSnapshotId.Should().NotBeNull();
    }

    [Fact]
    public async Task RowReview_LegacySectionSnapshotDoesNotGrantIndividualApproval()
    {
        // Arrange
        await SeedSectionAsync(SspSectionStatus.Approved);
        const string snapshot = """{"scalarContent":"original","userCategories":[{"CategoryName":"LEGACY UNREVIEWED CATEGORY"}]}""";
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var audit = new ProfileAuditEntry
            {
                TenantId = _tenant, SystemProfileSectionId = "section", Action = "Approved", PerformedBy = "legacy",
                SnapshotJson = snapshot, SnapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)))
            };
            db.ProfileAuditEntries.Add(audit);
            (await db.SystemProfileSections.SingleAsync()).ApprovedSnapshotId = audit.Id;
            await db.SaveChangesAsync();
        }

        // Act
        var exported = await new OscalSspExportService(_app.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspExportService>.Instance).ExportAsync("mission");

        // Assert
        exported.OscalJson.Should().NotContain("LEGACY UNREVIEWED CATEGORY");
        exported.ProfileSourceGaps.Should().Contain(g => g.Contains("legacy section approval"));
        using var inspect = _app.Services.CreateScope();
        var fresh = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await fresh.ProfileAuditEntries.SingleAsync()).SnapshotJson.Should().Be(snapshot);
        (await fresh.UserCategories.SingleAsync()).GovernanceStatus.Should().Be(SspSectionStatus.Draft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkSave_DisplayRenumberingPreservesUnchangedReviewedRows(bool approved)
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.UserCategories.SingleAsync()).SortOrder = 1;
            db.UserCategories.Add(new UserCategory { Id = "draft-sibling", TenantId = _tenant,
                SystemProfileSectionId = "section", CategoryName = "Removable draft", SortOrder = 0 });
            await db.SaveChangesAsync();
        }
        await ReviewRowAsync("existing", "submit", 1);
        if (approved)
        {
            _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
            await ReviewRowAsync("existing", "approve", 2);
            _user.SetupGet(x => x.CurrentUserId).Returns("owner");
        }
        var current = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var retained = current.GetProperty("userCategories").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == "existing");

        // Act
        var response = await _client.PutAsJsonAsync(Root + "UsersAndAccess",
            new { content = "original", childItems = new[] { retained } });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var inspect = _app.Services.CreateScope();
        var row = await inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>().UserCategories.SingleAsync();
        row.Id.Should().Be("existing");
        row.GovernanceStatus.Should().Be(approved ? SspSectionStatus.Approved : SspSectionStatus.UnderReview);
        row.Revision.Should().Be(approved ? 3 : 2);
        row.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task RemovalReview_ReturnsVerifiedReceiptForHiddenApprovedTombstone()
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        await ReviewRowAsync("existing", "submit", 1);
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        await ReviewRowAsync("existing", "approve", 2);
        _user.SetupGet(x => x.CurrentUserId).Returns("owner");
        await PutAsync("UsersAndAccess", """{"content":"original","childItems":[]}""");
        await ReviewRowAsync("existing", "submit", 4);
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");

        // Act
        var response = await ReviewRowAsync("existing", "approve", 5);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fresh = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        detail.GetProperty("userCategories").GetArrayLength().Should().Be(0);
        fresh.GetProperty("userCategories").GetArrayLength().Should().Be(0);
        var receipt = detail.GetProperty("reviewResult");
        receipt.GetProperty("categoryId").GetString().Should().Be("existing");
        receipt.GetProperty("action").GetString().Should().Be("approve");
        receipt.GetProperty("revision").GetInt32().Should().Be(6);
        receipt.GetProperty("governanceStatus").GetString().Should().Be("Approved");
        receipt.GetProperty("pendingDeletion").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkSave_LockedRowViolationRollsBackScalarAndEarlierSiblingEdits(bool removeLockedRow)
    {
        // Arrange
        await SeedSectionAsync();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.UserCategories.SingleAsync()).SortOrder = 1;
            db.UserCategories.Add(new UserCategory { Id = "draft", TenantId = _tenant,
                SystemProfileSectionId = "section", CategoryName = "Unchanged sibling", SortOrder = 0 });
            await db.SaveChangesAsync();
        }
        await ReviewRowAsync("existing", "submit", 1);
        var rows = new List<object> { new { id = "draft", revision = 1, categoryName = "Must roll back" } };
        if (!removeLockedRow) rows.Add(new { id = "existing", revision = 2, categoryName = "Forbidden edit" });

        // Act
        var response = await _client.PutAsJsonAsync(Root + "UsersAndAccess",
            new { content = "Scalar must roll back", childItems = rows });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var inspect = _app.Services.CreateScope();
        var fresh = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await fresh.SystemProfileSections.SingleAsync()).DraftContent.Should().Be("original");
        var sibling = await fresh.UserCategories.SingleAsync(c => c.Id == "draft");
        sibling.CategoryName.Should().Be("Unchanged sibling");
        sibling.Revision.Should().Be(1);
        var locked = await fresh.UserCategories.SingleAsync(c => c.Id == "existing");
        locked.CategoryName.Should().Be("Existing");
        locked.GovernanceStatus.Should().Be(SspSectionStatus.UnderReview);
        locked.Revision.Should().Be(2);
        (await fresh.ProfileAuditEntries.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkSave_EquivalentEmptyOptionalFieldsDoNotResetReviewedRows(bool approved)
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        await ReviewRowAsync("existing", "submit", 1);
        if (approved)
        {
            _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
            await ReviewRowAsync("existing", "approve", 2);
            _user.SetupGet(x => x.CurrentUserId).Returns("owner");
        }

        // Act
        var response = await _client.PutAsJsonAsync(Root + "UsersAndAccess", new
        {
            content = "original",
            childItems = new[] { new { id = "existing", revision = approved ? 3 : 2, categoryName = "Existing",
                description = "", accessMethod = "", dataSensitivityLevel = "" } }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _app.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().UserCategories.SingleAsync();
        row.GovernanceStatus.Should().Be(approved ? SspSectionStatus.Approved : SspSectionStatus.UnderReview);
        row.Revision.Should().Be(approved ? 3 : 2);
        row.Description.Should().BeNull();
        row.AccessMethod.Should().BeNull();
        row.DataSensitivityLevel.Should().BeNull();
    }

    [Theory]
    [InlineData(SspSectionStatus.UnderReview)]
    [InlineData(SspSectionStatus.Approved)]
    public async Task BulkSave_EquivalentContextJsonPreservesGovernanceAndOriginalBaseline(SspSectionStatus status)
    {
        // Arrange
        await SeedSectionAsync(status);
        const string original = "{ \"access\": \"SSO\", \"reviewed\": true }";
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var section = await db.SystemProfileSections.SingleAsync();
            section.DraftContent = original;
            if (status == SspSectionStatus.Approved) section.ApprovedContent = original;
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PutAsJsonAsync(Root + "UsersAndAccess", new
        {
            content = """{"reviewed":true,"access":"SSO"}""",
            childItems = new[] { new { id = "existing", revision = 1, categoryName = "Updated category" } }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var inspect = _app.Services.CreateScope();
        var fresh = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var saved = await fresh.SystemProfileSections.SingleAsync();
        saved.GovernanceStatus.Should().Be(status);
        saved.DraftContent.Should().Be(original);
        if (status == SspSectionStatus.Approved) saved.ApprovedContent.Should().Be(original);
        (await fresh.UserCategories.SingleAsync()).CategoryName.Should().Be("Updated category");
    }

    [Fact]
    public async Task HttpRoundTrip_TwoRows_IndependentReviewRetainsSnapshotAndRejectsStaleRevision()
    {
        // Arrange
        await AddReviewerAsync();
        const string context = """{"accessMethod":"SSO"}""";
        var createBody = new
        {
            content = context,
            childItems = new[]
            {
                new { categoryName = "Operators", approximateCount = 12, _tempId = "ui-row-a" },
                new { categoryName = "Analysts", approximateCount = 5, _tempId = "ui-row-b" }
            }
        };

        // Act
        var created = await _client.PutAsJsonAsync(Root + "UsersAndAccess", createBody);
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var originalRows = saved.GetProperty("userCategories").EnumerateArray().ToArray();
        originalRows.Should().HaveCount(2);
        var rowAId = originalRows.Single(c => c.GetProperty("categoryName").GetString() == "Operators")
            .GetProperty("id").GetString()!;
        var rowBId = originalRows.Single(c => c.GetProperty("categoryName").GetString() == "Analysts")
            .GetProperty("id").GetString()!;
        (await ReviewRowAsync(rowAId, "submit", 1)).StatusCode.Should().Be(HttpStatusCode.OK);
        _user.SetupGet(x => x.CurrentUserId).Returns("reviewer");
        var approved = await ReviewRowAsync(rowAId, "approve", 2);
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterApproval = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        var approvedRows = afterApproval.GetProperty("userCategories").EnumerateArray().ToArray();
        var rowAApproved = approvedRows.Single(c => c.GetProperty("id").GetString() == rowAId);
        var rowBDraft = approvedRows.Single(c => c.GetProperty("id").GetString() == rowBId);
        rowAApproved.GetProperty("governanceStatus").GetString().Should().Be("Approved");
        rowAApproved.GetProperty("revision").GetInt32().Should().Be(3);
        rowBDraft.GetProperty("governanceStatus").GetString().Should().Be("Draft");
        rowBDraft.GetProperty("revision").GetInt32().Should().Be(1);
        var approvedSnapshotId = rowAApproved.GetProperty("approvedSnapshotId").GetString()!;
        var editRows = approvedRows.Select(row =>
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(row)!;
            if (row.GetProperty("id").GetString() == rowAId)
                fields["categoryName"] = "Operators updated";
            return fields;
        }).ToArray();
        _user.SetupGet(x => x.CurrentUserId).Returns("owner");
        var edited = await _client.PutAsJsonAsync(Root + "UsersAndAccess", new { content = context, childItems = editRows });
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterEdit = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");
        int auditCountBeforeStaleRequest;
        using (var inspect = _app.Services.CreateScope())
            auditCountBeforeStaleRequest = await inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>()
                .ProfileAuditEntries.CountAsync();
        var stale = await ReviewRowAsync(rowAId, "submit", 3);
        var final = await _client.GetFromJsonAsync<JsonElement>(Root + "UsersAndAccess");

        // Assert
        Guid.TryParse(rowAId, out _).Should().BeTrue();
        Guid.TryParse(rowBId, out _).Should().BeTrue();
        rowAId.Should().NotBe(rowBId);
        var editedA = afterEdit.GetProperty("userCategories").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == rowAId);
        editedA.GetProperty("governanceStatus").GetString().Should().Be("Draft");
        editedA.GetProperty("revision").GetInt32().Should().Be(4);
        editedA.GetProperty("approvedSnapshotId").GetString().Should().Be(approvedSnapshotId);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        final.GetProperty("userCategories").GetRawText().Should().Be(afterEdit.GetProperty("userCategories").GetRawText());
        using var finalScope = _app.Services.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var persistedA = await db.UserCategories.SingleAsync(c => c.Id == rowAId);
        var persistedB = await db.UserCategories.SingleAsync(c => c.Id == rowBId);
        persistedA.CategoryName.Should().Be("Operators updated");
        persistedA.Revision.Should().Be(4);
        persistedB.CategoryName.Should().Be("Analysts");
        persistedB.GovernanceStatus.Should().Be(SspSectionStatus.Draft);
        persistedB.Revision.Should().Be(1);
        var retained = await db.ProfileAuditEntries.SingleAsync(a => a.Id == approvedSnapshotId);
        retained.Action.Should().Be("UserCategoryApproved");
        retained.UserCategoryId.Should().Be(rowAId);
        retained.SnapshotJson.Should().Contain("Operators").And.NotContain("Operators updated");
        (await db.ProfileAuditEntries.CountAsync()).Should().Be(auditCountBeforeStaleRequest);
        (await db.SystemProfileSections.SingleAsync()).DraftContent.Should().Be(context);
        (await db.SystemProfileSections.SingleAsync()).GovernanceStatus.Should().Be(SspSectionStatus.Draft);
    }
}
