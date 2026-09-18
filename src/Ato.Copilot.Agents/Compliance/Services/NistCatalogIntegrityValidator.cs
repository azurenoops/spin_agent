using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Validates NIST SP 800-53 catalog structure and the pinned release baseline.</summary>
public sealed class NistCatalogIntegrityValidator
{
    internal const string ExpectedOscalVersion = "1.1.3";
    internal const string ExpectedCatalogVersion = "5.2.0";
    internal const int ExpectedGroupCount = 20;
    internal const int ExpectedBaseControlCount = 324;
    internal const int ExpectedEnhancementCount = 872;

    private const string SchemaResourceSuffix = "oscal_catalog_schema.json";
    private const int MaximumSchemaViolations = 20;

    private static readonly Lazy<JsonSchema> CatalogSchema = new(LoadSchema);
    private readonly ILogger<NistCatalogIntegrityValidator> _logger;

    public NistCatalogIntegrityResult? LastResult { get; private set; }

    public NistCatalogIntegrityValidator(ILogger<NistCatalogIntegrityValidator> logger)
    {
        _logger = logger;
    }

    public NistCatalogIntegrityResult Validate(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var violations = new List<string>();
        var schemaResult = CatalogSchema.Value.Evaluate(
            JsonNode.Parse(document.RootElement.GetRawText()),
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (!schemaResult.IsValid)
        {
            CollectSchemaViolations(schemaResult, violations);
            if (violations.Count == 0)
            {
                violations.Add("OSCAL catalog schema validation failed.");
            }
        }

        var catalog = TryGetObject(document.RootElement, "catalog");
        var metadata = catalog is { } catalogElement
            ? TryGetObject(catalogElement, "metadata")
            : null;
        var oscalVersion = GetString(metadata, "oscal-version");
        var catalogVersion = GetString(metadata, "version");

        var groupCount = 0;
        var baseControlCount = 0;
        var enhancementCount = 0;
        if (catalog is { } value
            && value.TryGetProperty("groups", out var groups)
            && groups.ValueKind == JsonValueKind.Array)
        {
            groupCount = groups.GetArrayLength();
            foreach (var group in groups.EnumerateArray())
            {
                if (!group.TryGetProperty("controls", out var controls)
                    || controls.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                baseControlCount += controls.GetArrayLength();
                foreach (var control in controls.EnumerateArray())
                {
                    enhancementCount += CountNestedControls(control);
                }
            }
        }

        AddBaselineViolation(
            violations,
            string.Equals(oscalVersion, ExpectedOscalVersion, StringComparison.Ordinal),
            $"Expected OSCAL version {ExpectedOscalVersion}, found '{oscalVersion}'.");
        AddBaselineViolation(
            violations,
            string.Equals(catalogVersion, ExpectedCatalogVersion, StringComparison.Ordinal),
            $"Expected catalog version {ExpectedCatalogVersion}, found '{catalogVersion}'.");
        AddBaselineViolation(
            violations,
            groupCount == ExpectedGroupCount,
            $"Expected {ExpectedGroupCount} control families, found {groupCount}.");
        AddBaselineViolation(
            violations,
            baseControlCount == ExpectedBaseControlCount,
            $"Expected {ExpectedBaseControlCount} base controls, found {baseControlCount}.");
        AddBaselineViolation(
            violations,
            enhancementCount == ExpectedEnhancementCount,
            $"Expected {ExpectedEnhancementCount} control enhancements, found {enhancementCount}.");

        var result = new NistCatalogIntegrityResult
        {
            SchemaValid = schemaResult.IsValid,
            OscalVersion = oscalVersion,
            CatalogVersion = catalogVersion,
            GroupCount = groupCount,
            BaseControlCount = baseControlCount,
            EnhancementCount = enhancementCount,
            Violations = violations
        };
        LastResult = result;

        if (result.IsValid)
        {
            _logger.LogInformation(
                "NIST catalog integrity validation passed: OSCAL {OscalVersion}, catalog {CatalogVersion}, {GroupCount} families, {BaseControlCount} base controls, {EnhancementCount} enhancements, {TotalControlCount} total controls",
                result.OscalVersion,
                result.CatalogVersion,
                result.GroupCount,
                result.BaseControlCount,
                result.EnhancementCount,
                result.TotalControlCount);
        }
        else
        {
            _logger.LogError(
                "NIST catalog integrity validation failed: schemaValid={SchemaValid}, OSCAL {OscalVersion}, catalog {CatalogVersion}, {GroupCount} families, {BaseControlCount} base controls, {EnhancementCount} enhancements, violations={Violations}",
                result.SchemaValid,
                result.OscalVersion,
                result.CatalogVersion,
                result.GroupCount,
                result.BaseControlCount,
                result.EnhancementCount,
                string.Join(" | ", result.Violations));
        }

        return result;
    }

    private static JsonSchema LoadSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().SingleOrDefault(name =>
            name.EndsWith(SchemaResourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Embedded OSCAL catalog schema ending in '{SchemaResourceSuffix}' was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded OSCAL catalog schema '{resourceName}' could not be opened.");
        return JsonSchema.FromStream(stream).GetAwaiter().GetResult();
    }

    private static JsonElement? TryGetObject(JsonElement parent, string propertyName)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Object
                ? value
                : null;
    }

    private static string GetString(JsonElement? parent, string propertyName)
    {
        return parent is { } value
            && value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
    }

    private static int CountNestedControls(JsonElement control)
    {
        if (!control.TryGetProperty("controls", out var controls)
            || controls.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = controls.GetArrayLength();
        foreach (var nestedControl in controls.EnumerateArray())
        {
            count += CountNestedControls(nestedControl);
        }

        return count;
    }

    private static void AddBaselineViolation(List<string> violations, bool condition, string message)
    {
        if (!condition)
        {
            violations.Add(message);
        }
    }

    private static void CollectSchemaViolations(EvaluationResults result, List<string> violations)
    {
        if (violations.Count >= MaximumSchemaViolations)
        {
            return;
        }

        if (!result.IsValid && result.Errors is { Count: > 0 })
        {
            foreach (var error in result.Errors)
            {
                violations.Add(
                    $"OSCAL schema violation at {result.InstanceLocation?.ToString() ?? "$"}: {error.Value}");
                if (violations.Count >= MaximumSchemaViolations)
                {
                    return;
                }
            }
        }

        if (result.Details is not { Count: > 0 })
        {
            return;
        }

        foreach (var detail in result.Details)
        {
            CollectSchemaViolations(detail, violations);
            if (violations.Count >= MaximumSchemaViolations)
            {
                return;
            }
        }
    }
}