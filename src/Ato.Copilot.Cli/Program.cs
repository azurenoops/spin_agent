// Ato Copilot administrative CLI (ato-cli)
//
// Tenancy/operations tooling. Sub-commands are built in
// AtoCliCommandFactory so that the integration test project can invoke them
// programmatically without spawning a process. See
// specs/048-tenant-isolation/contracts/ato-cli-tenant.md.

using Ato.Copilot.Cli;
using System.CommandLine;

return await AtoCliCommandFactory.Build().InvokeAsync(args);

