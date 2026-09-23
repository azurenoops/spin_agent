using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class OrganizationCatalogStartupTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlServer_FirstUpgrade_InitializesCatalogBeforeTenantPassAndRefreshesMissingTables(
        bool missingWorkspaceTables)
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var database = await fixture.CreateDatabaseAsync();
        var trace = new StartupTrace();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(database.Database.GetConnectionString(), sql => sql.EnableRetryOnFailure())
            .AddInterceptors(trace).Options;
        await using var db = new AtoCopilotContext(options);
        await CreatePreCatalogDatabaseAsync(db, missingWorkspaceTables);
        await using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Database:Provider"] = "SqlServer" }).Build())
            .AddSingleton<IDbContextFactory<AtoCopilotContext>>(new ContextFactory(options))
            .AddSingleton<ILogger<AtoCopilotContext>>(trace)
            .BuildServiceProvider();
        trace.Clear();

        // Act
        await InvokeStartupAsync("MigrateDatabaseAsync", services);

        // Assert
        AssertCleanUpgrade(trace);
        await AssertCatalogAndLegacyDataAsync(db);

        // Act
        trace.Clear();
        await InvokeStartupAsync("MigrateDatabaseAsync", services);

        // Assert
        AssertCleanUpgrade(trace);
        await AssertCatalogAndLegacyDataAsync(db);
    }

    [Fact]
    public async Task Sqlite_FirstUpgrade_InitializesCatalogBeforeTenantColumnPass()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var trace = new StartupTrace();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection).AddInterceptors(trace).Options);
        await CreatePreCatalogDatabaseAsync(db, missingWorkspaceTables: true);
        trace.Clear();

        // Act
        await InvokeStartupAsync("EnsureSchemaAdditionsAsync", db, trace, CancellationToken.None, null);

        // Assert
        AssertCleanUpgrade(trace);
        await AssertCatalogAndLegacyDataAsync(db);
    }

    private static async Task CreatePreCatalogDatabaseAsync(AtoCopilotContext db, bool missingWorkspaceTables)
    {
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), DisplayName = "Preserve legacy organization" });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE OrganizationCatalogAdditions;
            DROP TABLE OrganizationCatalogEntries;
            """);
        if (missingWorkspaceTables)
            await db.Database.ExecuteSqlRawAsync("""
                DROP TABLE CapabilitySetupOperations;
                DROP TABLE ProviderReleaseImpacts;
                """);
    }

    private static async Task AssertCatalogAndLegacyDataAsync(AtoCopilotContext db)
    {
        (await db.OrganizationCatalogAdditions.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.OrganizationCatalogEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.Tenants.CountAsync(x => x.DisplayName == "Preserve legacy organization")).Should().Be(1);
    }

    private static void AssertCleanUpgrade(StartupTrace trace)
    {
        trace.Failures.Should().BeEmpty("first-upgrade startup must not issue commands against missing tables");
        trace.Messages.Should().NotContain(x => x.StartsWith("Creating table [OrganizationCatalog", StringComparison.Ordinal)
            || x.StartsWith("Creating table [CapabilitySetupOperations]", StringComparison.Ordinal)
            || x.StartsWith("Creating table [ProviderReleaseImpacts]", StringComparison.Ordinal),
            "generic synchronization must observe tables already created by additive schema modules");
    }

    private static Task InvokeStartupAsync(string name, params object?[] arguments)
    {
        // Execute the actual Program startup functions, not a copied ordering of schema modules.
        var program = typeof(McpProgram).Assembly.EntryPoint!.DeclaringType!;
        var method = program.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(x => x.Name.Contains($"g__{name}|", StringComparison.Ordinal));
        return (Task)method.Invoke(null, arguments)!;
    }

    private sealed class ContextFactory(DbContextOptions<AtoCopilotContext> options)
        : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
    }

    private sealed class StartupTrace : DbCommandInterceptor, ILogger<AtoCopilotContext>
    {
        public ConcurrentQueue<string> Failures { get; } = new();
        public ConcurrentQueue<string> Messages { get; } = new();
        public void Clear() { Failures.Clear(); Messages.Clear(); }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Failures.Enqueue($"{command.CommandText}\n{eventData.Exception.Message}");
            return Task.CompletedTask;
        }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
    }
}
