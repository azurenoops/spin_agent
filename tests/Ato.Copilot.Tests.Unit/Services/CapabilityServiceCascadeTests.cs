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

namespace Ato.Copilot.Tests.Unit.Services;

public class CapabilityServiceCascadeTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly CapabilityService _sut;

    private const string SystemId1 = "sys-001";
    private const string SystemId2 = "sys-002";
    private const string BoundaryId = "bnd-001";
    private const string CapId = "cap-001";
    private const string CapId2 = "cap-002";
    private const string ComponentId = "comp-001";

    public CapabilityServiceCascadeTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CapCascadeTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(options);
        var logger = Mock.Of<ILogger<CapabilityService>>();
        var narrativeService = new NarrativeTemplateService();
        _sut = new CapabilityService(_db, logger, narrativeService, Mock.Of<IDeviationService>(), Mock.Of<IOrgInheritanceService>());

        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.RegisteredSystems.AddRange(
            new RegisteredSystem
            {
                Id = SystemId1, Name = "System Alpha",
                SystemType = SystemType.MajorApplication,
                MissionCriticality = MissionCriticality.MissionEssential,
                HostingEnvironment = "Azure Gov", CreatedBy = "test", IsActive = true,
            },
            new RegisteredSystem
            {
                Id = SystemId2, Name = "System Beta",
                SystemType = SystemType.Enclave,
                MissionCriticality = MissionCriticality.MissionEssential,
                HostingEnvironment = "On-Prem", CreatedBy = "test", IsActive = true,
            }
        );

        _db.AuthorizationBoundaryDefinitions.Add(new AuthorizationBoundaryDefinition
        {
            Id = BoundaryId, RegisteredSystemId = SystemId1,
            Name = "Production", BoundaryType = BoundaryDefinitionType.Logical,
            IsPrimary = true, CreatedBy = "test",
        });

        _db.SecurityCapabilities.AddRange(
            new SecurityCapability
            {
                Id = CapId, Name = "MFA", Provider = "Microsoft Entra ID",
                Category = "IA", Description = "Enforces multi-factor authentication",
                ImplementationStatus = CapabilityStatus.Implemented,
                Owner = "Cloud Team", CreatedBy = "test",
            },
            new SecurityCapability
            {
                Id = CapId2, Name = "Firewall", Provider = "Azure Firewall",
                Category = "SC", Description = "Network boundary protection",
                ImplementationStatus = CapabilityStatus.Implemented,
                Owner = "Network Team", CreatedBy = "test",
            }
        );

        // Component with capability link and system assignment
        _db.SystemComponents.Add(new SystemComponent
        {
            Id = ComponentId, Name = "Entra ID Tenant",
            ComponentType = ComponentType.Thing,
            Status = ComponentStatus.Active,
            Owner = "Cloud Team", CreatedBy = "test",
        });

        _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
        {
            SystemComponentId = ComponentId,
            SecurityCapabilityId = CapId,
        });

        _db.ComponentSystemAssignments.Add(new ComponentSystemAssignment
        {
            SystemComponentId = ComponentId,
            RegisteredSystemId = SystemId1,
            AuthorizationBoundaryDefinitionId = BoundaryId,
            CreatedBy = "test",
        });

        // Mappings: CapId → IA-2 on system 1 and system 2
        _db.CapabilityControlMappings.AddRange(
            new CapabilityControlMapping
            {
                SecurityCapabilityId = CapId, ControlId = "ia-2",
                RegisteredSystemId = SystemId1, Role = CapabilityMappingRole.Primary,
                AuthorizationBoundaryDefinitionId = BoundaryId, CreatedBy = "test",
            },
            new CapabilityControlMapping
            {
                SecurityCapabilityId = CapId, ControlId = "ia-2",
                RegisteredSystemId = SystemId2, Role = CapabilityMappingRole.Primary,
                CreatedBy = "test",
            }
        );

        // NIST control
        _db.NistControls.Add(new NistControl
        {
            Id = "ia-2", Title = "Identification and Authentication",
            Family = "IA", ImpactLevel = "Low",
        });

        // Control implementations with existing narratives
        _db.ControlImplementations.AddRange(
            new ControlImplementation
            {
                Id = "impl-1",
                RegisteredSystemId = SystemId1, ControlId = "ia-2",
                SecurityCapabilityId = CapId,
                Narrative = "Old narrative system 1",
                IsAutoPopulated = true, AuthoredBy = "test", CurrentVersion = 1,
            },
            new ControlImplementation
            {
                Id = "impl-2",
                RegisteredSystemId = SystemId2, ControlId = "ia-2",
                SecurityCapabilityId = CapId,
                Narrative = "Old narrative system 2",
                IsAutoPopulated = true, AuthoredBy = "test", CurrentVersion = 1,
            }
        );

        _db.SaveChanges();
    }

    // ─── Cascade Regeneration Tests ──────────────────────────────────────────

    [Fact]
    public async Task UpdateCapability_DescriptionChanged_RegeneratesNarrativesAcrossSystems()
    {
        var request = new CreateCapabilityRequest
        {
            Name = "MFA",
            Provider = "Microsoft Entra ID",
            Category = "IA",
            Description = "Updated: enforces MFA with conditional access policies",
            ImplementationStatus = "Implemented",
            Owner = "Cloud Team",
        };

        var (result, conflict) = await _sut.UpdateCapabilityAsync(CapId, request, "admin");

        conflict.Should().BeFalse();
        result.Should().NotBeNull();
        result!.NarrativesUpdated.Should().Be(2);
        result.NarrativesSkipped.Should().Be(0);
    }

    [Fact]
    public async Task UpdateCapability_DescriptionChanged_CreatesNarrativeVersions()
    {
        var request = new CreateCapabilityRequest
        {
            Name = "MFA",
            Provider = "Microsoft Entra ID",
            Category = "IA",
            Description = "Changed description",
            ImplementationStatus = "Implemented",
            Owner = "Cloud Team",
        };

        await _sut.UpdateCapabilityAsync(CapId, request, "admin");

        var versions = await _db.NarrativeVersions.ToListAsync();
        versions.Should().HaveCount(2);
        versions.Should().AllSatisfy(v =>
        {
            v.ChangeReason.Should().Contain("Cascade");
            v.AuthoredBy.Should().Be("admin");
            v.VersionNumber.Should().Be(1);
        });

        // Verify CurrentVersion was incremented on implementations
        var impls = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId == CapId)
            .ToListAsync();
        impls.Should().AllSatisfy(i => i.CurrentVersion.Should().Be(2));
    }

    [Fact]
    public async Task UpdateCapability_DescriptionChanged_IncludesComponentContext()
    {
        var request = new CreateCapabilityRequest
        {
            Name = "MFA",
            Provider = "Microsoft Entra ID",
            Category = "IA",
            Description = "Updated description with component context",
            ImplementationStatus = "Implemented",
            Owner = "Cloud Team",
        };

        await _sut.UpdateCapabilityAsync(CapId, request, "admin");

        // System 1 has component assignment — narrative should include component details
        var impl1 = await _db.ControlImplementations.FindAsync("impl-1");
        impl1!.Narrative.Should().Contain("Entra ID Tenant");

        // System 2 has no component assignment — narrative should still be regenerated (without component details)
        var impl2 = await _db.ControlImplementations.FindAsync("impl-2");
        impl2!.Narrative.Should().NotBe("Old narrative system 2");
    }

    [Fact]
    public async Task UpdateCapability_ManuallyCustomized_SkipsRegeneration()
    {
        // Mark impl-1 as manually customized
        var impl = await _db.ControlImplementations.FindAsync("impl-1");
        impl!.IsManuallyCustomized = true;
        impl.Narrative = "Custom narrative — do not touch";
        await _db.SaveChangesAsync();

        var request = new CreateCapabilityRequest
        {
            Name = "MFA",
            Provider = "Microsoft Entra ID",
            Category = "IA",
            Description = "Changed description",
            ImplementationStatus = "Implemented",
            Owner = "Cloud Team",
        };

        var (result, _) = await _sut.UpdateCapabilityAsync(CapId, request, "admin");

        result!.NarrativesUpdated.Should().Be(1);
        result.NarrativesSkipped.Should().Be(1);

        // Custom narrative should be untouched
        var impl1 = await _db.ControlImplementations.FindAsync("impl-1");
        impl1!.Narrative.Should().Be("Custom narrative — do not touch");

        // Non-custom should be updated
        var impl2 = await _db.ControlImplementations.FindAsync("impl-2");
        impl2!.Narrative.Should().NotBe("Old narrative system 2");
    }

    [Fact]
    public async Task UpdateCapability_NoDescriptionChange_SkipsCascade()
    {
        var request = new CreateCapabilityRequest
        {
            Name = "MFA Renamed",
            Provider = "Microsoft Entra ID",
            Category = "IA",
            Description = "Enforces multi-factor authentication", // same as original
            ImplementationStatus = "Implemented",
            Owner = "Cloud Team",
        };

        var (result, _) = await _sut.UpdateCapabilityAsync(CapId, request, "admin");

        result!.NarrativesUpdated.Should().Be(0);
        result.NarrativesSkipped.Should().Be(0);

        // Narratives should be unchanged
        var impl1 = await _db.ControlImplementations.FindAsync("impl-1");
        impl1!.Narrative.Should().Be("Old narrative system 1");
    }

    [Fact]
    public async Task UpdateCapability_EmptyComponents_FallsBackToDeterministic()
    {
        // System 2 has NO component assignments — should still regenerate with deterministic template
        var request = new CreateCapabilityRequest
        {
            Name = "MFA",
            Provider = "Microsoft Entra ID",
            Category = "IA",
            Description = "Different description",
            ImplementationStatus = "Implemented",
            Owner = "Cloud Team",
        };

        await _sut.UpdateCapabilityAsync(CapId, request, "admin");

        var impl2 = await _db.ControlImplementations.FindAsync("impl-2");
        impl2!.Narrative.Should().NotBeNullOrEmpty();
        impl2.Narrative.Should().Contain("MFA");
    }

    // ─── Impact Preview Tests ────────────────────────────────────────────────

    [Fact]
    public async Task GetCapabilityImpactPreview_ReturnsCorrectCounts()
    {
        var preview = await _sut.GetCapabilityImpactPreviewAsync(CapId);

        preview.Should().NotBeNull();
        preview!.TotalNarratives.Should().Be(2);
        preview.TotalSystems.Should().Be(2);
        preview.CustomSkipped.Should().Be(0);
        preview.BySystem.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCapabilityImpactPreview_WithCustomNarratives_CountsSkipped()
    {
        // Mark one as custom
        var impl = await _db.ControlImplementations.FindAsync("impl-1");
        impl!.IsManuallyCustomized = true;
        await _db.SaveChangesAsync();

        var preview = await _sut.GetCapabilityImpactPreviewAsync(CapId);

        preview.Should().NotBeNull();
        preview!.TotalNarratives.Should().Be(1);
        preview.CustomSkipped.Should().Be(1);
        preview.BySystem.First(s => s.SystemId == SystemId1).CustomSkipped.Should().Be(1);
    }

    [Fact]
    public async Task GetCapabilityImpactPreview_NotFound_ReturnsNull()
    {
        var preview = await _sut.GetCapabilityImpactPreviewAsync("nonexistent");
        preview.Should().BeNull();
    }
}
