using System.CommandLine;
using Ato.Copilot.Cli.Infrastructure;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Cli.Commands.Tenant;

/// <summary>
/// T129 [FR-073..FR-076]: <c>ato-cli tenant status</c>. Read-only per-table
/// tenant-coverage report. Always dry-run; exit code is 0 even when rows are
/// missing tenant assignments per <c>contracts/ato-cli-tenant.md</c>.
/// </summary>
public static class TenantStatusCommand
{
    public static Command Build()
    {
        var connOpt = new Option<string?>(
            new[] { "--connection-string" },
            "Database connection string. Falls back to ATO_DB__CONNECTION_STRING.");
        var jsonOpt = new Option<bool>(new[] { "--json" }, "Emit JSON output.");
        var verboseOpt = new Option<bool>(new[] { "--verbose" }, "Increase log verbosity.");

        var cmd = new Command("status",
            "Show the current per-table tenant-coverage report (read-only).")
        {
            connOpt, jsonOpt, verboseOpt,
        };

        cmd.SetHandler(async (string? cs, bool json, bool verbose) =>
        {
            try
            {
                var connStr = CliServiceBuilder.ResolveConnectionString(cs);
                var sp = CliServiceBuilder.Build(connStr, verbose);
                using var scope = sp.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<MultiTenantMigrationService>();
                var preview = await service.PreviewAsync();

                if (json)
                {
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(preview,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    return;
                }

                Console.WriteLine($"{"TableName",-40} {"TotalRows",10} {"RowsMissingTenant",18}");
                foreach (var t in preview.Tables)
                {
                    Console.WriteLine(
                        $"{t.TableName,-40} {t.TotalRows,10} {t.RowsMissingTenant,18}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"tenant status failed: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, connOpt, jsonOpt, verboseOpt);

        return cmd;
    }
}
