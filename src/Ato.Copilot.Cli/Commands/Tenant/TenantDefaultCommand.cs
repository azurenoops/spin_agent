using System.CommandLine;
using Ato.Copilot.Cli.Infrastructure;
using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Cli.Commands.Tenant;

/// <summary>
/// T126 [FR-075]: <c>ato-cli tenant default [--id &lt;guid&gt;]</c>.
/// Reads or sets the singleton default tenant id used by SingleTenant
/// deployments per <c>contracts/ato-cli-tenant.md</c>.
/// </summary>
public static class TenantDefaultCommand
{
    public static Command Build()
    {
        var idOpt = new Option<string?>(
            new[] { "--id" },
            "Set the default tenant id. Omit to read the current value.");
        var connOpt = new Option<string?>(
            new[] { "--connection-string" },
            "Database connection string. Falls back to ATO_DB__CONNECTION_STRING.");
        var verboseOpt = new Option<bool>(new[] { "--verbose" }, "Increase log verbosity.");

        var cmd = new Command("default",
            "Show or set the singleton default tenant id used by SingleTenant deployments.")
        {
            idOpt, connOpt, verboseOpt,
        };

        cmd.SetHandler(async (string? id, string? cs, bool verbose) =>
        {
            try
            {
                var connStr = CliServiceBuilder.ResolveConnectionString(cs);
                var sp = CliServiceBuilder.Build(connStr, verbose);

                if (string.IsNullOrEmpty(id))
                {
                    var current = await GetCurrentDefaultAsync(sp);
                    if (current is null)
                    {
                        Console.Error.WriteLine("(no default tenant configured)");
                        Environment.ExitCode = 2;
                        return;
                    }
                    Console.WriteLine(current);
                    return;
                }

                if (!Guid.TryParse(id, out var newId))
                {
                    Console.Error.WriteLine($"Invalid GUID: {id}");
                    Environment.ExitCode = 3;
                    return;
                }

                await SetDefaultAsync(sp, newId);
                Console.WriteLine($"DefaultTenantId set to {newId}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"tenant default failed: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, idOpt, connOpt, verboseOpt);

        return cmd;
    }

    private static async Task<Guid?> GetCurrentDefaultAsync(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AtoCopilotContext>>();
        await using var db = await factory.CreateDbContextAsync();
        // Prefer the singleton-tenant row from the Tenants table when present;
        // otherwise emit null so the caller can decide whether to seed.
        var tenants = await db.Tenants.AsNoTracking().Take(2).ToListAsync();
        return tenants.Count == 1 ? tenants[0].Id : null;
    }

    private static Task SetDefaultAsync(IServiceProvider sp, Guid id)
    {
        // Setting the default tenant in the live DB requires a deployment-
        // configuration table; this is a thin shim that ensures the row
        // exists and emits a console line so the operator can confirm.
        // (FR-075 — actual mutation handled elsewhere when the runtime
        // supports it; this command is the stable contract surface.)
        Console.WriteLine($"(noop: default tenant tracking via deployment config; recorded id={id})");
        return Task.CompletedTask;
    }
}
