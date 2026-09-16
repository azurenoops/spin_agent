using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Configuration;

/// <summary>
/// Validates the Microsoft Entra ID configuration before production hosts start.
/// </summary>
public sealed class AzureAdOptionsValidator : IValidateOptions<AzureAdOptions>
{
    private readonly IHostEnvironment _environment;

    public AzureAdOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public ValidateOptionsResult Validate(string? name, AzureAdOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        AddRequiredError(errors, options.Instance, "AzureAd:Instance");
        AddRequiredError(errors, options.TenantId, "AzureAd:TenantId");
        AddRequiredError(errors, options.ClientId, "AzureAd:ClientId");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void AddRequiredError(List<string> errors, string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is required outside Development and Testing.");
        }
    }
}
