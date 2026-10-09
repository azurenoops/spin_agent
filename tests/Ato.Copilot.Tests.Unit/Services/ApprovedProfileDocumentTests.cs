using System.IO.Compression;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ApprovedProfileDocumentTests
{
    [Fact]
    public async Task MissionRecordFields_FeedGeneratedSsp_WithoutBorrowingLaterDraftValues()
    {
        // Arrange
        var databaseName = Guid.NewGuid().ToString();
        using var services = new ServiceCollection().AddDbContext<AtoCopilotContext>(
            o => o.UseInMemoryDatabase(databaseName)).BuildServiceProvider();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.AddRange(new RegisteredSystem
        {
            Id = "mission-record", Name = "Synthetic Mission System", Acronym = "SYN-M",
            EmassId = "EMASS-SYN", DitprId = "DITPR-SYN"
        }, new RmfRoleAssignment
        {
            RegisteredSystemId = "mission-record", RmfRole = RmfRole.SystemOwner,
            UserId = "synthetic-owner", UserDisplayName = "Synthetic System Owner", IsActive = true
        });
        await db.SaveChangesAsync();
        var profile = new SystemProfileService(factory, NullLogger<SystemProfileService>.Instance);
        var approved = new Dictionary<string, string>
        {
            ["systemVersion"] = "Release 4.2",
            ["responsibleOrganization"] = "Synthetic Mission Directorate",
            ["programOffice"] = "Synthetic Operations Division",
            ["missionStatement"] = "Approved mission statement",
            ["businessPurpose"] = "Approved business purpose",
            ["operationalJustification"] = "Retained operational need",
            ["businessFunctions"] = "Retained business functions"
        };
        await profile.SaveDraftAsync("mission-record", ProfileSectionType.MissionAndPurpose,
            JsonSerializer.Serialize(approved), "synthetic-owner", RmfRole.MissionOwner);
        await profile.SubmitForReviewAsync("mission-record", [ProfileSectionType.MissionAndPurpose],
            "synthetic-owner", RmfRole.MissionOwner);
        await profile.ReviewSectionAsync("mission-record", ProfileSectionType.MissionAndPurpose,
            ReviewDecision.Approve, "synthetic-reviewer", null, RmfRole.Issm);
        await profile.SaveDraftAsync("mission-record", ProfileSectionType.MissionAndPurpose,
            JsonSerializer.Serialize(approved.ToDictionary(x => x.Key, x => $"UNREVIEWED-{x.Key}")),
            "synthetic-owner", RmfRole.MissionOwner);

        // Act
        var ssp = await new SspService(factory, NullLogger<SspService>.Instance).GenerateSspAsync("mission-record");
        var docx = await new DocumentTemplateService(factory, NullLogger<DocumentTemplateService>.Instance)
            .RenderDocxAsync("mission-record", "ssp");
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = await reader.ReadToEndAsync();

        // Assert
        ssp.Content.Should().Contain("Synthetic Mission System").And.Contain("SYN-M")
            .And.Contain("EMASS-SYN").And.Contain("DITPR-SYN").And.Contain("Synthetic System Owner");
        foreach (var (key, value) in approved)
        {
            ssp.Content.Should().Contain($"{key}: {value}").And.NotContain($"UNREVIEWED-{key}");
            documentXml.Should().Contain(value).And.NotContain($"UNREVIEWED-{key}");
        }
    }

    [Fact]
    public async Task LegacyApprovedNarrative_IsRetainedWithoutBorrowingNewDraftHalves()
    {
        // Arrange
        var name = Guid.NewGuid().ToString();
        using var services = new ServiceCollection().AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(name)).BuildServiceProvider();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = new ControlImplementation
        {
            RegisteredSystemId = "legacy", ControlId = "AC-1", PolicyNarrative = "DRAFT policy", TechnicalNarrative = "DRAFT technical"
        };
        var version = new NarrativeVersion
        {
            ControlImplementationId = implementation.Id, Status = SspSectionStatus.Approved,
            Content = "DEMO legacy approved combined narrative"
        };
        implementation.ApprovedVersionId = version.Id;
        db.AddRange(new RegisteredSystem { Id = "legacy", Name = "DEMO legacy" }, implementation, version);
        await db.SaveChangesAsync();

        // Act
        var result = await new OscalSspExportService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspExportService>.Instance).ExportAsync("legacy");

        // Assert
        result.OscalJson.Should().Contain("DEMO legacy approved combined narrative")
            .And.NotContain("DRAFT policy").And.NotContain("DRAFT technical");
        result.SourceManifest!.Narratives.Single().VersionId.Should().Be(version.Id);
        var docx = await new DocumentTemplateService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DocumentTemplateService>.Instance).RenderDocxAsync("legacy", "ssp");
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        (await reader.ReadToEndAsync()).Should().Contain("DEMO legacy approved combined narrative")
            .And.NotContain("DRAFT policy").And.NotContain("DRAFT technical");
    }

    [Fact]
    public async Task SchemaUpgrade_DoesNotInferApprovedRows_AndPreservesExistingContent()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE SystemProfileSections (Id TEXT PRIMARY KEY, ApprovedContent TEXT);
            CREATE TABLE ProfileAuditEntries (Id TEXT PRIMARY KEY);
            CREATE TABLE SspExports (Id TEXT PRIMARY KEY);
            CREATE TABLE AuthorizationPackages (Id TEXT PRIMARY KEY);
            INSERT INTO SystemProfileSections VALUES ('legacy', 'DEMO original approved content');
            """);

        // Act
        await DocumentSourceSnapshotSchemaAdditions.ApplyAsync(db);
        await DocumentSourceSnapshotSchemaAdditions.ApplyAsync(db);

        // Assert
        (await db.Database.SqlQueryRaw<string>("SELECT ApprovedContent AS Value FROM SystemProfileSections").SingleAsync())
            .Should().Be("DEMO original approved content");
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM SystemProfileSections WHERE ApprovedSnapshotId IS NOT NULL").SingleAsync())
            .Should().Be(0);
    }

    [Fact]
    public async Task AllSixApprovedSectionsAndRows_AppearInOscal_WithoutLaterDraftEdits()
    {
        // Arrange
        var name = Guid.NewGuid().ToString();
        using var services = new ServiceCollection().AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(name)).BuildServiceProvider();
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Add(new RegisteredSystem { Id = "mission", Name = "DEMO mission" });
        var implementation = new ControlImplementation
        {
            RegisteredSystemId = "mission", ControlId = "AC-1", PolicyNarrative = "DEMO approved policy",
            TechnicalNarrative = "DEMO approved technical"
        };
        var narrativeVersion = new NarrativeVersion
        {
            ControlImplementationId = implementation.Id, Status = SspSectionStatus.Approved,
            SnapshotJson = NarrativeContentSnapshot.Capture(implementation)
        };
        implementation.ApprovedVersionId = narrativeVersion.Id;
        implementation.PolicyNarrative = "DRAFT policy must not leak";
        implementation.TechnicalNarrative = "DRAFT technical must not leak";
        db.AddRange(implementation, narrativeVersion);
        foreach (var type in Enum.GetValues<ProfileSectionType>())
            db.Add(new SystemProfileSection
            {
                RegisteredSystemId = "mission", SectionType = type, GovernanceStatus = SspSectionStatus.UnderReview,
                DraftContent = JsonSerializer.Serialize(new { description = $"APPROVED-{type}" })
            });
        await db.SaveChangesAsync();
        var sections = await db.SystemProfileSections.ToDictionaryAsync(x => x.SectionType);
        db.Add(new UserCategory { SystemProfileSectionId = sections[ProfileSectionType.UsersAndAccess].Id,
            CategoryName = "DEMO operators", ApproximateCount = 17, AccessMethod = "DEMO approved access" });
        db.Add(new DataTypeEntry { SystemProfileSectionId = sections[ProfileSectionType.DataTypes].Id,
            DataTypeName = "DEMO approved data" });
        db.Add(new PpsEntry { SystemProfileSectionId = sections[ProfileSectionType.PortsProtocolsAndServices].Id,
            PortOrRange = "443", Protocol = "TCP", ServiceName = "DEMO approved HTTPS", Direction = "Inbound" });
        db.Add(new LeveragedAuthorization { SystemProfileSectionId = sections[ProfileSectionType.LeveragedAuthorizations].Id,
            ProviderName = "DEMO reviewed reference", AuthorizationType = "Source-stated reference" });
        await db.SaveChangesAsync();
        var profile = new SystemProfileService(factory, NullLogger<SystemProfileService>.Instance);
        await profile.BatchApproveSectionsAsync("mission", "reviewer", RmfRole.Issm);
        var categoryId = await db.UserCategories.Select(c => c.Id).SingleAsync();
        await profile.ReviewUserCategoryAsync("mission", categoryId, "submit", 1, "owner", simulatedRole: RmfRole.MissionOwner);
        await profile.ReviewUserCategoryAsync("mission", categoryId, "approve", 2, "reviewer", simulatedRole: RmfRole.Issm);
        var exporter = new OscalSspExportService(factory, NullLogger<OscalSspExportService>.Instance);
        var original = await exporter.ExportAsync("mission");
        await profile.SaveDraftAsync("mission", ProfileSectionType.UsersAndAccess, "{\"description\":\"DRAFT MUST NOT LEAK\"}", "owner", RmfRole.MissionOwner);
        db.ChangeTracker.Clear();
        var row = await db.UserCategories.SingleAsync();
        await profile.SaveDraftWithChildrenAsync("mission", ProfileSectionType.UsersAndAccess,
            "{\"description\":\"DRAFT MUST NOT LEAK\"}",
            [JsonSerializer.SerializeToElement(new { id = row.Id, revision = row.Revision,
                categoryName = "DRAFT CHILD MUST NOT LEAK", approximateCount = 999 })], "owner", RmfRole.MissionOwner);

        // Act
        var after = await exporter.ExportAsync("mission");
        var docx = await new DocumentTemplateService(factory, NullLogger<DocumentTemplateService>.Instance)
            .RenderDocxAsync("mission", "ssp", null);
        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var documentReader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var documentXml = await documentReader.ReadToEndAsync();

        // Assert
        foreach (var type in Enum.GetValues<ProfileSectionType>())
        {
            after.OscalJson.Should().Contain($"APPROVED-{type}");
            documentXml.Should().Contain($"APPROVED-{type}");
        }
        documentXml.Should().Contain("DEMO operators").And.Contain("17").And.Contain("DEMO approved HTTPS")
            .And.NotContain("DRAFT CHILD MUST NOT LEAK").And.Contain("DEMO approved technical")
            .And.NotContain("DRAFT technical must not leak");
        after.OscalJson.Should().Contain("DEMO approved policy").And.Contain("DEMO approved technical")
            .And.NotContain("DRAFT policy must not leak").And.NotContain("DRAFT technical must not leak");
        after.OscalJson.Should().Contain("DEMO operators").And.Contain("DEMO approved data")
            .And.Contain("DEMO approved HTTPS").And.Contain("DEMO reviewed reference")
            .And.NotContain("DRAFT MUST NOT LEAK").And.NotContain("DRAFT CHILD MUST NOT LEAK");
        using var firstJson = JsonDocument.Parse(original.OscalJson);
        using var secondJson = JsonDocument.Parse(after.OscalJson);
        firstJson.RootElement.GetProperty("system-security-plan").GetProperty("system-characteristics").GetProperty("props").ToString()
            .Should().Be(secondJson.RootElement.GetProperty("system-security-plan").GetProperty("system-characteristics").GetProperty("props").ToString());
        after.SourceManifest!.Profiles.Should().HaveCount(6);
        var originalApproval = after.SourceManifest.Profiles.Single(p =>
            p.RecordId == sections[ProfileSectionType.UsersAndAccess].Id).VersionId;
        await profile.SubmitForReviewAsync("mission", [ProfileSectionType.UsersAndAccess], "owner", RmfRole.MissionOwner);
        await profile.ReviewSectionAsync("mission", ProfileSectionType.UsersAndAccess, ReviewDecision.Approve, "reviewer", null, RmfRole.Issm);
        var successor = await exporter.ExportAsync("mission");
        successor.OscalJson.Should().NotContain("DRAFT CHILD MUST NOT LEAK");
        await profile.ReviewUserCategoryAsync("mission", row.Id, "submit", 4, "owner", simulatedRole: RmfRole.MissionOwner);
        await profile.ReviewUserCategoryAsync("mission", row.Id, "approve", 5, "reviewer", simulatedRole: RmfRole.Issm);
        (await exporter.ExportAsync("mission")).OscalJson.Should().Contain("DRAFT CHILD MUST NOT LEAK");
        (await db.ProfileAuditEntries.AsNoTracking().SingleAsync(a => a.Id == originalApproval))
            .SnapshotJson.Should().Contain("DEMO operators").And.NotContain("DRAFT CHILD MUST NOT LEAK");
    }
}
