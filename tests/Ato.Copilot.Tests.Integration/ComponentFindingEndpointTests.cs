using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for component risk summary endpoint and finding-component
/// resolution workflow (Feature 040 — User Story 6).
/// </summary>
public class ComponentFindingEndpointTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _service;

    public ComponentFindingEndpointTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CompFindingIntegration_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(options);
        _service = new ComponentService(
            _db, NullLogger<ComponentService>.Instance, new NarrativeTemplateService(),
            new SystemCapabilityLinkService(_db, NullLogger<SystemCapabilityLinkService>.Instance));
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // ─── End-to-End: Import → Assess → Resolve → Risk Summary ───────────────

    [Fact]
    public async Task EndToEnd_ImportAssessResolveSummarize()
    {
        // 1. Create system
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Finding Test System",
            Acronym = "FTS",
            IsActive = true,
        };
        _db.RegisteredSystems.Add(system);

        // 2. Import 2 components
        var comp1 = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "SQL Database",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = system.Id,
            AzureResourceId = "/sub/rg/providers/Microsoft.Sql/servers/sql-prod",
        };
        var comp2 = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Web App",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = system.Id,
            AzureResourceId = "/sub/rg/providers/Microsoft.Web/sites/webapp-prod",
        };
        _db.SystemComponents.AddRange(comp1, comp2);

        // 3. Create assessment with findings for those resources + one unlinked
        var assessment = new ComplianceAssessment
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            Status = AssessmentStatus.Completed,
            Framework = "NIST80053",
        };
        assessment.Findings.AddRange(new[]
        {
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "AC-2", Title = "SQL Access",
                ResourceId = "/sub/rg/providers/Microsoft.Sql/servers/sql-prod",
                ResourceType = "Microsoft.Sql/servers",
                Severity = FindingSeverity.High, Status = FindingStatus.Open,
            },
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "SC-7", Title = "Web TLS",
                ResourceId = "/sub/rg/providers/Microsoft.Web/sites/webapp-prod",
                ResourceType = "Microsoft.Web/sites",
                Severity = FindingSeverity.Critical, Status = FindingStatus.Open,
            },
            new ComplianceFinding
            {
                Id = Guid.NewGuid().ToString(), ControlId = "AU-3", Title = "Orphan Finding",
                ResourceId = "/sub/rg/providers/Microsoft.Storage/storageAccounts/orphan",
                ResourceType = "Microsoft.Storage/storageAccounts",
                Severity = FindingSeverity.Medium, Status = FindingStatus.Open,
            },
        });
        _db.Assessments.Add(assessment);
        await _db.SaveChangesAsync();

        // 4. Resolve findings → components
        var resolved = await _service.ResolveFindingComponentsAsync(system.Id);
        resolved.Should().Be(2); // SQL + Web

        // 5. Get risk summary
        var summary = await _service.GetComponentRiskSummaryAsync(system.Id, assessment.Id);
        summary.TotalFindingCount.Should().Be(3);
        summary.UnlinkedFindingCount.Should().Be(1);
        summary.ComponentRisks.Should().HaveCount(2);

        var sqlRisk = summary.ComponentRisks.First(r => r.ComponentName == "SQL Database");
        sqlRisk.OpenFindingCount.Should().Be(1);
        sqlRisk.HighestSeverity.Should().Be("High");

        var webRisk = summary.ComponentRisks.First(r => r.ComponentName == "Web App");
        webRisk.OpenFindingCount.Should().Be(1);
        webRisk.HighestSeverity.Should().Be("Critical");
    }

    // ─── Retroactive Link After Component Import ─────────────────────────────

    [Fact]
    public async Task RetroactiveLink_AfterComponentCreation()
    {
        // 1. Create system + assessment with unlinked finding
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Retro System",
            Acronym = "RS",
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
        assessment.Findings.Add(new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(), ControlId = "CM-6", Title = "Config Gap",
            ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/new-vm",
            ResourceType = "Microsoft.Compute/virtualMachines",
            Severity = FindingSeverity.High, Status = FindingStatus.Open,
        });
        _db.Assessments.Add(assessment);
        await _db.SaveChangesAsync();

        // 2. Initially no components → risk summary shows all unlinked
        var before = await _service.GetComponentRiskSummaryAsync(system.Id, assessment.Id);
        before.UnlinkedFindingCount.Should().Be(1);
        before.ComponentRisks.Should().BeEmpty();

        // 3. Create component matching the finding's ResourceId
        var component = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New VM",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = system.Id,
            AzureResourceId = "/sub/rg/providers/Microsoft.Compute/vm/new-vm",
        };
        _db.SystemComponents.Add(component);
        await _db.SaveChangesAsync();

        // 4. Retroactive link
        var linked = await _service.RetroactiveLinkComponentAsync(component.Id);
        linked.Should().Be(1);

        // 5. Risk summary now shows the component
        var after = await _service.GetComponentRiskSummaryAsync(system.Id, assessment.Id);
        after.UnlinkedFindingCount.Should().Be(0);
        after.ComponentRisks.Should().HaveCount(1);
        after.ComponentRisks[0].ComponentName.Should().Be("New VM");
    }

    // ─── ComponentId Filtering ───────────────────────────────────────────────

    [Fact]
    public async Task RiskSummary_WithMultipleAssessments_AggregatesCorrectly()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Multi Assess System",
            Acronym = "MAS",
            IsActive = true,
        };
        _db.RegisteredSystems.Add(system);

        var comp = new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Shared VM",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = system.Id,
            AzureResourceId = "/sub/rg/providers/Microsoft.Compute/vm/shared",
        };
        _db.SystemComponents.Add(comp);

        // Assessment 1
        var a1 = new ComplianceAssessment
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            Status = AssessmentStatus.Completed,
            Framework = "NIST80053",
        };
        a1.Findings.Add(new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(), ControlId = "AC-2", Title = "A1F1",
            ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/shared",
            ResourceType = "Microsoft.Compute/virtualMachines",
            Severity = FindingSeverity.High, Status = FindingStatus.Open,
            ComponentId = comp.Id,
        });
        _db.Assessments.Add(a1);

        // Assessment 2 (specific query should only return this one)
        var a2 = new ComplianceAssessment
        {
            Id = Guid.NewGuid().ToString(),
            RegisteredSystemId = system.Id,
            Status = AssessmentStatus.Completed,
            Framework = "NIST80053",
        };
        a2.Findings.Add(new ComplianceFinding
        {
            Id = Guid.NewGuid().ToString(), ControlId = "SC-7", Title = "A2F1",
            ResourceId = "/sub/rg/providers/Microsoft.Compute/vm/shared",
            ResourceType = "Microsoft.Compute/virtualMachines",
            Severity = FindingSeverity.Critical, Status = FindingStatus.Open,
            ComponentId = comp.Id,
        });
        _db.Assessments.Add(a2);
        await _db.SaveChangesAsync();

        // Query specific assessment
        var result1 = await _service.GetComponentRiskSummaryAsync(system.Id, a1.Id);
        result1.TotalFindingCount.Should().Be(1);

        var result2 = await _service.GetComponentRiskSummaryAsync(system.Id, a2.Id);
        result2.TotalFindingCount.Should().Be(1);
        result2.ComponentRisks[0].HighestSeverity.Should().Be("Critical");

        // Query across all assessments
        var allResult = await _service.GetComponentRiskSummaryAsync(system.Id);
        allResult.TotalFindingCount.Should().Be(2);
        allResult.ComponentRisks.Should().HaveCount(1);
        allResult.ComponentRisks[0].OpenFindingCount.Should().Be(2);
    }
}
