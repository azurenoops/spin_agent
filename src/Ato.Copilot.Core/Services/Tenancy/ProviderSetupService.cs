using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.PackageImports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Tenancy;

public sealed partial class ProviderSetupService(
    IDbContextFactory<AtoCopilotContext> factory, ITenantContext tenant,
    IOptions<ProviderHandlingOptions> handling, IMemoryCache cache, ILogger<ProviderSetupService> logger)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidDataException("Stored provider setup metadata is unavailable.");
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static JsonElement Element(string value) => Read<JsonElement>(value);
    internal static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static JsonElement Part(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value : default;

    public void Authorize()
    {
        if (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue)
            throw new UnauthorizedAccessException("Use an ordinary provider administrator workspace.");
    }

    private async Task<CspProfile?> ProfileAsync(AtoCopilotContext db, CancellationToken ct)
    {
        Authorize();
        var profiles = await db.CspProfiles.Take(2).ToListAsync(ct);
        if (profiles.Count > 1) throw new InvalidDataException("Multiple provider profiles require administrator reconciliation.");
        return profiles.SingleOrDefault();
    }

    public object HandlingPolicy()
    {
        var options = handling.Value;
        var known = !string.IsNullOrWhiteSpace(options.PolicyId) && !string.IsNullOrWhiteSpace(options.Version)
            && options.Version.Length <= 100
            && !string.IsNullOrWhiteSpace(options.ApprovalReference) && options.ValidUntil.HasValue
            && options.AllowedClassifications.Length > 0
            && options.AllowedClassifications.All(x => x is "Unclassified" or "CUI" or "Secret");
        var expired = known && options.ValidUntil <= DateTimeOffset.UtcNow;
        return new
        {
            state = !known ? "Unknown" : expired ? "Expired" : "Known",
            policyId = options.PolicyId, version = options.Version, environmentLabel = options.EnvironmentLabel,
            approvalReference = options.ApprovalReference, validUntil = options.ValidUntil,
            allowedClassifications = known && !expired ? options.AllowedClassifications : [],
            allowedMarkings = known && !expired ? options.AllowedMarkings : [],
            syntheticOnly = options.SyntheticOnly, uploadsPermitted = known && !expired && options.UploadsEnabled,
            analysisPermitted = known && !expired && options.AnalysisEnabled,
            checkedAt = DateTimeOffset.UtcNow, reasonCode = !known ? "HANDLING_POLICY_UNKNOWN" : expired ? "HANDLING_POLICY_EXPIRED" : null
        };
    }

    public static void RequireHandling(ProviderHandlingOptions policy, ProviderUploadIntentInput intent, bool analysis = false)
    {
        if (string.IsNullOrWhiteSpace(policy.PolicyId) || string.IsNullOrWhiteSpace(policy.ApprovalReference)
            || string.IsNullOrWhiteSpace(policy.Version) || policy.Version.Length > 100
            || policy.AllowedClassifications.Length == 0
            || policy.AllowedClassifications.Any(x => x is not ("Unclassified" or "CUI" or "Secret"))
            || !policy.ValidUntil.HasValue || policy.ValidUntil <= DateTimeOffset.UtcNow
            || !(analysis ? policy.AnalysisEnabled : policy.UploadsEnabled))
            throw new ArgumentException("HANDLING_POLICY_UNKNOWN: Approved deployment handling permission is unavailable.");
        if (policy.Version != intent.HandlingPolicyVersion)
            throw new DbUpdateConcurrencyException("HANDLING_POLICY_CHANGED: Review current deployment handling limits.");
        if (intent.DeclaredContent is null || !policy.AllowedClassifications.Contains(intent.DeclaredContent.Classification)
            || intent.DeclaredContent.Markings is null || intent.DeclaredContent.Markings.Any(x => !policy.AllowedMarkings.Contains(x))
            || policy.SyntheticOnly && !intent.DeclaredContent.ContainsOnlySyntheticData)
            throw new ArgumentException("HANDLING_NOT_PERMITTED: The declared source content exceeds deployment handling limits.");
    }

    private static object Access(ProviderSetupActor actor) => new
    {
        state = "Authorized", checkedAt = DateTimeOffset.UtcNow, actor, scope = "Provider"
    };

    public async Task<object> StateAsync(ProviderSetupActor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await ProfileAsync(db, ct);
        var draft = profile is null ? null : await db.Set<ProviderSetupDraft>().SingleOrDefaultAsync(x => x.ProviderId == profile.Id, ct);
        var intents = profile is null || draft is null ? [] : await db.Set<CspPackageUploadIntent>().Where(x => x.ProviderId == profile.Id && x.DraftId == draft.Id)
            .OrderBy(x => x.Id).Take(25).ToListAsync(ct);
        var projections = new List<object>();
        foreach (var intent in intents) projections.Add(await IntentStateAsync(db, intent, ct));
        var metadata = draft is null ? default : Element(draft.CommittedMetadataJson);
        return new
        {
            providerId = profile?.Id, profileRevision = profile?.SetupRevision,
            profile = ProfileState(profile),
            draft = draft is null ? null : new
            {
                draftId = draft.Id, draft.Revision, draft.SchemaVersion,
                currentScreen = Text(Element(draft.DraftJson), "currentScreen") ?? "p-details",
                savedAt = draft.UpdatedAt, savedBy = draft.UpdatedBy, fields = Element(draft.DraftJson),
                committedOfferingId = Text(metadata, "committedOfferingId"),
                completion = draft.CompletionSnapshotJson is null ? (JsonElement?)null : Element(draft.CompletionSnapshotJson)
            },
            access = Access(actor), handling = HandlingPolicy(), uploadIntents = projections,
            facts = await ActionsCoreAsync(db, profile, draft, ct)
        };
    }

    private static object ProfileState(CspProfile? profile) => new
    {
        cspProfileId = profile?.Id, onboardingState = (profile?.OnboardingState ?? OnboardingState.Pending).ToString(),
        currentStep = profile?.OnboardingState == OnboardingState.Active ? "Complete"
            : profile?.IdentityCompletedAt is null ? "Identity" : profile.SupportCompletedAt is null ? "SupportContact"
            : profile.ClassificationCompletedAt is null ? "Classification" : "Review",
        identity = profile?.IdentityCompletedAt is null ? null : new { profile.LegalEntityName, profile.DisplayName, profile.LogoUrl },
        supportContact = profile?.SupportCompletedAt is null ? null : new { profile.PrimarySupportEmail, profile.SupportPhone },
        classification = profile?.ClassificationCompletedAt is null ? null : new { defaultClassificationFloor = profile.DefaultClassificationFloor.ToString() },
        onboardingCompletedAt = profile?.OnboardingCompletedAt
    };

    public async Task<object> ActionsAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > 1000000) throw new ArgumentException("Invalid action page.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await ProfileAsync(db, ct);
        var draft = profile is null ? null : await db.Set<ProviderSetupDraft>().SingleOrDefaultAsync(x => x.ProviderId == profile.Id, ct);
        var actions = await ActionsCoreAsync(db, profile, draft, ct);
        return new { items = actions.Skip((page - 1) * pageSize).Take(pageSize), page, pageSize, total = actions.Count };
    }

    private static async Task<List<object>> ActionsCoreAsync(AtoCopilotContext db, CspProfile? profile,
        ProviderSetupDraft? draft, CancellationToken ct)
    {
        var actions = new List<object>();
        if (profile is null || draft is null) return actions;
        var fields = Element(draft.DraftJson);
        foreach (var (field, label, destination) in new[]
        {
            ("firstOffering", "Add or select a service offering", "/workspaces/csp/authorizations/create"),
            ("sources", "Add provider source material", "/workspaces/csp/authorizations/import"),
            ("securityContact", "Identify the security review contact", "/onboarding/csp?reentry=resume")
        })
        {
            var section = Part(fields, field);
            var choice = Text(section, "choice");
            if (choice is not ("Deferred" or "Unspecified" or null or "Selected")) continue;
            actions.Add(new
            {
                actionId = $"provider:{profile.Id:D}:setup:{field}", label, state = choice == "Deferred" ? "Deferred" : "Open",
                ownerRole = "CSP.Admin", reasonCode = choice == "Deferred" ? "EXPLICIT_DEFERRAL" : "SETUP_NOT_RECORDED",
                reason = Text(Part(section, "deferral"), "reason"), source = new { kind = "ProviderSetupDraft", id = draft.Id, revision = draft.Revision },
                destination = new { path = destination, label }, contribution = "Retain service identity and source provenance for reviewed documentation.",
                evaluatedAt = DateTimeOffset.UtcNow
            });
        }
        var intents = await db.Set<CspPackageUploadIntent>().Where(x => x.ProviderId == profile.Id && x.DraftId == draft.Id).OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var intent in intents)
        {
            var package = await db.CspPackages.SingleOrDefaultAsync(x => x.ProviderId == profile.Id && x.IdempotencyKey == intent.IdempotencyKey, ct);
            var path = package is null ? $"/onboarding/csp?reentry=resume&intentId={intent.Id:D}"
                : $"/workspaces/csp/authorizations/import?packageId={package.Id:D}";
            actions.Add(new
            {
                actionId = $"upload-intent:{intent.Id:D}:reconcile", label = package is null ? "Check source receipt" : "Review retained source package",
                state = package is null ? "Unknown" : "Open", ownerRole = "CSP.Admin",
                reasonCode = package is null ? "RECEIPT_NOT_OBSERVED" : "SOURCE_REVIEW_SEPARATE",
                source = new { kind = "CspPackageUploadIntent", id = intent.Id, revision = intent.Revision },
                destination = new { path, label = package is null ? "Check receipt" : "Review sources" },
                contribution = "Preserve and review source evidence before publication.", evaluatedAt = DateTimeOffset.UtcNow
            });
        }
        return actions;
    }
}
