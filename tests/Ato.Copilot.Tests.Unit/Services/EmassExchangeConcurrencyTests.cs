using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class EmassExchangeConcurrencyTests
{
    [Fact]
    public async Task Additive_schema_is_repeatable_and_enforces_unique_history_and_request_keys()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await EmassExchangeSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await EmassExchangeSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var row = new EmassExchangeRecord { TenantId = Guid.NewGuid(), RegisteredSystemId = "system", Version = 1, IdempotencyKey = "key" };
        db.Add(row);
        await db.SaveChangesAsync();
        // Act
        db.Add(new EmassExchangeRecord { TenantId = row.TenantId, RegisteredSystemId = "system", Version = 1, IdempotencyKey = "other" });
        var duplicateVersion = () => db.SaveChangesAsync();
        // Assert
        await duplicateVersion.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();
        // Act
        db.Add(new EmassExchangeRecord { TenantId = row.TenantId, RegisteredSystemId = "system", Version = 2, IdempotencyKey = "key" });
        var duplicateKey = () => db.SaveChangesAsync();
        // Assert
        await duplicateKey.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Competing_insert_after_version_read_is_replayed_or_returns_conflict(bool identicalRequest, bool reuseKey)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var winnerDb = new AtoCopilotContext(options);
        await winnerDb.Database.EnsureCreatedAsync();
        var tenant = new TenantContext(Guid.NewGuid()) { PersonId = Guid.NewGuid() };
        var generated = DateTimeOffset.UtcNow.AddDays(-1);
        winnerDb.RegisteredSystems.Add(new() { Id = "system", TenantId = tenant.TenantId, Name = "Race fixture", CreatedBy = "test" });
        winnerDb.AuthorizationPackages.Add(new() { Id = "package", TenantId = tenant.TenantId, RegisteredSystemId = "system",
            Status = PackageStatus.Completed, GeneratedAt = generated, CompletedAt = generated, ContentHash = "retained-hash" });
        await winnerDb.SaveChangesAsync();
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(x => x.GetAccessAsync(tenant.TenantId, tenant.PersonId, "system", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse("system", ["Issm"], new(true, false, false, false, false, false, false, false, false)));
        var request = new RecordEmassExchangeRequest("package", "retained-hash", generated, "ReceiptRecorded", "Workflow", "REF",
            DateTimeOffset.UtcNow.AddHours(-1), "", "same-key", 0);
        var winner = new EmassExchangeService(winnerDb, tenant, access.Object);
        var race = new BeforeExchangeInsert(async () =>
            await winner.RecordAsync("system", identicalRequest ? request : reuseKey
                ? request with { ExternalReference = "different-receipt" }
                : request with { IdempotencyKey = "winner-key" }, "actor"));
        await using var loserDb = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection).AddInterceptors(race).Options);
        var loser = new EmassExchangeService(loserDb, tenant, access.Object);
        // Act
        if (identicalRequest)
        {
            var result = await loser.RecordAsync("system", request, "actor");
            // Assert
            result.Id.Should().Be((await winner.GetHistoryAsync("system")).Items.Single().Id);
            // Act
            var correction = await winner.RecordAsync("system", request with
            {
                ExpectedVersion = 1, IdempotencyKey = "correction", SupersedesId = result.Id,
                Outcome = "ImportRejected", Notes = "Returned result corrected.",
            }, "actor");
            // Assert
            correction.SupersedesId.Should().Be(result.Id);
        }
        else
        {
            var write = () => loser.RecordAsync("system", request, "actor");
            // Assert
            await write.Should().ThrowAsync<EmassExchangeConflictException>();
        }
        (await winner.GetHistoryAsync("system")).Items.Should().HaveCount(identicalRequest ? 2 : 1);
        (await winner.GetExportsAsync("system")).Should().ContainSingle().Which.Should()
            .Be(new EmassExchangeExport("package", "retained-hash", generated, PackagePurpose.Legacy.ToString()));
    }

    [Fact]
    public async Task Nonconflict_storage_failure_is_rethrown_and_does_not_leave_a_pending_insert_for_retry()
    {
        // Arrange
        var failure = new FailOnceExchangeSave();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failure).Options);
        var tenant = new TenantContext(Guid.NewGuid()) { PersonId = Guid.NewGuid() };
        var generated = DateTimeOffset.UtcNow.AddDays(-1);
        db.RegisteredSystems.Add(new() { Id = "system", TenantId = tenant.TenantId, Name = "Failure fixture", CreatedBy = "test" });
        db.AuthorizationPackages.Add(new()
        {
            Id = "package", TenantId = tenant.TenantId, RegisteredSystemId = "system",
            Status = PackageStatus.Completed, GeneratedAt = generated, CompletedAt = generated, ContentHash = "retained-hash",
        });
        db.SaveChanges();
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(x => x.GetAccessAsync(tenant.TenantId, tenant.PersonId, "system", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse("system", ["Issm"], new(true, false, false, false, false, false, false, false, false)));
        var service = new EmassExchangeService(db, tenant, access.Object);
        var request = new RecordEmassExchangeRequest("package", "retained-hash", generated, "ReceiptRecorded", "Workflow", "REF",
            DateTimeOffset.UtcNow.AddHours(-1), "", "retry-key", 0);

        // Act
        var write = () => service.RecordAsync("system", request, "actor");

        // Assert
        (await write.Should().ThrowAsync<DbUpdateException>()).Which.Should().BeSameAs(failure.Exception);
        db.ChangeTracker.Entries<EmassExchangeRecord>().Should().BeEmpty();
        (await service.GetHistoryAsync("system")).Items.Should().BeEmpty();

        // Act
        var retried = await service.RecordAsync("system", request, "actor");

        // Assert
        retried.Version.Should().Be(1);
        (await service.GetHistoryAsync("system")).Items.Should().ContainSingle().Which.Should().Be(retried);
    }

    private sealed class FailOnceExchangeSave : SaveChangesInterceptor
    {
        public DbUpdateException Exception { get; } = new("Injected storage failure, unrelated to a unique constraint.");
        private bool failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!failed)
            {
                failed = true;
                throw Exception;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BeforeExchangeInsert(Func<Task> concurrentWrite) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await concurrentWrite();
            return result;
        }
    }
}
