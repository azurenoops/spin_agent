using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for cascade narrative regeneration — end-to-end flows
/// testing capability update cascade, component update cascade, and NarrativeVersion creation.
/// </summary>
public class CascadeRegenerationTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly ComponentService _componentService;
    private readonly CapabilityService _capabilityService;

    private const string SystemId = "sys-cascade-001";
    private const string BoundaryId = "bnd-cascade-001";
    private const string CapId = "cap-cascade-001";

    public CascadeRegenerationTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CascadeIntegration_{Guid.NewGuid()}")
            .Options;
        var factory = new IntegrationTestDbContextFactory(options);
        _db = factory.Context;
        var narrativeService = new NarrativeTemplateService();
        _componentService = new ComponentService(
    factory, Mock.Of<ILogger<ComponentService>>(), narrativeService, new SystemCapabilityLinkService(factory, Mock.Of<ILogger<SystemCapabilityLinkService>>()));
_capabilityService = new CapabilityService(
    _db, Mock.Of<ILogger<CapabilityService>>(), narrativeService, Mock.Of<IDeviationService>(), Mock.Of<IOrgInheritanceService>());
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
            Id = SystemId, Name = "Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Gov", CreatedBy = "test", IsActive = true,
        });

        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = BoundaryId, RegisteredSystemId = SystemId,
            Name = "Production", BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true, CreatedBy = "test",
        });

        _db.SecurityCapabilities.Add(new SecurityCapability
        {
            Id = CapId, Name = "MFA", Provider = "Entra ID",
            Category = "IA", Description = "Multi-factor authentication",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "IAM Team", CreatedBy = "test",
        });

        _db.NistControls.AddRange(
            new NistControl { Id = "ia-2", Title = "Identification and Authentication", Family = "IA", ImpactLevel = "Low" },
            new NistControl { Id = "ia-5", Title = "Authenticator Management", Family = "IA", ImpactLevel = "Moderate" }
        );

        _db.SaveChanges();
    }

    // ─── End-to-End Capability Cascade ───────────────────────────────────────

    [Fact]
    public async Task CapabilityCascade_EndToEnd_RegeneratesNarrativesWithComponents()
    {
        // 1. Create component and assign
        var comp = await _componentService.CreateOrgComponentAsync(new CreateComponentRequest
        {
            Name = "Conditional Access",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [CapId],
        }, "test");
        await _componentService.AssignToSystemAsync(comp!.Id,
            new AssignComponentRequest
            {
                RegisteredSystemId = SystemId,
                AuthorizationBoundaryDefinitionId = BoundaryId,
            }, "test");

        // 2. Create mappings (generates initial narratives)
        await _capabilityService.CreateMappingsAsync(CapId, new CreateMappingsRequest
        {
            Mappings = [
                new CreateMappingItem { ControlId = "ia-2", Role = "Primary", RegisteredSystemId = SystemId },
                new CreateMappingItem { ControlId = "ia-5", Role = "Supporting", RegisteredSystemId = SystemId },
            ],
        }, "test");

        var initialImpls = await _db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == SystemId)
            .ToListAsync();
        initialImpls.Should().HaveCount(2);
        var initialNarrative = initialImpls.First().Narrative;

        // 3. Update capability description → triggers cascade
        var (result, _) = await _capabilityService.UpdateCapabilityAsync(CapId, new CreateCapabilityRequest
        {
            Name = "MFA",
            Provider = "Entra ID",
            Category = "IA",
            Description = "UPDATED: enforces MFA with conditional access policies and risk-based sign-in",
            ImplementationStatus = "Implemented",
            Owner = "IAM Team",
        }, "admin");

        result.Should().NotBeNull();
        result!.NarrativesUpdated.Should().Be(2);

        // 4. Verify NarrativeVersions created
        var versions = await _db.NarrativeVersions.ToListAsync();
        versions.Should().HaveCount(2);
        versions.Should().AllSatisfy(v =>
        {
            v.ChangeReason.Should().Contain("Cascade");
            v.Content.Should().NotBeNullOrEmpty();
        });

        // 5. Verify narratives now contain component name
        var updatedImpls = await _db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == SystemId)
            .ToListAsync();
        updatedImpls.Should().AllSatisfy(i =>
        {
            i.Narrative.Should().Contain("Conditional Access");
            i.CurrentVersion.Should().Be(2);
        });
    }

    // ─── End-to-End Component Cascade ────────────────────────────────────────

    [Fact]
    public async Task ComponentCascade_EndToEnd_RegeneratesNarrativesOnRename()
    {
        // 1. Create component linked to capability
        var comp = await _componentService.CreateOrgComponentAsync(new CreateComponentRequest
        {
            Name = "Old Firewall Name",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [CapId],
        }, "test");
        await _componentService.AssignToSystemAsync(comp!.Id,
            new AssignComponentRequest { RegisteredSystemId = SystemId }, "test");

        // 2. Create mappings (generates initial narratives)
        await _capabilityService.CreateMappingsAsync(CapId, new CreateMappingsRequest
        {
            Mappings = [new CreateMappingItem { ControlId = "ia-2", Role = "Primary", RegisteredSystemId = SystemId }],
        }, "test");

        // 3. Rename component → triggers cascade
        await _componentService.UpdateOrgComponentAsync(comp.Id, new CreateComponentRequest
        {
            Name = "New Firewall Name",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [CapId],
        });

        // 4. Verify narrative updated with new name
        var impl = await _db.ControlImplementations
            .FirstOrDefaultAsync(ci => ci.RegisteredSystemId == SystemId && ci.ControlId == "ia-2");
        impl.Should().NotBeNull();
        impl!.Narrative.Should().Contain("New Firewall Name");

        // 5. Verify NarrativeVersion created
        var versions = await _db.NarrativeVersions
            .Where(v => v.ControlImplementationId == impl.Id)
            .ToListAsync();
        versions.Should().NotBeEmpty();
        versions.Last().ChangeReason.Should().Contain("component");
    }

    // ─── Custom Narrative Skipped ────────────────────────────────────────────

    [Fact]
    public async Task Cascade_CustomNarrativeNotOverwritten_EndToEnd()
    {
        // 1. Create component + mappings
        var comp = await _componentService.CreateOrgComponentAsync(new CreateComponentRequest
        {
            Name = "Test Component",
            ComponentType = "Thing",
            Status = "Active",
            LinkedCapabilityIds = [CapId],
        }, "test");
        await _componentService.AssignToSystemAsync(comp!.Id,
            new AssignComponentRequest { RegisteredSystemId = SystemId }, "test");

        await _capabilityService.CreateMappingsAsync(CapId, new CreateMappingsRequest
        {
            Mappings = [new CreateMappingItem { ControlId = "ia-2", Role = "Primary", RegisteredSystemId = SystemId }],
        }, "test");

        // 2. Mark the narrative as manually customized
        var impl = await _db.ControlImplementations
            .FirstAsync(ci => ci.RegisteredSystemId == SystemId && ci.ControlId == "ia-2");
        impl.IsManuallyCustomized = true;
        impl.Narrative = "This is a custom narrative that shall not be overwritten";
        await _db.SaveChangesAsync();

        // 3. Update capability → should skip custom narrative
        var (result, _) = await _capabilityService.UpdateCapabilityAsync(CapId, new CreateCapabilityRequest
        {
            Name = "MFA",
            Provider = "Entra ID",
            Category = "IA",
            Description = "Changed description to trigger cascade",
            ImplementationStatus = "Implemented",
            Owner = "IAM Team",
        }, "admin");

        result!.NarrativesSkipped.Should().Be(1);

        // 4. Verify custom narrative untouched
        impl = await _db.ControlImplementations
            .FirstAsync(ci => ci.RegisteredSystemId == SystemId && ci.ControlId == "ia-2");
        impl.Narrative.Should().Be("This is a custom narrative that shall not be overwritten");
    }
}
