using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;

namespace Ato.Copilot.Tests.Unit.Data;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SchemaAdditionsHostCollection
{
    public const string Name = "Schema additions host";
}

/// <summary>
/// Issue #868 — EnsureSchemaAdditionsAsync must fail startup on DDL error
/// across ALL providers (fail-fast-on-all-providers contract).
///
/// Design (Tony Stark artifact cb62eb1b, 2026-09-08):
///   SQLite bootstrap scripts are idempotent by design —
///   CREATE TABLE IF NOT EXISTS, PRAGMA-guarded ALTER TABLE,
///   CREATE INDEX IF NOT EXISTS — so any exception that reaches the catch
///   block is a genuine schema failure, never a benign "already applied"
///   race. Swallowing it lets the service boot on a structurally broken
///   database. The isSqlServer guard has been removed; rethrow is unconditional.
///
/// Test strategy: use the SQLite provider pointed at a path that cannot be
/// opened. The provider name passes the isSqlite check, so the DDL branch is
/// entered, the database call throws, and we assert:
///   AC1: The exception IS propagated (fail-fast on all providers).
///   AC2: LogError is called with the class name in the message.
/// </summary>
[Collection(SchemaAdditionsHostCollection.Name)]
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
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite("Data Source=/tmp/esa-test-nonexistent-dir-99999/test.db")
            .Options;
        return new AtoCopilotContext(options);
    }

    private static Mock<ILogger<AtoCopilotContext>> BuildLogger() =>
        new Mock<ILogger<AtoCopilotContext>>(MockBehavior.Loose);

    private static TestEnvironmentVariables SchemaEnvironment() => new(new Dictionary<string, string?>
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Testing",
        ["ATO_RUN_MODE"] = "http",
        ["ATO_AZUREAI__ENABLED"] = "false",
        ["ATO_Auth__BypassForTests"] = "true",
        ["ATO_Auth__Impersonation__SigningKey"] = "schema-idempotency-tests-signing-key-32-bytes!",
    });

    [Fact]
    public async Task EnsureSchemaAdditions_OnSecondSqliteStartup_DoesNotThrow()
    {
        // Arrange
        var databaseFile = Path.Combine(Path.GetTempPath(), $"schema-idempotency-{Guid.NewGuid():N}.db");
        using var environment = SchemaEnvironment();
        try
        {
            await using (var firstBoot = new SchemaAdditionsFactory(databaseFile))
            using (firstBoot.CreateClient())
            {
            }

            // Act
            var act = async () =>
            {
                await using var secondBoot = new SchemaAdditionsFactory(databaseFile);
                using var client = secondBoot.CreateClient();
            };

            // Assert
            await act.Should().NotThrowAsync();
        }
        finally
        {
            File.Delete(databaseFile);
        }
    }

    [Fact]
    public async Task HttpComposition_RegistersOneHostedFanoutWorker_AndScopedDeliveryService()
    {
        // Arrange
        var databaseFile = Path.Combine(Path.GetTempPath(), $"fanout-composition-{Guid.NewGuid():N}.db");
        using var environment = SchemaEnvironment();
        try
        {
            await using var factory = new SchemaAdditionsFactory(databaseFile);
            using var client = factory.CreateClient();
            await using var first = factory.Services.CreateAsyncScope();
            await using var second = factory.Services.CreateAsyncScope();

            // Act
            var delivery = first.ServiceProvider.GetRequiredService<Ato.Copilot.Core.Services.CspResponsibilityFanoutService>();

            // Assert
            delivery.Should().BeSameAs(first.ServiceProvider.GetRequiredService<Ato.Copilot.Core.Services.CspResponsibilityFanoutService>());
            delivery.Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<Ato.Copilot.Core.Services.CspResponsibilityFanoutService>());
            factory.FanoutRegistrations.Should().ContainSingle().Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
            using var worker = ActivatorUtilities.CreateInstance<Ato.Copilot.Core.Services.CspResponsibilityFanoutWorker>(factory.Services);
            await worker.RunOnceAsync();
        }
        finally
        {
            File.Delete(databaseFile);
        }
    }

    [Fact]
    public async Task HttpComposition_MapsStandaloneLibrariesAndReceiptHistory()
    {
        // Arrange
        var databaseFile = Path.Combine(Path.GetTempPath(), $"library-routes-{Guid.NewGuid():N}.db");
        using var environment = SchemaEnvironment();
        try
        {
            await using var factory = new SchemaAdditionsFactory(databaseFile);
            using var client = factory.CreateClient();

            // Act
            var endpoints = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>()
                .Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().ToArray();

            // Assert
            foreach (var path in new[]
            {
                "/api/narrative-library/access",
                "/api/csp/narrative-library/access",
                "/api/systems/{systemId}/narrative-library/proposals/{id:guid}/impact-receipts",
            })
            {
                var endpoint = endpoints.Should().ContainSingle(candidate => candidate.RoutePattern.RawText == path).Which;
                endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                    .Should().NotBeNull("reference scope endpoints must remain authenticated");
            }
        }
        finally
        {
            File.Delete(databaseFile);
        }
    }

    private sealed class SchemaAdditionsFactory(string databaseFile) : WebApplicationFactory<McpProgram>
    {
        public IReadOnlyList<ServiceDescriptor> FanoutRegistrations { get; private set; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Sqlite",
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={databaseFile};Mode=ReadWriteCreate",
                    ["Deployment:Mode"] = "SingleTenant",
                }));
            builder.ConfigureServices(services =>
            {
                FanoutRegistrations = services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(Ato.Copilot.Core.Services.CspResponsibilityFanoutWorker)).ToArray();
                services.RemoveAll<IHostedService>();
            });
        }
    }

    private sealed class TestEnvironmentVariables : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues;

        public TestEnvironmentVariables(IReadOnlyDictionary<string, string?> values)
        {
            _originalValues = values.Keys.ToDictionary(
                key => key,
                Environment.GetEnvironmentVariable);
            foreach (var (key, value) in values)
                Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach (var (key, value) in _originalValues)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public async Task CategorizationHistory_OnSqlite_CreatesSchemaIdempotently()
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AtoCopilotContext(options);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "Tenants" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Tenants" PRIMARY KEY
            );
            """);
        var logger = BuildLogger();

        // Act
        await CategorizationHistorySchemaAdditions.ApplyAsync(context, logger.Object);
        await CategorizationHistorySchemaAdditions.ApplyAsync(context, logger.Object);

        // Assert
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index'
              AND name = 'UX_CategorizationHistory_Tenant_System_Version';
            """;
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EmassConflicts_OnSqlite_CreatesSchemaIdempotently()
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AtoCopilotContext(options);
        var logger = BuildLogger();

        // Act
        await EmassConflictsSchemaAdditions.ApplyAsync(context, logger.Object);
        await EmassConflictsSchemaAdditions.ApplyAsync(context, logger.Object);

        // Assert
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index'
              AND name IN ('IX_EmassConflict_SystemId_Status', 'IX_EmassConflict_BatchId');
            """;
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(2);
    }

    [Fact]
    public async Task AuthorizationOverrides_OnSqlite_CreatesSchemaIdempotently()
    {
        // Arrange
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AtoCopilotContext(options);
        var logger = BuildLogger();

        // Act
        await AuthorizationOverridesSchemaAdditions.ApplyAsync(context, logger.Object);
        await AuthorizationOverridesSchemaAdditions.ApplyAsync(context, logger.Object);

        // Assert
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index'
              AND name IN ('IX_AuthorizationOverride_DecisionId_ExpirationDate', 'IX_AuthorizationOverrides_TenantId');
            """;
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(2);
    }

    // ─── TenantsAndOrganizationsSchemaAdditions ───────────────────────────────

    /// <summary>
    /// AC1: DDL failure propagates as InvalidOperationException (fail-fast on all providers).
    /// AC2: LogError is called with the class name in the message.
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

        // Assert AC1 — fail-fast: exception propagates on all providers
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TenantsAndOrganizationsSchemaAdditions*");

        // Assert AC2 — failure is visible in logs
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
