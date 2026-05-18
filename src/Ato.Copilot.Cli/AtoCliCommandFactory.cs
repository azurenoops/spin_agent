using System.CommandLine;
using Ato.Copilot.Cli.Commands.Tenant;

namespace Ato.Copilot.Cli;

/// <summary>
/// T125 [FR-073..FR-076]: ato-cli root command + `tenant` sub-command tree.
/// Wires the four leaf commands (default, assign, migrate, status) per
/// <c>specs/048-tenant-isolation/contracts/ato-cli-tenant.md</c>.
/// </summary>
public static class AtoCliCommandFactory
{
    /// <summary>
    /// Build the root command tree. Exposed as a static factory so that the
    /// integration test project can invoke commands programmatically without
    /// spawning a process.
    /// </summary>
    public static RootCommand Build()
    {
        var root = new RootCommand("ato-cli — ATO Copilot administrative tooling");
        root.AddCommand(BuildTenantCommand());
        return root;
    }

    private static Command BuildTenantCommand()
    {
        var tenant = new Command("tenant",
            "Tenancy migration & operational tooling (FR-073..FR-076).");
        tenant.AddCommand(TenantDefaultCommand.Build());
        tenant.AddCommand(TenantAssignCommand.Build());
        tenant.AddCommand(TenantMigrateCommand.Build());
        tenant.AddCommand(TenantStatusCommand.Build());
        return tenant;
    }
}
