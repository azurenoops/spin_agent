using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Imports OSCAL 1.1.2 SSP control implementations into SPIN Agent.
/// Idempotent: unchanged narratives count as skipped.
/// Supports Preview (diff only) and Full (upsert) modes.
/// Feature 076 — T009.
/// </summary>
public class OscalSspImportService : IOscalSspImportService
{
    private const string FedRampNs = "https://fedramp.gov/ns/oscal";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OscalSspImportService> _logger;

    public OscalSspImportService(IServiceScopeFactory scopeFactory, ILogger<OscalSspImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<OscalImportResult> ImportAsync(
        string systemId,
        string oscalJson,
        ImportMode mode = ImportMode.Preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(oscalJson, nameof(oscalJson));

        var runId = Guid.NewGuid().ToString();
        var errors = new List<string>();

        // Parse + validate top-level structure
        JsonDocument doc;
        try { doc = JsonDocument.Parse(oscalJson); }
        catch (JsonException ex)
        {
            return new OscalImportResult { RunId = runId, Mode = mode,
                ValidationErrors = new List<string> { $"Invalid JSON: {ex.Message}" } };
        }

        if (!doc.RootElement.TryGetProperty("system-security-plan", out var ssp) ||
            !ssp.TryGetProperty("control-implementation", out var ci) ||
            !ci.TryGetProperty("implemented-requirements", out var reqs))
        {
            return new OscalImportResult { RunId = runId, Mode = mode,
                ValidationErrors = new List<string> { "Missing system-security-plan.control-implementation.implemented-requirements" } };
        }

        // Extract all implemented requirements
        var incoming = new List<(string ControlId, string Narrative, string Status)>();
        foreach (var req in reqs.EnumerateArray())
        {
            var controlId = req.TryGetProperty("control-id", out var cid) ? cid.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(controlId)) continue;

            // Collapse by-components descriptions
            var parts = new List<string>();
            if (req.TryGetProperty("statements", out var stmts))
                foreach (var stmt in stmts.EnumerateArray())
                    if (stmt.TryGetProperty("by-components", out var byComp))
                        foreach (var bc in byComp.EnumerateArray())
                            if (bc.TryGetProperty("description", out var desc) && desc.GetString() is { Length: > 0 } d)
                                parts.Add(d);

            // Extract implementation-status from FedRAMP props
            var status = "Planned";
            if (req.TryGetProperty("props", out var props))
                foreach (var prop in props.EnumerateArray())
                    if (prop.TryGetProperty("name", out var pn) && pn.GetString() == "implementation-status" &&
                        prop.TryGetProperty("ns", out var ns) && ns.GetString() == FedRampNs &&
                        prop.TryGetProperty("value", out var pv))
                    {
                        status = CapitaliseFirstWord(pv.GetString() ?? "Planned");
                        break;
                    }

            incoming.Add((controlId.ToUpperInvariant(), string.Join("

", parts), status));
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Load existing implementations for diff
        var existing = await db.ControlImplementations
            .Where(ci2 => ci2.RegisteredSystemId == systemId)
            .ToDictionaryAsync(ci2 => ci2.ControlId, cancellationToken);

        int created = 0, updated = 0, skipped = 0, failed = 0;
        var preview = new List<OscalImportPreviewItem>();

        foreach (var (controlId, narrative, statusStr) in incoming)
        {
            try
            {
                existing.TryGetValue(controlId, out var current);
                var isNew = current == null;
                var unchanged = current != null &&
                    string.Equals(current.Narrative?.Trim(), narrative.Trim(), StringComparison.OrdinalIgnoreCase);

                var action = isNew ? "create" : unchanged ? "skip" : "update";
                preview.Add(new OscalImportPreviewItem
                {
                    ControlId       = controlId,
                    Action          = action,
                    CurrentNarrative = current?.Narrative,
                    NewNarrative    = unchanged ? null : narrative
                });

                if (action == "skip") { skipped++; continue; }

                if (mode == ImportMode.Full)
                {
                    if (isNew)
                    {
                        db.ControlImplementations.Add(new ControlImplementation
                        {
                            Id = Guid.NewGuid().ToString(),
                            RegisteredSystemId = systemId,
                            ControlId = controlId,
                            Narrative = narrative,
                            ImplementationStatus = Enum.TryParse<ImplementationStatus>(statusStr, out var s) ? s : ImplementationStatus.Planned,
                            AuthoredBy = "oscal-import",
                            AuthoredAt = DateTime.UtcNow,
                            ModifiedAt = DateTime.UtcNow
                        });
                        created++;
                    }
                    else
                    {
                        current!.Narrative = narrative;
                        current.ModifiedAt = DateTime.UtcNow;
                        updated++;
                    }
                }
                else
                {
                    if (isNew) created++; else updated++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"Failed to process {controlId}: {ex.Message}");
            }
        }

        if (mode == ImportMode.Full && (created + updated) > 0)
            await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("OSCAL SSP import ({Mode}) for {SystemId}: +{C} ~{U} ={S} !{F}",
            mode, systemId, created, updated, skipped, failed);

        return new OscalImportResult
        {
            RunId            = runId,
            Mode             = mode,
            ControlsCreated  = created,
            ControlsUpdated  = updated,
            ControlsSkipped  = skipped,
            ControlsFailed   = failed,
            ValidationErrors = errors,
            Preview          = preview
        };
    }

    private static string CapitaliseFirstWord(string s) =>
        string.IsNullOrEmpty(s) ? s :
        char.ToUpper(s[0]) + s[1..].Replace("-", string.Empty);
}
