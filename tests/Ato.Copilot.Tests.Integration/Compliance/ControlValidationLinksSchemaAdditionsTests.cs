using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

public sealed class ControlValidationLinksSchemaAdditionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AtoCopilotContext _context;

    public ControlValidationLinksSchemaAdditionsTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _context = new AtoCopilotContext(
            new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _context.Database.ExecuteSqlRaw("DROP TABLE ControlValidationLinks;");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ApplyAsync_Twice_CreatesTableForeignKeysAndIndexes()
    {
        // Arrange / Act
        await ControlValidationLinksSchemaAdditions.ApplyAsync(_context, NullLogger.Instance);
        await ControlValidationLinksSchemaAdditions.ApplyAsync(_context, NullLogger.Instance);

        // Assert
        (await ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ControlValidationLinks';"))
            .Should().Be(1);
        (await ScalarAsync("SELECT COUNT(*) FROM pragma_foreign_key_list('ControlValidationLinks');"))
            .Should().Be(2);
        (await ScalarAsync("SELECT COUNT(*) FROM pragma_index_list('ControlValidationLinks') WHERE name='IX_ControlValidationLink_Implementation_Target' AND [unique]=1;"))
            .Should().Be(1);
        (await ScalarAsync("SELECT COUNT(*) FROM pragma_index_list('ControlValidationLinks') WHERE name='IX_ControlValidationLinks_TenantId';"))
            .Should().Be(1);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}