using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Agents.Compliance.Services;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for org-level inheritance endpoints (Feature 044).
/// Covers T017b: GET /org-defaults, POST /org-defaults/derive, error paths.
/// Uses in-memory EF Core database with OrgInheritanceService as the SUT.
/// </summary>
public class OrgInheritanceEndpointTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;
    private readonly OrgInheritanceService _sut;

    private const string SystemId = "sys-org-inh-001";
    private const string BaselineId = "bl-org-inh-001";

    public OrgInheritanceEndpointTests()
    {
        var dbName = $"OrgInheritanceInt_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();

        _sut = new OrgInheritanceService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrgInheritanceService>.Instance);

        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
        _serviceProvider.Dispose();
    }

    private void SeedData()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = SystemId, Name = "Org Inheritance Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Gov", CreatedBy = "test", IsActive = true,
        });

        _db.ControlBaselines.Add(new ControlBaseline
        {
            Id = BaselineId,
            RegisteredSystemId = SystemId,
            BaselineLevel = "Moderate",
            TotalControls = 5,
            ControlIds = new List<string> { "AC-2", "AC-3", "IA-2", "SC-7", "SI-2" },
            CreatedBy = "test",
        });

        _db.SaveChanges();
    }

    private SecurityCapability AddOrgCapability(string name, string provider, string category = "AC")
    {
        var cap = new SecurityCapability
        {
            Name = name, Provider = provider, Category = category,
            Description = $"{name} description",
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "test-owner", CreatedBy = "test",
        };
        _db.SecurityCapabilities.Add(cap);
        return cap;
    }

    private void AddOrgMapping(SecurityCapability cap, string controlId, CapabilityMappingRole role)
    {
        _db.CapabilityControlMappings.Add(new CapabilityControlMapping
        {
            SecurityCapabilityId = cap.Id,
            SecurityCapability = cap,
            ControlId = controlId,
            Role = role,
            CreatedBy = "test",
            RegisteredSystemId = null,
        });
    }

    // ─── POST /org-defaults/derive ───────────────────────────────────────────

    [Fact]
    public async Task Derive_WithCapabilities_ReturnsSummary()
    {
        var capMfa = AddOrgCapability("MFA", "Microsoft Entra ID", "IA");
        AddOrgMapping(capMfa, "IA-2", CapabilityMappingRole.Primary);

        var capFirewall = AddOrgCapability("Shared Firewall", "Palo Alto", "SC");
        AddOrgMapping(capFirewall, "SC-7", CapabilityMappingRole.Shared);
        await _db.SaveChangesAsync();

        var result = await _sut.DeriveOrgDefaultsAsync("test-user");

        result.DerivedCount.Should().Be(2);
        result.InheritedCount.Should().Be(1);
        result.SharedCount.Should().Be(1);
        result.RemovedCount.Should().Be(0);
        result.DerivedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Verify org defaults persisted
        var defaults = await _db.OrgInheritanceDefaults.ToListAsync();
        defaults.Should().HaveCount(2);
    }

    [Fact]
    public async Task Derive_NoCapabilities_ReturnsEmpty()
    {
        var result = await _sut.DeriveOrgDefaultsAsync("test-user");

        result.DerivedCount.Should().Be(0);
        result.InheritedCount.Should().Be(0);
        result.SharedCount.Should().Be(0);
    }

    // ─── GET /org-defaults ───────────────────────────────────────────────────

    [Fact]
    public async Task GetOrgDefaults_ReturnsPaginatedResults()
    {
        // Seed org defaults directly  
        for (int i = 1; i <= 5; i++)
        {
            _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
            {
                ControlId = $"AC-{i}",
                InheritanceType = InheritanceType.Inherited,
                Provider = "Microsoft",
                SourceCapabilityIds = $"cap-{i}",
                SourceCapabilityNames = $"Cap {i}",
                MappingRole = CapabilityMappingRole.Primary,
                DerivedAt = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync();

        var result = await _sut.GetOrgDefaultsAsync(null, null, null, 1, 3);

        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetOrgDefaults_WithTypeFilter_FiltersCorrectly()
    {
        _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
        {
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Microsoft",
            SourceCapabilityIds = "cap-1",
            SourceCapabilityNames = "MFA",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow,
        });
        _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
        {
            ControlId = "SC-7",
            InheritanceType = InheritanceType.Shared,
            Provider = "Palo Alto",
            SourceCapabilityIds = "cap-2",
            SourceCapabilityNames = "Firewall",
            MappingRole = CapabilityMappingRole.Shared,
            DerivedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _sut.GetOrgDefaultsAsync(null, "Shared", null, 1, 50);

        result.Items.Should().HaveCount(1);
        result.Items[0].ControlId.Should().Be("SC-7");
    }

    // ─── End-to-end: Derive → Propagate ──────────────────────────────────────

    [Fact]
    public async Task DeriveAndPropagate_EndToEnd()
    {
        // 1. Create org-wide capabilities
        var capMfa = AddOrgCapability("MFA", "Microsoft Entra ID", "IA");
        AddOrgMapping(capMfa, "IA-2", CapabilityMappingRole.Primary);
        AddOrgMapping(capMfa, "AC-2", CapabilityMappingRole.Supporting);
        await _db.SaveChangesAsync();

        // 2. Derive org defaults
        var deriveResult = await _sut.DeriveOrgDefaultsAsync("test-user");
        deriveResult.DerivedCount.Should().Be(2);

        // 3. Propagate to system
        var baselineControlIds = new HashSet<string>(new[] { "AC-2", "AC-3", "IA-2", "SC-7", "SI-2" });
        var propResult = await _sut.PropagateToSystemAsync(
            SystemId, BaselineId, baselineControlIds, "test-user");

        propResult.PropagatedCount.Should().Be(2); // IA-2 and AC-2
        propResult.SkippedCount.Should().Be(0);

        // 4. Verify system designations
        var designations = await _db.ControlInheritances
            .Where(ci => ci.ControlBaselineId == BaselineId)
            .ToListAsync();

        designations.Should().HaveCount(2);
        designations.Should().AllSatisfy(d =>
        {
            d.DesignationSource.Should().Be("OrgDerived");
            d.InheritanceType.Should().Be(InheritanceType.Inherited);
        });
    }

    [Fact]
    public async Task DeriveAndRevert_EndToEnd()
    {
        // 1. Derive org defaults
        var cap = AddOrgCapability("MFA", "Microsoft Entra ID", "IA");
        AddOrgMapping(cap, "AC-2", CapabilityMappingRole.Primary);
        await _db.SaveChangesAsync();

        await _sut.DeriveOrgDefaultsAsync("test-user");

        // 2. Propagate
        var baselineControlIds = new HashSet<string>(new[] { "AC-2", "AC-3", "IA-2", "SC-7", "SI-2" });
        await _sut.PropagateToSystemAsync(SystemId, BaselineId, baselineControlIds, "test-user");

        // 3. Manually override
        var ci = await _db.ControlInheritances.SingleAsync(c => c.ControlId == "AC-2");
        ci.InheritanceType = InheritanceType.Shared;
        ci.Provider = "Custom Override";
        ci.DesignationSource = "Manual";
        await _db.SaveChangesAsync();

        // 4. Revert to org defaults
        var revertResult = await _sut.RevertToOrgDefaultsAsync(
            SystemId, new[] { "AC-2" }, "test-user");

        revertResult.RevertedCount.Should().Be(1);

        _db.ChangeTracker.Clear();
        var reverted = await _db.ControlInheritances.SingleAsync(c => c.ControlId == "AC-2");
        reverted.InheritanceType.Should().Be(InheritanceType.Inherited);
        reverted.DesignationSource.Should().Be("OrgDerived");
    }

    // ─── T037a: Audit trail tracks change sources ───────────────────────────

    [Fact]
    public async Task AuditTrail_TracksOrgDerivedAndManualSources()
    {
        // 0. Seed capabilities so derive produces org defaults
        var cap = AddOrgCapability("AuditTestCap", "Azure Gov");
        AddOrgMapping(cap, "AC-2", CapabilityMappingRole.Primary);
        await _db.SaveChangesAsync();

        // 1. Derive org defaults
        await _sut.DeriveOrgDefaultsAsync("test-user");
        _db.ChangeTracker.Clear();

        // 2. Propagate to system (creates OrgDerived audit entries)
        var baselineControlIds = new HashSet<string> { "AC-2", "AC-3", "IA-2", "SC-7", "SI-2" };
        await _sut.PropagateToSystemAsync(SystemId, BaselineId, baselineControlIds, "test-user");
        _db.ChangeTracker.Clear();

        // 3. Check OrgDerived audit entries exist
        var auditEntries = await _db.InheritanceAuditEntries
            .Where(e => e.ControlBaselineId == BaselineId)
            .ToListAsync();

        auditEntries.Should().Contain(e => e.ChangeSource == InheritanceChangeSource.OrgDerived);

        // 4. Manually override AC-2 then revert
        var ci = await _db.ControlInheritances.SingleAsync(c => c.ControlId == "AC-2" && c.ControlBaselineId == BaselineId);
        ci.InheritanceType = InheritanceType.Shared;
        ci.Provider = "Override Provider";
        ci.DesignationSource = "Manual";
        await _db.SaveChangesAsync();

        await _sut.RevertToOrgDefaultsAsync(SystemId, new[] { "AC-2" }, "test-user");
        _db.ChangeTracker.Clear();

        // 5. Revert creates Manual audit entries (user-initiated)
        var revertAudits = await _db.InheritanceAuditEntries
            .Where(e => e.ControlBaselineId == BaselineId && e.ControlId == "AC-2")
            .ToListAsync();

        revertAudits.Should().Contain(e => e.ChangeSource == InheritanceChangeSource.Manual,
            "Revert is user-initiated and should use Manual source");
    }

    [Fact]
    public async Task CascadePropagation_CreatesOrgPropagationAuditEntries()
    {
        // 0. Seed capabilities mapped to controls in the baseline
        var cap = AddOrgCapability("CascadeCap", "Azure Gov");
        AddOrgMapping(cap, "AC-2", CapabilityMappingRole.Primary);
        AddOrgMapping(cap, "AC-3", CapabilityMappingRole.Supporting);
        await _db.SaveChangesAsync();

        // 1. Derive org defaults (cascade should propagate to system with baseline)
        var result = await _sut.DeriveOrgDefaultsAsync("test-user");
        _db.ChangeTracker.Clear();

        result.AffectedSystems.Should().BeGreaterOrEqualTo(1);

        // 2. Check that OrgPropagation audit entries were created
        var cascadeAudits = await _db.InheritanceAuditEntries
            .Where(e => e.ChangeSource == InheritanceChangeSource.OrgPropagation)
            .ToListAsync();

        cascadeAudits.Should().NotBeEmpty("cascade should create OrgPropagation audit entries");
    }

    // ─── T047: Performance assertions ───────────────────────────────────────

    [Fact]
    public async Task Derive_PerformanceWithinBounds()
    {
        // Seed 200 capability mappings to stress-test derivation
        var cap = AddOrgCapability("PerfTestCap", "Azure Gov");
        for (int i = 1; i <= 200; i++)
            AddOrgMapping(cap, $"XX-{i}", CapabilityMappingRole.Primary);
        await _db.SaveChangesAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _sut.DeriveOrgDefaultsAsync("perf-test");
        sw.Stop();

        result.DerivedCount.Should().Be(200);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "derivation should complete within 5s (SC-001)");
    }

    [Fact]
    public async Task GetOrgDefaults_PerformanceWithinBounds()
    {
        // Seed 100 org defaults
        for (int i = 1; i <= 100; i++)
        {
            _db.OrgInheritanceDefaults.Add(new OrgInheritanceDefault
            {
                ControlId = $"PF-{i}",
                InheritanceType = InheritanceType.Inherited,
                Provider = "Provider",
                SourceCapabilityIds = "cap-1",
                SourceCapabilityNames = "Cap 1",
                MappingRole = CapabilityMappingRole.Primary,
                DerivedAt = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _sut.GetOrgDefaultsAsync(pageSize: 100);
        sw.Stop();

        result.TotalCount.Should().Be(100);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "page load should complete within 5s (SC-002)");
    }
}
