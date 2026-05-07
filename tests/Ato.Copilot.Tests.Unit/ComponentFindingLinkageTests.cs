using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Unit tests for finding-component resolution, retroactive linking,
/// and risk summary aggregation (Feature 040 — User Story 6).
/// </summary>
public class ComponentFindingLinkageTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _service;

    public ComponentFindingLinkageTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var factory = new TestDbContextFactory(options);
        _db = factory.Context;
        _service = new ComponentService(factory, NullLogger<ComponentService>.Instance, new NarrativeTemplateService(), new SystemCapabilityLinkService(factory, NullLogger<SystemCapabilityLinkService>.Instance));
    }

    public void Dispose() => _db.Dispose();

    private async Task<(string systemId, string assessmentId)> SeedSystemWithAssessment()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test System",
            Acronym = "TS",
            IsActive = true,
        };
        _db.RegisteredSystems.Add(system);

        var assessment = new ComplianceAssessment
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            Status = AssessmentStatus.Completed,
            Framework = "NIST80053",
        };
        _db.Assessments.Add(assessment);
        await _db.SaveChangesAsync();
        return (system.Id, assessment.Id);
    }

    // ─── ResolveFindingComponentsAsync Tests ─────────────────────────────────

    [Fact]
    public async Task Resolve_LinksFindings_ByResourceIdMatch()
    {
        var (systemId, assessmentId) = await SeedSystemWithAssessment();

        var component = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "VM-01",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = systemId,
            AzureResourceId = "/subscriptions/sub1/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm-01",
        };
        _db.SystemComponents.Add(component);

        var finding = new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(),
            ControlId = "AC-2",
            Title = "Access finding",
            ResourceId = "/subscriptions/sub1/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm-01",
            ResourceType = "Microsoft.Compute/virtualMachines",
            Severity = FindingSeverity.High,
            Status = FindingStatus.Open,
        };
        var assessment = await _db.Assessments.FindAsync(assessmentId);
        assessment!.Findings.Add(finding);
        await _db.SaveChangesAsync();

        var linked = await _service.ResolveFindingComponentsAsync(systemId);

        linked.Should().Be(1);
        var updatedFinding = await _db.Findings.FindAsync(finding.Id);
        updatedFinding!.ComponentId.Should().Be(component.Id);
    }

    [Fact]
    public async Task Resolve_SkipsFindings_WhenNoMatchingComponent()
    {
        var (systemId, assessmentId) = await SeedSystemWithAssessment();

        var finding = new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(),
            ControlId = "AC-3",
            Title = "No match finding",
            ResourceId = "/subscriptions/sub1/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/sa-01",
            ResourceType = "Microsoft.Storage/storageAccounts",
            Severity = FindingSeverity.Medium,
            Status = FindingStatus.Open,
        };
        var assessment = await _db.Assessments.FindAsync(assessmentId);
        assessment!.Findings.Add(finding);
        await _db.SaveChangesAsync();

        var linked = await _service.ResolveFindingComponentsAsync(systemId);
        linked.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_SkipsAlreadyLinkedFindings()
    {
        var (systemId, assessmentId) = await SeedSystemWithAssessment();

        var component = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "VM-02",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = systemId,
            AzureResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-02",
        };
        _db.SystemComponents.Add(component);

        var finding = new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(),
            ControlId = "AU-3",
            Title = "Already linked",
            ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-02",
            ResourceType = "Microsoft.Compute/virtualMachines",
            Severity = FindingSeverity.Low,
            Status = FindingStatus.Open,
            ComponentId = "some-existing-component-id",
        };
        var assessment = await _db.Assessments.FindAsync(assessmentId);
        assessment!.Findings.Add(finding);
        await _db.SaveChangesAsync();

        var linked = await _service.ResolveFindingComponentsAsync(systemId);
        linked.Should().Be(0);
        var updatedFinding = await _db.Findings.FindAsync(finding.Id);
        updatedFinding!.ComponentId.Should().Be("some-existing-component-id");
    }

    // ─── RetroactiveLinkComponentAsync Tests ─────────────────────────────────

    [Fact]
    public async Task RetroactiveLink_LinksUnlinkedFindings_OnNewComponent()
    {
        var (systemId, assessmentId) = await SeedSystemWithAssessment();

        var finding = new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(),
            ControlId = "SC-7",
            Title = "Unlinked finding",
            ResourceId = "/sub/rg/providers/Microsoft.Network/nsg/nsg-01",
            ResourceType = "Microsoft.Network/networkSecurityGroups",
            Severity = FindingSeverity.High,
            Status = FindingStatus.Open,
        };
        var assessment = await _db.Assessments.FindAsync(assessmentId);
        assessment!.Findings.Add(finding);

        var component = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "NSG-01",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = systemId,
            AzureResourceId = "/sub/rg/providers/Microsoft.Network/nsg/nsg-01",
        };
        _db.SystemComponents.Add(component);
        await _db.SaveChangesAsync();

        var linked = await _service.RetroactiveLinkComponentAsync(component.Id);
        linked.Should().Be(1);

        var updatedFinding = await _db.Findings.FindAsync(finding.Id);
        updatedFinding!.ComponentId.Should().Be(component.Id);
    }

    [Fact]
    public async Task RetroactiveLink_Returns0_WhenNoAzureResourceId()
    {
        var (systemId, _) = await SeedSystemWithAssessment();

        var component = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Manual Component",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = systemId,
        };
        _db.SystemComponents.Add(component);
        await _db.SaveChangesAsync();

        var linked = await _service.RetroactiveLinkComponentAsync(component.Id);
        linked.Should().Be(0);
    }

    // ─── GetComponentRiskSummaryAsync Tests ──────────────────────────────────

    [Fact]
    public async Task RiskSummary_AggregatesCorrectly()
    {
        var (systemId, assessmentId) = await SeedSystemWithAssessment();

        var comp1 = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "SQL DB",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = systemId,
            AzureResourceId = "/sub/rg/providers/Microsoft.Sql/servers/sql-01",
        };
        var comp2 = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "VM Prod",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = systemId,
            AzureResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-01",
        };
        _db.SystemComponents.AddRange(comp1, comp2);

        var assessment = await _db.Assessments.FindAsync(assessmentId);
        assessment!.Findings.AddRange(new[]
        {
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "AC-2", Title = "F1",
                ResourceId = "/sub/rg/providers/Microsoft.Sql/servers/sql-01",
                ResourceType = "Microsoft.Sql/servers",
                Severity = FindingSeverity.High, Status = FindingStatus.Open,
                ComponentId = comp1.Id,
            },
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "AC-3", Title = "F2",
                ResourceId = "/sub/rg/providers/Microsoft.Sql/servers/sql-01",
                ResourceType = "Microsoft.Sql/servers",
                Severity = FindingSeverity.Critical, Status = FindingStatus.InProgress,
                ComponentId = comp1.Id,
            },
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "SC-7", Title = "F3",
                ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/vm-01",
                ResourceType = "Microsoft.Compute/virtualMachines",
                Severity = FindingSeverity.Medium, Status = FindingStatus.Open,
                ComponentId = comp2.Id,
            },
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "AU-3", Title = "F4 Unlinked",
                ResourceId = "/sub/rg/providers/Microsoft.Storage/sa/sa-01",
                ResourceType = "Microsoft.Storage/storageAccounts",
                Severity = FindingSeverity.Low, Status = FindingStatus.Open,
            },
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetComponentRiskSummaryAsync(systemId, assessmentId);

        result.TotalFindingCount.Should().Be(4);
        result.UnlinkedFindingCount.Should().Be(1);
        result.ComponentRisks.Should().HaveCount(2);

        var sqlRisk = result.ComponentRisks.First(r => r.ComponentId == comp1.Id);
        sqlRisk.ComponentName.Should().Be("SQL DB");
        sqlRisk.OpenFindingCount.Should().Be(2);
        sqlRisk.HighestSeverity.Should().Be("Critical");

        var vmRisk = result.ComponentRisks.First(r => r.ComponentId == comp2.Id);
        vmRisk.ComponentName.Should().Be("VM Prod");
        vmRisk.OpenFindingCount.Should().Be(1);
    }

    [Fact]
    public async Task RiskSummary_ExcludesRemediatedFindings()
    {
        var (systemId, assessmentId) = await SeedSystemWithAssessment();

        var comp = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Remediated VM",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = systemId,
        };
        _db.SystemComponents.Add(comp);

        var assessment = await _db.Assessments.FindAsync(assessmentId);
        assessment!.Findings.AddRange(new[]
        {
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "AC-2", Title = "Fixed",
                ResourceId = "/some/resource",
                ResourceType = "Microsoft.Compute/virtualMachines",
                Severity = FindingSeverity.High, Status = FindingStatus.Remediated,
                ComponentId = comp.Id,
            },
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "AC-3", Title = "FP",
                ResourceId = "/some/resource",
                ResourceType = "Microsoft.Compute/virtualMachines",
                Severity = FindingSeverity.Medium, Status = FindingStatus.FalsePositive,
                ComponentId = comp.Id,
            },
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetComponentRiskSummaryAsync(systemId, assessmentId);
        result.TotalFindingCount.Should().Be(2);

        // Both findings are Remediated or FalsePositive — 0 open
        var compRisk = result.ComponentRisks.First(r => r.ComponentId == comp.Id);
        compRisk.OpenFindingCount.Should().Be(0);
    }

    [Fact]
    public async Task Resolve_LinksSTIGResourceIdFinding()
    {
        // STIG/SCAP imports may produce findings with Azure resource IDs
        var (systemId, assessmentId) = await SeedSystemWithAssessment();

        var component = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Windows Server",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = systemId,
            AzureResourceId = "/subscriptions/sub1/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/win-server",
        };
        _db.SystemComponents.Add(component);

        // Simulate STIG finding using Azure resource ID format  
        var finding = new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(),
            ControlId = "CM-6",
            Title = "STIG V-12345",
            ResourceId = "/subscriptions/sub1/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/win-server",
            ResourceType = "Microsoft.Compute/virtualMachines",
            Severity = FindingSeverity.High,
            Status = FindingStatus.Open,
            ScanSource = ScanSourceType.Combined,
        };
        var assessment = await _db.Assessments.FindAsync(assessmentId);
        assessment!.Findings.Add(finding);
        await _db.SaveChangesAsync();

        var linked = await _service.ResolveFindingComponentsAsync(systemId);
        linked.Should().Be(1);

        var updated = await _db.Findings.FindAsync(finding.Id);
        updated!.ComponentId.Should().Be(component.Id);
    }
}
