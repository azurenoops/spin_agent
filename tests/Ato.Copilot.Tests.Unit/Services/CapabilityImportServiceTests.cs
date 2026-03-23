using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

namespace Ato.Copilot.Tests.Unit.Services;

public class CapabilityImportServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly Mock<IOrgInheritanceService> _orgServiceMock;
    private readonly CapabilityImportService _sut;
    private readonly string _tempDir;

    public CapabilityImportServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"CapImportTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);

        // Setup temp directory with a test CSP profile
        _tempDir = Path.Combine(Path.GetTempPath(), $"csp-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src", "seed-data", "csp-profiles"));

        var testProfile = new
        {
            profileId = "test-azure-high",
            name = "Test Azure Gov FedRAMP High",
            provider = "Microsoft Azure Government",
            baselineLevel = "high",
            description = "Test profile",
            version = "1.0",
            services = new[]
            {
                new
                {
                    name = "Microsoft Entra ID",
                    category = "Identity & Access Management",
                    description = "Identity services",
                    controls = new[]
                    {
                        new { controlId = "ac-2", inheritanceType = "Inherited", customerResponsibility = "" },
                        new { controlId = "ac-3", inheritanceType = "Shared", customerResponsibility = "Configure RBAC roles" },
                    }
                },
                new
                {
                    name = "Azure Monitor",
                    category = "Audit & Logging",
                    description = "Logging services",
                    controls = new[]
                    {
                        new { controlId = "au-2", inheritanceType = "Inherited", customerResponsibility = "" },
                        new { controlId = "au-3", inheritanceType = "Inherited", customerResponsibility = "" },
                    }
                }
            }
        };

        File.WriteAllText(
            Path.Combine(_tempDir, "src", "seed-data", "csp-profiles", "test-profile.json"),
            JsonSerializer.Serialize(testProfile));

        // Create CspProfileService with temp directory
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(Path.Combine(_tempDir, "non-existent"));
        // Use the working directory approach; override current dir
        var origDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_tempDir);
        var cspService = new CspProfileService(
            Mock.Of<ILogger<CspProfileService>>(), envMock.Object);
        Directory.SetCurrentDirectory(origDir);

        _orgServiceMock = new Mock<IOrgInheritanceService>();
        _orgServiceMock.Setup(o => o.DeriveOrgDefaultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgDerivationResult(5, 3, 2, 0, 2, DateTime.UtcNow));

        _sut = new CapabilityImportService(
            _db,
            cspService,
            _orgServiceMock.Object,
            new NarrativeTemplateService(),
            Mock.Of<ILogger<CapabilityImportService>>());

        SeedNistControls();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedNistControls()
    {
        _db.NistControls.AddRange(
            new NistControl { Id = "ac-2", Title = "Account Management", Family = "AC", Description = "Test" },
            new NistControl { Id = "ac-3", Title = "Access Enforcement", Family = "AC", Description = "Test" },
            new NistControl { Id = "au-2", Title = "Event Logging", Family = "AU", Description = "Test" },
            new NistControl { Id = "au-3", Title = "Content of Audit Records", Family = "AU", Description = "Test" }
        );
        _db.SaveChanges();
    }

    // ─── FindOrCreateComponentAsync Tests ────────────────────────────────────

    [Fact]
    public async Task FindOrCreateComponentAsync_NewComponent_CreatesIt()
    {
        var component = await _sut.FindOrCreateComponentAsync("New Service", "A new service");
        await _db.SaveChangesAsync();

        component.Name.Should().Be("New Service");
        component.ComponentType.Should().Be(ComponentType.Thing);
        component.RegisteredSystemId.Should().BeNull();
        _db.SystemComponents.Should().HaveCount(1);
    }

    [Fact]
    public async Task FindOrCreateComponentAsync_ExistingComponent_ReusesIt()
    {
        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "Existing Service",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = null,
            CreatedBy = "test",
        });
        await _db.SaveChangesAsync();

        var component = await _sut.FindOrCreateComponentAsync("Existing Service", "desc");
        component.Name.Should().Be("Existing Service");
        _db.SystemComponents.Should().HaveCount(1);
    }

    [Fact]
    public async Task FindOrCreateComponentAsync_DifferentType_DoesNotMatch()
    {
        _db.SystemComponents.Add(new SystemComponent
        {
            Name = "Some Service",
            ComponentType = ComponentType.Person,
            RegisteredSystemId = null,
            CreatedBy = "test",
        });
        await _db.SaveChangesAsync();

        var component = await _sut.FindOrCreateComponentAsync("Some Service", "desc");
        _db.Entry(component).State.Should().Be(EntityState.Added);
    }

    // ─── FindOrCreateCapabilityAsync Tests ──────────────────────────────────

    [Fact]
    public async Task FindOrCreateCapabilityAsync_NewCapability_CreatesIt()
    {
        var cap = await _sut.FindOrCreateCapabilityAsync("MFA", "Entra ID", "IA", "Multi-factor");
        await _db.SaveChangesAsync();

        cap.Name.Should().Be("MFA");
        cap.Provider.Should().Be("Entra ID");
        cap.ImplementationStatus.Should().Be(CapabilityStatus.Implemented);
    }

    [Fact]
    public async Task FindOrCreateCapabilityAsync_CaseInsensitiveDedup()
    {
        _db.SecurityCapabilities.Add(new SecurityCapability
        {
            Name = "Azure Monitor / Audit",
            Provider = "microsoft azure government",
            Category = "AU",
            Description = "Existing",
            CreatedBy = "test",
        });
        await _db.SaveChangesAsync();

        var cap = await _sut.FindOrCreateCapabilityAsync(
            "azure monitor / audit", "MICROSOFT AZURE GOVERNMENT", "AU", "New desc");

        cap.Description.Should().Be("Existing");
        _db.SecurityCapabilities.Should().HaveCount(1);
    }

    // ─── ImportCspProfileAsync Tests ────────────────────────────────────────

    [Fact]
    public async Task ImportCspProfileAsync_ValidProfile_CreatesFullPipeline()
    {
        var result = await _sut.ImportCspProfileAsync("test-azure-high", "skip");

        result.DryRun.Should().BeFalse();
        result.ProfileName.Should().Be("Test Azure Gov FedRAMP High");
        result.ComponentsCreated.Should().Be(2);
        result.CapabilitiesCreated.Should().Be(2);
        result.ControlMappingsCreated.Should().Be(4);

        // Verify components saved
        var components = await _db.SystemComponents.ToListAsync();
        components.Should().HaveCount(2);
        components.Select(c => c.Name).Should().Contain("Microsoft Entra ID");
        components.Select(c => c.Name).Should().Contain("Azure Monitor");

        // Verify capabilities saved
        var capabilities = await _db.SecurityCapabilities.ToListAsync();
        capabilities.Should().HaveCount(2);

        // Verify control mappings
        var mappings = await _db.CapabilityControlMappings.ToListAsync();
        mappings.Should().HaveCount(4);
        mappings.Should().OnlyContain(m => m.Role == CapabilityMappingRole.Primary);

        // Verify component-capability links
        var links = await _db.ComponentCapabilityLinks.ToListAsync();
        links.Should().HaveCount(2);

        // Verify org defaults were derived
        _orgServiceMock.Verify(
            o => o.DeriveOrgDefaultsAsync("capability-import", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportCspProfileAsync_DuplicateRun_ReusesExisting()
    {
        // First import
        var result1 = await _sut.ImportCspProfileAsync("test-azure-high", "skip");
        result1.ComponentsCreated.Should().Be(2);
        result1.CapabilitiesCreated.Should().Be(2);

        // Second import — should reuse
        var result2 = await _sut.ImportCspProfileAsync("test-azure-high", "skip");
        result2.ComponentsCreated.Should().Be(0);
        result2.ComponentsReused.Should().Be(2);
        result2.CapabilitiesCreated.Should().Be(0);
        result2.CapabilitiesReused.Should().Be(2);
        result2.ControlMappingsCreated.Should().Be(0);
    }

    [Fact]
    public async Task ImportCspProfileAsync_ConflictResolution_PrimaryToSupporting()
    {
        // Pre-create a Primary mapping for ac-2 from a different capability
        var existingCap = new SecurityCapability
        {
            Name = "Existing Cap",
            Provider = "Other Provider",
            Category = "AC",
            Description = "Pre-existing",
            CreatedBy = "test",
        };
        _db.SecurityCapabilities.Add(existingCap);
        await _db.SaveChangesAsync();

        _db.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = existingCap.Id,
            ControlId = "ac-2",
            RegisteredSystemId = null,
            Role = CapabilityMappingRole.Primary,
            CreatedBy = "test",
        });
        await _db.SaveChangesAsync();

        // Import with overwrite — new mapping for ac-2 should be Supporting
        var result = await _sut.ImportCspProfileAsync("test-azure-high", "overwrite");

        var ac2Mappings = await _db.CapabilityControlMappings
            .Where(m => m.ControlId == "ac-2")
            .ToListAsync();
        ac2Mappings.Should().HaveCount(2);
        ac2Mappings.Should().ContainSingle(m => m.Role == CapabilityMappingRole.Primary);
        ac2Mappings.Should().ContainSingle(m => m.Role == CapabilityMappingRole.Supporting);
    }

    [Fact]
    public async Task ImportCspProfileAsync_InvalidProfileId_ThrowsKeyNotFound()
    {
        var act = () => _sut.ImportCspProfileAsync("nonexistent-profile", "skip");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ─── ImportCspProfilePreviewAsync Tests ─────────────────────────────────

    [Fact]
    public async Task ImportCspProfilePreviewAsync_ValidProfile_ReturnsPreviewWithoutPersisting()
    {
        var preview = await _sut.ImportCspProfilePreviewAsync("test-azure-high", "skip");

        preview.DryRun.Should().BeTrue();
        preview.ProfileName.Should().Be("Test Azure Gov FedRAMP High");
        preview.ComponentsToCreate.Should().Be(2);
        preview.CapabilitiesToCreate.Should().Be(2);
        preview.ControlMappingsToCreate.Should().Be(4);

        // Nothing should have been persisted
        (await _db.SystemComponents.CountAsync()).Should().Be(0);
        (await _db.SecurityCapabilities.CountAsync()).Should().Be(0);
        (await _db.CapabilityControlMappings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportCspProfilePreviewAsync_WithExistingData_ShowsReuseCounts()
    {
        // First import for real
        await _sut.ImportCspProfileAsync("test-azure-high", "skip");

        // Then preview again — should show reuse
        var preview = await _sut.ImportCspProfilePreviewAsync("test-azure-high", "skip");
        preview.ComponentsToReuse.Should().Be(2);
        preview.CapabilitiesToReuse.Should().Be(2);
        preview.ComponentsToCreate.Should().Be(0);
        preview.CapabilitiesToCreate.Should().Be(0);
    }

    // ─── ImportCrmAsync Tests ────────────────────────────────────────────────

    [Fact]
    public async Task ImportCrmAsync_ProviderFamilyGrouping_ProducesCorrectCapabilityNames()
    {
        var rows = new List<CrmImportRow>
        {
            new() { ControlId = "ac-2", InheritanceType = "Inherited", Provider = "Zscaler" },
            new() { ControlId = "ac-3", InheritanceType = "Shared", Provider = "Zscaler" },
            new() { ControlId = "au-2", InheritanceType = "Inherited", Provider = "Zscaler" },
        };

        var result = await _sut.ImportCrmAsync("test.csv", rows, "skip");

        result.CapabilitiesCreated.Should().Be(2);  // "Zscaler / Access Control" and "Zscaler / Audit and Accountability"
        result.ComponentsCreated.Should().Be(1);      // "Zscaler"

        var caps = await _db.SecurityCapabilities.ToListAsync();
        caps.Should().Contain(c => c.Name == "Zscaler / Access Control");
        caps.Should().Contain(c => c.Name == "Zscaler / Audit and Accountability");
    }

    [Fact]
    public async Task ImportCrmAsync_EmptyProvider_GroupedAsUnspecifiedProvider()
    {
        var rows = new List<CrmImportRow>
        {
            new() { ControlId = "ac-2", InheritanceType = "Inherited", Provider = "" },
            new() { ControlId = "ac-3", InheritanceType = "Shared", Provider = null },
        };

        var result = await _sut.ImportCrmAsync("test.csv", rows, "skip");

        result.CapabilitiesCreated.Should().Be(1);
        result.ComponentsCreated.Should().Be(0); // No component for empty provider

        var cap = await _db.SecurityCapabilities.FirstAsync();
        cap.Name.Should().Be("Unspecified Provider / Access Control");
    }

    [Fact]
    public async Task ImportCrmAsync_UnmatchedControlIds_Counted()
    {
        var rows = new List<CrmImportRow>
        {
            new() { ControlId = "ac-2", InheritanceType = "Inherited", Provider = "TestCo" },
            new() { ControlId = "xx-99", InheritanceType = "Inherited", Provider = "TestCo" },
            new() { ControlId = "zz-1", InheritanceType = "Shared", Provider = "TestCo" },
        };

        var result = await _sut.ImportCrmAsync("test.csv", rows, "skip");

        result.UnmatchedRows.Should().Be(2);
        result.ControlMappingsCreated.Should().Be(1); // only ac-2
    }

    [Fact]
    public async Task ImportCrmAsync_ExistingProviderComponent_Reused()
    {
        // Pre-create a component with the same provider name
        _db.SystemComponents.Add(new SystemComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Zscaler",
            ComponentType = ComponentType.Thing,
            RegisteredSystemId = null, // org-level
        });
        await _db.SaveChangesAsync();

        var rows = new List<CrmImportRow>
        {
            new() { ControlId = "ac-2", InheritanceType = "Inherited", Provider = "Zscaler" },
        };

        var result = await _sut.ImportCrmAsync("test.csv", rows, "skip");

        result.ComponentsReused.Should().Be(1);
        result.ComponentsCreated.Should().Be(0);
        (await _db.SystemComponents.CountAsync(c => c.Name == "Zscaler")).Should().Be(1);
    }
}
