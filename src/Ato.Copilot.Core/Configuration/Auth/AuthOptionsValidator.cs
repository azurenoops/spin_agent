using System.Text.Json;
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
/// <para>
/// Feature 051 T113: when <see cref="AuthTeamsSsoOptions.Mode"/> =
/// <see cref="TeamsSsoMode.Required"/>, the validator additionally reads
/// the deployed Teams app manifest (via an injectable reader delegate
/// for testability) and fails startup if
/// <c>webApplicationInfo.id</c> is missing/empty or the manifest file
/// itself is absent. See <c>contracts/m365-bot.md § 2</c>.
/// </para>
/// </remarks>
public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    private readonly IHostEnvironment _env;
    private readonly Func<string> _manifestReader;

    /// <summary>
    /// Production constructor — resolves the Teams manifest via
    /// <see cref="ResolveDefaultManifest"/> from candidate filesystem paths
    /// relative to <see cref="IHostEnvironment.ContentRootPath"/>.
    /// </summary>
    public AuthOptionsValidator(IHostEnvironment env)
        : this(env, manifestReader: null)
    {
    }

    /// <summary>
    /// Test-friendly constructor — accepts a delegate that returns the
    /// manifest JSON (or throws <see cref="FileNotFoundException"/> if no
    /// manifest file exists at any candidate path).
    /// </summary>
    public AuthOptionsValidator(IHostEnvironment env, Func<string>? manifestReader)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _manifestReader = manifestReader ?? (() => ResolveDefaultManifest(_env));
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

        // Feature 051 T113 — manifest-advertised SSO gate (FR-021 / R12).
        // Only enforced when Mode = Required; Optional/Disabled never read
        // the manifest from this validator.
        if (options.TeamsSso.Mode == TeamsSsoMode.Required)
        {
            var manifestError = ValidateTeamsManifestForRequiredMode();
            if (manifestError is not null)
            {
                errors.Add(manifestError);
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private string? ValidateTeamsManifestForRequiredMode()
    {
        string manifestJson;
        try
        {
            manifestJson = _manifestReader();
        }
        catch (FileNotFoundException ex)
        {
            return
                $"Auth:TeamsSso:Mode = Required but the Teams manifest could not be located: {ex.Message}. " +
                "Provide the deployed Teams app manifest at extensions/m365/appPackage/manifest.json " +
                "(or extensions/m365/src/manifest/manifest.json in dev) and set webApplicationInfo.id.";
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifestJson);
        }
        catch (JsonException ex)
        {
            return
                $"Auth:TeamsSso:Mode = Required but the Teams manifest is not valid JSON: {ex.Message}. " +
                "Inspect extensions/m365/appPackage/manifest.json.";
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("webApplicationInfo", out var webAppInfo) ||
                webAppInfo.ValueKind != JsonValueKind.Object ||
                !webAppInfo.TryGetProperty("id", out var idProp) ||
                idProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                return
                    "Auth:TeamsSso:Mode = Required but the Teams manifest's webApplicationInfo.id " +
                    "is missing or empty. Add the Entra app's client ID to the manifest's " +
                    "webApplicationInfo block — see contracts/m365-bot.md § 2.";
            }
        }

        return null;
    }

    /// <summary>
    /// Resolve the Teams manifest from candidate filesystem paths relative
    /// to <see cref="IHostEnvironment.ContentRootPath"/>. Tries the
    /// published <c>appPackage/manifest.json</c> first (deploy-time
    /// artifact), then the dev locations.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// Thrown when no manifest exists at any candidate path. The validator
    /// converts this into a clear startup error message.
    /// </exception>
    private static string ResolveDefaultManifest(IHostEnvironment env)
    {
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "..", "..", "extensions", "m365", "appPackage", "manifest.json"),
            Path.Combine(env.ContentRootPath, "..", "..", "extensions", "m365", "manifest", "manifest.json"),
            Path.Combine(env.ContentRootPath, "..", "..", "extensions", "m365", "src", "manifest", "manifest.json"),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }
        }

        throw new FileNotFoundException(
            $"No manifest at any of: {string.Join(", ", candidates.Select(Path.GetFullPath))}.");
    }
}
