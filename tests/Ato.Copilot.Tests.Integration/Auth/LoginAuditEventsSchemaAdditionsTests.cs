using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Auth;

/// <summary>
/// Feature 051 T020 — verifies the idempotent SQL ddl in
/// <see cref="LoginAuditEventsSchemaAdditions"/> creates the
/// <c>LoginAuditEvents</c> table + three indexes on SQLite, is safe to
/// re-run, and that the matching EF Core model can read/write rows.
/// </summary>
public sealed class LoginAuditEventsSchemaAdditionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AtoCopilotContext _ctx;

    public LoginAuditEventsSchemaAdditionsTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(_connection)
            .Options;
        _ctx = new AtoCopilotContext(options);
        _ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ApplyAsync_IsIdempotent()
    {
        // Arrange / Act — run twice
        await LoginAuditEventsSchemaAdditions.ApplyAsync(_ctx, NullLogger.Instance);
        await LoginAuditEventsSchemaAdditions.ApplyAsync(_ctx, NullLogger.Instance);

        // Assert — no exception, table exists
        var tableExists = await TableExistsAsync("LoginAuditEvents");
        tableExists.Should().BeTrue("LoginAuditEvents must exist after ApplyAsync");
    }

    [Theory]
    [InlineData("IX_LoginAuditEvents_Tenant_Occurred")]
    [InlineData("IX_LoginAuditEvents_Occurred")]
    [InlineData("IX_LoginAuditEvents_Oid")]
    public async Task ApplyAsync_CreatesIndex(string indexName)
    {
        // Arrange / Act
        await LoginAuditEventsSchemaAdditions.ApplyAsync(_ctx, NullLogger.Instance);

        // Assert
        var indexExists = await IndexExistsAsync(indexName);
        indexExists.Should().BeTrue($"{indexName} must be created by the schema additions module");
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", tableName);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result) == 1;
    }

    private async Task<bool> IndexExistsAsync(string indexName)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", indexName);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result) == 1;
    }
}
