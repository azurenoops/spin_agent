using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.State.Abstractions;

namespace Ato.Copilot.Agents.Configuration.Tools;

/// <summary>
/// MCP tool for managing Security Posture Intelligence Navigator configuration settings.
/// Supports sub-actions: get_configuration, set_subscription, set_framework,
/// set_baseline, and set_preference.
/// Settings are persisted in IAgentStateManager shared state with "config:" key prefix.
/// Thread-safe via SemaphoreSlim for multi-step read-modify-write operations.
/// </summary>
public class ConfigurationTool : BaseTool
{
    /// <summary>Agent ID used for IAgentStateManager key scoping.</summary>
    private const string AgentId = "configuration";

    /// <summary>State key for the complete ConfigurationSettings object.</summary>
    private const string SettingsKey = "config:settings";

    /// <summary>State key for quick-access subscription ID.</summary>
    private const string SubscriptionKey = "config:subscriptionId";

    /// <summary>State key for quick-access framework.</summary>
    private const string FrameworkKey = "config:framework";

    /// <summary>State key for quick-access baseline.</summary>
    private const string BaselineKey = "config:baseline";

    /// <summary>Semaphore for thread-safe read-modify-write on configuration state.</summary>
    private static readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>Valid preference names and their allowed values.</summary>
    private static readonly Dictionary<string, HashSet<string>> ValidPreferences = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dryRunDefault"] = new(StringComparer.OrdinalIgnoreCase) { "true", "false" },
        ["defaultScanType"] = new(StringComparer.OrdinalIgnoreCase) { "resource", "policy", "combined" },
        ["cloudEnvironment"] = new(StringComparer.OrdinalIgnoreCase) { "AzureGovernment", "AzureCloud" },
        ["region"] = new(StringComparer.OrdinalIgnoreCase) { } // Any string allowed
    };

    private readonly IAgentStateManager _stateManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationTool"/> class.
    /// </summary>
    /// <param name="stateManager">Agent state manager for persisting configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public ConfigurationTool(IAgentStateManager stateManager, ILogger<ConfigurationTool> logger)
        : base(logger)
    {
        _stateManager = stateManager;
    }

    /// <inheritdoc />
    public override string Name => "configuration_manage";

    /// <inheritdoc />
    public override string Description =>
        "Manage Security Posture Intelligence Navigator settings: subscription, framework, baseline, environment, and preferences";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["action"] = new()
        {
            Name = "action",
            Description = "Configuration action: get_configuration, set_subscription, set_framework, set_baseline, set_preference",
            Type = "string",
            Required = true
        },
        ["subscriptionId"] = new()
        {
            Name = "subscriptionId",
            Description = "Azure subscription ID (for set_subscription action)",
            Type = "string"
        },
        ["framework"] = new()
        {
            Name = "framework",
            Description = "Compliance framework: NIST80053, FedRAMPHigh, FedRAMPModerate, DoDIL5 (for set_framework action)",
            Type = "string"
        },
        ["baseline"] = new()
        {
            Name = "baseline",
            Description = "Baseline level: High, Moderate, Low (for set_baseline action)",
            Type = "string"
        },
        ["preferenceName"] = new()
        {
            Name = "preferenceName",
            Description = "Preference name: dryRunDefault, defaultScanType, cloudEnvironment, region (for set_preference action)",
            Type = "string"
        },
        ["preferenceValue"] = new()
        {
            Name = "preferenceValue",
            Description = "Preference value (for set_preference action)",
            Type = "string"
        }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var action = GetArg<string>(arguments, "action")?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(action))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.MissingRequiredParam,
                "The 'action' parameter is required",
                "Specify an action: get_configuration, set_subscription, set_framework, set_baseline, set_preference",
                Name, sw.ElapsedMilliseconds));
        }

        try
        {
            return action switch
            {
                "get_configuration" => await HandleGetConfigurationAsync(sw, cancellationToken),
                "set_subscription" => await HandleSetSubscriptionAsync(arguments, sw, cancellationToken),
                "set_framework" => await HandleSetFrameworkAsync(arguments, sw, cancellationToken),
                "set_baseline" => await HandleSetBaselineAsync(arguments, sw, cancellationToken),
                "set_preference" => await HandleSetPreferenceAsync(arguments, sw, cancellationToken),
                _ => SerializeResponse(ToolResponse<object>.Fail(
                    ErrorCodes.MissingRequiredParam,
                    $"Unknown action '{action}'",
                    "Valid actions: get_configuration, set_subscription, set_framework, set_baseline, set_preference",
                    Name, sw.ElapsedMilliseconds))
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Configuration tool error for action {Action}", action);
            return SerializeResponse(ToolResponse<object>.Fail(
                "CONFIGURATION_ERROR",
                $"An error occurred: {ex.Message}",
                "Try the operation again. If the problem persists, check the logs.",
                Name, sw.ElapsedMilliseconds));
        }
    }

    /// <summary>
    /// Handles the get_configuration action by returning all current settings.
    /// </summary>
    private async Task<string> HandleGetConfigurationAsync(Stopwatch sw, CancellationToken ct)
    {
        var settings = await GetOrCreateSettingsAsync(ct);

        Logger.LogInformation("Configuration retrieved | Sub: {Sub} | Framework: {Framework}",
            settings.SubscriptionId ?? "(not set)", settings.Framework);

        return SerializeResponse(ToolResponse<ConfigurationSettings>.Success(settings, Name, sw.ElapsedMilliseconds));
    }

    /// <summary>
    /// Handles the set_subscription action with GUID validation.
    /// </summary>
    private async Task<string> HandleSetSubscriptionAsync(
        Dictionary<string, object?> args, Stopwatch sw, CancellationToken ct)
    {
        var subscriptionId = GetArg<string>(args, "subscriptionId")?.Trim();

        if (string.IsNullOrEmpty(subscriptionId))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.MissingRequiredParam,
                "The 'subscriptionId' parameter is required for set_subscription",
                "Provide a valid GUID-format subscription ID (e.g., 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx')",
                Name, sw.ElapsedMilliseconds));
        }

        if (!Guid.TryParse(subscriptionId, out _))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.InvalidSubscriptionId,
                $"Invalid subscription ID format: '{subscriptionId}'",
                "Provide a valid GUID-format subscription ID (e.g., 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx')",
                Name, sw.ElapsedMilliseconds));
        }

        await _stateLock.WaitAsync(ct);
        try
        {
            var settings = await GetOrCreateSettingsAsync(ct);
            var previousValue = settings.SubscriptionId;

            settings.SubscriptionId = subscriptionId;
            settings.LastUpdated = DateTime.UtcNow;

            await PersistSettingsAsync(settings, ct);

            Logger.LogInformation("Subscription set to {SubscriptionId} (was {Previous})",
                subscriptionId, previousValue ?? "(not set)");

            var result = new { message = $"Default subscription set to {subscriptionId}", subscriptionId, previousValue };
            return SerializeResponse(ToolResponse<object>.Success(result, Name, sw.ElapsedMilliseconds));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Handles the set_framework action with framework validation.
    /// </summary>
    private async Task<string> HandleSetFrameworkAsync(
        Dictionary<string, object?> args, Stopwatch sw, CancellationToken ct)
    {
        var framework = GetArg<string>(args, "framework")?.Trim();

        if (string.IsNullOrEmpty(framework))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.MissingRequiredParam,
                "The 'framework' parameter is required for set_framework",
                "Use one of: NIST80053, FedRAMPHigh, FedRAMPModerate, DoDIL5",
                Name, sw.ElapsedMilliseconds));
        }

        var normalized = ComplianceFrameworks.Normalize(framework);
        if (normalized == null)
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.InvalidFramework,
                $"Invalid framework: '{framework}'",
                "Use one of: NIST80053, FedRAMPHigh, FedRAMPModerate, DoDIL5",
                Name, sw.ElapsedMilliseconds));
        }

        await _stateLock.WaitAsync(ct);
        try
        {
            var settings = await GetOrCreateSettingsAsync(ct);
            var previousValue = settings.Framework;

            settings.Framework = normalized;
            settings.LastUpdated = DateTime.UtcNow;

            await PersistSettingsAsync(settings, ct);

            var displayName = ComplianceFrameworks.DisplayNames.GetValueOrDefault(normalized, normalized);
            Logger.LogInformation("Framework set to {Framework} (was {Previous})", normalized, previousValue);

            var result = new { message = $"Default framework set to {displayName}", framework = normalized, previousValue };
            return SerializeResponse(ToolResponse<object>.Success(result, Name, sw.ElapsedMilliseconds));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Handles the set_baseline action with baseline validation.
    /// </summary>
    private async Task<string> HandleSetBaselineAsync(
        Dictionary<string, object?> args, Stopwatch sw, CancellationToken ct)
    {
        var baseline = GetArg<string>(args, "baseline")?.Trim();

        if (string.IsNullOrEmpty(baseline))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.MissingRequiredParam,
                "The 'baseline' parameter is required for set_baseline",
                "Use one of: High, Moderate, Low",
                Name, sw.ElapsedMilliseconds));
        }

        if (!ComplianceFrameworks.IsValidBaseline(baseline))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.InvalidBaseline,
                $"Invalid baseline: '{baseline}'",
                "Use one of: High, Moderate, Low",
                Name, sw.ElapsedMilliseconds));
        }

        // Normalize to canonical case
        var normalizedBaseline = ComplianceFrameworks.ValidBaselines
            .First(b => string.Equals(b, baseline, StringComparison.OrdinalIgnoreCase));

        await _stateLock.WaitAsync(ct);
        try
        {
            var settings = await GetOrCreateSettingsAsync(ct);
            var previousValue = settings.Baseline;

            settings.Baseline = normalizedBaseline;
            settings.LastUpdated = DateTime.UtcNow;

            await PersistSettingsAsync(settings, ct);

            Logger.LogInformation("Baseline set to {Baseline} (was {Previous})", normalizedBaseline, previousValue);

            var result = new { message = $"Default baseline set to {normalizedBaseline}", baseline = normalizedBaseline, previousValue };
            return SerializeResponse(ToolResponse<object>.Success(result, Name, sw.ElapsedMilliseconds));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Handles the set_preference action with preference name and value validation.
    /// </summary>
    private async Task<string> HandleSetPreferenceAsync(
        Dictionary<string, object?> args, Stopwatch sw, CancellationToken ct)
    {
        var preferenceName = GetArg<string>(args, "preferenceName")?.Trim();
        var preferenceValue = GetArg<string>(args, "preferenceValue")?.Trim();

        if (string.IsNullOrEmpty(preferenceName))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.MissingRequiredParam,
                "The 'preferenceName' parameter is required for set_preference",
                "Valid preferences: dryRunDefault, defaultScanType, cloudEnvironment, region",
                Name, sw.ElapsedMilliseconds));
        }

        if (string.IsNullOrEmpty(preferenceValue))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.MissingRequiredParam,
                $"The 'preferenceValue' parameter is required for set_preference",
                $"Provide a value for '{preferenceName}'",
                Name, sw.ElapsedMilliseconds));
        }

        if (!ValidPreferences.ContainsKey(preferenceName))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.InvalidPreferenceName,
                $"Unknown preference: '{preferenceName}'",
                "Valid preferences: dryRunDefault, defaultScanType, cloudEnvironment, region",
                Name, sw.ElapsedMilliseconds));
        }

        // Validate value (region allows any string)
        var allowedValues = ValidPreferences[preferenceName];
        if (allowedValues.Count > 0 && !allowedValues.Contains(preferenceValue))
        {
            return SerializeResponse(ToolResponse<object>.Fail(
                ErrorCodes.InvalidPreferenceValue,
                $"Invalid value '{preferenceValue}' for preference '{preferenceName}'",
                $"Valid values: {string.Join(", ", allowedValues)}",
                Name, sw.ElapsedMilliseconds));
        }

        await _stateLock.WaitAsync(ct);
        try
        {
            var settings = await GetOrCreateSettingsAsync(ct);
            var previousValue = GetPreferenceValue(settings, preferenceName);

            ApplyPreference(settings, preferenceName, preferenceValue);
            settings.LastUpdated = DateTime.UtcNow;

            await PersistSettingsAsync(settings, ct);

            // Normalize display name for the message
            var canonicalName = ValidPreferences.Keys
                .First(k => string.Equals(k, preferenceName, StringComparison.OrdinalIgnoreCase));

            Logger.LogInformation("Preference {Name} set to {Value} (was {Previous})",
                canonicalName, preferenceValue, previousValue);

            var result = new
            {
                message = $"{canonicalName} set to {preferenceValue}",
                preferenceName = canonicalName,
                preferenceValue,
                previousValue
            };
            return SerializeResponse(ToolResponse<object>.Success(result, Name, sw.ElapsedMilliseconds));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Retrieves existing settings from state or creates defaults.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current or default ConfigurationSettings.</returns>
    private async Task<ConfigurationSettings> GetOrCreateSettingsAsync(CancellationToken ct)
    {
        var settings = await _stateManager.GetStateAsync<ConfigurationSettings>(AgentId, SettingsKey, ct);
        return settings ?? new ConfigurationSettings();
    }

    /// <summary>
    /// Persists settings to state manager (both the full object and quick-access keys).
    /// </summary>
    /// <param name="settings">Settings to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task PersistSettingsAsync(ConfigurationSettings settings, CancellationToken ct)
    {
        await _stateManager.SetStateAsync(AgentId, SettingsKey, settings, ct);

        // Also set quick-access keys for Compliance Agent consumption
        if (settings.SubscriptionId != null)
            await _stateManager.SetStateAsync(AgentId, SubscriptionKey, settings.SubscriptionId, ct);
        await _stateManager.SetStateAsync(AgentId, FrameworkKey, settings.Framework, ct);
        await _stateManager.SetStateAsync(AgentId, BaselineKey, settings.Baseline, ct);
    }

    /// <summary>
    /// Gets the current value of a preference from settings.
    /// </summary>
    /// <param name="settings">Current settings.</param>
    /// <param name="preferenceName">Preference name (case-insensitive).</param>
    /// <returns>Current value as string.</returns>
    private static string GetPreferenceValue(ConfigurationSettings settings, string preferenceName)
    {
        return preferenceName.ToLowerInvariant() switch
        {
            "dryrundefault" => settings.DryRunDefault.ToString(),
            "defaultscantype" => settings.DefaultScanType,
            "cloudenvironment" => settings.CloudEnvironment,
            "region" => settings.Region,
            _ => string.Empty
        };
    }

    /// <summary>
    /// Applies a preference value to the settings object.
    /// </summary>
    /// <param name="settings">Settings to modify.</param>
    /// <param name="preferenceName">Preference name (case-insensitive).</param>
    /// <param name="value">Value to set.</param>
    private static void ApplyPreference(ConfigurationSettings settings, string preferenceName, string value)
    {
        switch (preferenceName.ToLowerInvariant())
        {
            case "dryrundefault":
                settings.DryRunDefault = bool.Parse(value);
                break;
            case "defaultscantype":
                settings.DefaultScanType = value.ToLowerInvariant();
                break;
            case "cloudenvironment":
                // Normalize to canonical case
                settings.CloudEnvironment = value.Equals("AzureCloud", StringComparison.OrdinalIgnoreCase)
                    ? "AzureCloud" : "AzureGovernment";
                break;
            case "region":
                settings.Region = value.ToLowerInvariant();
                break;
        }
    }

    /// <summary>
    /// Serializes a ToolResponse to JSON string for MCP protocol.
    /// </summary>
    /// <typeparam name="T">Response data type.</typeparam>
    /// <param name="response">Response to serialize.</param>
    /// <returns>JSON string.</returns>
    private static string SerializeResponse<T>(ToolResponse<T> response)
    {
        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }
}
