using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Configuration;

/// <summary>
/// Validates <see cref="AzureAiOptions"/> at startup.
/// Prevents the application from starting with a missing or placeholder AI endpoint
/// in non-Development environments (fix for issue #656 — hardcoded endpoint removed
/// from appsettings.json; operators must supply ATO_AZUREAI__ENDPOINT via environment
/// variable, Key Vault reference, or appsettings.Production.json).
/// </summary>
public sealed class AzureAiOptionsValidator : IValidateOptions<AzureAiOptions>
{
    private readonly IHostEnvironment _env;

    public AzureAiOptionsValidator(IHostEnvironment env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public ValidateOptionsResult Validate(string? name, AzureAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Only validate when AI is enabled — if Enabled=false the endpoint is irrelevant.
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();

        // Endpoint is required in all non-Development, non-Testing environments.
        // Operators must supply ATO_AZUREAI__ENDPOINT (env var) or equivalent.
        if (!_env.IsDevelopment() && !_env.IsEnvironment("Testing") &&
            string.IsNullOrWhiteSpace(options.Endpoint))
        {
            errors.Add(
                "AzureAi:Endpoint is required outside Development. " +
                "Set the ATO_AZUREAI__ENDPOINT environment variable (or equivalent Key Vault reference) " +
                "to the Azure OpenAI service URL (e.g. https://my-service.openai.azure.us/). " +
                "The endpoint has been intentionally removed from appsettings.json to prevent " +
                "accidental production credential leakage (issue #656).");
        }

        // Foundry provider requires a project endpoint.
        if (options.Provider == AiProvider.Foundry &&
            !_env.IsDevelopment() && !_env.IsEnvironment("Testing") &&
            string.IsNullOrWhiteSpace(options.FoundryProjectEndpoint))
        {
            errors.Add(
                "AzureAi:FoundryProjectEndpoint is required when Provider = Foundry outside Development. " +
                "Set ATO_AZUREAI__FOUNDRYPROJECTENDPOINT.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
