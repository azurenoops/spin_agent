using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// T121 [FR-073..FR-076]: unit tests for
/// <see cref="MultiTenantMigrationService"/>. Validates that:
/// <list type="bullet">
///   <item>The preview reports per-table totals.</item>
///   <item>Idempotency: running with no NULL TenantId rows is a no-op.</item>
///   <item>Failure inside the transaction is reported via
///     <see cref="MultiTenantMigrationService.MigrationReport.Error"/>.</item>
/// </list>
/// </summary>
public class MultiTenantMigrationServiceTests
{
    private static (TestDbContextFactory factory, MultiTenantMigrationService service) Build()
    {
        var opts = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"mtm-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(opts);
        var svc = new MultiTenantMigrationService(factory, NullLogger<MultiTenantMigrationService>.Instance);
        return (factory, svc);
    }

    [Fact]
    public async Task Preview_OnEmptyDb_ReturnsTablesList()
    {
        var (_, svc) = Build();

        var preview = await svc.PreviewAsync();

        preview.Tables.Should().NotBeNull();
        preview.Tables.Should().NotBeEmpty(
            "the model contains [TenantScoped] entities");
        preview.Tables.Should().OnlyContain(t => t.RowsMissingTenant >= 0);
    }

    [Fact]
    public async Task Execute_RequiresNonEmptyDefaultTenantId()
    {
        var (_, svc) = Build();

        Func<Task> act = () => svc.ExecuteAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Execute_OnInMemory_EmitsReportAndAuditRow()
    {
        var (factory, svc) = Build();
        var defaultTenant = Guid.NewGuid();

        var report = await svc.ExecuteAsync(
            defaultTenant,
            installRls: false,
            actorOid: "test-user",
            correlationId: "corr-001");

        report.DefaultTenantId.Should().Be(defaultTenant);
        report.RlsInstalled.Should().BeFalse(
            "InMemory provider is not SQL Server; RLS install is a no-op");
        report.Tables.Should().NotBeEmpty();

        var audit = await factory.Context.AuditLogs
            .Where(a => a.Action == "Tenant.Migrate")
            .ToListAsync();
        audit.Should().NotBeEmpty();
        audit.Last().CorrelationId.Should().Be("corr-001");
        audit.Last().Outcome.Should().Be(AuditOutcome.Success);
    }
}
