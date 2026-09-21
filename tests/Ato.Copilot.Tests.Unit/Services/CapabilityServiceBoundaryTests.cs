using FluentAssertions;
using Ato.Copilot.Core.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
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

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(true, null, false)]
    [InlineData(false, "", false)]
    [InlineData(true, "   ", false)]
    [InlineData(false, "Template text", true)]
    [InlineData(true, "Model text", true)]
    public async Task Regenerate_ProvenanceUsesModelOutcome(bool bulk, string? modelText, bool expectedAi)
    {
        // Arrange
        var fallback = new NarrativeTemplateService().GenerateEnrichedNarrative(
            "MFA", "Entra ID", "MFA", "AC-2", "AC-2", null, "Primary");
        var responseText = modelText == "Template text" ? fallback : modelText;
        var client = new Mock<IChatClient>();
        client.Setup(service => service.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        var generator = new NarrativeTemplateService(client.Object,
            new AzureAiOptions { Enabled = true, Endpoint = "https://test.openai.azure.us/" },
            Mock.Of<ILogger<NarrativeTemplateService>>());
        var service = new CapabilityService(_db, Mock.Of<ILogger<CapabilityService>>(),
            generator, Mock.Of<IDeviationService>(), Mock.Of<IOrgInheritanceService>());
        var implementation = new ControlImplementation
        {
            RegisteredSystemId = SystemId, ControlId = "AC-2", SecurityCapabilityId = CapId,
            PolicyNarrative = "Human policy", AuthoredBy = "author",
            ImplementationStatus = ImplementationStatus.Planned, ApprovalStatus = SspSectionStatus.Draft
        };
        _db.ControlImplementations.Add(implementation);
        await _db.SaveChangesAsync();

        // Act
        if (bulk)
            await service.BulkRegenerateNarrativesForCapabilityAsync(SystemId, CapId, "author");
        else
            await service.RegenerateNarrativeWithAiAsync(SystemId, "AC-2", "author");

        // Assert
        await _db.Entry(implementation).ReloadAsync();
        implementation.AiSuggested.Should().Be(expectedAi);
        implementation.IsAutoPopulated.Should().BeTrue();
        implementation.Narrative.Should().Be(expectedAi ? responseText : fallback);
        implementation.PolicyNarrative.Should().Be("Human policy");
        implementation.ImplementationStatus.Should().Be(ImplementationStatus.Planned);
        implementation.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
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
    public async Task GetAvailableCapabilities_WithOrganizationAndCspSources_ReturnsOnlyEligibleItems()
    {
        // Arrange
        var organizationCapability = new SecurityCapability
        {
            Id = "cap-available",
            Name = "Endpoint Protection",
            Provider = "Organization SOC",
            Category = "SI",
            Description = "Organization-managed endpoint protection",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "ISSO",
            CreatedBy = "test",
        };
        _db.SecurityCapabilities.Add(organizationCapability);
        _db.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = organizationCapability.Id,
            ControlId = "SI-3",
            RegisteredSystemId = "another-system",
            Role = CapabilityMappingRole.Primary,
            CreatedBy = "test",
        });

        var published = CreateCspComponent("Published CSP", CspInheritedComponentStatus.Published);
        var draft = CreateCspComponent("Draft CSP", CspInheritedComponentStatus.Draft);
        var eligibleCsp = CreateCspCapability(published, "CSP Key Management", CspInheritedCapabilityStatus.Mapped, "SC-12");
        var needsReview = CreateCspCapability(published, "Needs Review", CspInheritedCapabilityStatus.NeedsReview, "AC-2");
        var unpublished = CreateCspCapability(draft, "Draft Parent", CspInheritedCapabilityStatus.Mapped, "AU-2");
        var subscribed = CreateCspCapability(published, "Already Subscribed", CspInheritedCapabilityStatus.Mapped, "IA-2");
        _db.AddRange(published, draft, eligibleCsp, needsReview, unpublished, subscribed);
        _db.CapabilitySubscriptions.Add(new CapabilitySubscription
        {
            RegisteredSystemId = SystemId,
            CspInheritedCapabilityId = subscribed.Id.ToString(),
            IsActive = true,
            SubscribedBy = "test",
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetAvailableCapabilitiesAsync(SystemId, search: null);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle(item =>
            item.Id == organizationCapability.Id && item.Source == "Organization");
        result.Items.Should().ContainSingle(item =>
            item.Id == eligibleCsp.Id.ToString() && item.Source == "CSP");
        result.Items.Should().NotContain(item => item.Id == CapId);
        result.Items.Should().NotContain(item => item.Id == needsReview.Id.ToString());
        result.Items.Should().NotContain(item => item.Id == unpublished.Id.ToString());
        result.Items.Should().NotContain(item => item.Id == subscribed.Id.ToString());
    }

    [Fact]
    public async Task GetAvailableCapabilities_WithSearch_PreservesExistingExclusionCount()
    {
        // Arrange
        var matching = new SecurityCapability
        {
            Id = "cap-search-match",
            Name = "Endpoint Protection",
            Provider = "Organization SOC",
            Category = "SI",
            Description = "Organization-managed endpoint protection",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "ISSO",
            CreatedBy = "test",
        };
        var nonmatching = new SecurityCapability
        {
            Id = "cap-search-other",
            Name = "Backup Service",
            Provider = "Continuity Team",
            Category = "CP",
            Description = "Organization-managed backups",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "ISSO",
            CreatedBy = "test",
        };
        _db.SecurityCapabilities.AddRange(matching, nonmatching);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetAvailableCapabilitiesAsync(SystemId, "Organization SOC");

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle(item => item.Id == matching.Id);
        result.TotalCount.Should().Be(3);
        result.ExcludedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetCapabilityCoverage_WithActiveCspSubscription_IncludesCspNarrativeStatus()
    {
        // Arrange
        var component = CreateCspComponent("Azure Government", CspInheritedComponentStatus.Published);
        var capability = CreateCspCapability(
            component,
            "Managed Audit Logging",
            CspInheritedCapabilityStatus.Mapped,
            "AU-2",
            "AU-6");
        _db.AddRange(component, capability);
        _db.CapabilitySubscriptions.Add(new CapabilitySubscription
        {
            RegisteredSystemId = SystemId,
            CspInheritedCapabilityId = capability.Id.ToString(),
            IsActive = true,
            SubscribedBy = "test",
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = SystemId,
            ControlId = "AU-2",
            Narrative = "Audit events are centrally collected.",
            TechnicalNarrative = "Audit events are centrally collected.",
            AuthoredBy = "test",
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.GetCapabilityCoverageAsync(SystemId);

        // Assert
        var cspCoverage = result!.Capabilities.Should().ContainSingle(item =>
            item.CapabilityId == capability.Id.ToString()).Subject;
        cspCoverage.Source.Should().Be("CSP");
        cspCoverage.MappedControlCount.Should().Be(2);
        cspCoverage.NarrativeStatus.Populated.Should().Be(1);
        cspCoverage.NarrativeStatus.Empty.Should().Be(1);
        result.Summary.TotalCapabilities.Should().Be(2);
        result.Summary.TotalMappedControls.Should().Be(5);
    }

    [Fact]
    public async Task BulkRegenerate_WithSubscribedCspCapability_CreatesMissingImplementations()
    {
        // Arrange
        var component = CreateCspComponent("Microsoft Entra ID P2", CspInheritedComponentStatus.Published);
        var capability = CreateCspCapability(
            component,
            "Access Reviews & Governance",
            CspInheritedCapabilityStatus.Mapped,
            "AC-2",
            "AC-6",
            "AU-6");
        _db.AddRange(component, capability);
        _db.CapabilitySubscriptions.Add(new CapabilitySubscription
        {
            RegisteredSystemId = SystemId,
            CspInheritedCapabilityId = capability.Id.ToString(),
            IsActive = true,
            SubscribedBy = "test",
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.BulkRegenerateNarrativesForCapabilityAsync(
            SystemId, capability.Id.ToString(), "test-user");

        // Assert
        result.Should().NotBeNull();
        result!.TotalControls.Should().Be(3);
        result.Regenerated.Should().Be(3);
        result.SkippedCustom.Should().Be(0);
        result.Failed.Should().Be(0);
        result.RegeneratedControlIds.Should().BeEquivalentTo("AC-2", "AC-6", "AU-6");

        var implementations = await _db.ControlImplementations
            .Where(implementation => implementation.RegisteredSystemId == SystemId)
            .ToListAsync();
        implementations.Should().HaveCount(3);
        implementations.Should().OnlyContain(implementation =>
            implementation.SecurityCapabilityId == null &&
            implementation.IsAutoPopulated &&
            implementation.Narrative != null &&
            implementation.Narrative.Contains("Microsoft Entra ID P2") &&
            implementation.TechnicalNarrative == implementation.Narrative &&
            implementation.PolicyNarrative == null);
    }

    [Fact]
    public async Task BulkRegenerate_WithSubscribedCspCapability_PreservesCustomAndDoesNotDuplicateRows()
    {
        // Arrange
        var component = CreateCspComponent("Microsoft Entra ID P2", CspInheritedComponentStatus.Published);
        var capability = CreateCspCapability(
            component,
            "Access Reviews & Governance",
            CspInheritedCapabilityStatus.Mapped,
            "AC-2",
            "AC-6");
        _db.AddRange(component, capability);
        _db.CapabilitySubscriptions.Add(new CapabilitySubscription
        {
            RegisteredSystemId = SystemId,
            CspInheritedCapabilityId = capability.Id.ToString(),
            IsActive = true,
            SubscribedBy = "test",
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = SystemId,
            ControlId = "AC-2",
            Narrative = "Approved custom narrative",
            IsManuallyCustomized = true,
            AuthoredBy = "author",
        });
        await _db.SaveChangesAsync();

        // Act
        var first = await _sut.BulkRegenerateNarrativesForCapabilityAsync(
            SystemId, capability.Id.ToString(), "test-user");
        var second = await _sut.BulkRegenerateNarrativesForCapabilityAsync(
            SystemId, capability.Id.ToString(), "test-user");

        // Assert
        first.Should().NotBeNull();
        first!.Regenerated.Should().Be(1);
        first.SkippedCustom.Should().Be(1);
        second.Should().NotBeNull();
        second!.Regenerated.Should().Be(1);
        second.SkippedCustom.Should().Be(1);

        var implementations = await _db.ControlImplementations
            .Where(implementation => implementation.RegisteredSystemId == SystemId)
            .ToListAsync();
        implementations.Should().HaveCount(2);
        implementations.Single(implementation => implementation.ControlId == "AC-2")
            .Narrative.Should().Be("Approved custom narrative");
        (await _db.NarrativeVersions.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false, CspInheritedComponentStatus.Published, CspInheritedCapabilityStatus.Mapped)]
    [InlineData(true, CspInheritedComponentStatus.Draft, CspInheritedCapabilityStatus.Mapped)]
    [InlineData(true, CspInheritedComponentStatus.Published, CspInheritedCapabilityStatus.NeedsReview)]
    public async Task BulkRegenerate_WithIneligibleCspCapability_ReturnsNotFound(
        bool isSubscribed,
        CspInheritedComponentStatus componentStatus,
        CspInheritedCapabilityStatus capabilityStatus)
    {
        // Arrange
        var component = CreateCspComponent("CSP", componentStatus);
        var capability = CreateCspCapability(component, "Capability", capabilityStatus, "AC-2");
        _db.AddRange(component, capability);
        if (isSubscribed)
        {
            _db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                RegisteredSystemId = SystemId,
                CspInheritedCapabilityId = capability.Id.ToString(),
                IsActive = true,
                SubscribedBy = "test",
            });
        }
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.BulkRegenerateNarrativesForCapabilityAsync(
            SystemId, capability.Id.ToString(), "test-user");

        // Assert
        result.Should().BeNull();
        (await _db.ControlImplementations.CountAsync(implementation =>
            implementation.RegisteredSystemId == SystemId)).Should().Be(0);
    }

    private static CspInheritedComponent CreateCspComponent(
        string name,
        CspInheritedComponentStatus status) => new()
    {
        CspProfileId = Guid.NewGuid(),
        Name = name,
        Description = $"{name} description",
        ComponentType = CspComponentType.Service,
        SourceFormat = SourceFormat.OscalJson,
        Status = status,
        ImportedBy = "test",
    };

    private static CspInheritedCapability CreateCspCapability(
        CspInheritedComponent component,
        string name,
        CspInheritedCapabilityStatus status,
        params string[] controls) => new()
    {
        CspInheritedComponentId = component.Id,
        CspInheritedComponent = component,
        Name = name,
        Description = $"{name} description",
        Status = status,
        MappedNistControlIds = [.. controls],
        CreatedBy = "test",
    };

    [Fact]
    public async Task UpdateCapability_BoundaryScoped_ReturnsNarrativesByBoundary()
    {
        // Arrange
        _db.NistControls.Add(new NistControl
        {
            Id = "AC-2", Family = "AC", Title = "Account Management",
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = SystemId,
            ControlId = "AC-2",
            SecurityCapabilityId = CapId,
            AiSuggested = true,
            AuthoredBy = "test",
        });
        await _db.SaveChangesAsync();

        var request = new CreateCapabilityRequest
        {
            Name = "MFA", Provider = "Entra ID", Category = "IA",
            Description = "Updated MFA description",
            ImplementationStatus = "Implemented", Owner = "Test",
        };

        // Act
        var (result, conflict) = await _sut.UpdateCapabilityAsync(CapId, request, "test");

        // Assert
        var implementation = await _db.ControlImplementations.SingleAsync();
        implementation.AiSuggested.Should().BeFalse();
        implementation.IsAutoPopulated.Should().BeTrue();
        conflict.Should().BeFalse();
        result.Should().NotBeNull();
        result!.NarrativesUpdated.Should().Be(1);
        result.NarrativesByBoundary.Should().NotBeNull();
        // AC-2 mapping has null boundary FK → tracked as "Organization-Wide"
        result.NarrativesByBoundary.Should().ContainKey("Organization-Wide");
    }

    [Fact]
    public async Task CreateMappings_DeterministicFallback_ClearsPriorAiProvenance()
    {
        // Arrange
        _db.NistControls.Add(new NistControl { Id = "ac-3", Family = "AC", Title = "Access Enforcement" });
        var implementation = new ControlImplementation
        {
            RegisteredSystemId = SystemId, ControlId = "ac-3", AiSuggested = true,
            Narrative = "Previous model output", AuthoredBy = "author"
        };
        _db.ControlImplementations.Add(implementation);
        await _db.SaveChangesAsync();

        // Act
        var result = await _sut.CreateMappingsAsync(CapId, new CreateMappingsRequest
        {
            Mappings = [new CreateMappingItem { ControlId = "ac-3", Role = "Primary", RegisteredSystemId = SystemId }]
        }, "author");

        // Assert
        result!.NarrativesGenerated.Should().Be(1);
        implementation.AiSuggested.Should().BeFalse();
        implementation.IsAutoPopulated.Should().BeTrue();
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
