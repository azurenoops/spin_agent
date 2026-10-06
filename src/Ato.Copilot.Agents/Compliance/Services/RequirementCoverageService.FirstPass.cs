using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed record RequirementFirstPassInput(int ExpectedVersion, int ExpectedBaselineRevision, string Kind);
public sealed record RequirementFirstPassResponse(string SystemId, string ControlId, string Kind, int ExpectedVersion,
    string ContextHash, string Token, DateTimeOffset GeneratedAt, IReadOnlyList<RequirementFirstPassSourceReference> Sources,
    IReadOnlyList<RequirementResponseDraft> Responses, IReadOnlyList<RequirementParameterDraft> Parameters,
    IReadOnlyList<string> Questions, IReadOnlyList<string> Conflicts);

public sealed partial class RequirementCoverageService
{
    private sealed record FirstPassFrame(RequirementFirstPassContext Context, string Hash, int BaselineRevision,
        IReadOnlyList<string> Gaps);
    private sealed record FirstPassProof(Guid TenantId, Guid PersonId, string SystemId, string ControlId,
        int NarrativeVersion, RequirementFirstPassProvenance Provenance);
    private IDataProtector FirstPassProtector => dataProtection?.CreateProtector("RequirementCoverage.FirstPass.v1")
        ?? throw new InvalidOperationException("AI_NOT_AVAILABLE: Protected first-pass provenance is not configured.");

    public async Task<RequirementFirstPassResponse> GenerateFirstPassAsync(string systemId, string controlId,
        RequirementFirstPassInput input, CancellationToken ct = default)
    {
        var permission = await RequireAsync(systemId, ct);
        if (!permission.CanAuthorNarratives) throw new UnauthorizedAccessException("Narrative author permission is required for an AI first pass.");
        if (input.Kind is not ("Policy" or "Technical")) throw new ArgumentException("Choose Policy or Technical.");
        if (generator is null) throw new InvalidOperationException("AI_NOT_AVAILABLE: First-pass drafting is not configured.");
        var protector = FirstPassProtector;
        var frame = await CaptureFirstPassAsync(systemId, controlId, input.Kind, input.ExpectedVersion, ct);
        CheckRevision(frame.BaselineRevision, input.ExpectedBaselineRevision);
        var result = await generator.GenerateRequirementFirstPassAsync(frame.Context, ct);
        NarrativeTemplateService.ValidateRequirementSuggestion(frame.Context, result);
        permission = await RequireAsync(systemId, ct);
        if (!permission.CanAuthorNarratives) throw new UnauthorizedAccessException("Narrative author permission changed during generation.");
        var current = await CaptureFirstPassAsync(systemId, controlId, input.Kind, input.ExpectedVersion, ct);
        if (current.Hash != frame.Hash) throw new InvalidOperationException("CONCURRENCY_CONFLICT: System sources changed during generation. Reload and prepare a new first pass.");
        var provenance = new RequirementFirstPassProvenance(frame.Hash, input.Kind, DateTimeOffset.UtcNow,
            frame.Context.Sources.Select(s => new RequirementFirstPassSourceReference(s.Id, s.Kind, s.Title, s.Version, s.ContentHash, s.ReviewState)).ToArray());
        var token = protector.Protect(JsonSerializer.Serialize(new FirstPassProof(TenantId, PersonId, systemId,
            frame.Context.ControlId, input.ExpectedVersion, provenance), Json));
        return new(systemId, controlId, input.Kind, input.ExpectedVersion, frame.Hash, token, provenance.GeneratedAt,
            provenance.Sources, result.Responses, result.Parameters, result.Questions.Concat(frame.Gaps).Distinct().ToArray(), result.Conflicts);
    }

    private async Task<RequirementFirstPassProvenance> ValidateFirstPassProofAsync(string systemId, string controlId,
        int version, string token, CancellationToken ct)
    {
        if (token.Length is < 1 or > 200_000) throw new ArgumentException("First-pass proof is invalid.");
        FirstPassProof proof;
        try
        {
            proof = JsonSerializer.Deserialize<FirstPassProof>(FirstPassProtector.Unprotect(token), Json)
                ?? throw new ArgumentException("First-pass proof is invalid.");
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        { throw new ArgumentException("First-pass proof cannot be verified. Prepare a new first pass; your entered text is retained.", error); }
        if (proof.TenantId != TenantId || proof.PersonId != PersonId || proof.SystemId != systemId || proof.ControlId != controlId)
            throw new UnauthorizedAccessException("First-pass provenance belongs to a different author/system scope.");
        if (proof.NarrativeVersion != version || proof.Provenance.GeneratedAt > DateTimeOffset.UtcNow
            || proof.Provenance.GeneratedAt < DateTimeOffset.UtcNow.AddHours(-24))
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: First pass is expired or based on another revision. Prepare it again.");
        var current = await CaptureFirstPassAsync(systemId, controlId, proof.Provenance.Kind, version, ct);
        if (current.Hash != proof.Provenance.ContextHash)
            throw new InvalidOperationException("CONCURRENCY_CONFLICT: First-pass sources changed. Reload and prepare a new first pass before saving.");
        return proof.Provenance;
    }

    private async Task<FirstPassFrame> CaptureFirstPassAsync(string systemId, string controlId, string kind, int version, CancellationToken ct)
    {
        var baseline = await db.ControlBaselines.AsNoTracking().SingleOrDefaultAsync(b => b.TenantId == TenantId && b.RegisteredSystemId == systemId, ct)
            ?? throw new KeyNotFoundException("System baseline not found.");
        var binding = (await BindingAsync(baseline, ct))!;
        var control = Resolve(RequirementCatalog.Parse(binding.CatalogJson), controlId);
        if (control.Withdrawn || !baseline.ControlIds.Contains(control.Id) && !baseline.ControlIds.Contains(control.DisplayId))
            throw new InvalidOperationException("First-pass drafting requires a selected, non-withdrawn source control.");
        var implementations = await db.ControlImplementations.AsNoTracking().Where(i => i.TenantId == TenantId && i.RegisteredSystemId == systemId
            && (i.ControlId == control.Id || i.ControlId == control.DisplayId)).Take(2).ToListAsync(ct);
        if (implementations.Count != 1) throw new InvalidOperationException("Create or reconcile this control's narrative draft before preparing requirement responses.");
        var implementation = implementations[0];
        CheckRevision(implementation.CurrentVersion, version);
        if (implementation.ApprovalStatus == SspSectionStatus.UnderReview) throw new InvalidOperationException("Finish the current review before preparing a first pass.");
        var sources = new List<RequirementDraftSource>();
        var gaps = new List<string>();
        void Add(string id, string type, string title, string sourceVersion, string state, object content, IEnumerable<string>? recorded = null)
        {
            var json = JsonSerializer.Serialize(content, Json);
            sources.Add(new(id, type, title, sourceVersion, Hash(json), state, json, recorded?.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToArray() ?? []));
        }
        var grounding = new NarrativeGroundingService(db, TenantId, new NarrativeLibraryService(db, tenant, accessService));
        var policy = await grounding.CaptureAsync(implementation, "Policy", ct);
        var technical = await grounding.CaptureAsync(implementation, "Technical", ct);
        Add("grounding:policy", "ScopedGrounding", "System policy and applicable reference context", Hash(policy), "Declared/referenced; not verified enforcement", JsonSerializer.Deserialize<JsonElement>(policy));
        Add("grounding:technical", "ScopedGrounding", "Capabilities, components and recorded technical observations", Hash(technical), "Source states retained; not automatic implementation", JsonSerializer.Deserialize<JsonElement>(technical));
        Add("narrative:current", "WorkingNarrative", "Existing control draft", version.ToString(), "Unreviewed source claims",
            new { implementation.PolicyNarrative, implementation.TechnicalNarrative, implementation.Narrative });
        var profiles = await db.SystemProfileSections.AsNoTracking().Where(p => p.TenantId == TenantId && p.RegisteredSystemId == systemId)
            .OrderBy(p => p.Id).ToListAsync(ct);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "missionStatement", "businessPurpose", "responsibleOrganization",
            "programOffice", "operationalJustification", "businessFunctions", "systemVersion", "accessOverview", "authenticationMethod",
            "hostingModel", "cloudProvider", "networkZones", "geographicLocations", "availabilityTier", "disasterRecoveryPosture",
            "dataOverview", "highestSensitivityLevel", "ppsOverview", "leveragedAuthOverview" };
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.DraftContent)) continue;
            JsonDocument document;
            try { document = JsonDocument.Parse(profile.DraftContent); }
            catch (JsonException) { gaps.Add($"{profile.SectionType}: legacy source content needs structured mapping; it was not used for AI drafting."); continue; }
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                { gaps.Add($"{profile.SectionType}: source fields are not structured; review the profile."); continue; }
                var fields = document.RootElement.EnumerateObject().Where(p => allowed.Contains(p.Name) && p.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!);
                if (fields.Count > 0) Add($"profile:{profile.Id}", "SystemProfile", $"Recorded {profile.SectionType}", Hash(profile.DraftContent),
                    $"Working {profile.GovernanceStatus}; not a retained approval", fields, fields.Values);
            }
        }
        var profileIds = profiles.Select(p => p.Id).ToArray();
        var users = await db.UserCategories.AsNoTracking().Where(u => u.TenantId == TenantId && profileIds.Contains(u.SystemProfileSectionId)
            && !u.PendingDeletion).OrderBy(u => u.Id).Select(u => new { u.Id, u.Revision, u.GovernanceStatus, u.CategoryName,
                u.Description, u.IdentityType, u.PrivilegeLevel, u.Affiliation, u.AccessMethod, u.AuthenticationMethod, u.ResponsibleOwner,
                u.UserLocations, u.PermittedEnvironments, u.AuthorizedDataTypes, u.DataSensitivityLevel }).ToListAsync(ct);
        foreach (var user in users) Add($"users:{user.Id}", "UserCategory", user.CategoryName, user.Revision.ToString(),
            $"Recorded {user.GovernanceStatus}; documentation is not an account grant", user,
            new[] { user.AccessMethod, user.AuthenticationMethod, user.ResponsibleOwner, user.UserLocations, user.PermittedEnvironments, user.AuthorizedDataTypes }.OfType<string>());
        var data = await db.DataTypeEntries.AsNoTracking().Where(d => d.TenantId == TenantId && profileIds.Contains(d.SystemProfileSectionId))
            .OrderBy(d => d.Id).Select(d => new { d.Id, d.DataTypeName, d.Description, d.SensitivityClassification, d.Source, d.Destination, d.ApplicableRegulations,
                d.CuiCategory, d.ConfidentialityImpact, d.IntegrityImpact, d.AvailabilityImpact, d.PrivacyApplicability,
                d.RetentionRule, d.DisposalMethod, d.CategorizationRationale, d.CategorizationReference }).ToListAsync(ct);
        foreach (var item in data) Add($"data:{item.Id}", "InformationType", item.DataTypeName, Hash(JsonSerializer.Serialize(item)), "Working declared information",
            item, new[] { item.DataTypeName, item.Description, item.Source, item.Destination, item.ApplicableRegulations,
                item.CuiCategory, item.RetentionRule, item.DisposalMethod }.OfType<string>());
        var privacy = await db.PrivacyImpactAssessments.AsNoTracking().Where(p => p.TenantId == TenantId && p.RegisteredSystemId == systemId)
            .OrderBy(p => p.Id).Select(p => new { p.Id, p.Version, p.Status, p.SystemDescription, p.PurposeOfCollection, p.IntendedUse,
                p.SharingPartners, p.NoticeAndConsent, p.IndividualAccess, p.Safeguards, p.RetentionPeriod, p.DisposalMethod, p.SornRequired, p.SornReference }).ToListAsync(ct);
        foreach (var item in privacy) Add($"privacy:{item.Id}", "PrivacyAssessment", "Recorded privacy assessment", item.Version.ToString(),
            $"Recorded {item.Status}; not processing authority", item, new[] { item.PurposeOfCollection, item.IntendedUse, item.NoticeAndConsent,
                item.IndividualAccess, item.Safeguards, item.RetentionPeriod, item.DisposalMethod }.OfType<string>());
        var assignments = await db.ComponentSystemAssignments.AsNoTracking().Where(a => a.TenantId == TenantId && a.RegisteredSystemId == systemId
            && a.PolicySourceSnapshotJson != null).OrderBy(a => a.Id).ToListAsync(ct);
        foreach (var assignment in assignments)
        {
            var retained = RetainedPolicySource.Read(assignment);
            if (retained is null || retained.Id != assignment.SystemComponentId || retained.Revision != assignment.PolicySourceRevision)
                throw new InvalidOperationException("The retained policy source identity/version cannot be verified.");
            Add($"policy:{assignment.Id}", "RetainedPolicy", retained.Name, retained.Revision, "Retained reference; applicability unreviewed",
                new { retained.Name, retained.Description, assignment.PolicyRationale }, new[] { retained.Description, assignment.PolicyRationale }.OfType<string>());
        }
        var snapshot = Deserialize(implementation.RequirementCoverageJson);
        foreach (var parameter in snapshot?.Parameters ?? new Dictionary<string, string>())
            if (control.Parameters.Any(p => p.Id == parameter.Key))
                Add($"parameter:{parameter.Key}", "RecordedParameter", $"Recorded value for {parameter.Key}", version.ToString(), "Recorded assignment; review required",
                    new { parameter.Key, parameter.Value }, [parameter.Value]);
        var context = new RequirementFirstPassContext(control.DisplayId, control.Title, kind, control.Requirements, control.Parameters, sources);
        var serialized = JsonSerializer.Serialize(new { baseline.CoverageRevision, binding.ContentHash, version, context, gaps }, Json);
        if (serialized.Length > 100_000) throw new ArgumentException("The scoped first-pass context exceeds the generation limit. Review or narrow source descriptions.");
        return new(context, Hash(serialized), baseline.CoverageRevision, gaps);
    }
}
