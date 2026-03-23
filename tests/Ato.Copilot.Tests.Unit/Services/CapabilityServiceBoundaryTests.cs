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

public class CapabilityServiceBoundaryTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly CapabilityService _sut;

    private const string SystemId = "sys-001";
    private const string BoundaryPrimary = "bnd-primary";
    private const string BoundaryDevTest = "bnd-devtest";
    private const string CapId = "cap-001";

    public CapabilityServiceBoundaryTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CapBoundaryTests_{Guid.NewGuid()}")
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
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId,
            Name = "Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true,
        });

        _db.AuthorizationBoundaryDefinitions.AddRange(
            new AuthorizationBoundaryDefinition
            {
                Id = BoundaryPrimary, RegisteredSystemId = SystemId,
                Name = "Primary", BoundaryType = BoundaryDefinitionType.Logical,
                IsPrimary = true, CreatedBy = "test",
            },
            new AuthorizationBoundaryDefinition
            {
                Id = BoundaryDevTest, RegisteredSystemId = SystemId,
                Name = "Dev/Test", BoundaryType = BoundaryDefinitionType.Logical,
                IsPrimary = false, CreatedBy = "test",
            }
        );

        _db.SecurityCapabilities.Add(new SecurityCapability
        {
            Id = CapId, Name = "MFA", Provider = "Entra ID",
            Category = "IA", Description = "MFA",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "Test", CreatedBy = "test",
        });

        // Org-wide mapping (null boundary FK) — covers AC-2
        _db.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = CapId, ControlId = "AC-2",
            RegisteredSystemId = SystemId, Role = CapabilityMappingRole.Primary,
            AuthorizationBoundaryDefinitionId = null, // org-wide
            CreatedBy = "test",
        });

        // Boundary-specific mapping — covers IA-2 for Primary only
        _db.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = CapId, ControlId = "IA-2",
            RegisteredSystemId = SystemId, Role = CapabilityMappingRole.Primary,
            AuthorizationBoundaryDefinitionId = BoundaryPrimary,
            CreatedBy = "test",
        });

        // Boundary-specific mapping — covers SC-7 for Dev/Test only
        _db.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = CapId, ControlId = "SC-7",
            RegisteredSystemId = SystemId, Role = CapabilityMappingRole.Primary,
            AuthorizationBoundaryDefinitionId = BoundaryDevTest,
            CreatedBy = "test",
        });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetCoveredControlIds_NoBoundaryFilter_ReturnsAll()
    {
        var result = await _sut.GetCoveredControlIdsAsync(SystemId, null, default);

        result.Should().HaveCount(3);
        result.Should().Contain("AC-2");
        result.Should().Contain("IA-2");
        result.Should().Contain("SC-7");
    }

    [Fact]
    public async Task GetCoveredControlIds_PrimaryBoundary_IncludesOrgWide()
    {
        var result = await _sut.GetCoveredControlIdsAsync(SystemId, BoundaryPrimary, default);

        // Should include AC-2 (org-wide) + IA-2 (Primary-specific)
        result.Should().HaveCount(2);
        result.Should().Contain("AC-2");
        result.Should().Contain("IA-2");
        // Should NOT include SC-7 (Dev/Test-specific)
        result.Should().NotContain("SC-7");
    }

    [Fact]
    public async Task GetCoveredControlIds_DevTestBoundary_IncludesOrgWide()
    {
        var result = await _sut.GetCoveredControlIdsAsync(SystemId, BoundaryDevTest, default);

        // Should include AC-2 (org-wide) + SC-7 (DevTest-specific)
        result.Should().HaveCount(2);
        result.Should().Contain("AC-2");
        result.Should().Contain("SC-7");
        // Should NOT include IA-2 (Primary-specific)
        result.Should().NotContain("IA-2");
    }

    [Fact]
    public async Task GetCoveredControlIds_NullFkMeansAllBoundaries()
    {
        // AC-2 with null FK should appear in all boundary queries
        var primary = await _sut.GetCoveredControlIdsAsync(SystemId, BoundaryPrimary, default);
        var devTest = await _sut.GetCoveredControlIdsAsync(SystemId, BoundaryDevTest, default);
        var all = await _sut.GetCoveredControlIdsAsync(SystemId, null, default);

        primary.Should().Contain("AC-2");
        devTest.Should().Contain("AC-2");
        all.Should().Contain("AC-2");
    }

    [Fact]
    public async Task UpdateCapability_BoundaryScoped_ReturnsNarrativesByBoundary()
    {
        // Setup: add a ControlImplementation linked to the capability
        _db.NistControls.Add(new NistControl
        {
            Id = "AC-2", Family = "AC", Title = "Account Management",
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = SystemId,
            ControlId = "AC-2",
            SecurityCapabilityId = CapId,
            AuthoredBy = "test",
        });
        await _db.SaveChangesAsync();

        var request = new CreateCapabilityRequest
        {
            Name = "MFA", Provider = "Entra ID", Category = "IA",
            Description = "Updated MFA description",
            ImplementationStatus = "Implemented", Owner = "Test",
        };

        var (result, conflict) = await _sut.UpdateCapabilityAsync(CapId, request, "test");

        conflict.Should().BeFalse();
        result.Should().NotBeNull();
        result!.NarrativesUpdated.Should().Be(1);
        result.NarrativesByBoundary.Should().NotBeNull();
        // AC-2 mapping has null boundary FK → tracked as "Organization-Wide"
        result.NarrativesByBoundary.Should().ContainKey("Organization-Wide");
    }

    [Fact]
    public async Task UpdateCapability_SkipsCustomizedNarratives_LogsAuditEvent()
    {
        _db.NistControls.Add(new NistControl
        {
            Id = "IA-2", Family = "IA", Title = "Identification and Authentication",
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = SystemId,
            ControlId = "IA-2",
            SecurityCapabilityId = CapId,
            AuthoredBy = "test",
            IsManuallyCustomized = true,
            Narrative = "Custom narrative should be preserved",
        });
        await _db.SaveChangesAsync();

        var request = new CreateCapabilityRequest
        {
            Name = "MFA", Provider = "Entra ID", Category = "IA",
            Description = "Changed description",
            ImplementationStatus = "Implemented", Owner = "Test",
        };

        var (result, _) = await _sut.UpdateCapabilityAsync(CapId, request, "test");

        result.Should().NotBeNull();
        result!.NarrativesSkipped.Should().Be(1);

        // Verify audit event was logged with CompositeNarrativeSkipped type
        var activity = await _db.DashboardActivities
            .FirstOrDefaultAsync(a => a.EventType == "CompositeNarrativeSkipped");
        activity.Should().NotBeNull();
        activity!.Summary.Should().Contain("IA-2");

        // Verify the original narrative was preserved
        var impl = await _db.ControlImplementations
            .FirstOrDefaultAsync(ci => ci.ControlId == "IA-2" && ci.RegisteredSystemId == SystemId);
        impl!.Narrative.Should().Be("Custom narrative should be preserved");
    }

    [Fact]
    public async Task UpdateCapability_MultiMapping_GeneratesCompositeNarrative()
    {
        // Add a second capability + mapping for the same control
        var cap2 = new SecurityCapability
        {
            Id = "cap-002", Name = "PAM", Provider = "CyberArk",
            Category = "AC", Description = "Privileged access management",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "Test", CreatedBy = "test",
        };
        _db.SecurityCapabilities.Add(cap2);
        _db.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = "cap-002", ControlId = "AC-2",
            RegisteredSystemId = SystemId, Role = CapabilityMappingRole.Supporting,
            AuthorizationBoundaryDefinitionId = BoundaryPrimary,
            CreatedBy = "test",
        });
        _db.NistControls.Add(new NistControl
        {
            Id = "AC-2", Family = "AC", Title = "Account Management",
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = SystemId,
            ControlId = "AC-2",
            SecurityCapabilityId = CapId,
            AuthoredBy = "test",
        });
        await _db.SaveChangesAsync();

        var request = new CreateCapabilityRequest
        {
            Name = "MFA", Provider = "Entra ID", Category = "IA",
            Description = "Updated MFA description",
            ImplementationStatus = "Implemented", Owner = "Test",
        };

        var (result, _) = await _sut.UpdateCapabilityAsync(CapId, request, "test");

        result!.NarrativesUpdated.Should().Be(1);

        // Verify composite narrative was generated
        var impl = await _db.ControlImplementations
            .FirstOrDefaultAsync(ci => ci.ControlId == "AC-2" && ci.RegisteredSystemId == SystemId);
        impl!.Narrative.Should().Contain("through the following capabilities");
        impl.Narrative.Should().Contain("Organization-Wide");
        impl.Narrative.Should().Contain("Within the Primary boundary");
    }
}
