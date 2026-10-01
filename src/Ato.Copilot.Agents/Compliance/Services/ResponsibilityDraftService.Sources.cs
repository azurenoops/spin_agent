using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class ResponsibilityDraftService
{
    private sealed record Captured(Guid TenantId, string BaselineId, string Hash,
        IReadOnlyList<ResponsibilityDraftScope> Scopes, IReadOnlyList<ResponsibilityDraftSource> Sources,
        Dictionary<string, ResponsibilityDraftValue> Values, IReadOnlyList<string> Questions,
        IReadOnlyList<string> Conflicts, bool HasControlEvidence, bool AllowAllocationProposal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)
        ?? throw new InvalidDataException("Responsibility draft data is invalid.");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Control(string value) => string.IsNullOrWhiteSpace(value) || value.Length > 20
        ? throw new ArgumentException("A control identifier of 1-20 characters is required.") : value.Trim().ToUpperInvariant();

    private async Task<Captured> CaptureAsync(string systemId, string controlId, Guid? scopeId, CancellationToken ct)
    {
        await responsibilities.AuthorizeAsync(systemId, false, ct);
        var system = await db.RegisteredSystems.AsNoTracking().SingleAsync(x => x.Id == systemId, ct);
        var owner = system.TenantId;
        var baseline = await db.ControlBaselines.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == owner && x.RegisteredSystemId == systemId, ct)
            ?? throw new ArgumentException("Select a system baseline before preparing responsibility drafts.");
        if (!baseline.ControlIds.Contains(controlId, StringComparer.OrdinalIgnoreCase))
            throw new KeyNotFoundException("Control is not in this system baseline.");
        var environmentsForSystem = await environments.ListAsync(systemId, ct);
        var scopes = environmentsForSystem.ProviderScopes.Where(x => x.State != "Removed").Select(x =>
            new ResponsibilityDraftScope(x.AssignmentId, $"{x.ProviderName} · {x.OfferingName} · {x.HostingScopeName}",
                x.ProviderName, x.ReviewRequired)).ToArray();
        var selected = scopeId.HasValue ? environmentsForSystem.ProviderScopes.SingleOrDefault(x =>
            x.AssignmentId == scopeId && x.State != "Removed")
                ?? throw new KeyNotFoundException("Provider scope is not selected for this system.") : null;
        var sources = new List<ResponsibilityDraftSource>();
        void Add(string id, string title, string origin, object content, string? href = null)
        {
            var text = Serialize(content);
            sources.Add(new(id, title, origin, Hash(text), text, href));
        }
        Add("system", system.Name, "From system records",
            new { system.Id, system.Name, system.Description, system.HostingEnvironment, BaselineId = baseline.Id,
                baseline.BaselineLevel, baseline.ControlIds }, $"/systems/{systemId}/profile/MissionAndPurpose");
        var implementation = await db.ControlImplementations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == owner && x.RegisteredSystemId == systemId && x.ControlId == controlId, ct);
        var groundingRecord = implementation ?? new ControlImplementation
            { Id = "", TenantId = owner, RegisteredSystemId = systemId, ControlId = controlId };
        var grounding = new NarrativeGroundingService(db, owner, library);
        Add("policy", $"{controlId} policy sources", "From system records",
            JsonSerializer.Deserialize<JsonElement>(await grounding.CaptureAsync(groundingRecord, "Policy", ct)),
            $"/systems/{systemId}/narratives?control={Uri.EscapeDataString(controlId)}&statement=policy");
        Add("technical", $"{controlId} technical sources", "From system records",
            JsonSerializer.Deserialize<JsonElement>(await grounding.CaptureAsync(groundingRecord, "Technical", ct)),
            $"/systems/{systemId}/narratives?control={Uri.EscapeDataString(controlId)}&statement=technical");
        if (implementation is not null)
            Add("narrative", $"{controlId} saved implementation v{implementation.CurrentVersion}", "From system records",
                new { implementation.Id, implementation.CurrentVersion, implementation.Narrative,
                    implementation.TechnicalNarrative, implementation.ApprovalStatus, implementation.ImplementationStatus });
        var existing = await db.ControlInheritances.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == owner && x.ControlBaselineId == baseline.Id && x.ControlId == controlId, ct);
        var values = ResponsibilityDraftGenerator.FieldNames.ToDictionary(x => x,
            x => new ResponsibilityDraftValue(x == "allocation" ? "NeedsConfirmation" : "", "From system records", []));
        var questions = new List<string>();
        var conflicts = new List<string>();
        if (existing is not null)
        {
            Add("responsibility", $"{controlId} saved responsibility", "From system records",
                new { existing.Id, existing.InheritanceType, existing.Provider, existing.CustomerResponsibility,
                    existing.SetBy, existing.SetAt, existing.DesignationSource });
            values["customer"] = new(existing.CustomerResponsibility ?? "", "From system records", ["responsibility"]);
            if (selected is null)
            {
                values["allocation"] = new(existing.InheritanceType.ToString(), "From system records", ["responsibility"]);
                values["provider"] = new(existing.Provider ?? "", "From system records", ["responsibility"]);
            }
        }
        if (selected is not null)
        {
            var choices = await environments.ProviderScopeChoicesAsync(systemId, ct);
            var choice = choices.Choices.SingleOrDefault(x => x.OfferingId == selected.OfferingId
                && x.HostingScopeRevisionId == selected.HostingScopeRevisionId);
            Add("provider-scope", $"{selected.ProviderName} · {selected.HostingScopeName}", "From provider source",
                new { selected.AssignmentId, selected.AssignmentVersion, selected.ProviderId, selected.OfferingId,
                    selected.HostingScopeRevisionId, selected.HostingScopeRevision, selected.AssignedScopes,
                    selected.SelectionVersion, selected.RelationshipState, selected.ReviewRequired,
                    Exclusions = choice?.Exclusions, selected.PublishedDuties });
            values["provider"] = new(selected.ProviderName ?? "", "From provider source", ["provider-scope"]);
            values["scope"] = new(Serialize(selected.AssignedScopes), "From provider source", ["provider-scope"],
                "Published/assigned scope is not a verified assertion of coverage for every system resource.");
            if (choice is not null)
                values["exclusions"] = new(Serialize(choice.Exclusions), "From provider source", ["provider-scope"]);
            var claims = selected.PublishedDuties.Capabilities.SelectMany(x => new[]
                { (Type: "Inherited", Controls: x.ProviderControlIds), (Type: "Shared", Controls: x.SharedControlIds),
                    (Type: "Customer", Controls: x.CustomerControlIds) }
                .Where(entry => entry.Controls.Contains(controlId, StringComparer.OrdinalIgnoreCase)).Select(entry => entry.Type))
                .Distinct().ToArray();
            if (claims.Length == 1 && selected.PublishedDuties.State == "Available")
                values["allocation"] = new(claims[0], "From provider source", ["provider-scope"],
                    "Published split only; system applicability and authorized acceptance remain separate.");
            else
            {
                values["allocation"] = new("NeedsConfirmation", "From provider source", ["provider-scope"]);
                if (claims.Length > 1) conflicts.Add("Published provider sources disagree on the control allocation.");
                questions.Add(selected.PublishedDuties.Reason ?? "No complete published control-specific split is available.");
            }
            if (selected.ReviewRequired) questions.Add("Verify this provider scope's applicability to the system.");
        }
        else questions.Add("No provider scope selected. Absence of a provider does not establish customer ownership.");
        if (string.IsNullOrWhiteSpace(values["customer"].Value)) questions.Add("Customer and local operational duties need review.");
        questions.Add("Reference claims and evidence metadata do not establish control satisfaction.");
        var ordered = sources.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var hasControlEvidence = existing is not null || implementation is not null
            && (!string.IsNullOrWhiteSpace(implementation.Narrative) || !string.IsNullOrWhiteSpace(implementation.TechnicalNarrative))
            || ordered.Where(x => x.Id is "policy" or "technical").Any(x =>
            {
                using var doc = JsonDocument.Parse(x.Content);
                return new[] { "referenceClaims", "evidenceArtifactMetadata", "declaredCapabilities" }.Any(key =>
                    doc.RootElement.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0);
            });
        return new(owner, baseline.Id, Hash(Serialize(ordered)), scopes, ordered, values, questions, conflicts,
            hasControlEvidence, selected is null || selected.PublishedDuties.State == "Available");
    }

    private static ResponsibilityDraftSuggestion EnforceSources(Captured captured, ResponsibilityDraftSuggestion generated)
    {
        var values = captured.Values.ToDictionary(x => x.Key, x => x.Value);
        foreach (var (key, value) in generated.Values)
            if (key != "provider" && (string.IsNullOrWhiteSpace(values[key].Value)
                || key == "allocation" && values[key].Value == "NeedsConfirmation"
                    && !captured.Conflicts.Any() && captured.AllowAllocationProposal))
                values[key] = value;
        if (string.IsNullOrWhiteSpace(values["provider"].Value)
            && values["allocation"].Value is "Inherited" or "Shared")
            values["allocation"] = captured.Values["allocation"];
        if (values["allocation"].Origin == "AI proposed" && !captured.HasControlEvidence)
            values["allocation"] = captured.Values["allocation"];
        return new(values, captured.Questions.Concat(generated.Questions).Distinct().ToArray(),
            captured.Conflicts.Concat(generated.Conflicts).Distinct().ToArray());
    }
}
