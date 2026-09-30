using System.Text.Json;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services;

/// <summary>Finite preparation intents, not manually completable workflow or phase debt.</summary>
public static class SystemSetupPreparationProjection
{
    public static void ConfirmExplicitFields(RegisteredSystem system, IReadOnlyCollection<string> validatedFields)
    {
        if (system.SetupDraftJson is null || validatedFields.Count == 0) return;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var intent = JsonSerializer.Deserialize<SystemSetupIntent>(system.SetupDraftJson, options)
            ?? throw new InvalidOperationException("Saved system preparation intent is unreadable.");
        var remaining = intent.UnconfirmedFields.Where(field => !validatedFields.Contains(field)).ToArray();
        if (remaining.Length == intent.UnconfirmedFields.Count) return;
        system.SetupDraftJson = JsonSerializer.Serialize(intent with { UnconfirmedFields = remaining }, options);
        system.SetupRevision++;
        system.SetupLastCommandJson = null;
    }

    public static IReadOnlyList<string> UnconfirmedFields(RegisteredSystem system) =>
        system.SetupDraftJson is null ? [] :
        JsonSerializer.Deserialize<SystemSetupIntent>(system.SetupDraftJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.UnconfirmedFields
            ?? throw new InvalidOperationException("Saved system preparation intent is unreadable.");

    public static IReadOnlyList<SystemSetupTask> Build(RegisteredSystem system, SystemWorkspacePermissions? permission = null)
    {
        if (system.SetupDraftJson is null) return [];
        var intent = JsonSerializer.Deserialize<SystemSetupIntent>(system.SetupDraftJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Saved system preparation intent is unreadable.");
        var path = $"/systems/{Uri.EscapeDataString(system.Id)}";
        var items = new List<SystemSetupTask>
        {
            new("setup:definition", "Complete system definition", "Confirm system type, criticality, boundary and supporting facts in their reviewed records.",
                "open", "Provide attributable facts for the SSP and receiving package.", $"{path}/profile/MissionAndPurpose", "Mission Owner", permission?.CanEditProfile == true),
            new("setup:team", "Review effective system roles", "Use current assignments and action-specific permissions; a preparation contact is not a role grant.",
                "reviewRequired", "Identify accountable authors and reviewers.", $"{path}/roles", "ISSM", permission?.CanAssignSystemRoles == true),
            new("setup:hosting", "Review hosting and recorded scope", "A saved hosting choice does not establish an allocation, scope acceptance or inherited controls.",
                intent.HostingChoice == "deferred" ? "deferred" : "reviewRequired",
                "Document the actual environment and reviewed responsibility boundary.", $"{path}/profile/EnvironmentAndDeployment", "ISSM", permission?.CanEditProfile == true),
            new("setup:documents", "Review system documentation", "Setup does not generate or approve an SSP or submit an eMASS package.",
                "reviewRequired", "Prepare reviewed system documentation with original source provenance.", $"{path}/documents", "ISSM", permission?.CanRead == true),
            new("monitoring:connection", "Review monitoring connection", "Configuration and permission checks are separate from successful collection.",
                intent.MonitoringChoice == "configureLater" ? "deferred" : "open",
                "Establish permitted evidence collection without asserting monitoring health.", $"{path}/assessments/environment", "ISSO", permission?.CanRunAssessments == true),
            new("monitoring:scope", "Verify system-scoped monitoring", "Subscription-level monitoring does not prove per-system scope or attribution.",
                "blocked", "Preserve reviewed scope and traceable monitoring evidence.", $"{path}/conmon", "ISSM", permission?.CanRead == true),
        };
        if (intent.SourceChoice != "blank")
            items.Add(new("setup:source-review", "Review existing documentation sources",
                "Retain a supported source, then review exact target fields before applying proposals.",
                "open", "Retain and review source provenance before applying documented facts.", $"{path}/setup?step=s-sources", "ISSM", permission?.CanManageSystem == true));
        return items;
    }

    public static IReadOnlyList<SystemSetupTask> WithSources(
        IReadOnlyList<SystemSetupTask> tasks, IReadOnlyList<SystemSourceReceipt> sources,
        SystemWorkspacePermissions? permission = null)
    {
        if (sources.Count == 0) return tasks;
        return tasks.Where(x => x.Id != "setup:source-review").Concat(
            sources.Where(x => x.ReviewState != "applied").Select(source => new SystemSetupTask(
                $"source:{source.Kind}:{source.SessionId}", $"Review {source.FileName}",
                source.Error ?? "The original is retained; review does not grant document approval.",
                source.AnalysisState == "parsed" ? "reviewRequired" : "blocked",
                "Carry reviewed identity and original source provenance into system documentation.",
                $"/systems/{Uri.EscapeDataString(source.SystemId)}/setup?source={source.Kind}&receipt={source.SessionId}",
                "ISSM", permission?.CanManageSystem == true))).ToArray();
    }
}
