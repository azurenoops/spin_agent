using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

public class OrgInheritanceServiceTests : IDisposable
{
    private readonly AtoCopilotContext _db;
    private readonly OrgInheritanceService _sut;

    public OrgInheritanceServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"OrgInheritanceTests_{Guid.NewGuid()}")
            .Options;
        _db = new AtoCopilotContext(dbOptions);

        // Build a service scope factory that resolves our in-memory context
        var services = new ServiceCollection();
        services.AddSingleton(dbOptions);
        services.AddScoped(_ => new AtoCopilotContext(dbOptions));
        var sp = services.BuildServiceProvider();

        _sut = new OrgInheritanceService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<OrgInheritanceService>>());
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private SecurityCapability AddCapability(string name, string provider, CapabilityStatus status = CapabilityStatus.Implemented)
    {
        var cap = new SecurityCapability
        {
            Name = name,
            Provider = provider,
            Category = "AC",
            Description = $"{name} description",
            ImplementationStatus = status,
            Owner = "test-owner",
            CreatedBy = "test",
        };
        _db.SecurityCapabilities.Add(cap);
        return cap;
    }

    private CapabilityControlMapping AddOrgMapping(SecurityCapability cap, string controlId, CapabilityMappingRole role)
    {
        var mapping = new CapabilityControlMapping
        {
            SecurityCapabilityId = cap.Id,
            SecurityCapability = cap,
            ControlId = controlId,
            Role = role,
            CreatedBy = "test",
            RegisteredSystemId = null, // org-wide
        };
        _db.CapabilityControlMappings.Add(mapping);
        return mapping;
    }

    // ─── DeriveOrgDefaultsAsync Tests ────────────────────────────────────────

    [Fact]
    public async Task DeriveOrgDefaults_PrimaryMapping_InheritedType()
    {
        var cap = AddCapability("MFA", "Microsoft Entra ID");
        AddOrgMapping(cap, "IA-2", CapabilityMappingRole.Primary);
        await _db.SaveChangesAsync();

        var result = await _sut.DeriveOrgDefaultsAsync("test");

        result.DerivedCount.Should().Be(1);
        result.InheritedCount.Should().Be(1);
        result.SharedCount.Should().Be(0);

        var defaults = await _db.OrgInheritanceDefaults.ToListAsync();
        defaults.Should().HaveCount(1);
        defaults[0].ControlId.Should().Be("IA-2");
        defaults[0].InheritanceType.Should().Be(InheritanceType.Inherited);
        defaults[0].Provider.Should().Be("Microsoft Entra ID");
    }

    [Fact]
    public async Task DeriveOrgDefaults_SupportingMapping_InheritedType()
    {
        var cap = AddCapability("Log Analytics", "Azure Monitor");
        AddOrgMapping(cap, "AU-6", CapabilityMappingRole.Supporting);
        await _db.SaveChangesAsync();

        var result = await _sut.DeriveOrgDefaultsAsync("test");

        result.InheritedCount.Should().Be(1);
        var d = await _db.OrgInheritanceDefaults.SingleAsync();
        d.InheritanceType.Should().Be(InheritanceType.Inherited);
    }

    [Fact]
    public async Task DeriveOrgDefaults_SharedMapping_SharedType()
    {
        var cap = AddCapability("Shared Firewall", "Palo Alto");
        AddOrgMapping(cap, "SC-7", CapabilityMappingRole.Shared);
        await _db.SaveChangesAsync();

        var result = await _sut.DeriveOrgDefaultsAsync("test");

        result.SharedCount.Should().Be(1);
        var d = await _db.OrgInheritanceDefaults.SingleAsync();
        d.InheritanceType.Should().Be(InheritanceType.Shared);
    }

    [Fact]
    public async Task DeriveOrgDefaults_PrimaryOverShared_InheritedWins()
    {
        var capPrimary = AddCapability("MFA", "Microsoft Entra ID");
        var capShared = AddCapability("Shared Auth", "Okta");
        AddOrgMapping(capPrimary, "IA-2", CapabilityMappingRole.Primary);
        AddOrgMapping(capShared, "IA-2", CapabilityMappingRole.Shared);
        await _db.SaveChangesAsync();

        var result = await _sut.DeriveOrgDefaultsAsync("test");

        result.DerivedCount.Should().Be(1);
        result.InheritedCount.Should().Be(1);
        result.SharedCount.Should().Be(0);

        var d = await _db.OrgInheritanceDefaults.SingleAsync();
        d.InheritanceType.Should().Be(InheritanceType.Inherited);
        d.MappingRole.Should().Be(CapabilityMappingRole.Primary);
    }

    [Fact]
    public async Task DeriveOrgDefaults_MultipleProviders_MergedInProvider()
    {
        var cap1 = AddCapability("MFA", "Microsoft Entra ID");
        var cap2 = AddCapability("SSO", "Okta");
        AddOrgMapping(cap1, "IA-2", CapabilityMappingRole.Primary);
        AddOrgMapping(cap2, "IA-2", CapabilityMappingRole.Supporting);
        await _db.SaveChangesAsync();

        var result = await _sut.DeriveOrgDefaultsAsync("test");

        var d = await _db.OrgInheritanceDefaults.SingleAsync();
        d.Provider.Should().Contain("Microsoft Entra ID");
        d.Provider.Should().Contain("Okta");
    }

    [Fact]
    public async Task DeriveOrgDefaults_PlannedCapability_Excluded()
    {
        var cap = AddCapability("Planned Only", "Future Corp", CapabilityStatus.Planned);
        AddOrgMapping(cap, "AC-1", CapabilityMappingRole.Primary);
        await _db.SaveChangesAsync();

        var result = await _sut.DeriveOrgDefaultsAsync("test");

        result.DerivedCount.Should().Be(0);
        (await _db.OrgInheritanceDefaults.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeriveOrgDefaults_NoMappings_ReturnsZero()
    {
        var result = await _sut.DeriveOrgDefaultsAsync("test");

        result.DerivedCount.Should().Be(0);
        result.RemovedCount.Should().Be(0);
    }

    [Fact]
    public async Task DeriveOrgDefaults_StaleDefault_Removed()
    {
        // Pre-seed an existing default that no longer has mappings
        _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
        {
            ControlId = "AC-99",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Old Provider",
            SourceCapabilityIds = "cap-old",
            SourceCapabilityNames = "Old Capability",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        var result = await _sut.DeriveOrgDefaultsAsync("test");

        result.RemovedCount.Should().Be(1);
        (await _db.OrgInheritanceDefaults.CountAsync()).Should().Be(0);
    }

    // ─── PropagateToSystemAsync Tests ────────────────────────────────────────

    [Fact]
    public async Task PropagateToSystem_NoOverrides_PropagatesAll()
    {
        // Seed org defaults
        _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
        {
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Microsoft Entra ID",
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "MFA",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        });

        // Seed baseline
        var baseline = new ControlBaseline
        {
            RegisteredSystemId = "sys-1",
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-2", "AC-3" },
        };
        _db.ControlBaselines.Add(baseline);
        await _db.SaveChangesAsync();

        var result = await _sut.PropagateToSystemAsync(
            "sys-1", baseline.Id,
            new HashSet<string>(new[] { "AC-2", "AC-3" }),
            "test");

        result.PropagatedCount.Should().Be(1);
        result.SkippedCount.Should().Be(0);
        result.PropagatedControlIds.Should().Contain("AC-2");
    }

    [Fact]
    public async Task PropagateToSystem_ManualOverride_Skipped()
    {
        var orgDefault = new OrgInheritanceDefault
        {
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Microsoft",
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "MFA",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        };
        _db.OrgInheritanceDefaults.Add(orgDefault);

        var baseline = new ControlBaseline
        {
            RegisteredSystemId = "sys-1",
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-2" },
        };
        _db.ControlBaselines.Add(baseline);

        // Pre-existing manual override
        _db.ControlInheritances.Add(new ControlInheritance
        {
            ControlBaselineId = baseline.Id,
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Shared,
            Provider = "Custom Provider",
            DesignationSource = "Manual",
            SetBy = "issm",
            SetAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.PropagateToSystemAsync(
            "sys-1", baseline.Id,
            new HashSet<string>(new[] { "AC-2" }),
            "test");

        result.PropagatedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
    }

    [Fact]
    public async Task PropagateToSystem_EmptyOrgDefaults_PropagatesNothing()
    {
        var baseline = new ControlBaseline
        {
            RegisteredSystemId = "sys-1",
            BaselineLevel = "Low",
            ControlIds = new List<string> { "AC-2" },
        };
        _db.ControlBaselines.Add(baseline);
        await _db.SaveChangesAsync();

        var result = await _sut.PropagateToSystemAsync(
            "sys-1", baseline.Id,
            new HashSet<string>(new[] { "AC-2" }),
            "test");

        result.PropagatedCount.Should().Be(0);
        result.SkippedCount.Should().Be(0);
    }

    [Fact]
    public async Task PropagateToSystem_ZeroBaselineControls_PropagatesNothing()
    {
        _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
        {
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Provider",
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "Cap",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        });
        var baseline = new ControlBaseline
        {
            RegisteredSystemId = "sys-1",
            BaselineLevel = "Low",
            ControlIds = new List<string>(),
        };
        _db.ControlBaselines.Add(baseline);
        await _db.SaveChangesAsync();

        var result = await _sut.PropagateToSystemAsync(
            "sys-1", baseline.Id,
            new HashSet<string>(),
            "test");

        result.PropagatedCount.Should().Be(0);
    }

    [Fact]
    public async Task PropagateToSystem_ProfileApplyOverride_Skipped()
    {
        var orgDefault = new OrgInheritanceDefault
        {
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Microsoft",
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "MFA",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        };
        _db.OrgInheritanceDefaults.Add(orgDefault);

        var baseline = new ControlBaseline
        {
            RegisteredSystemId = "sys-1",
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-2" },
        };
        _db.ControlBaselines.Add(baseline);

        _db.ControlInheritances.Add(new ControlInheritance
        {
            ControlBaselineId = baseline.Id,
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "CSP Profile Provider",
            DesignationSource = "ProfileApply",
            SetBy = "system",
            SetAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.PropagateToSystemAsync(
            "sys-1", baseline.Id,
            new HashSet<string>(new[] { "AC-2" }),
            "test");

        result.SkippedCount.Should().Be(1);
    }

    // ─── RevertToOrgDefaultsAsync Tests ──────────────────────────────────────

    [Fact]
    public async Task RevertToOrgDefaults_RevertsOverride()
    {
        // Seed system + baseline
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "sys-revert",
            Name = "Revert System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure",
            CreatedBy = "test",
            IsActive = true,
        });

        var baseline = new ControlBaseline
        {
            RegisteredSystemId = "sys-revert",
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-2" },
        };
        _db.ControlBaselines.Add(baseline);

        var orgDefault = new OrgInheritanceDefault
        {
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Microsoft Entra ID",
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "MFA",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        };
        _db.OrgInheritanceDefaults.Add(orgDefault);

        _db.ControlInheritances.Add(new ControlInheritance
        {
            ControlBaselineId = baseline.Id,
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Shared,
            Provider = "Custom Override",
            DesignationSource = "Manual",
            SetBy = "issm",
            SetAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.RevertToOrgDefaultsAsync(
            "sys-revert", new[] { "AC-2" }, "test");

        result.RevertedCount.Should().Be(1);
        result.Skipped.Should().BeEmpty();

        // Clear tracked entities so we read fresh from the in-memory store
        _db.ChangeTracker.Clear();
        var ci = await _db.ControlInheritances.SingleAsync(c => c.ControlId == "AC-2");
        ci.InheritanceType.Should().Be(InheritanceType.Inherited);
        ci.Provider.Should().Be("Microsoft Entra ID");
        ci.DesignationSource.Should().Be("OrgDerived");
    }

    [Fact]
    public async Task RevertToOrgDefaults_NoOrgDefault_Skipped()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "sys-revert2",
            Name = "Revert System 2",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure",
            CreatedBy = "test",
            IsActive = true,
        });

        var baseline = new ControlBaseline
        {
            RegisteredSystemId = "sys-revert2",
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-99" },
        };
        _db.ControlBaselines.Add(baseline);
        await _db.SaveChangesAsync();

        var result = await _sut.RevertToOrgDefaultsAsync(
            "sys-revert2", new[] { "AC-99" }, "test");

        result.RevertedCount.Should().Be(0);
        result.Skipped.Should().HaveCount(1);
        result.Skipped[0].ControlId.Should().Be("AC-99");
    }

    // ─── GetOrgDefaultsAsync Tests ───────────────────────────────────────────

    [Fact]
    public async Task GetOrgDefaults_ReturnsPaginated()
    {
        for (int i = 1; i <= 15; i++)
        {
            _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
            {
                ControlId = $"AC-{i}",
                InheritanceType = i <= 10 ? InheritanceType.Inherited : InheritanceType.Shared,
                Provider = "Provider",
                SourceCapabilityIds = "cap-1",
                SourceCapabilityNames = "Cap",
                MappingRole = CapabilityMappingRole.Primary,
                DerivedAt = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync();

        var result = await _sut.GetOrgDefaultsAsync(null, null, null, 1, 10);

        result.Items.Should().HaveCount(10);
        result.TotalCount.Should().Be(15);
    }

    [Fact]
    public async Task GetOrgDefaults_FamilyFilter_FiltersCorrectly()
    {
        _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
        {
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Provider",
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "Cap",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        });
        _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
        {
            ControlId = "IA-5",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Provider",
            SourceCapabilityIds = "cap-2",
            SourceCapabilityNames = "Cap2",
            MappingRole = CapabilityMappingRole.Supporting,
            DerivedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetOrgDefaultsAsync("AC", null, null, 1, 50);

        result.Items.Should().HaveCount(1);
        result.Items[0].ControlId.Should().Be("AC-2");
    }
}
