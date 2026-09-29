using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Poam;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public class TaskTicketPersistenceTests
{
    [Fact]
    public async Task SqliteSchema_IsIdempotent_LeaseSurvivesFreshContext_RejectsDuplicateClaim()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        var tenantId = Guid.NewGuid();
        await using (var setup = new AtoCopilotContext(options))
        {
            await TaskTicketingSchemaAdditions.ApplyAsync(setup, NullLogger.Instance);
            await TaskTicketingSchemaAdditions.ApplyAsync(setup, NullLogger.Instance);
            setup.Set<TaskTicketLink>().Add(new()
            {
                TenantId = tenantId, RegisteredSystemId = "system", TaskId = "task",
                State = "Pending", CreateAttempted = true, CorrelationKey = "stable"
            });
            await setup.SaveChangesAsync();
        }
        // Act
        await using var restarted = new AtoCopilotContext(options);
        // This isolated additive-schema fixture contains no system/role tables; authorization is tested by the service suite.
        var retained = await restarted.Set<TaskTicketLink>().IgnoreQueryFilters().SingleAsync();
        restarted.Set<TaskTicketLink>().Add(new() { TenantId = tenantId, RegisteredSystemId = "system", TaskId = "task" });
        var duplicate = () => restarted.SaveChangesAsync();
        // Assert
        retained.State.Should().Be("Pending");
        retained.CreateAttempted.Should().BeTrue();
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SqliteConcurrentRefresh_RejectsStaleVersion()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using (var setup = new AtoCopilotContext(options))
        {
            await TaskTicketingSchemaAdditions.ApplyAsync(setup, NullLogger.Instance);
            setup.Set<TaskTicketLink>().Add(new() { TenantId = Guid.NewGuid(), RegisteredSystemId = "system", TaskId = "task" });
            await setup.SaveChangesAsync();
        }
        await using var first = new AtoCopilotContext(options);
        await using var second = new AtoCopilotContext(options);
        var firstLink = await first.Set<TaskTicketLink>().IgnoreQueryFilters().SingleAsync();
        var staleLink = await second.Set<TaskTicketLink>().IgnoreQueryFilters().SingleAsync();
        // Act
        firstLink.State = "Linked"; firstLink.RowVersion = Guid.NewGuid();
        await first.SaveChangesAsync();
        staleLink.State = "Unlinked"; staleLink.RowVersion = Guid.NewGuid();
        var stale = () => second.SaveChangesAsync();
        // Assert
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
