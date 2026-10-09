using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Tenancy;

public sealed partial class ProviderSetupService
{
    private async Task<SetupMutation> CommitPortfolioAndOfferingAsync(
        AtoCopilotContext db, CspProfile profile, ProviderSetupDraft draft, JsonElement fields,
        CommitProviderSetup request, ProviderSetupActor actor, CancellationToken ct)
    {
        var portfolioInput = Part(fields, "portfolio");
        var portfolioChoice = Text(portfolioInput, "choice");
        ServicePortfolio? portfolio = null;
        if (portfolioChoice == "Existing")
        {
            if (!Guid.TryParse(Text(portfolioInput, "portfolioId"), out var portfolioId))
                throw new ArgumentException("Select an existing service portfolio.");
            portfolio = await db.Set<ServicePortfolio>().SingleOrDefaultAsync(
                x => x.ProviderId == profile.Id && x.Id == portfolioId, ct)
                ?? throw new KeyNotFoundException("Service portfolio was not found.");
            if (!request.ExpectedPortfolioRevision.HasValue)
                throw new DbUpdateConcurrencyException("Expected service portfolio revision is required.");
            if (portfolio.Revision != request.ExpectedPortfolioRevision.Value)
                throw new DbUpdateConcurrencyException("Service portfolio revision changed.");
        }
        else if (portfolioChoice == "New")
        {
            portfolio = new ServicePortfolio
            {
                ProviderId = profile.Id,
                Name = Required(Text(portfolioInput, "name"), "portfolio name", 256),
                Description = Bounded(Text(portfolioInput, "description"), "portfolio description", 8000) ?? "",
                CreatedBy = actor.ObjectId,
                UpdatedBy = actor.ObjectId
            };
            db.Add(portfolio);
        }
        else if (portfolioChoice is not ("Deferred" or "Unspecified"))
        {
            throw new ArgumentException("Choose a service portfolio or explicitly defer it.");
        }

        var offeringInput = Part(fields, "firstOffering");
        var offeringChoice = Text(offeringInput, "choice");
        ProviderOffering? offering = null;
        if (offeringChoice == "Existing")
        {
            if (!Guid.TryParse(Text(offeringInput, "offeringId"), out var offeringId))
                throw new ArgumentException("Select an existing offering.");
            offering = await db.Set<ProviderOffering>().SingleOrDefaultAsync(
                x => x.ProviderId == profile.Id && x.Id == offeringId, ct)
                ?? throw new KeyNotFoundException("Offering was not found.");
            ProviderAuthorizationStore.Expected(offering, request.ExpectedOfferingRevision
                ?? throw new DbUpdateConcurrencyException("Expected offering revision is required."));
        }
        else if (offeringChoice == "New")
        {
            offering = ProviderAuthorizationService.CreateOfferingRow(
                profile.Id, OfferingInput(offeringInput), actor.ObjectId);
            db.Add(offering);
            ProviderAuthorizationStore.Audit(db, offering, offering.Id, "OfferingCreated", actor.ObjectId);
        }
        else if (offeringChoice is not ("Deferred" or "Unspecified"))
        {
            throw new ArgumentException("Choose an offering or explicitly defer.");
        }

        if (portfolio is not null && offering is not null)
        {
            var active = await db.Set<ServicePortfolioOfferingRevision>().SingleOrDefaultAsync(
                x => x.ProviderId == profile.Id && x.OfferingId == offering.Id
                    && x.IsPrimary && x.State == "Active", ct);
            if (active is not null && active.PortfolioId != portfolio.Id)
                throw new DbUpdateConcurrencyException("The offering already has a different active primary portfolio.");
            if (active is null)
            {
                db.Add(new ServicePortfolioOfferingRevision
                {
                    ProviderId = profile.Id,
                    PortfolioId = portfolio.Id,
                    OfferingId = offering.Id,
                    IsPrimary = true,
                    State = "Active",
                    Reason = "Provider first-login setup",
                    CreatedBy = actor.ObjectId
                });
            }
        }

        var committed = JsonNode.Parse(draft.CommittedMetadataJson)!.AsObject();
        var updated = JsonNode.Parse(draft.DraftJson)!.AsObject();
        if (portfolio is not null)
        {
            committed["committedPortfolioId"] = portfolio.Id.ToString("D");
            updated["portfolio"]!["choice"] = "Existing";
            updated["portfolio"]!["portfolioId"] = portfolio.Id.ToString("D");
            updated["portfolio"]!["expectedRevision"] = portfolio.Revision;
        }
        if (offering is not null)
        {
            committed["committedOfferingId"] = offering.Id.ToString("D");
            updated["firstOffering"]!["choice"] = "Existing";
            updated["firstOffering"]!["offeringId"] = offering.Id.ToString("D");
            updated["firstOffering"]!["expectedRevision"] = offering.Revision;
        }
        draft.SchemaVersion = CurrentSchemaVersion;
        draft.CommittedMetadataJson = committed.ToJsonString();
        draft.DraftJson = updated.ToJsonString();
        return new SetupMutation(
            offering?.Id, offering?.Revision, portfolio?.Id, portfolio?.Revision);
    }

    private async Task<SetupMutation> CommitAuthorizationIntentAsync(
        AtoCopilotContext db, CspProfile profile, ProviderSetupDraft draft, JsonElement fields,
        CommitProviderSetup request, ProviderSetupActor actor, CancellationToken ct)
    {
        var input = Part(fields, "authorizationStartingPoint");
        var startingPoint = Text(input, "choice");
        if (startingPoint is not ("ExistingAuthorization" or "InitialAuthorization" or "DetermineLater"))
            throw new ArgumentException("Choose an authorization starting point.");

        var metadata = JsonNode.Parse(draft.CommittedMetadataJson)!.AsObject();
        Guid? offeringId = Guid.TryParse(metadata["committedOfferingId"]?.GetValue<string>(), out var parsed)
            ? parsed : null;
        var current = await db.Set<ProviderOfferingAuthorizationIntentRevision>()
            .Where(x => x.ProviderId == profile.Id && x.SetupId == draft.Id)
            .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
        var facts = CanonicalPart(input, "existingDecision", "{}");
        var sources = CanonicalPart(input, "sources", "[]");
        var unresolved = CanonicalPart(input, "unresolvedFields", "[]");
        if (current is not null
            && current.StartingPoint == startingPoint
            && current.OfferingId == offeringId
            && current.UnconfirmedFactsJson == facts
            && current.SourcesJson == sources
            && current.UnresolvedFieldsJson == unresolved)
        {
            metadata["committedAuthorizationIntentId"] = current.Id.ToString("D");
            draft.CommittedMetadataJson = metadata.ToJsonString();
            return new SetupMutation(offeringId, null, null, null, current.Id, current.Revision);
        }
        if (current is not null && request.ExpectedAuthorizationIntentRevision != current.Revision)
            throw new DbUpdateConcurrencyException("Authorization intent revision changed.");

        var row = new ProviderOfferingAuthorizationIntentRevision
        {
            ProviderId = profile.Id,
            SetupId = draft.Id,
            OfferingId = offeringId,
            PredecessorId = current?.Id,
            StartingPoint = startingPoint,
            UnconfirmedFactsJson = facts,
            SourcesJson = sources,
            UnresolvedFieldsJson = unresolved,
            Revision = (current?.Revision ?? 0) + 1,
            CreatedBy = actor.ObjectId
        };
        db.Add(row);
        metadata["committedAuthorizationIntentId"] = row.Id.ToString("D");
        draft.SchemaVersion = CurrentSchemaVersion;
        draft.CommittedMetadataJson = metadata.ToJsonString();
        return new SetupMutation(offeringId, null, null, null, row.Id, row.Revision);
    }

    private static string CanonicalPart(JsonElement value, string property, string fallback)
    {
        var part = Part(value, property);
        return part.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? fallback : Canonical(part);
    }

    private static string Required(string? value, string field, int max) =>
        string.IsNullOrWhiteSpace(value) || value.Length > max
            ? throw new ArgumentException($"A valid {field} is required.")
            : value.Trim();

    private static string? Bounded(string? value, string field, int max) =>
        value?.Length > max ? throw new ArgumentException($"{field} exceeds {max} characters.") : value;
}
