using System.CommandLine;
using System.Text.Json;
using Ato.Copilot.Cli.Infrastructure;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Cli.Commands.Tenant;

/// <summary>
/// T128 [FR-073..FR-076]: <c>ato-cli tenant migrate</c>. Wraps
/// <see cref="MultiTenantMigrationService.ExecuteAsync"/> as the air-gapped
/// equivalent of the <c>POST /api/admin/migrate-to-multitenant</c> endpoint.
/// </summary>
public static class TenantMigrateCommand
{
    public static Command Build()
    {
        var connOpt = new Option<string?>(
            new[] { "--connection-string" },
            "Database connection string. Falls back to ATO_DB__CONNECTION_STRING.");
        var defaultOpt = new Option<string>(
            new[] { "--default-tenant-id" },
            "Default tenant id for rows not matched by any CSV entry.")
        { IsRequired = true };
        var csvOpt = new Option<string?>(
            new[] { "--csv" },
            "Optional CSV mapping file (TableName,RowIdPrefix,TenantId).");
        var rlsOpt = new Option<bool>(
            new[] { "--install-rls" },
            getDefaultValue: () => true,
            "Install SQL Server Row-Level Security policies after backfill.");
        var reportOpt = new Option<string?>(
            new[] { "--report-out" },
            "Path to write the JSON migration report. Default: stdout.");
        var verboseOpt = new Option<bool>(new[] { "--verbose" }, "Increase log verbosity.");

        var cmd = new Command("migrate",
            "Run the full migration: backfill TenantId columns + install RLS policies.")
        {
            connOpt, defaultOpt, csvOpt, rlsOpt, reportOpt, verboseOpt,
        };

        cmd.SetHandler(async (string? cs, string defaultId, string? csv, bool rls, string? reportOut, bool verbose) =>
        {
            try
            {
                if (!Guid.TryParse(defaultId, out var defaultTenantId))
                {
                    Console.Error.WriteLine($"Invalid --default-tenant-id: {defaultId}");
                    Environment.ExitCode = 3;
                    return;
                }

                List<MultiTenantMigrationService.TenantOverride>? overrides = null;
                if (!string.IsNullOrEmpty(csv))
                {
                    if (!File.Exists(csv))
                    {
                        Console.Error.WriteLine($"CSV not found: {csv}");
                        Environment.ExitCode = 5;
                        return;
                    }
                    try
                    {
                        overrides = TenantAssignCsvLoader.Read(csv);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"CSV parse error: {ex.Message}");
                        Environment.ExitCode = 5;
                        return;
                    }
                }

                var connStr = CliServiceBuilder.ResolveConnectionString(cs);
                var sp = CliServiceBuilder.Build(connStr, verbose);
                using var scope = sp.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<MultiTenantMigrationService>();
                var correlationId = Guid.NewGuid().ToString("D");
                Console.WriteLine($"correlationId={correlationId}");

                var report = await service.ExecuteAsync(
                    defaultTenantId,
                    overrides,
                    installRls: rls,
                    actorOid: $"cli:{Environment.UserName}@{Environment.MachineName}",
                    correlationId: correlationId);

                var json = JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true });
                if (!string.IsNullOrEmpty(reportOut))
                {
                    await File.WriteAllTextAsync(reportOut, json);
                }
                else
                {
                    Console.WriteLine(json);
                }

                if (!string.IsNullOrEmpty(report.Error))
                {
                    Environment.ExitCode = 7;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"tenant migrate failed: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, connOpt, defaultOpt, csvOpt, rlsOpt, reportOpt, verboseOpt);

        return cmd;
    }
}

/// <summary>Shared CSV loader used by both <c>assign</c> and <c>migrate</c>.</summary>
internal static class TenantAssignCsvLoader
{
    public static List<MultiTenantMigrationService.TenantOverride> Read(string path)
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
