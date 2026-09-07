using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

namespace Ato.Copilot.Tests.Unit.Data;

/// <summary>
/// Issue #868 — EnsureSchemaAdditionsAsync must fail startup on DDL error.
///
/// Each ApplyAsync method must rethrow and log at Error level so the process
/// halts rather than continuing with an incomplete schema.
///
/// Test strategy: use the SQLite provider pointed at a path that cannot be
/// opened. The provider name passes the isSqlite check, so the DDL branch is
/// entered, any database operation throws, and we assert the exception
/// propagates (rather than being swallowed) and LogError is called.
/// </summary>
public class EnsureSchemaAdditionsAsyncTests
{
    // ─── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an AtoCopilotContext wired to a SQLite path that cannot be
    /// opened, so any database call throws. The provider name is still
    /// "Microsoft.EntityFrameworkCore.Sqlite", which passes the isSqlite
    /// branch check in each ApplyAsync method.
    /// </summary>
    private static AtoCopilotContext BuildBrokenSqliteContext()
    {
        // Point to a path inside a directory that does not exist.
        // SQLite will fail to create the file when any DB call is made.
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite("Data Source=/tmp/esa-test-nonexistent-dir-99999/test.db")
            .Options;
        return new AtoCopilotContext(options);
    }

    private static Mock<ILogger<AtoCopilotContext>> BuildLogger() =>
        new Mock<ILogger<AtoCopilotContext>>(MockBehavior.Loose);

    // ─── TenantsAndOrganizationsSchemaAdditions ───────────────────────────────

    /// <summary>
    /// AC1: ApplyAsync re-throws on DDL exception.
    /// AC2: LogError is called with provider name in message.
    /// </summary>
    [Fact]
    public async Task TenantsAndOrganizations_WhenDdlFails_ThrowsInvalidOperationAndLogsError()
    {
        // Arrange
        await using var ctx = BuildBrokenSqliteContext();
        var logger = BuildLogger();

        // Act
        var act = async () =>
            await TenantsAndOrganizationsSchemaAdditions.ApplyAsync(ctx, logger.Object, CancellationToken.None);

        // Assert — re-throws wrapped in InvalidOperationException
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TenantsAndOrganizationsSchemaAdditions*");

        // Assert — LogError was called
        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("TenantsAndOrganizationsSchemaAdditions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // ─── CapabilityHistoryEventsSchemaAdditions ───────────────────────────────

    [Fact]
    public async Task CapabilityHistoryEvents_WhenDdlFails_ThrowsInvalidOperationAndLogsError()
    {
        await using var ctx = BuildBrokenSqliteContext();
        var logger = BuildLogger();

        var act = async () =>
            await CapabilityHistoryEventsSchemaAdditions.ApplyAsync(ctx, logger.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CapabilityHistoryEventsSchemaAdditions*");

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("CapabilityHistoryEventsSchemaAdditions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // ─── LoginAuditEventsSchemaAdditions ──────────────────────────────────────

    [Fact]
    public async Task LoginAuditEvents_WhenDdlFails_ThrowsInvalidOperationAndLogsError()
    {
        await using var ctx = BuildBrokenSqliteContext();
        var logger = BuildLogger();

        var act = async () =>
            await LoginAuditEventsSchemaAdditions.ApplyAsync(ctx, logger.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LoginAuditEventsSchemaAdditions*");

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("LoginAuditEventsSchemaAdditions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // ─── GlobalBaselineSchemaAdditions ────────────────────────────────────────

    [Fact]
    public async Task GlobalBaseline_WhenDdlFails_ThrowsInvalidOperationAndLogsError()
    {
        await using var ctx = BuildBrokenSqliteContext();
        var logger = BuildLogger();

        var act = async () =>
            await GlobalBaselineSchemaAdditions.ApplyAsync(ctx, logger.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*GlobalBaselineSchemaAdditions*");

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("GlobalBaselineSchemaAdditions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // ─── AuditLogTenantAttributionAdditions ───────────────────────────────────

    [Fact]
    public async Task AuditLogTenantAttribution_WhenDdlFails_ThrowsInvalidOperationAndLogsError()
    {
        await using var ctx = BuildBrokenSqliteContext();
        var logger = BuildLogger();

        var act = async () =>
            await AuditLogTenantAttributionAdditions.ApplyAsync(ctx, logger.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AuditLogTenantAttributionAdditions*");

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("AuditLogTenantAttributionAdditions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
