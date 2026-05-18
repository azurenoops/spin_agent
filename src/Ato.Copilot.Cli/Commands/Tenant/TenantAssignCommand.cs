using System.CommandLine;
using Ato.Copilot.Cli.Infrastructure;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Cli.Commands.Tenant;

/// <summary>
/// T127 [FR-074]: <c>ato-cli tenant assign --csv &lt;path&gt;</c>. Loads the
/// CSV mapping file (<c>TableName,RowIdPrefix,TenantId</c>) and applies each
/// row as a <see cref="MultiTenantMigrationService.TenantOverride"/> via
/// <see cref="MultiTenantMigrationService.ExecuteAsync"/>.
/// </summary>
public static class TenantAssignCommand
{
    public static Command Build()
    {
        var csvOpt = new Option<string>(
            new[] { "--csv" },
            "Path to a CSV file with columns: TableName,RowIdPrefix,TenantId.")
        { IsRequired = true };
        var defaultOpt = new Option<string?>(
            new[] { "--default-tenant-id" },
            "Default tenant id for rows not matched by any CSV entry.");
        var connOpt = new Option<string?>(
            new[] { "--connection-string" },
            "Database connection string. Falls back to ATO_DB__CONNECTION_STRING.");
        var verboseOpt = new Option<bool>(new[] { "--verbose" }, "Increase log verbosity.");

        var cmd = new Command("assign",
            "Assign rows to tenants based on a CSV mapping.")
        {
            csvOpt, defaultOpt, connOpt, verboseOpt,
        };

        cmd.SetHandler(async (string csv, string? defaultId, string? cs, bool verbose) =>
        {
            try
            {
                if (!File.Exists(csv))
                {
                    Console.Error.WriteLine($"CSV not found: {csv}");
                    Environment.ExitCode = 5;
                    return;
                }

                List<MultiTenantMigrationService.TenantOverride> overrides;
                try
                {
                    overrides = ReadCsv(csv);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"CSV parse error: {ex.Message}");
                    Environment.ExitCode = 5;
                    return;
                }

                Guid? defaultTenant = null;
                if (!string.IsNullOrEmpty(defaultId))
                {
                    if (!Guid.TryParse(defaultId, out var g))
                    {
                        Console.Error.WriteLine($"Invalid --default-tenant-id: {defaultId}");
                        Environment.ExitCode = 3;
                        return;
                    }
                    defaultTenant = g;
                }
                if (defaultTenant is null)
                {
                    Console.Error.WriteLine(
                        "--default-tenant-id is required when one or more rows would otherwise be unmatched.");
                    Environment.ExitCode = 4;
                    return;
                }

                var connStr = CliServiceBuilder.ResolveConnectionString(cs);
                var sp = CliServiceBuilder.Build(connStr, verbose);
                using var scope = sp.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<MultiTenantMigrationService>();
                var report = await service.ExecuteAsync(
                    defaultTenant.Value,
                    overrides,
                    installRls: false,
                    actorOid: $"cli:{Environment.UserName}@{Environment.MachineName}",
                    correlationId: Guid.NewGuid().ToString("D"));

                if (!string.IsNullOrEmpty(report.Error))
                {
                    Console.Error.WriteLine($"assign failed: {report.Error}");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine($"Assigned across {report.Tables.Count} table(s).");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"tenant assign failed: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, csvOpt, defaultOpt, connOpt, verboseOpt);

        return cmd;
    }

    /// <summary>
    /// Tiny CSV parser that handles the simple
    /// <c>TableName,RowIdPrefix,TenantId</c> shape mandated by
    /// <c>contracts/ato-cli-tenant.md</c>. Comments (<c>#</c>) and blank
    /// lines are skipped; a header row is auto-detected.
    /// </summary>
    private static List<MultiTenantMigrationService.TenantOverride> ReadCsv(string path)
    {
        var rows = new List<MultiTenantMigrationService.TenantOverride>();
        var first = true;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length < 3)
            {
                throw new FormatException($"Expected 3 columns, got {parts.Length} in line: {raw}");
            }
            // Header row detection: TenantId column non-GUID.
            if (first && !Guid.TryParse(parts[2].Trim(), out _))
            {
                first = false;
                continue;
            }
            first = false;
            if (!Guid.TryParse(parts[2].Trim(), out var g))
            {
                throw new FormatException($"Invalid TenantId GUID: {parts[2]}");
            }
            var prefix = parts[1].Trim();
            rows.Add(new MultiTenantMigrationService.TenantOverride(
                parts[0].Trim(),
                string.IsNullOrEmpty(prefix) ? null : prefix,
                g));
        }
        return rows;
    }
}
