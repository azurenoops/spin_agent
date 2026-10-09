using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Tenancy;

public sealed partial class ProviderSetupService
{
    private static async Task EnsureCompletionWorkItemsAsync(
        AtoCopilotContext db, CspProfile profile, ProviderSetupDraft draft, JsonElement fields,
        IReadOnlyList<Guid> unresolvedReceipts, ProviderSetupActor actor, CancellationToken ct)
    {
        var specifications = new List<WorkSpecification>();
        AddDeferral("operationalContact", "DeferredOperationalContact", "Record the operational support contact.",
            "/onboarding/csp?reentry=resume");
        AddDeferral("securityContact", "DeferredSecurityContact", "Record the operational security contact.",
            "/onboarding/csp?reentry=resume");
        AddDeferral("portfolio", "DeferredPortfolio", "Create or select the provider service portfolio.",
            "/workspaces/csp/authorizations");
        AddDeferral("firstOffering", "DeferredOffering", "Create or select the provider service offering.",
            "/workspaces/csp/authorizations/create");
        AddDeferral("sources", "DeferredSources", "Retain and review provider source material.",
            "/workspaces/csp/authorizations/import");

        var authorization = Part(fields, "authorizationStartingPoint");
        switch (Text(authorization, "choice"))
        {
            case "InitialAuthorization":
                specifications.Add(new("PrepareInitialAuthorization",
                    "Prepare the offering authorization package without asserting an authorization result.",
                    "/workspaces/csp/authorizations", null));
                break;
            case "DetermineLater":
                specifications.Add(new("DetermineAuthorizationScope",
                    "Determine the offering authorization starting point and reviewed scope.",
                    "/workspaces/csp/authorizations", null));
                break;
            case "ExistingAuthorization":
                var existing = Part(authorization, "existingDecision");
                foreach (var field in new[] { "decisionReference", "issuingAuthority", "effectiveDate", "expirationDate" })
                    if (string.IsNullOrWhiteSpace(Text(existing, field)))
                        specifications.Add(new($"ExistingAuthorization{field}",
                            $"Confirm the existing authorization {field} from reviewed source material.",
                            "/workspaces/csp/authorizations", null));
                specifications.Add(new("ExistingAuthorizationCoverage",
                    "Map reviewed existing-authorization scope to an exact boundary before claiming coverage.",
                    "/workspaces/csp/authorizations", null));
                var unresolved = Part(authorization, "unresolvedFields");
                if (unresolved.ValueKind == JsonValueKind.Array)
                    foreach (var value in unresolved.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String))
                        specifications.Add(new($"ExistingAuthorization:{value.GetString()}",
                            $"Resolve the declared existing-authorization field: {value.GetString()}.",
                            "/workspaces/csp/authorizations", null));
                break;
        }

        foreach (var intentId in unresolvedReceipts.Distinct())
            specifications.Add(new("UnresolvedReceipt",
                "Reconcile the retained upload intent before submitting duplicate bytes.",
                $"/onboarding/csp?reentry=resume&intentId={intentId:D}", intentId));

        var metadata = Element(draft.CommittedMetadataJson);
        var portfolioId = Guid.TryParse(Text(metadata, "committedPortfolioId"), out var parsedPortfolio)
            ? parsedPortfolio : (Guid?)null;
        var offeringId = Guid.TryParse(Text(metadata, "committedOfferingId"), out var parsedOffering)
            ? parsedOffering : (Guid?)null;
        foreach (var specification in specifications.DistinctBy(x => new { x.Type, x.UploadIntentId }))
        {
            var idempotencyKey =
                $"{profile.Id:D}:{draft.Id:D}:{specification.Type}:{specification.UploadIntentId?.ToString("D") ?? "-"}:{draft.Revision + 1}";
            if (await db.Set<ProviderSetupWorkItem>().AnyAsync(
                    x => x.ProviderId == profile.Id && x.IdempotencyKey == idempotencyKey, ct))
                continue;
            db.Add(new ProviderSetupWorkItem
            {
                ProviderId = profile.Id,
                SetupId = draft.Id,
                PortfolioId = portfolioId,
                OfferingId = offeringId,
                UploadIntentId = specification.UploadIntentId,
                Type = specification.Type,
                ReasonCode = "SETUP_FOLLOW_UP",
                OwnerRole = "PortalAdministrator",
                AcceptanceCriteria = specification.AcceptanceCriteria,
                Destination = specification.Destination,
                SourceRevision = draft.Revision + 1,
                IdempotencyKey = idempotencyKey,
                CreatedBy = actor.ObjectId
            });
        }

        void AddDeferral(string section, string type, string criteria, string destination)
        {
            var value = Part(fields, section);
            if (Text(value, "choice") == "Deferred")
                specifications.Add(new(type, criteria, destination, null));
        }
    }

    private sealed record WorkSpecification(
        string Type, string AcceptanceCriteria, string Destination, Guid? UploadIntentId);
}
