using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Cli.Infrastructure;

/// <summary>
/// T125: minimal service-provider bootstrap shared by every <c>tenant</c>
/// sub-command. Builds an <see cref="IDbContextFactory{TContext}"/> targeting
/// the supplied connection string (SQLite if <c>--connection-string</c> ends
/// in <c>.db</c>, SQL Server otherwise) plus the
/// <see cref="MultiTenantMigrationService"/> shared with the in-process
/// admin endpoint.
/// </summary>
public static class CliServiceBuilder
{
    /// <summary>
    /// Build a one-shot service provider for a single CLI invocation.
    /// </summary>
    public static IServiceProvider Build(string connectionString, bool verbose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddSimpleConsole(o => o.SingleLine = true);
            b.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        var isSqlite = connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase)
                    || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)
                       && !connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
                       && !connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase);

        services.AddDbContextFactory<AtoCopilotContext>(opts =>
        {
            if (isSqlite)
            {
                opts.UseSqlite(connectionString);
            }
            else
            {
                opts.UseSqlServer(connectionString);
            }
        });

        services.AddScoped<MultiTenantMigrationService>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Resolve the connection string from the option or the
    /// <c>ATO_DB__CONNECTION_STRING</c> environment variable per
    /// the CLI contract.
    /// </summary>
    public static string ResolveConnectionString(string? optionValue) =>
        !string.IsNullOrWhiteSpace(optionValue)
            ? optionValue
            : Environment.GetEnvironmentVariable("ATO_DB__CONNECTION_STRING")
              ?? throw new InvalidOperationException(
                  "--connection-string was not provided and ATO_DB__CONNECTION_STRING is not set.");
}
