using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Exports a SecurityAssessmentPlan to OSCAL 1.1.2 assessment-plan JSON.
/// Maps SAP entities (controls, team members, schedule) to the NIST OSCAL assessment-plan model.
/// </summary>
public class OscalSapExportService : IOscalSapExportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OscalSapExportService> _logger;

    private static readonly JsonSerializerOptions OscalJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OscalSapExportService(
        IServiceScopeFactory scopeFactory,
        ILogger<OscalSapExportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ExportAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await db.RegisteredSystems.FindAsync([systemId], cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var sap = await db.SecurityAssessmentPlans
            .AsNoTracking()
            .Include(s => s.ControlEntries)
            .Include(s => s.TeamMembers)
            .Where(s => s.RegisteredSystemId == systemId && s.Status == SapStatus.Finalized)
            .OrderByDescending(s => s.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"No finalized SAP found for system '{systemId}'.");

        var oscal = BuildAssessmentPlan(sap, system);

        _logger.LogInformation(
            "OSCAL SAP export for system {SystemId}: SAP {SapId}, {ControlCount} controls, {TeamCount} team members",
            systemId, sap.Id, sap.ControlEntries.Count, sap.TeamMembers.Count);

        return JsonSerializer.Serialize(oscal, OscalJsonOpts);
    }

    private static Dictionary<string, object> BuildAssessmentPlan(
        SecurityAssessmentPlan sap,
        RegisteredSystem system)
    {
        var apUuid = Guid.NewGuid().ToString();
        var lastModified = sap.FinalizedAt?.ToString("o") ?? sap.GeneratedAt.ToString("o");

        // Build metadata
        var metadata = new Dictionary<string, object>
        {
            ["title"] = sap.Title,
            ["last-modified"] = lastModified,
            ["version"] = "1.0",
            ["oscal-version"] = "1.1.2"
        };

        // Build responsible-parties from team members
        var parties = new List<object>();
        var responsibleParties = new List<object>();

        foreach (var member in sap.TeamMembers)
        {
            var partyUuid = Guid.NewGuid().ToString();
            parties.Add(new Dictionary<string, object>
            {
                ["uuid"] = partyUuid,
                ["type"] = "person",
                ["name"] = member.Name,
                ["props"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["name"] = "organization",
                        ["value"] = member.Organization
                    }
                }
            });

            responsibleParties.Add(new Dictionary<string, object>
            {
                ["role-id"] = NormalizeRoleId(member.Role),
                ["party-uuids"] = new[] { partyUuid }
            });
        }

        if (parties.Count > 0)
            metadata["parties"] = parties;

        // Build import-ssp reference
        var importSsp = new Dictionary<string, string>
        {
            ["href"] = $"#ssp-{system.Id}"
        };

        // Build reviewed-controls with control-selections
        var controlsByFamily = sap.ControlEntries
            .GroupBy(c => c.ControlFamily)
            .OrderBy(g => g.Key);

        var controlSelections = new List<object>();
        foreach (var family in controlsByFamily)
        {
            controlSelections.Add(new Dictionary<string, object>
            {
                ["include-controls"] = family.Select(c => new Dictionary<string, string>
                {
                    ["control-id"] = c.ControlId.ToLowerInvariant()
                }).ToList()
            });
        }

        var reviewedControls = new Dictionary<string, object>
        {
            ["control-selections"] = controlSelections
        };

        // Build assessment-subjects
        var assessmentSubjects = new[]
        {
            new Dictionary<string, object>
            {
                ["type"] = "single-system",
                ["description"] = $"Assessment of {system.Name}",
                ["include-all"] = new Dictionary<string, object>()
            }
        };

        // Build assessment-activities from unique methods
        var activities = sap.ControlEntries
            .SelectMany(c => c.AssessmentMethods ?? [])
            .Distinct()
            .OrderBy(m => m)
            .Select(method => new Dictionary<string, object>
            {
                ["uuid"] = Guid.NewGuid().ToString(),
                ["title"] = $"{method} Assessment Activity",
                ["description"] = $"Assessment activity using the {method} method per NIST SP 800-53A.",
                ["props"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["name"] = "method",
                        ["value"] = method
                    }
                }
            })
            .ToList<object>();

        // Build tasks with timing
        var tasks = new List<object>();
        if (sap.ScheduleStart.HasValue || sap.ScheduleEnd.HasValue)
        {
            var task = new Dictionary<string, object>
            {
                ["uuid"] = Guid.NewGuid().ToString(),
                ["type"] = "action",
                ["title"] = "Security Assessment Execution",
                ["description"] = $"Execute assessment activities for {system.Name}"
            };

            if (sap.ScheduleStart.HasValue && sap.ScheduleEnd.HasValue)
            {
                task["timing"] = new Dictionary<string, object>
                {
                    ["within-date-range"] = new Dictionary<string, string>
                    {
                        ["start"] = sap.ScheduleStart.Value.ToString("o"),
                        ["end"] = sap.ScheduleEnd.Value.ToString("o")
                    }
                };
            }

            tasks.Add(task);
        }

        // Assemble assessment-plan root
        var apRoot = new Dictionary<string, object>
        {
            ["uuid"] = apUuid,
            ["metadata"] = metadata,
            ["import-ssp"] = importSsp,
            ["reviewed-controls"] = reviewedControls,
            ["assessment-subjects"] = assessmentSubjects
        };

        if (activities.Count > 0)
            apRoot["assessment-activities"] = activities;

        if (tasks.Count > 0)
            apRoot["tasks"] = tasks;

        if (responsibleParties.Count > 0)
            apRoot["responsible-parties"] = responsibleParties;

        return new Dictionary<string, object>
        {
            ["assessment-plan"] = apRoot
        };
    }

    private static string NormalizeRoleId(string role) =>
        role.ToLowerInvariant().Replace(' ', '-');
}
