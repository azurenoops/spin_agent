using System.Text;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public sealed class NarrativeLibraryConcurrencyTests
{
    [Fact]
    public async Task ConcurrentImportsTranslateDuplicateLineageToExplicitConflict()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = new TenantContext(Guid.NewGuid());
        await using (var seed = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.RegisteredSystems.Add(new() { TenantId = tenant.TenantId, Id = "system-1", Name = "Synthetic" });
            seed.RmfRoleAssignments.Add(new() { TenantId = tenant.TenantId, RegisteredSystemId = "system-1",
                UserId = "author", RmfRole = RmfRole.Isso });
            await seed.SaveChangesAsync();
        }
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstDb = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection)
            .AddInterceptors(new FirstWriter(secondReady.Task, firstCommitted)).Options);
        await using var secondDb = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection)
            .AddInterceptors(new SecondWriter(secondReady, firstCommitted.Task)).Options);
        var first = new NarrativeLibraryService(firstDb, tenant);
        var second = new NarrativeLibraryService(secondDb, tenant);
        using var firstInput = new MemoryStream(Encoding.UTF8.GetBytes("First reference"));
        using var secondInput = new MemoryStream(Encoding.UTF8.GetBytes("Concurrent reference"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act
        var winner = first.ImportAsync("system-1", "author", "Same title", "System", "system-1", "first.txt", firstInput, timeout.Token);
        var loser = second.ImportAsync("system-1", "author", "Same title", "System", "system-1", "second.txt", secondInput, timeout.Token);

        // Assert
        try
        {
            (await winner).Version.Should().Be(1);
            var conflict = async () => await loser;
            await conflict.Should().ThrowAsync<InvalidOperationException>().WithMessage("CONCURRENCY_CONFLICT:*");
            (await firstDb.NarrativeReferences.CountAsync()).Should().Be(1);
        }
        finally
        {
            await timeout.CancelAsync();
        }
    }

    private sealed class FirstWriter(Task secondReady, TaskCompletionSource firstCommitted) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await secondReady.WaitAsync(cancellationToken);
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            firstCommitted.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            firstCommitted.TrySetException(eventData.Exception);
            return Task.CompletedTask;
        }
    }

    private sealed class SecondWriter(TaskCompletionSource secondReady, Task firstCommitted) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            secondReady.TrySetResult();
            await firstCommitted.WaitAsync(cancellationToken);
            return result;
        }
    }
}
