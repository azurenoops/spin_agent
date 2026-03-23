using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Validates OSCAL JSON documents against official NIST OSCAL 1.1.2 JSON schemas
/// bundled as embedded resources. Supports SSP, POA&M, Assessment Results, and Assessment Plan.
/// </summary>
public class OscalSchemaValidationService : IOscalSchemaValidationService
{
    private readonly IEmassExportService _emassExportService;
    private readonly IOscalSapExportService _sapExportService;
    private readonly ILogger<OscalSchemaValidationService> _logger;

    private static readonly Dictionary<string, JsonSchema> SchemaCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SchemaLock = new();
    private static bool _schemasLoaded;

    private static readonly Dictionary<string, string> ModelSchemaMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ssp"] = "oscal_ssp_schema.json",
        ["poam"] = "oscal_poam_schema.json",
        ["assessment-results"] = "oscal_assessment-results_schema.json",
        ["assessment-plan"] = "oscal_assessment-plan_schema.json"
    };

    public OscalSchemaValidationService(
        IEmassExportService emassExportService,
        IOscalSapExportService sapExportService,
        ILogger<OscalSchemaValidationService> logger)
    {
        _emassExportService = emassExportService;
        _sapExportService = sapExportService;
        _logger = logger;
        EnsureSchemasLoaded();
    }

    public Task<OscalSchemaValidationResult> ValidateAsync(
        string oscalJson,
        string modelType,
        CancellationToken cancellationToken = default)
    {
        if (!ModelSchemaMap.TryGetValue(modelType, out var schemaFile))
        {
            return Task.FromResult(new OscalSchemaValidationResult
            {
                IsValid = false,
                ModelType = modelType,
                Violations = [new OscalSchemaViolation
                {
                    JsonPath = "$",
                    Message = $"Unsupported model type '{modelType}'. Supported: {string.Join(", ", ModelSchemaMap.Keys)}"
                }]
            });
        }

        if (!SchemaCache.TryGetValue(schemaFile, out var schema))
        {
            return Task.FromResult(new OscalSchemaValidationResult
            {
                IsValid = false,
                ModelType = modelType,
                Violations = [new OscalSchemaViolation
                {
                    JsonPath = "$",
                    Message = $"Schema '{schemaFile}' not found in embedded resources."
                }]
            });
        }

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(oscalJson);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new OscalSchemaValidationResult
            {
                IsValid = false,
                ModelType = modelType,
                Violations = [new OscalSchemaViolation
                {
                    JsonPath = "$",
                    Message = $"Invalid JSON: {ex.Message}"
                }]
            });
        }

        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        };

        var result = schema.Evaluate(document, options);
        var violations = new List<OscalSchemaViolation>();

        if (!result.IsValid)
        {
            CollectViolations(result, violations);
        }

        return Task.FromResult(new OscalSchemaValidationResult
        {
            IsValid = result.IsValid,
            ModelType = modelType,
            Violations = violations
        });
    }

    public async Task<OscalSchemaValidationResult> ValidateForSystemAsync(
        string systemId,
        string modelType,
        CancellationToken cancellationToken = default)
    {
        string oscalJson;
        try
        {
            if (string.Equals(modelType, "assessment-plan", StringComparison.OrdinalIgnoreCase))
            {
                oscalJson = await _sapExportService.ExportAsync(systemId, cancellationToken);
            }
            else
            {
                var oscalModel = modelType.ToLowerInvariant() switch
                {
                    "ssp" => OscalModelType.Ssp,
                    "assessment-results" => OscalModelType.AssessmentResults,
                    "poam" => OscalModelType.Poam,
                    _ => throw new ArgumentException($"Unsupported model type '{modelType}'.")
                };
                oscalJson = await _emassExportService.ExportOscalAsync(systemId, oscalModel, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate OSCAL {ModelType} for system {SystemId}", modelType, systemId);
            return new OscalSchemaValidationResult
            {
                IsValid = false,
                ModelType = modelType,
                Violations = [new OscalSchemaViolation
                {
                    JsonPath = "$",
                    Message = $"Failed to generate OSCAL document: {ex.Message}"
                }]
            };
        }

        return await ValidateAsync(oscalJson, modelType, cancellationToken);
    }

    private static void CollectViolations(EvaluationResults results, List<OscalSchemaViolation> violations)
    {
        if (results.Details is { Count: > 0 })
        {
            foreach (var detail in results.Details)
            {
                if (!detail.IsValid && detail.Errors is { Count: > 0 })
                {
                    foreach (var error in detail.Errors)
                    {
                        violations.Add(new OscalSchemaViolation
                        {
                            JsonPath = detail.InstanceLocation?.ToString() ?? "$",
                            Message = error.Value
                        });
                    }
                }

                // Recurse into nested details
                if (detail.Details is { Count: > 0 })
                {
                    CollectViolations(detail, violations);
                }
            }
        }
    }

    private void EnsureSchemasLoaded()
    {
        if (_schemasLoaded) return;

        lock (SchemaLock)
        {
            if (_schemasLoaded) return;

            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();

            foreach (var (modelType, schemaFileName) in ModelSchemaMap)
            {
                var resourceName = resourceNames.FirstOrDefault(r =>
                    r.EndsWith(schemaFileName.Replace("-", "_"), StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    _logger.LogWarning("OSCAL schema resource not found for {SchemaFile}", schemaFileName);
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logger.LogWarning("Could not load OSCAL schema stream for {ResourceName}", resourceName);
                    continue;
                }

                var schema = JsonSchema.FromStream(stream).Result;
                SchemaCache[schemaFileName] = schema;
                _logger.LogInformation("Loaded OSCAL schema: {SchemaFile}", schemaFileName);
            }

            _schemasLoaded = true;
        }
    }
}
