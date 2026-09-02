using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Mcp.Extensions;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Shared service-collection helpers for integration test scaffolding.
/// Centralizes the DI registration ceremony required to bring up an MCP test
/// server with strict scope validation against an InMemory database.
/// </summary>
internal static class IntegrationTestServiceExtensions
{
    /// <summary>
    /// Registers the full MCP service graph (without hosted background services)
    /// and overrides the database registration to use an InMemory provider keyed
    /// by <paramref name="dbName"/>. This is the single entry point integration
    /// tests should call to obtain a runtime-equivalent DI graph.
    /// </summary>
    /// <param name="services">The service collection to register services in.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="dbName">
    /// Name for the InMemory database. Use a unique GUID per-test to isolate
    /// state between concurrent test classes.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtoCopilotMcpForTesting(
        this IServiceCollection services,
        IConfiguration configuration,
        string dbName)
    {
        // Register full MCP graph WITHOUT hosted services (avoids background
        // workers spinning up during TestServer / WebApplicationFactory startup).
        services.AddAtoCopilotMcp(configuration, includeHostedServices: false);

        // SignalR hubs (NotificationHub, PackageHub) are consumed transitively by
        // export notifiers (Singleton). Production Program.cs registers SignalR via
        // AddSignalR() in the request-pipeline section; tests need it on the DI
        // graph for strict scope validation to pass at Build() time.
        services.AddSignalR();

        // AddAtoCopilotMcp / AddAtoCopilotCore registered SQLite/SQL Server-backed
        // IDbContextFactory<AtoCopilotContext>. Override with InMemory for tests.
        // EF Core 8+ also registers IDbContextOptionsConfiguration<TContext>;
        // leaving that descriptor in place applies BOTH UseSqlite and
        // UseInMemoryDatabase to the same provider ("dual provider" crash).
        RemoveAtoCopilotContextRegistrations(services);

        services.AddDbContextFactory<AtoCopilotContext>(
            options => options.UseInMemoryDatabase(dbName),
            ServiceLifetime.Singleton);

        // Production registers AddHealthChecks separately in Program.cs after
        // AddAtoCopilotMcp(). Some tests call MapHealthChecks("/health"), which
        // requires the HealthCheckService to be present on the DI graph.
        services.AddHealthChecks();

        return services;
    }

    /// <summary>
    /// Strips every EF Core descriptor for <see cref="AtoCopilotContext"/> so a
    /// subsequent <c>AddDbContextFactory</c> can register a different provider.
    /// EF Core 8+ keeps <see cref="IDbContextOptionsConfiguration{TContext}"/>
    /// after <c>RemoveAll&lt;IDbContextFactory&gt;</c>, which would otherwise
    /// leave both Sqlite and InMemory applied to the same service provider.
    /// </summary>
    private static void RemoveAtoCopilotContextRegistrations(IServiceCollection services)
    {
        services.RemoveAll<IDbContextFactory<AtoCopilotContext>>();
        services.RemoveAll<AtoCopilotContext>();
        services.RemoveAll<DbContextOptions<AtoCopilotContext>>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<IDbContextOptionsConfiguration<AtoCopilotContext>>();
    }
}
