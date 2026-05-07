using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Onboarding;

namespace Ato.Copilot.Tests.Unit.Onboarding;

/// <summary>
/// Race-condition unit tests for <see cref="BootstrapAdministratorService"/> — verifies
/// that two simultaneous first-users serialize through the per-tenant lock and that
/// exactly one wins (FR-001 / FR-002).
/// </summary>
public class BootstrapAdministratorServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<IWizardAuditService> _audit = new();
    private readonly BootstrapAdministratorService _sut;

    public BootstrapAdministratorServiceTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"BootstrapAdminTests_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
        _sut = new BootstrapAdministratorService(
            _factory,
            _audit.Object,
            NullLogger<BootstrapAdministratorService>.Instance);
    }

    public void Dispose()
    {
        using var db = _factory.CreateDbContext();
        db.Database.EnsureDeleted();
    }

    [Fact]
    public async Task GrantAsync_FirstUser_BecomesAdministrator()
    {
        var tenantId = Guid.NewGuid();
        var subject = Guid.NewGuid();

        var result = await _sut.GrantAsync(
            tenantId, subject, "Bootstrap Admin", "admin@example.test", Guid.NewGuid());

        result.Granted.Should().BeTrue();
        result.AssignmentId.Should().NotBeNull();

        await using var db = _factory.CreateDbContext();
        var assignments = await db.OrganizationRoleAssignments
            .Where(a => a.TenantId == tenantId)
            .ToListAsync();
        assignments.Should().HaveCount(1);
    }

    [Fact]
    public async Task GrantAsync_TwoUsersConcurrent_OnlyOneWins()
    {
        var tenantId = Guid.NewGuid();
        var subjectA = Guid.NewGuid();
        var subjectB = Guid.NewGuid();

        var taskA = Task.Run(() => _sut.GrantAsync(
            tenantId, subjectA, "User A", "a@example.test", Guid.NewGuid()));
        var taskB = Task.Run(() => _sut.GrantAsync(
            tenantId, subjectB, "User B", "b@example.test", Guid.NewGuid()));
        var results = await Task.WhenAll(taskA, taskB);

        results.Count(r => r.Granted).Should().Be(1);
        results.Count(r => !r.Granted).Should().Be(1);
        results.Single(r => !r.Granted).ErrorCode
            .Should().Be(WizardErrorCodes.BootstrapRace);
    }

    [Fact]
    public async Task GrantAsync_WhenAdminAlreadyExists_ReturnsRaceCode()
    {
        var tenantId = Guid.NewGuid();
        await _sut.GrantAsync(tenantId, Guid.NewGuid(), "First", "first@example.test", Guid.NewGuid());

        var result = await _sut.GrantAsync(
            tenantId, Guid.NewGuid(), "Second", "second@example.test", Guid.NewGuid());

        result.Granted.Should().BeFalse();
        result.ErrorCode.Should().Be(WizardErrorCodes.BootstrapRace);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
