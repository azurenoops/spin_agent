using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit;

public class BoundaryMigrationStartupTests
{
    [Fact]
    public async Task SqliteStartup_CreatesSentinelAndCanRunAgain()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var services = new ServiceCollection()
            .AddDbContext<AtoCopilotContext>(options => options.UseSqlite(connection))
            .BuildServiceProvider();
        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().Database.EnsureCreatedAsync();
        var migration = new BoundaryMigrationService(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BoundaryMigrationService>.Instance);

        // Act
        await migration.StartAsync(default);
        await migration.StartAsync(default);

        // Assert
        await using var verify = services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var flags = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM __MigrationFlags WHERE Name = 'F040_BoundaryToComponent'").SingleAsync();
        flags.Should().Be(1);
    }
}
