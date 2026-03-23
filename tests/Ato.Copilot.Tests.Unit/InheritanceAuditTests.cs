using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit;

/// <summary>
/// Feature 043 – T036: Unit tests for InheritanceAuditEntry creation in
/// BaselineService.SetInheritanceAsync.
/// </summary>
public class InheritanceAuditTests : IDisposable
{
    private const string SystemId = "sys-audit-test";

    private readonly ServiceProvider _sp;
    private readonly BaselineService _service;

    public InheritanceAuditTests()
    {
        var dbName = $"AuditTests_{Guid.NewGuid()}";

        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o =>
            o.UseInMemoryDatabase(dbName));

        _sp = services.BuildServiceProvider();

        // Seed a baseline with 3 controls (use a directly-constructed context
        // sharing the same InMemory database to guarantee data is visible)
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using var seedCtx = new AtoCopilotContext(options);
        seedCtx.Database.EnsureCreated();

        seedCtx.ControlBaselines.Add(new ControlBaseline
        {
            Id = "bl-1",
            RegisteredSystemId = SystemId,
            BaselineLevel = "Moderate",
            ControlIds = ["AC-1", "AC-2", "AC-3"],
            CreatedBy = "test-setup",
            Inheritances = []
        });
        seedCtx.SaveChanges();

        _service = new BaselineService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IReferenceDataService>(),
            NullLogger<BaselineService>.Instance,
            Mock.Of<IOrgInheritanceService>());
    }

    public void Dispose()
    {
        _sp.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task NewDesignation_CreatesAuditEntry_WithNullPrevious()
    {
        var result = await _service.SetInheritanceAsync(
            SystemId,
            [new InheritanceInput { ControlId = "AC-1", InheritanceType = "Inherited", Provider = "Azure Gov" }],
            "tester");

        result.ControlsUpdated.Should().Be(1);

        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var entries = await ctx.InheritanceAuditEntries
            .Where(e => e.ControlId == "AC-1")
            .ToListAsync();

        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.PreviousInheritanceType.Should().BeNull();
        entry.NewInheritanceType.Should().Be("Inherited");
        entry.PreviousProvider.Should().BeNull();
        entry.NewProvider.Should().Be("Azure Gov");
        entry.Actor.Should().Be("tester");
        entry.ChangeSource.Should().Be(InheritanceChangeSource.Manual);
    }

    [Fact]
    public async Task UpdateDesignation_RecordsPreviousValues()
    {
        // First: set Inherited
        await _service.SetInheritanceAsync(
            SystemId,
            [new InheritanceInput { ControlId = "AC-2", InheritanceType = "Inherited", Provider = "AWS" }],
            "user-a");

        // Second: change to Customer
        await _service.SetInheritanceAsync(
            SystemId,
            [new InheritanceInput { ControlId = "AC-2", InheritanceType = "Customer", CustomerResponsibility = "Org implements" }],
            "user-b");

        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var entries = await ctx.InheritanceAuditEntries
            .Where(e => e.ControlId == "AC-2")
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        entries.Should().HaveCount(2);

        var second = entries[1];
        second.PreviousInheritanceType.Should().Be("Inherited");
        second.NewInheritanceType.Should().Be("Customer");
        second.PreviousProvider.Should().Be("AWS");
        second.NewProvider.Should().BeNull();
        second.PreviousCustomerResponsibility.Should().BeNull();
        second.NewCustomerResponsibility.Should().Be("Org implements");
        second.Actor.Should().Be("user-b");
    }

    [Fact]
    public async Task ChangeSource_IsRecordedCorrectly()
    {
        await _service.SetInheritanceAsync(
            SystemId,
            [new InheritanceInput { ControlId = "AC-3", InheritanceType = "Shared", Provider = "Azure Gov" }],
            "user-c",
            InheritanceChangeSource.BulkUpdate);

        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var entry = await ctx.InheritanceAuditEntries
            .FirstOrDefaultAsync(e => e.ControlId == "AC-3");

        entry.Should().NotBeNull();
        entry!.ChangeSource.Should().Be(InheritanceChangeSource.BulkUpdate);
    }

    [Fact]
    public async Task SkippedControls_DoNotCreateAuditEntries()
    {
        var result = await _service.SetInheritanceAsync(
            SystemId,
            [new InheritanceInput { ControlId = "ZZ-9999", InheritanceType = "Inherited" }],
            "user-d");

        result.ControlsUpdated.Should().Be(0);
        result.SkippedControls.Should().Contain("ZZ-9999");

        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var entries = await ctx.InheritanceAuditEntries
            .Where(e => e.ControlId == "ZZ-9999")
            .ToListAsync();

        entries.Should().BeEmpty();
    }
}
