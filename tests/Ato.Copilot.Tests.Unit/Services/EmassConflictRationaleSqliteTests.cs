using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class EmassConflictRationaleSqliteTests
{
    [Theory]
    [InlineData(ConflictStatus.AcceptEmass)]
    [InlineData(ConflictStatus.KeepSpin)]
    [InlineData(ConflictStatus.Deferred)]
    public async Task Resolution_audit_commits_atomically_and_stale_reviewer_cannot_replace_it(ConflictStatus resolution)
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options;
        await using var seed = new AtoCopilotContext(options);
        await seed.Database.EnsureCreatedAsync();
        var tenant = Guid.NewGuid();
        seed.RegisteredSystems.Add(new() { Id = "system", TenantId = tenant, Name = "Original", CreatedBy = "fixture" });
        seed.EmassConflicts.Add(new()
        {
            Id = "conflict", TenantId = tenant, RegisteredSystemId = "system", EntityType = "SystemInfo",
            FieldName = "SystemInfo.SystemName", SpinValue = "Original", EmassValue = "Returned", SyncBatchId = "batch",
        });
        await seed.SaveChangesAsync();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(options));
        var service = new EmassRoundTripSyncService(factory.Object);
        await using var staleReviewer = new AtoCopilotContext(options);
        var staleConflict = await staleReviewer.EmassConflicts.SingleAsync();

        // Act
        await service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(resolution, Rationale: "Committed review reason"), "first-reviewer");
        staleConflict.ConflictStatus = ConflictStatus.AcceptEmass;
        staleConflict.ResolvedAt = DateTimeOffset.UtcNow;
        staleConflict.ResolvedBy = "stale-reviewer";
        staleConflict.Notes = "Attempted overwrite";
        var staleWrite = () => staleReviewer.SaveChangesAsync();

        // Assert
        await staleWrite.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verify = new AtoCopilotContext(options);
        var saved = await verify.EmassConflicts.SingleAsync();
        saved.ConflictStatus.Should().Be(resolution);
        saved.ResolvedBy.Should().Be("first-reviewer");
        saved.Notes.Should().Be("Committed review reason");
        saved.SpinValue.Should().Be("Original");
        saved.EmassValue.Should().Be("Returned");
        var audit = await verify.AuditLogs.SingleAsync();
        JsonSerializer.Deserialize<JsonElement>(audit.Details).GetProperty("Rationale").GetString().Should().Be(saved.Notes);
        (await verify.RegisteredSystems.SingleAsync()).Name.Should().Be(resolution == ConflictStatus.AcceptEmass ? "Returned" : "Original");
        verify.Set<EmassExchangeRecord>().Should().BeEmpty();
    }
}
