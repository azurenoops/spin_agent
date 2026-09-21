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
        var incoming = new List<(string ControlId, string? PolicyNarrative, string? TechnicalNarrative, string Status)>();
        foreach (var req in reqs.EnumerateArray())
        {
            var controlId = req.TryGetProperty("control-id", out var cid) ? cid.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(controlId)) continue;

            var policyParts = new List<string>();
            var technicalParts = new List<string>();
            var combinedParts = new List<string>();
            if (req.TryGetProperty("statements", out var stmts))
            {
                foreach (var stmt in stmts.EnumerateArray())
                {
                    var statementId = stmt.TryGetProperty("statement-id", out var sid)
                        ? sid.GetString()
                        : null;
                    var target = statementId?.EndsWith(".policy", StringComparison.OrdinalIgnoreCase) == true
                        ? policyParts
                        : statementId?.EndsWith(".technical", StringComparison.OrdinalIgnoreCase) == true
                            ? technicalParts
                            : combinedParts;

                    if (stmt.TryGetProperty("description", out var description) &&
                        description.GetString() is { Length: > 0 } text)
                    {
                        target.Add(text);
                    }

                    if (stmt.TryGetProperty("by-components", out var byComponents))
                    {
                        foreach (var component in byComponents.EnumerateArray())
                        {
                            if (component.TryGetProperty("description", out var componentDescription) &&
                                componentDescription.GetString() is { Length: > 0 } componentText)
                            {
                                target.Add(componentText);
                            }
                        }
                    }
                }
            }

            technicalParts.AddRange(combinedParts);
            var policyNarrative = JoinNarrativeParts(policyParts);
            var technicalNarrative = JoinNarrativeParts(technicalParts);

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

            incoming.Add((controlId.ToUpperInvariant(), policyNarrative, technicalNarrative, status));
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Load existing implementations for diff
        var existing = await db.ControlImplementations
            .Where(ci2 => ci2.RegisteredSystemId == systemId)
            .ToDictionaryAsync(ci2 => ci2.ControlId, cancellationToken);

        int created = 0, updated = 0, skipped = 0, failed = 0;
        var preview = new List<OscalImportPreviewItem>();

        foreach (var (controlId, policyNarrative, technicalNarrative, statusStr) in incoming)
        {
            try
            {
                existing.TryGetValue(controlId, out var current);
                var isNew = current == null;
                var unchanged = current != null &&
                    (policyNarrative is null || string.Equals(current.PolicyNarrative?.Trim(), policyNarrative, StringComparison.Ordinal)) &&
                    (technicalNarrative is null || string.Equals(current.TechnicalNarrative?.Trim(), technicalNarrative, StringComparison.Ordinal));

                var currentNarrative = string.Join("\n\n", new[] { current?.PolicyNarrative, current?.TechnicalNarrative }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                var newNarrative = string.Join("\n\n", new[] { policyNarrative, technicalNarrative }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

                var action = isNew ? "create" : unchanged ? "skip" : "update";
                preview.Add(new OscalImportPreviewItem
                {
                    ControlId       = controlId,
                    Action          = action,
                    CurrentNarrative = currentNarrative,
                    NewNarrative    = unchanged ? null : newNarrative
                });

                if (action == "skip") { skipped++; continue; }

                if (mode == ImportMode.Full)
                {
                    if (isNew)
                    {
                        var implementation = new ControlImplementation
                        {
                            Id = Guid.NewGuid().ToString(),
                            RegisteredSystemId = systemId,
                            ControlId = controlId,
                            PolicyNarrative = policyNarrative,
                            ImplementationStatus = Enum.TryParse<ImplementationStatus>(statusStr, out var s) ? s : ImplementationStatus.Planned,
                            AuthoredBy = "oscal-import",
                            AuthoredAt = DateTime.UtcNow,
                            ModifiedAt = DateTime.UtcNow
                        };
                        implementation.SetCombinedNarrative(technicalNarrative);
                        db.ControlImplementations.Add(implementation);
                        db.NarrativeVersions.Add(CreateImportVersion(implementation, runId));
                        existing[controlId] = implementation;
                        created++;
                    }
                    else
                    {
                        if (current!.ApprovalStatus == SspSectionStatus.UnderReview)
                            throw new InvalidOperationException("UNDER_REVIEW: Cannot import into a narrative under review.");
                        if (!db.NarrativeVersions.Local.Any(version =>
                            version.ControlImplementationId == current.Id && version.VersionNumber == current.CurrentVersion) &&
                            !await db.NarrativeVersions.AnyAsync(version =>
                            version.ControlImplementationId == current.Id && version.VersionNumber == current.CurrentVersion,
                            cancellationToken))
                        {
                            db.NarrativeVersions.Add(CreateImportVersion(current, runId, "Before OSCAL import"));
                        }
                        if (policyNarrative is not null)
                            current.PolicyNarrative = policyNarrative;
                        if (technicalNarrative is not null &&
                            !string.Equals(current.TechnicalNarrative, technicalNarrative, StringComparison.Ordinal))
                        {
                            current.SetCombinedNarrative(technicalNarrative);
                            current.AiSuggested = false;
                            current.IsAutoPopulated = false;
                            current.IsManuallyCustomized = false;
                        }
                        current.CurrentVersion++;
                        current.ApprovalStatus = SspSectionStatus.Draft;
                        current.AuthoredBy = "oscal-import";
                        current.ModifiedAt = DateTime.UtcNow;
                        db.NarrativeVersions.Add(CreateImportVersion(current, runId));
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

    private static NarrativeVersion CreateImportVersion(
        ControlImplementation implementation, string runId, string reason = "OSCAL import") => new()
    {
        TenantId = implementation.TenantId,
        ControlImplementationId = implementation.Id,
        VersionNumber = implementation.CurrentVersion,
        Content = implementation.TechnicalNarrative ?? implementation.Narrative ?? string.Empty,
        SnapshotJson = NarrativeContentSnapshot.Capture(implementation),
        Status = implementation.ApprovalStatus,
        AuthoredBy = implementation.AuthoredBy,
        ChangeReason = $"{reason}; run {runId}",
    };

    private static string CapitaliseFirstWord(string s) =>
        string.IsNullOrEmpty(s) ? s :
        char.ToUpper(s[0]) + s[1..].Replace("-", string.Empty);

    private static string? JoinNarrativeParts(IEnumerable<string> parts)
    {
        var narrative = string.Join("\n\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(narrative) ? null : narrative;
    }
}
