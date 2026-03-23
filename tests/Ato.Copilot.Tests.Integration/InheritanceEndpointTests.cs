using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Agents.Compliance.Services;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Integration tests for inheritance endpoints — covers T037 (GET/PUT /inheritance, GET /audit)
/// and T040 (POST /import/preview, POST /import/apply).
/// Tests use an in-memory EF Core database with BaselineService as the SUT.
/// </summary>
public class InheritanceEndpointTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;
    private readonly BaselineService _sut;

    private const string SystemId = "sys-inh-int-001";
    private const string BaselineId = "bl-inh-int-001";
    private static readonly string[] TestControlIds = [
        "AC-1", "AC-2", "AC-2(1)", "AC-3", "AC-4", "AC-5", "AC-6", "AC-7",
        "AT-1", "AT-2", "AU-1", "AU-2", "CA-1", "CM-1", "SI-1", "SI-2",
    ];

    public InheritanceEndpointTests()
    {
        var dbName = $"InheritanceInt_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddSingleton<IReferenceDataService>(Mock.Of<IReferenceDataService>());
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();

        _sut = new BaselineService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<IReferenceDataService>(),
            NullLogger<BaselineService>.Instance,
            Mock.Of<IOrgInheritanceService>());

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
            Id = SystemId, Name = "Inheritance Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Gov", CreatedBy = "test", IsActive = true,
        });

        _db.ControlBaselines.Add(new ControlBaseline
        {
            Id = BaselineId,
            RegisteredSystemId = SystemId,
            BaselineLevel = "Moderate",
            TotalControls = TestControlIds.Length,
            ControlIds = TestControlIds.ToList(),
            CreatedBy = "test",
        });

        // No ControlInheritance records seeded — absence means "undesignated"
        _db.SaveChanges();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T037: GET /inheritance — pagination, family filter, type filter
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetInheritance_BaselineHasNoDesignationsInitially()
    {
        var baseline = await _db.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstAsync(b => b.Id == BaselineId);

        // No ControlInheritance records yet — all controls are "undesignated"
        baseline.Inheritances.Should().BeEmpty();
        baseline.TotalControls.Should().Be(TestControlIds.Length);
    }

    [Fact]
    public async Task SetInheritance_SingleControl_UpdatesCorrectly()
    {
        var inputs = new List<InheritanceInput>
        {
            new()
            {
                ControlId = "AC-1",
                InheritanceType = "Inherited",
                Provider = "Azure Government (FedRAMP High)",
            }
        };

        var result = await _sut.SetInheritanceAsync(SystemId, inputs, "test-user");

        result.ControlsUpdated.Should().Be(1);
        result.InheritedCount.Should().BeGreaterOrEqualTo(1);

        var updated = await _db.Set<ControlInheritance>()
            .FirstAsync(ci => ci.ControlBaselineId == BaselineId && ci.ControlId == "AC-1");
        updated.InheritanceType.Should().Be(InheritanceType.Inherited);
        updated.Provider.Should().Be("Azure Government (FedRAMP High)");
    }

    [Fact]
    public async Task SetInheritance_BulkUpdate_UpdatesMultipleControls()
    {
        var inputs = new List<InheritanceInput>
        {
            new() { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure Gov" },
            new() { ControlId = "AC-2", InheritanceType = "Shared", Provider = "Azure Gov", CustomerResponsibility = "Manage user accounts" },
            new() { ControlId = "AC-3", InheritanceType = "Customer" },
            new() { ControlId = "AT-1", InheritanceType = "Inherited", Provider = "Azure Gov" },
            new() { ControlId = "AT-2", InheritanceType = "Inherited", Provider = "Azure Gov" },
        };

        var result = await _sut.SetInheritanceAsync(
            SystemId, inputs, "test-user", InheritanceChangeSource.BulkUpdate);

        result.ControlsUpdated.Should().Be(5);
        result.InheritedCount.Should().BeGreaterOrEqualTo(3); // AC-1, AT-1, AT-2
        result.SharedCount.Should().BeGreaterOrEqualTo(1); // AC-2
        result.CustomerCount.Should().BeGreaterOrEqualTo(1); // AC-3
    }

    [Fact]
    public async Task SetInheritance_InvalidControlId_SkipsUnknownControls()
    {
        var inputs = new List<InheritanceInput>
        {
            new() { ControlId = "XX-999", InheritanceType = "Inherited", Provider = "Azure Gov" },
        };

        var result = await _sut.SetInheritanceAsync(SystemId, inputs, "test-user");

        result.ControlsUpdated.Should().Be(0);
        result.SkippedControls.Should().Contain("XX-999");
    }

    [Fact]
    public async Task SetInheritance_SharedWithoutProvider_StillSaves()
    {
        var inputs = new List<InheritanceInput>
        {
            new() { ControlId = "AC-4", InheritanceType = "Shared", CustomerResponsibility = "Customer manages" },
        };

        var result = await _sut.SetInheritanceAsync(SystemId, inputs, "test-user");

        result.ControlsUpdated.Should().Be(1);
        var updated = await _db.Set<ControlInheritance>()
            .FirstAsync(ci => ci.ControlBaselineId == BaselineId && ci.ControlId == "AC-4");
        updated.InheritanceType.Should().Be(InheritanceType.Shared);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T037: GET /{controlId}/audit — audit trail
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AuditEntry_CreatedOnDesignationChange()
    {
        // First designation
        await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "AC-5", InheritanceType = "Inherited", Provider = "Azure Gov" }
        }, "user-a");

        // Second change
        await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "AC-5", InheritanceType = "Shared", Provider = "Azure Gov", CustomerResponsibility = "Customer manages access" }
        }, "user-b");

        var audits = await _db.Set<InheritanceAuditEntry>()
            .Where(a => a.ControlId == "AC-5" && a.ControlBaselineId == BaselineId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync();

        audits.Should().HaveCountGreaterOrEqualTo(2);

        var latest = audits.Last();
        latest.NewInheritanceType.Should().Be("Shared");
        latest.Actor.Should().Be("user-b");
    }

    [Fact]
    public async Task AuditEntry_RecordsPreviousValues()
    {
        await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "AC-6", InheritanceType = "Inherited", Provider = "Azure Gov" }
        }, "user-a");

        await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "AC-6", InheritanceType = "Customer" }
        }, "user-b");

        var audits = await _db.Set<InheritanceAuditEntry>()
            .Where(a => a.ControlId == "AC-6" && a.ControlBaselineId == BaselineId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync();

        var lastAudit = audits.Last();
        lastAudit.PreviousInheritanceType.Should().Be("Inherited");
        lastAudit.NewInheritanceType.Should().Be("Customer");
        lastAudit.PreviousProvider.Should().Be("Azure Gov");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T037: Undesignated filter — controls with no ControlInheritance record
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FilterByType_UndesignatedOnly()
    {
        // Set some controls to Inherited — only these will have ControlInheritance records
        await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure Gov" },
            new InheritanceInput { ControlId = "AC-2", InheritanceType = "Inherited", Provider = "Azure Gov" },
        }, "test-user");

        // "Undesignated" = controls in baseline that have no ControlInheritance record
        var designatedControlIds = await _db.Set<ControlInheritance>()
            .Where(ci => ci.ControlBaselineId == BaselineId)
            .Select(ci => ci.ControlId)
            .ToListAsync();

        var undesignated = TestControlIds.Count(id => !designatedControlIds.Contains(id));

        undesignated.Should().Be(TestControlIds.Length - 2);
    }

    [Fact]
    public async Task FilterByFamily_ReturnsOnlyMatchingControls()
    {
        // Set all 8 AC controls so they have records
        await _sut.SetInheritanceAsync(SystemId, TestControlIds
            .Where(id => id.StartsWith("AC-"))
            .Select(id => new InheritanceInput { ControlId = id, InheritanceType = "Customer" })
            .ToArray(), "test-user");

        var acInheritances = await _db.Set<ControlInheritance>()
            .Where(ci => ci.ControlBaselineId == BaselineId && ci.ControlId.StartsWith("AC-"))
            .ToListAsync();

        acInheritances.Should().HaveCount(8); // AC-1, AC-2, AC-2(1), AC-3, AC-4, AC-5, AC-6, AC-7
    }

    // ═══════════════════════════════════════════════════════════════════════
    // T040: Import preview and apply
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Import_ValidDesignations_AppliesCorrectly()
    {
        // Simulate what import/apply does — calls SetInheritanceAsync with CrmImport source
        var importInputs = new List<InheritanceInput>
        {
            new() { ControlId = "SI-1", InheritanceType = "Inherited", Provider = "Azure Gov" },
            new() { ControlId = "SI-2", InheritanceType = "Shared", Provider = "Azure Gov", CustomerResponsibility = "Monitor system integrity" },
        };

        var result = await _sut.SetInheritanceAsync(
            SystemId, importInputs, "import-user", InheritanceChangeSource.CrmImport);

        result.ControlsUpdated.Should().Be(2);
        result.InheritedCount.Should().BeGreaterOrEqualTo(1);
        result.SharedCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task Import_NotFoundControls_SkippedGracefully()
    {
        var importInputs = new List<InheritanceInput>
        {
            new() { ControlId = "ZZ-1", InheritanceType = "Inherited", Provider = "Azure Gov" },
            new() { ControlId = "ZZ-2", InheritanceType = "Customer" },
            new() { ControlId = "AC-1", InheritanceType = "Customer" }, // valid
        };

        var result = await _sut.SetInheritanceAsync(
            SystemId, importInputs, "import-user", InheritanceChangeSource.CrmImport);

        result.ControlsUpdated.Should().Be(1); // only AC-1
        result.SkippedControls.Should().Contain("ZZ-1");
        result.SkippedControls.Should().Contain("ZZ-2");
    }

    [Fact]
    public async Task Import_OverwriteExisting_UpdatesDesignation()
    {
        // First set AC-7 to Inherited
        await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "AC-7", InheritanceType = "Inherited", Provider = "Azure Gov" }
        }, "user-a");

        // Import overwrites to Customer
        var result = await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "AC-7", InheritanceType = "Customer" }
        }, "import-user", InheritanceChangeSource.CrmImport);

        result.ControlsUpdated.Should().Be(1);

        var updated = await _db.Set<ControlInheritance>()
            .FirstAsync(ci => ci.ControlBaselineId == BaselineId && ci.ControlId == "AC-7");
        updated.InheritanceType.Should().Be(InheritanceType.Customer);
    }

    [Fact]
    public async Task Import_AuditTrail_RecordsImportSource()
    {
        await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "CM-1", InheritanceType = "Inherited", Provider = "Azure Gov" }
        }, "import-user", InheritanceChangeSource.CrmImport);

        var audit = await _db.Set<InheritanceAuditEntry>()
            .Where(a => a.ControlId == "CM-1" && a.ControlBaselineId == BaselineId)
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();

        audit.Should().NotBeNull();
        audit!.ChangeSource.Should().Be(InheritanceChangeSource.CrmImport);
        audit.Actor.Should().Be("import-user");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Summary recalculation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Summary_RecalculatesAfterDesignations()
    {
        await _sut.SetInheritanceAsync(SystemId, new[]
        {
            new InheritanceInput { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure" },
            new InheritanceInput { ControlId = "AC-2", InheritanceType = "Inherited", Provider = "Azure" },
            new InheritanceInput { ControlId = "AC-3", InheritanceType = "Shared", Provider = "Azure", CustomerResponsibility = "Manage" },
            new InheritanceInput { ControlId = "AC-4", InheritanceType = "Customer" },
        }, "test-user");

        // SetInheritanceAsync uses its own scoped DbContext — detach and reload to see updates
        _db.ChangeTracker.Clear();
        var baseline = await _db.ControlBaselines.FirstAsync(b => b.Id == BaselineId);
        baseline.InheritedControls.Should().BeGreaterOrEqualTo(2);
        baseline.SharedControls.Should().BeGreaterOrEqualTo(1);
        baseline.CustomerControls.Should().BeGreaterOrEqualTo(1);
    }
}
