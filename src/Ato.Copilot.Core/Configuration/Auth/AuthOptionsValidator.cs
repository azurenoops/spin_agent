using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Configuration.Auth;

/// <summary>
/// Validates the bound <see cref="AuthOptions"/> at startup
/// (<see cref="OptionsBuilderExtensions.ValidateOnStart{TOptions}"/>).
/// </summary>
/// <remarks>
/// Per <c>contracts/internal-services.md § 5.2</c> and
/// <c>research.md § R12</c>. The Development-vs-anything-else check
/// uses <see cref="HostEnvironmentEnvExtensions.IsDevelopment(IHostEnvironment)"/>
/// so the same convention as the rest of the codebase applies.
/// </remarks>
public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    private readonly IHostEnvironment _env;

    public AuthOptionsValidator(IHostEnvironment env)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
    }

    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        // Cookie signing key MUST be set in Production-style environments.
        // Development AND the integration-test "Testing" environment both
        // get a free pass — they boot many DbContexts/options validators
        // synchronously and a real signing key is not exercised by the
        // few endpoints currently shipped (FR-016 / Phase 3 contract).
        if (!_env.IsDevelopment() && !_env.IsEnvironment("Testing") &&
            string.IsNullOrWhiteSpace(options.Cookie.SigningKey))
        {
            errors.Add("Auth:Cookie:SigningKey is required outside Development.");
        }

        // IdleTimeoutMinutes range (FR-007).
        if (options.IdleTimeoutMinutes < 5 || options.IdleTimeoutMinutes > 480)
        {
            errors.Add("Auth:IdleTimeoutMinutes must be between 5 and 480.");
        }

        // RememberTenantCookieDays range (FR-012).
        if (options.RememberTenantCookieDays < 1 || options.RememberTenantCookieDays > 365)
        {
            errors.Add("Auth:RememberTenantCookieDays must be between 1 and 365.");
        }

        // Archive run-hour range.
        if (options.Archive.RunHourUtc < 0 || options.Archive.RunHourUtc > 23)
        {
            errors.Add("Auth:Archive:RunHourUtc must be between 0 and 23.");
        }

        // Throttle counts must be positive (FR-034).
        if (options.Throttle.Production.PerIpPerMinute <= 0)
        {
            errors.Add("Auth:Throttle:Production:PerIpPerMinute must be > 0.");
        }
        if (options.Throttle.Production.PerIdentityPerMinute <= 0)
        {
            errors.Add("Auth:Throttle:Production:PerIdentityPerMinute must be > 0.");
        }
        if (options.Throttle.Development.PerIpPerMinute <= 0)
        {
            errors.Add("Auth:Throttle:Development:PerIpPerMinute must be > 0.");
        }
        if (options.Throttle.Development.PerIdentityPerMinute <= 0)
        {
            errors.Add("Auth:Throttle:Development:PerIdentityPerMinute must be > 0.");
        }

        // Teams SSO Required mode must have a connection name (FR-021).
        if (options.TeamsSso.Mode == TeamsSsoMode.Required &&
            string.IsNullOrWhiteSpace(options.TeamsSso.ConnectionName))
        {
            errors.Add("Auth:TeamsSso:ConnectionName is required when Mode = Required.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
