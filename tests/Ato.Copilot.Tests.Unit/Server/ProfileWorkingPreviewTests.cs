using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Server;

public sealed partial class ProfileDraftPersistenceTests
{
    private OscalSspExportService Oscal() => new(_app.Services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<OscalSspExportService>.Instance);

    private static JsonElement PreviewProfile(OscalExportResult preview, string type)
    {
        using var json = JsonDocument.Parse(preview.OscalJson);
        foreach (var property in json.RootElement.GetProperty("system-security-plan")
                     .GetProperty("system-characteristics").GetProperty("props").EnumerateArray())
        {
            if (property.GetProperty("name").GetString() != "working-profile") continue;
            using var profile = JsonDocument.Parse(property.GetProperty("value").GetString()!);
            if (profile.RootElement.GetProperty("sectionType").GetString() == type) return profile.RootElement.Clone();
        }
        throw new Xunit.Sdk.XunitException($"Working profile {type} missing.");
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public async Task WorkingPreview_AllChildTypesUseSavedValuesAndExplicitWorkingProvenance(
        string section, string list, string field, string row)
    {
        // Arrange
        (await PutAsync(section, $$"""{"content":"Saved working scalar","childItems":[{{row}}]}"""))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var result = await Oscal().PreviewAsync("mission");

        // Assert
        var profile = PreviewProfile(result, section);
        var content = profile.GetProperty("content");
        content.GetProperty("scalarContent").GetString().Should().Be("Saved working scalar");
        content.GetProperty(list)[0].GetProperty(field).GetString().Should().NotBeNullOrEmpty();
        profile.GetProperty("sourceState").GetString().Should().Be("CurrentWorkingData");
        profile.GetProperty("governanceStatus").GetString().Should().Be("Draft");
        result.SourceManifest!.PreviewOnly.Should().BeTrue();
        var source = result.SourceManifest.Profiles.Single();
        source.Kind.Should().Be("WorkingProfile");
        source.ContentHash.Should().Be(profile.GetProperty("contentHash").GetString());
        source.VersionId.Should().Be("working:" + source.ContentHash);
        result.OscalJson.Should().NotContain("foreign-child");
        result.BuildPreviewSourceGaps().Should().Contain(g => g.Code == "WORKING_PROFILE_PREVIEW_ONLY");
    }

    [Theory]
    [InlineData("MissionAndPurpose")]
    [InlineData("EnvironmentAndDeployment")]
    public async Task WorkingPreview_ScalarOnlySectionsUseSavedDrafts(string section)
    {
        // Arrange
        (await PutAsync(section, """{"content":"Saved scalar contribution"}""")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var preview = await Oscal().PreviewAsync("mission");

        // Assert
        PreviewProfile(preview, section).GetProperty("content").GetProperty("scalarContent")
            .GetString().Should().Be("Saved scalar contribution");
    }

    [Fact]
    public async Task WorkingPreview_ChangesAfterSave_WhileOrdinaryExportKeepsExactApprovedBaseline()
    {
        // Arrange
        await SeedSectionAsync(SspSectionStatus.UnderReview);
        await AddReviewerAsync();
        using var scope = _app.Services.CreateScope();
        var profileService = scope.ServiceProvider.GetRequiredService<ISystemProfileService>();
        await profileService.ReviewSectionAsync("mission", ProfileSectionType.UsersAndAccess, ReviewDecision.Approve, "reviewer");
        await ReviewRowAsync("existing", "submit", 1);
        _user.SetupGet(c => c.CurrentUserId).Returns("reviewer");
        await ReviewRowAsync("existing", "approve", 2);
        var oscal = Oscal();
        var beforeExport = await oscal.ExportAsync("mission");
        var beforePreview = await oscal.PreviewAsync("mission");
        _user.SetupGet(c => c.CurrentUserId).Returns("owner");

        // Act
        var saved = await PutAsync("UsersAndAccess", """
            {"content":"NEW WORKING ACCESS","childItems":[
              {"id":"existing","revision":3,"categoryName":"NEW WORKING OPERATORS","approximateCount":23},
              {"categoryName":"NEW SIBLING DRAFT","approximateCount":4}]}
            """);
        var working = await oscal.PreviewAsync("mission");
        var approved = await oscal.ExportAsync("mission");

        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        working.OscalJson.Should().Contain("NEW WORKING ACCESS").And.Contain("NEW WORKING OPERATORS").And.Contain("NEW SIBLING DRAFT");
        var rows = PreviewProfile(working, "UsersAndAccess").GetProperty("content").GetProperty("userCategories");
        rows[0].GetProperty("revision").GetInt32().Should().Be(4);
        rows[0].GetProperty("governanceStatus").GetString().Should().Be("Draft");
        rows[0].GetProperty("approvedSnapshotId").GetString().Should().NotBeNullOrEmpty();
        approved.OscalJson.Should().Contain("Existing").And.NotContain("NEW WORKING ACCESS")
            .And.NotContain("NEW WORKING OPERATORS").And.NotContain("NEW SIBLING DRAFT");
        approved.SourceManifest!.Profiles.Should().BeEquivalentTo(beforeExport.SourceManifest!.Profiles);
        approved.SourceManifest.HasWorkingProfileSources.Should().BeFalse();
        working.SourceManifest!.Profiles.Single().ContentHash.Should().NotBe(beforePreview.SourceManifest!.Profiles.Single().ContentHash);
    }

    [Fact]
    public async Task WorkingPreview_RetainedBytesRemainImmutable_AndCannotReachQueueOrWorkerOutput()
    {
        // Arrange
        await SeedSectionAsync();
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "working-profile-preview-tests-" + Guid.NewGuid().ToString("N"));
        var channel = Channel.CreateUnbounded<SspExportJob>();
        var schema = new Mock<IOscalSchemaValidationService>();
        var service = new SspExportService(_app.Services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ISspService>(), Mock.Of<IDocumentTemplateService>(), Oscal(), schema.Object, Mock.Of<ISspExportNotifier>(),
            NullLogger<SspExportService>.Instance, Options.Create(new ExportSettings { DataPath = directory }), channel);
        try
        {
            // Act
            var snapshot = await service.CreatePreviewAsync("mission", "owner");
            await PutAsync("UsersAndAccess", """
                {"content":"Later scalar","childItems":[{"id":"existing","revision":1,"categoryName":"LATER WORKING ROW"}]}
                """);
            using var inspect = _app.Services.CreateScope();
            var db = inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var stored = await db.SspExports.SingleAsync(s => s.Id == snapshot.PreviewId);
            stored.SourceGapsJson = "[]";
            var forced = new SspExport { SystemId = "mission", Format = "json", Status = "Pending",
                SourcePreviewId = snapshot.PreviewId, GeneratedBy = "owner", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
            db.SspExports.Add(forced);
            await db.SaveChangesAsync();
            var enqueue = () => service.EnqueueFromPreviewAsync("mission", snapshot.PreviewId!.Value, "owner");
            await service.ProcessExportAsync(new SspExportJob(forced.Id, "mission", "json", null, "owner"));

            // Assert
            snapshot.CanGenerate.Should().BeFalse();
            var retained = await File.ReadAllTextAsync(Path.Combine(directory, "exports", stored.FilePath!));
            retained.Should().Be(snapshot.Content).And.NotContain("LATER WORKING ROW");
            await enqueue.Should().ThrowAsync<ArgumentException>().WithMessage("*review-only*");
            channel.Reader.TryRead(out _).Should().BeFalse();
            (await service.GetExportAsync(forced.Id))!.Status.Should().Be("Failed");
            (await db.SspExports.AsNoTracking().SingleAsync(s => s.Id == forced.Id)).ErrorMessage.Should().Contain("review-only");
            schema.Verify(s => s.ValidateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WorkingPreview_RejectsForeignTenantSystem()
    {
        // Arrange
        var service = Oscal();

        // Act
        var preview = () => service.PreviewAsync("foreign");

        // Assert
        await preview.Should().ThrowAsync<InvalidOperationException>().WithMessage("SYSTEM_NOT_FOUND:*");
    }

    [Fact]
    public async Task WorkingPreview_ShowsPendingRemovalButExcludesApprovedTombstone()
    {
        // Arrange
        await SeedSectionAsync();
        await AddReviewerAsync();
        await ReviewRowAsync("existing", "submit", 1);
        _user.SetupGet(c => c.CurrentUserId).Returns("reviewer");
        await ReviewRowAsync("existing", "approve", 2);
        _user.SetupGet(c => c.CurrentUserId).Returns("owner");
        await PutAsync("UsersAndAccess", """{"content":"original","childItems":[]}""");

        // Act
        var pending = await Oscal().PreviewAsync("mission");
        await ReviewRowAsync("existing", "submit", 4);
        _user.SetupGet(c => c.CurrentUserId).Returns("reviewer");
        await ReviewRowAsync("existing", "approve", 5);
        var removed = await Oscal().PreviewAsync("mission");

        // Assert
        var row = PreviewProfile(pending, "UsersAndAccess").GetProperty("content").GetProperty("userCategories")[0];
        row.GetProperty("pendingDeletion").GetBoolean().Should().BeTrue();
        row.GetProperty("governanceStatus").GetString().Should().Be("Draft");
        row.GetProperty("revision").GetInt32().Should().Be(4);
        pending.ProfileSourceGaps.Should().Contain(g => g.Contains("pending removal"));
        PreviewProfile(removed, "UsersAndAccess").GetProperty("content").GetProperty("userCategories").GetArrayLength().Should().Be(0);
        removed.SourceManifest!.HasWorkingProfileSources.Should().BeTrue();
    }
}
