using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class DocumentPreviewHttpTests
{
    [Theory]
    [InlineData("sap", "security-assessment-plan", "SAVED SAP narrative")]
    [InlineData("sar", "security-assessment-report", "SAVED SAR finding detail")]
    [InlineData("poam", "poam-register", "SAVED completed weakness")]
    public async Task AssessmentPreview_ReadsCompleteSavedDocument_AndRefreshesWithoutRetaining(
        string type, string root, string expected)
    {
        // Arrange
        var fixture = await SeedAsync();
        var other = await SeedAsync();
        await SeedAssessmentDocuments(fixture.System);
        await SeedAssessmentDocuments(other.System, "FOREIGN");
        using var client = Client(fixture.Actor);
        var path = $"/api/dashboard/systems/{fixture.System}/documents/{type}/preview";
        var before = await AssessmentDocumentState(fixture.System);
        var filesBefore = PreviewFiles();

        // Act
        using var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        preview.GetProperty("available").GetBoolean().Should().BeTrue();
        preview.GetProperty("documentType").GetString().Should().Be(type);
        preview.GetProperty("sourceState").GetString().Should().Be("CurrentWorkingData");
        preview.GetProperty("isPreview").GetBoolean().Should().BeTrue();
        preview.GetProperty("canGenerate").GetBoolean().Should().BeFalse();
        var content = preview.GetProperty("content").GetString()!;
        content.Should().Contain(expected).And.NotContain("FOREIGN");
        using var document = JsonDocument.Parse(content);
        document.RootElement.TryGetProperty(root, out var body).Should().BeTrue();
        preview.GetProperty("contentHash").GetString().Should().Be(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
        preview.GetProperty("sourceRecords").GetArrayLength().Should().BeGreaterThan(0);
        if (type == "poam")
        {
            body.GetProperty("itemCount").GetInt32().Should().Be(2);
            content.Should().Contain("Ongoing").And.Contain("Completed").And.Contain("SAVED milestone");
        }
        else preview.GetProperty("documentStatus").GetString().Should().Be("Draft");
        (await AssessmentDocumentState(fixture.System)).Should().Be(before);
        PreviewFiles().Should().Equal(filesBefore);
        var repeated = await client.GetFromJsonAsync<JsonElement>(path);
        repeated.GetProperty("contentHash").GetString().Should().Be(preview.GetProperty("contentHash").GetString());

        // Arrange
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            if (type == "sap")
                (await db.SecurityAssessmentPlans.SingleAsync(x => x.RegisteredSystemId == fixture.System)).Content = "REFRESHED saved narrative";
            else if (type == "sar")
                (await db.SarSections.SingleAsync(x => x.SecurityAssessmentReport!.RegisteredSystemId == fixture.System)).Content = "REFRESHED saved narrative";
            else
                (await db.PoamItems.FirstAsync(x => x.RegisteredSystemId == fixture.System)).Weakness = "REFRESHED saved narrative";
            await db.SaveChangesAsync();
        }

        // Act
        var refreshed = await client.GetFromJsonAsync<JsonElement>(path);

        // Assert
        refreshed.GetProperty("content").GetString().Should().Contain("REFRESHED saved narrative");
        refreshed.GetProperty("contentHash").GetString().Should().NotBe(preview.GetProperty("contentHash").GetString());
    }

    [Theory]
    [InlineData("sap")]
    [InlineData("sar")]
    [InlineData("poam")]
    public async Task AssessmentPreview_EmptySourcesAreExplicit_AndDoNotCreateDocuments(string type)
    {
        // Arrange
        var fixture = await SeedAsync();
        using var client = Client(fixture.Actor);
        var before = await AssessmentDocumentState(fixture.System);

        // Act
        using var response = await client.GetAsync($"/api/dashboard/systems/{fixture.System}/documents/{type}/preview");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("available").GetBoolean().Should().Be(type == "poam");
        if (type == "poam")
        {
            result.GetProperty("content").GetString().Should().Contain("\"itemCount\": 0");
            result.GetProperty("sourceGaps").EnumerateArray().Should().Contain(x =>
                x.GetProperty("code").GetString() == "EMPTY_POAM_REGISTER");
        }
        else
        {
            result.GetProperty("reasonCode").GetString().Should().Be($"{type.ToUpperInvariant()}_NOT_FOUND");
            result.TryGetProperty("content", out _).Should().BeFalse();
        }
        (await AssessmentDocumentState(fixture.System)).Should().Be(before);
    }

    [Theory]
    [InlineData("sap")]
    [InlineData("sar")]
    [InlineData("poam")]
    public async Task AssessmentPreview_UsesRealSystemReadAndTenantAuthorization(string type)
    {
        // Arrange
        var own = await SeedAsync();
        var other = await SeedAsync();
        var foreign = await SeedAsync(WorkspaceMembershipFactory.TenantBId);
        await SeedAssessmentDocuments(other.System);
        await SeedAssessmentDocuments(foreign.System);
        using var client = Client(own.Actor);

        // Act
        using var denied = await client.GetAsync($"/api/dashboard/systems/{other.System}/documents/{type}/preview");
        using var hidden = await client.GetAsync($"/api/dashboard/systems/{foreign.System}/documents/{type}/preview");
        using var missing = await client.GetAsync($"/api/dashboard/systems/{Guid.NewGuid()}/documents/{type}/preview");

        // Assert
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("sap")]
    [InlineData("sar")]
    [InlineData("poam")]
    public async Task AssessmentPreview_DeniesAnonymousAndRevokedReaders(string type)
    {
        // Arrange
        var fixture = await SeedAsync();
        using var anonymous = factory.CreateClient();
        using var client = Client(fixture.Actor);
        var path = $"/api/dashboard/systems/{fixture.System}/documents/{type}/preview";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.SystemRoleAssignments.RemoveRange(await db.SystemRoleAssignments
                .Where(x => x.RegisteredSystemId == fixture.System).ToListAsync());
            await db.SaveChangesAsync();
        }

        // Act
        using var unauthenticated = await anonymous.GetAsync(path);
        using var revoked = await client.GetAsync(path);

        // Assert
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        revoked.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AssessmentPreview_SelectsLatestSavedPlanRatherThanOlderFinalizedPlan()
    {
        // Arrange
        var fixture = await SeedAsync();
        await SeedAssessmentDocuments(fixture.System);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.SecurityAssessmentPlans.Add(new()
            {
                TenantId = Tenant, RegisteredSystemId = fixture.System, Status = SapStatus.Finalized,
                GeneratedAt = DateTime.UtcNow.AddYears(-1), Content = "OLDER finalized content"
            });
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{fixture.System}/documents/sap/preview");

        // Assert
        preview.GetProperty("content").GetString().Should().Contain("SAVED SAP narrative").And.NotContain("OLDER");
        preview.GetProperty("documentStatus").GetString().Should().Be("Draft");
    }

    private string[] PreviewFiles()
    {
        var path = factory.Services.GetRequiredService<IOptions<ExportSettings>>().Value.ExportsPath;
        return Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Order().ToArray() : [];
    }

    [Fact]
    public async Task AssessmentPreview_DoesNotRelabelCompletedAssessmentAsSavedSar()
    {
        // Arrange
        var fixture = await SeedAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Assessments.Add(new()
            {
                TenantId = Tenant, RegisteredSystemId = fixture.System, Status = AssessmentStatus.Completed,
                ExecutiveSummary = "NOT a saved SAR", CompletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{fixture.System}/documents/sar/preview");

        // Assert
        preview.GetProperty("available").GetBoolean().Should().BeFalse();
        preview.GetProperty("reasonCode").GetString().Should().Be("SAR_NOT_FOUND");
        preview.TryGetProperty("content", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AssessmentPreview_ReturnsEntirePoamRegisterBeyondDashboardPageLimit()
    {
        // Arrange
        var fixture = await SeedAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.PoamItems.AddRange(Enumerable.Range(0, 105).Select(i => new PoamItem
            {
                TenantId = Tenant, RegisteredSystemId = fixture.System, Weakness = $"Saved weakness {i}",
                Status = i % 2 == 0 ? PoamStatus.Completed : PoamStatus.Ongoing
            }));
            await db.SaveChangesAsync();
        }
        using var client = Client(fixture.Actor);

        // Act
        var preview = await client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{fixture.System}/documents/poam/preview");

        // Assert
        using var content = JsonDocument.Parse(preview.GetProperty("content").GetString()!);
        content.RootElement.GetProperty("poam-register").GetProperty("itemCount").GetInt32().Should().Be(105);
        content.RootElement.GetProperty("poam-register").GetProperty("items").GetArrayLength().Should().Be(105);
        preview.GetProperty("sourceRecords").GetArrayLength().Should().Be(105);
    }

    private async Task SeedAssessmentDocuments(string systemId, string prefix = "SAVED")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenant = await db.RegisteredSystems.Where(x => x.Id == systemId).Select(x => x.TenantId).SingleAsync();
        db.SecurityAssessmentPlans.Add(new()
        {
            TenantId = tenant, RegisteredSystemId = systemId, Title = $"{prefix} SAP", Content = $"{prefix} SAP narrative",
            ScopeNotes = "Persisted scope", RulesOfEngagement = "Persisted rules", Status = SapStatus.Draft,
            ControlEntries = [new() { TenantId = tenant, ControlId = "AC-2", ControlFamily = "Access Control",
                AssessmentObjectives = ["Persisted objective"], EvidenceRequirements = ["Persisted evidence"] }],
            TeamMembers = [new() { TenantId = tenant, Name = "Saved assessor", Role = "Lead Assessor" }]
        });
        db.SecurityAssessmentReports.Add(new()
        {
            TenantId = tenant, RegisteredSystemId = systemId, Title = $"{prefix} SAR", Status = SarStatus.Draft,
            TotalControlsAssessed = 7, NotSatisfiedCount = 2, FindingsByFamily = "{\"AC\":2}",
            Sections = [new() { TenantId = tenant, SectionType = SarSectionType.FindingDetails,
                Title = "Saved findings", Content = $"{prefix} SAR finding detail" }]
        });
        db.PoamItems.AddRange(new PoamItem
        {
            TenantId = tenant, RegisteredSystemId = systemId, Weakness = $"{prefix} completed weakness",
            Status = PoamStatus.Completed, PointOfContact = "Saved owner", CostEstimate = 12,
            Milestones = [new() { TenantId = tenant, Description = $"{prefix} milestone", Sequence = 1 }]
        }, new PoamItem { TenantId = tenant, RegisteredSystemId = systemId, Weakness = $"{prefix} open weakness" });
        await db.SaveChangesAsync();
    }

    private async Task<string> AssessmentDocumentState(string systemId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        return JsonSerializer.Serialize(new
        {
            Plans = await db.SecurityAssessmentPlans.Where(x => x.RegisteredSystemId == systemId)
                .Select(x => new { x.Id, x.Status, x.Content, x.ContentHash, x.FinalizedAt }).ToListAsync(),
            Reports = await db.SecurityAssessmentReports.Where(x => x.RegisteredSystemId == systemId)
                .Select(x => new { x.Id, x.Status, x.ApprovedAt, x.ModifiedAt }).ToListAsync(),
            Items = await db.PoamItems.Where(x => x.RegisteredSystemId == systemId)
                .Select(x => new { x.Id, x.Status, x.Weakness, x.ModifiedAt }).ToListAsync(),
            Exports = await db.SspExports.CountAsync(x => x.SystemId == systemId),
            Packages = await db.AuthorizationPackages.CountAsync(x => x.RegisteredSystemId == systemId)
        });
    }
}
