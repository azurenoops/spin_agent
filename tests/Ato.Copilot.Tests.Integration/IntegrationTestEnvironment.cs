using System.Runtime.CompilerServices;

namespace Ato.Copilot.Tests.Integration;

/// <summary>
/// Process-wide defaults that must be in place before any
/// <c>WebApplicationFactory&lt;McpProgram&gt;</c> boots <c>Program.Main</c>.
/// Factory <c>ConfigureWebHost</c> in-memory configuration is too late for
/// <c>ValidateAzureAiEndpointConfig</c>, which runs during host construction.
/// </summary>
internal static class IntegrationTestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // appsettings.json ships AzureAi:Enabled=true with an empty Endpoint.
        // CI does not provide ATO_AZUREAI__ENDPOINT. Disable AI for the whole
        // integration-test process so Program.Main can build an IHost.
        Environment.SetEnvironmentVariable("ATO_AZUREAI__ENABLED", "false");
    }
}
