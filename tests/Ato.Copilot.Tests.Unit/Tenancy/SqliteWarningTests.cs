using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// T106 [US5]: Verifies <see cref="RlsPolicyInstaller"/> emits the FR-033
/// "SQLite — RLS NOT installed" warning at startup when the active EF Core
/// provider is SQLite. The same call against a non-SQL-Server / non-SQLite
/// provider (e.g. in-memory) must remain silent.
/// </summary>
public class SqliteWarningTests
{
    [Fact]
    public async Task SqliteProvider_LogsRlsUnavailableWarning()
    {
        await using var conn = new SqliteConnection("Filename=:memory:");
        await conn.OpenAsync();

        var optsBuilder = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(conn);
        await using var db = new AtoCopilotContext(optsBuilder.Options);

        var logger = new Mock<ILogger>();
        // Capture every Warning message into a list so we can assert later.
        var warnings = new List<string>();
        logger
            .Setup(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>(
                (_, _, state, _, _) => warnings.Add(state.ToString() ?? string.Empty));

        await RlsPolicyInstaller.ApplyAsync(db, logger.Object);

        warnings.Should().ContainSingle(w =>
            w.Contains("SQLite", StringComparison.Ordinal)
            && w.Contains("Row-Level Security NOT installed", StringComparison.Ordinal)
            && w.Contains("NOT FOR PRODUCTION", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InMemoryProvider_DoesNotLogSqliteWarning()
    {
        var optsBuilder = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());
        await using var db = new AtoCopilotContext(optsBuilder.Options);

        var logger = new Mock<ILogger>();
        var warnings = new List<string>();
        logger
            .Setup(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>(
                (_, _, state, _, _) => warnings.Add(state.ToString() ?? string.Empty));

        await RlsPolicyInstaller.ApplyAsync(db, logger.Object);

        warnings.Should().NotContain(w => w.Contains("SQLite", StringComparison.Ordinal));
    }
}
