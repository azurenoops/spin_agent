using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration.Poam;

/// <summary>
/// Integration tests for POA&M service — CRUD, lifecycle, pagination, bulk ops, export, and concurrency.
/// </summary>
public class PoamEndpointTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly PoamService _sut;

    private const string SystemId = "sys-poam-int-001";
    private const string CompId1 = "comp-int-001";
    private const string CompId2 = "comp-int-002";

    public PoamEndpointTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"PoamIntegration_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(options);
        _sut = new PoamService(_db, Mock.Of<ILogger<PoamService>>());
        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId, Name = "POA&M Integration System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Gov", CreatedBy = "test", IsActive = true,
        });

        _db.SystemComponents.AddRange(
            new SystemComponent { Id = CompId1, RegisteredSystemId = SystemId, Name = "Web Server",
                ComponentType = ComponentType.Thing, Status = ComponentStatus.Active, CreatedBy = "test" },
            new SystemComponent { Id = CompId2, RegisteredSystemId = SystemId, Name = "DB Server",
                ComponentType = ComponentType.Thing, Status = ComponentStatus.Active, CreatedBy = "test" }
        );

        _db.SaveChanges();
    }

    private async Task<PoamItem> CreateTestPoamAsync(
        string controlId = "AC-2", CatSeverity severity = CatSeverity.CatII, int dueDays = 30)
    {
        return await _sut.CreateAsync(
            SystemId, $"Test weakness for {controlId}", "STIG",
            controlId, severity, "test-poc",
            DateTime.UtcNow.AddDays(dueDays),
            createdBy: "test-user");
    }

    // ─── CRUD Happy Path ────────────────────────────────────────────────────

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete()
    {
        // Create
        var created = await _sut.CreateAsync(
            SystemId, "Weak password policy", "STIG", "AC-2", CatSeverity.CatII,
            "test-poc", DateTime.UtcNow.AddDays(30),
            resourcesRequired: "Resources needed", createdBy: "test-user");
        created.Should().NotBeNull();
        created.Weakness.Should().Be("Weak password policy");

        // Read
        var detail = await _sut.GetByIdAsync(created.Id);
        detail.Should().NotBeNull();
        detail!.SecurityControlNumber.Should().Be("AC-2");

        // Update status: Ongoing → Completed
        await _sut.UpdateStatusAsync(created.Id, PoamStatus.Completed,
            created.RowVersion, "test-user");
        var updated = await _sut.GetByIdAsync(created.Id);
        updated!.Status.Should().Be(PoamStatus.Completed);

        // Delete
        await _sut.DeleteAsync(created.Id);
        var deleted = await _sut.GetByIdAsync(created.Id);
        deleted.Should().BeNull();
    }

    // ─── Pagination ─────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Pagination_ReturnsCorrectPages()
    {
        for (var i = 0; i < 15; i++)
        {
            await _sut.CreateAsync(
                SystemId, $"Weakness {i}", "ACAS", $"AC-{i}",
                CatSeverity.CatII, "test-poc",
                DateTime.UtcNow.AddDays(30 + i), createdBy: "test-user");
        }

        var page1 = await _sut.ListAsync(SystemId, page: 1, pageSize: 10);
        page1.Items.Should().HaveCount(10);
        page1.TotalCount.Should().Be(15);

        var page2 = await _sut.ListAsync(SystemId, page: 2, pageSize: 10);
        page2.Items.Should().HaveCount(5);
    }

    // ─── Lifecycle Transitions ──────────────────────────────────────────────

    [Fact]
    public async Task StatusTransition_OngoingToCompleted_Succeeds()
    {
        var poam = await CreateTestPoamAsync("AC-3", CatSeverity.CatI);

        await _sut.UpdateStatusAsync(poam.Id, PoamStatus.Completed,
            poam.RowVersion, "test-user");

        var result = await _sut.GetByIdAsync(poam.Id);
        result!.Status.Should().Be(PoamStatus.Completed);
        result.ActualCompletionDate.Should().NotBeNull();
    }

    [Fact]
    public async Task StatusTransition_OngoingToDelayed_Succeeds()
    {
        var poam = await CreateTestPoamAsync("AC-4", CatSeverity.CatII);

        await _sut.UpdateStatusAsync(poam.Id, PoamStatus.Delayed,
            poam.RowVersion, "test-user",
            delayReason: "Vendor delay",
            revisedDate: DateTime.UtcNow.AddDays(60));

        var result = await _sut.GetByIdAsync(poam.Id);
        result!.Status.Should().Be(PoamStatus.Delayed);
    }

    // ─── Bulk Create from Findings ──────────────────────────────────────────

    [Fact]
    public async Task BulkCreateFromFindings_CreatesPOAMs()
    {
        _db.Findings.Add(new ComplianceFinding
        {
            Id = "finding-int-001",
            ControlId = "AC-5",
            ControlFamily = "AC",
            Title = "Finding weakness",
            Description = "Finding weakness detail",
            Severity = FindingSeverity.High,
            Source = "ACAS",
            Status = FindingStatus.Open,
            DiscoveredAt = DateTime.UtcNow,
            ResourceId = "test-resource",
            ResourceType = "Microsoft.Compute/virtualMachines",
            AssessmentId = "assess-001",
        });
        await _db.SaveChangesAsync();

        var result = await _sut.BulkCreateFromFindingsAsync(
            SystemId, new[] { "finding-int-001" }, createdBy: "test-user");

        result.Created.Should().BeGreaterOrEqualTo(1);
        result.Results.Should().Contain(r => r.FindingId == "finding-int-001" && r.Status == "created");
    }

    // ─── Component Linkage ──────────────────────────────────────────────────

    [Fact]
    public async Task ComponentLinkage_LinkAndUnlink()
    {
        var poam = await CreateTestPoamAsync("AC-6", CatSeverity.CatIII);

        await _sut.LinkComponentsAsync(poam.Id, new[] { CompId1, CompId2 });

        var detail = await _sut.GetByIdAsync(poam.Id);
        detail!.ComponentLinks.Should().HaveCount(2);

        await _sut.UnlinkComponentsAsync(poam.Id, new[] { CompId1 });
        var after = await _sut.GetByIdAsync(poam.Id);
        after!.ComponentLinks.Should().HaveCount(1);
    }

    // ─── Metrics ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Metrics_ReturnsCorrectCounts()
    {
        await _sut.CreateAsync(
            SystemId, "W1", "STIG", "AC-7", CatSeverity.CatI,
            "poc", DateTime.UtcNow.AddDays(-5), createdBy: "test-user");
        await _sut.CreateAsync(
            SystemId, "W2", "STIG", "AC-8", CatSeverity.CatII,
            "poc", DateTime.UtcNow.AddDays(30), createdBy: "test-user");

        var metrics = await _sut.GetMetricsAsync(SystemId);

        metrics.TotalOpen.Should().BeGreaterOrEqualTo(2);
        metrics.CatICount.Should().BeGreaterOrEqualTo(1);
        metrics.CatIICount.Should().BeGreaterOrEqualTo(1);
    }

    // ─── Export ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportEmassExcel_ProducesValidXlsx()
    {
        await CreateTestPoamAsync("AC-9");

        var data = await _sut.ExportEmassExcelAsync(SystemId);

        data.Should().NotBeEmpty();
        // XLSX files start with PK (ZIP magic bytes)
        data[0].Should().Be(0x50);
        data[1].Should().Be(0x4B);
    }

    [Fact]
    public async Task ExportOscalJson_ProducesValidJson()
    {
        await CreateTestPoamAsync("AC-10", CatSeverity.CatI);

        var data = await _sut.ExportOscalJsonAsync(SystemId);
        var json = System.Text.Encoding.UTF8.GetString(data);

        json.Should().Contain("plan_of_action_and_milestones");
    }

    [Fact]
    public async Task ExportCsv_ProducesValidCsv()
    {
        await CreateTestPoamAsync("AC-11", CatSeverity.CatIII);

        var data = await _sut.ExportCsvAsync(SystemId);
        var csv = System.Text.Encoding.UTF8.GetString(data);

        csv.Should().Contain("SecurityControlNumber");
    }
}
