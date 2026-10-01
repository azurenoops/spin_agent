using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

public sealed partial class ProviderSetupService
{
    public Task<ProviderSetupCommandResult> SaveAsync(SaveProviderSetupDraft request, string key, ProviderSetupActor actor, CancellationToken ct)
    {
        ValidateDraft(request.Draft);
        return CommandAsync("SaveDraft", request.ExpectedRevision, request, key, actor, async (db, profile, draft) =>
        {
            var registered = await db.Set<CspPackageUploadIntent>().Where(x => x.ProviderId == profile.Id && x.DraftId == draft.Id).Select(x => x.Id).ToListAsync(ct);
            var sources = Part(request.Draft, "sources");
            var ids = Part(sources, "intentIds");
            var selected = ids.ValueKind == JsonValueKind.Array ? ids.EnumerateArray().Select(x => x.GetGuid()).ToHashSet() : [];
            if (registered.Any(id => !selected.Contains(id)) || selected.Any(id => !registered.Contains(id)))
                throw new DbUpdateConcurrencyException("Retain all registered upload attempts; receipt uncertainty cannot be silently removed.");
            if (registered.Count > 0 && Text(sources, "choice") != "Intents")
                throw new DbUpdateConcurrencyException("Registered source attempts cannot be converted to deferral.");
            draft.DraftJson = Canonical(request.Draft);
            return (null, null);
        }, ct);
    }

    public Task<ProviderSetupCommandResult> CommitAsync(CommitProviderSetup request, string key, ProviderSetupActor actor, CancellationToken ct)
    {
        if (request.Section is not ("Details" or "Contacts" or "FirstOffering")) throw new ArgumentException("Unknown setup section.");
        return CommandAsync($"Commit{request.Section}", request.ExpectedRevision, request, key, actor, async (db, profile, draft) =>
        {
            var fields = Element(draft.DraftJson);
            if (profile.SetupRevision != request.ExpectedProfileRevision) throw new DbUpdateConcurrencyException("Provider profile revision changed.");
            if (request.Section == "Details")
            {
                if (profile.OnboardingState == OnboardingState.Active) throw new CspAlreadyOnboardedException();
                var details = Part(fields, "details");
                CspProfileService.ApplyIdentity(profile, Text(details, "legalEntityName") ?? "", Text(details, "displayName") ?? "", Text(details, "logoUrl"));
                CspProfileService.ApplySupport(profile, Text(details, "serviceContactEmail") ?? "", Text(details, "supportPhone"));
                if (Part(details, "confirmLegacyClassificationDefault").ValueKind != JsonValueKind.True
                    || !Enum.TryParse<ClassificationLevel>(Text(details, "legacyClassificationDefault"), out var classification))
                    throw new ArgumentException("Explicitly confirm the deployment's legacy default; this does not authorize source handling.");
                CspProfileService.ApplyClassification(profile, classification);
                profile.SetupRevision++;
                profile.UpdatedAt = DateTimeOffset.UtcNow;
                profile.UpdatedBy = actor.ObjectId;
                profile.OnboardingState = OnboardingState.InWizard;
                var metadata = JsonNode.Parse(draft.CommittedMetadataJson)!.AsObject();
                metadata["serviceContactName"] = Text(details, "serviceContactName");
                draft.CommittedMetadataJson = metadata.ToJsonString();
            }
            if (request.Section == "Contacts")
            {
                var contact = Part(fields, "securityContact");
                if (Text(contact, "choice") is not ("ContactOnly" or "Deferred"))
                    throw new ArgumentException("Save a contact-only security reviewer or explicitly defer. Contact details do not establish access.");
                if (Text(contact, "choice") == "ContactOnly"
                    && (string.IsNullOrWhiteSpace(Text(contact, "displayName")) || Text(contact, "email")?.Contains('@') != true))
                    throw new ArgumentException("Record the security review contact name and email, or explicitly defer.");
                var metadata = JsonNode.Parse(draft.CommittedMetadataJson)!.AsObject();
                metadata["securityContact"] = JsonNode.Parse(contact.GetRawText());
                draft.CommittedMetadataJson = metadata.ToJsonString();
            }
            if (request.Section != "FirstOffering") return (null, null);
            var offering = Part(fields, "firstOffering");
            var choice = Text(offering, "choice");
            if (choice == "Deferred") return (null, null);
            ProviderOffering row;
            if (choice == "Existing")
            {
                if (!Guid.TryParse(Text(offering, "offeringId"), out var id)) throw new ArgumentException("Select an existing offering.");
                row = await db.Set<ProviderOffering>().SingleOrDefaultAsync(x => x.ProviderId == profile.Id && x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Offering was not found.");
                ProviderAuthorizationStore.Expected(row, request.ExpectedOfferingRevision ?? 0);
            }
            else if (choice == "New")
            {
                var input = OfferingInput(offering);
                row = ProviderAuthorizationService.CreateOfferingRow(profile.Id, input, actor.ObjectId);
                db.Add(row);
                ProviderAuthorizationStore.Audit(db, row, row.Id, "OfferingCreated", actor.ObjectId);
            }
            else throw new ArgumentException("Choose an offering or explicitly defer.");
            var committed = JsonNode.Parse(draft.CommittedMetadataJson)!.AsObject();
            committed["committedOfferingId"] = row.Id.ToString("D");
            draft.CommittedMetadataJson = committed.ToJsonString();
            var updated = JsonNode.Parse(draft.DraftJson)!.AsObject();
            updated["firstOffering"]!["choice"] = "Existing";
            updated["firstOffering"]!["offeringId"] = row.Id.ToString("D");
            updated["firstOffering"]!["expectedRevision"] = row.Revision;
            draft.DraftJson = updated.ToJsonString();
            return (row.Id, row.Revision);
        }, ct);
    }

    private static CreateProviderOfferingRequest OfferingInput(JsonElement offering)
    {
        var environments = Part(offering, "environments").ValueKind == JsonValueKind.Array
            ? Part(offering, "environments").EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
        var description = Part(offering, "serviceDescription").ValueKind == JsonValueKind.Object
            ? Part(offering, "serviceDescription").Deserialize<ProviderServiceDescription>(JsonOptions) : null;
        var serviceModel = Text(offering, "serviceModel");
        var management = Text(offering, "managementArrangement");
        if (description is not null)
        {
            if (description.EnvironmentKind is not ("AzureCommercial" or "AzureGovernment" or "AwsGovCloud" or "Microsoft365DoD" or "Other")
                || description.ServiceModel is not (null or "InfrastructureShared" or "Platform" or "Software" or "BrokeredHosting")
                || description.ManagedBy is not (null or "Provider" or "SharedOperations" or "MissionOwner")
                || description.DeclaredImpactLevel is not (null or "IL2" or "IL4" or "IL5" or "IL6" or "Other"))
                throw new ArgumentException("Invalid declared service description.");
            ProviderAuthorizationStore.Text(description.EnvironmentLabel, "environment label", 256, description.EnvironmentKind == "Other");
            ProviderAuthorizationStore.Text(description.IntendedUse, "intended use", 2000, false);
            ProviderAuthorizationStore.Text(description.DeclaredImpactText, "declared impact", 256, description.DeclaredImpactLevel == "Other");
            var environment = description.EnvironmentKind switch
            {
                "AzureCommercial" => "AzureCloud",
                "AzureGovernment" => "AzureUSGovernment",
                "Microsoft365DoD" => "Microsoft365DoD",
                _ => "ManualService"
            };
            var declaredOnly = description.EnvironmentKind is "AwsGovCloud" or "Microsoft365DoD" or "Other";
            if (!(declaredOnly && environments.Length == 0) && !environments.SequenceEqual([environment]))
                throw new ArgumentException("The declared environment must match the offering environment; no Azure scope is inferred.");
            environments = [environment];
            var declaredModel = description.ServiceModel switch
            {
                "InfrastructureShared" => "InfrastructureSharedServices",
                "Platform" => "PlatformService",
                "Software" => "SoftwareAsAService",
                "BrokeredHosting" => "BrokeredCloudSpace",
                _ => null
            };
            var declaredManagement = description.ManagedBy switch
            {
                "Provider" => "ProviderManaged",
                "SharedOperations" => "SharedOperations",
                "MissionOwner" => "MissionOwnerManaged",
                _ => null
            };
            if (serviceModel is not null && declaredModel is not null && serviceModel != declaredModel
                || management is not null && declaredManagement is not null && management != declaredManagement)
                throw new ArgumentException("The declared service identity must match the offering identity.");
            serviceModel ??= declaredModel;
            management ??= declaredManagement;
        }
        return new(Text(offering, "name") ?? "", Text(offering, "description") ?? "", environments)
        {
            ServiceModel = serviceModel, ManagementArrangement = management,
            ServiceOwner = Text(offering, "serviceOwner"), SecurityContact = Text(offering, "securityContact")
        };
    }

    public Task<ProviderSetupCommandResult> CompleteAsync(CompleteProviderSetup request, string key, ProviderSetupActor actor, CancellationToken ct)
    {
        if (!request.Confirmed) throw new ArgumentException("Confirm the provider setup before completion.");
        return CommandAsync("Complete", request.ExpectedRevision, request, key, actor, async (db, profile, draft) =>
        {
            if (profile.SetupRevision != request.ExpectedProfileRevision) throw new DbUpdateConcurrencyException("Provider profile revision changed.");
            await RequireSourcesAsync(db, draft, request.AcknowledgedUnresolvedIntentIds ?? [], ct);
            var fields = Element(draft.DraftJson);
            var contact = Part(fields, "securityContact");
            if (Text(contact, "choice") is not ("Deferred" or "ContactOnly")
                || Text(contact, "choice") == "ContactOnly" && Part(Element(draft.CommittedMetadataJson), "securityContact").ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Save a review contact or explicitly defer it before completion.");
            var offering = Part(fields, "firstOffering");
            if (Text(offering, "choice") != "Deferred")
            {
                if (Text(offering, "choice") != "Existing" || !Guid.TryParse(Text(offering, "offeringId"), out var offeringId)
                    || !await db.Set<ProviderOffering>().AnyAsync(x => x.ProviderId == profile.Id && x.Id == offeringId, ct))
                    throw new ArgumentException("Save or select an existing offering, or explicitly defer it before completion.");
            }
            if (profile.IdentityCompletedAt is null || profile.SupportCompletedAt is null || profile.ClassificationCompletedAt is null)
                throw new ArgumentException("Save provider identity, support contact and explicit deployment default before completion.");
            var now = DateTimeOffset.UtcNow;
            if (profile.OnboardingState != OnboardingState.Active)
            {
                profile.OnboardingState = OnboardingState.Active;
                profile.OnboardingCompletedAt = now;
                profile.UpdatedAt = now;
                profile.UpdatedBy = actor.ObjectId;
                profile.SetupRevision++;
            }
            draft.CompletionSnapshotJson = Json(new { completedAt = now, profileRevision = profile.SetupRevision,
                draftRevision = draft.Revision + 1, acknowledgedUnresolvedIntentIds = request.AcknowledgedUnresolvedIntentIds });
            var updated = JsonNode.Parse(draft.DraftJson)!.AsObject();
            updated["currentScreen"] = "p-ready";
            draft.DraftJson = updated.ToJsonString();
            return (null, null);
        }, ct);
    }

    private async Task<ProviderSetupCommandResult> CommandAsync(string operation, long expectedRevision, object request,
        string key, ProviderSetupActor actor, Func<AtoCopilotContext, CspProfile, ProviderSetupDraft, Task<(Guid?, long?)>> change, CancellationToken ct)
    {
        Authorize();
        if (!Guid.TryParseExact(key, "D", out _)) throw new ArgumentException("Use a stable UUID Idempotency-Key.");
        var requestJson = Canonical(Element(Json(request)));
        var hash = Hash(requestJson);
        await using var strategy = await factory.CreateDbContextAsync(ct);
        var committed = await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var profile = await ProfileAsync(db, ct);
            if (profile is null)
            {
                profile = new CspProfile { LegalEntityName = "Pending", DisplayName = "Pending",
                    CreatedBy = actor.ObjectId, OnboardingState = OnboardingState.InWizard };
                db.Add(profile);
            }
            var previous = await db.Set<ProviderSetupCommand>().SingleOrDefaultAsync(x =>
                x.ProviderId == profile.Id && x.Operation == operation && x.IdempotencyKey == key, ct);
            if (previous is not null)
            {
                if (previous.IntentHash != hash) throw new DbUpdateConcurrencyException("SETUP_INTENT_CONFLICT: The key identifies different saved input.");
                return (Command: previous, Replayed: true);
            }
            var draft = await db.Set<ProviderSetupDraft>().SingleOrDefaultAsync(x => x.ProviderId == profile.Id, ct);
            if (expectedRevision != (draft?.Revision ?? 0)) throw new DbUpdateConcurrencyException("SETUP_REVISION_CONFLICT: Reload before reconciling your edits.");
            if (draft is null)
            {
                if (operation != "SaveDraft") throw new ArgumentException("Save a provider setup draft first.");
                draft = new ProviderSetupDraft { ProviderId = profile.Id, CreatedBy = actor.ObjectId, UpdatedBy = actor.ObjectId, Revision = 0 };
                db.Add(draft);
            }
            var (offeringId, offeringRevision) = await change(db, profile, draft);
            draft.Revision++;
            draft.UpdatedAt = DateTimeOffset.UtcNow;
            draft.UpdatedBy = actor.ObjectId;
            var command = new ProviderSetupCommand { ProviderId = profile.Id, DraftId = draft.Id,
                Operation = operation, IdempotencyKey = key, IntentHash = hash, RequestJson = requestJson,
                CommittedDraftRevision = draft.Revision, CreatedBy = actor.ObjectId };
            command.OutcomeJson = Json(new ProviderSetupCommitOutcome(command.Id, profile.Id, draft.Id, operation,
                command.CreatedAt, draft.Revision, profile.SetupRevision, offeringId, offeringRevision));
            command.HistoricalCommitSnapshotJson = Json(new { committedBy = actor,
                factReferences = new[] { new { kind = "ProviderSetupDraft", id = draft.Id, revision = draft.Revision } } });
            db.Add(command);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            cache.Remove(CspProfileService.CacheKey);
            logger.LogInformation("ProviderSetup.CommandCommitted operation={Operation} provider={ProviderId} revision={Revision}",
                operation, profile.Id, draft.Revision);
            return (Command: command, Replayed: false);
        });
        return await ResultAsync(committed.Command, committed.Replayed, actor, ct);
    }

    public async Task<ProviderSetupCommandResult> ReconcileAsync(ReconcileProviderSetup request, ProviderSetupActor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var profile = await ProfileAsync(db, ct);
        var command = profile is null ? null : await db.Set<ProviderSetupCommand>().SingleOrDefaultAsync(x =>
            x.ProviderId == profile.Id && x.Operation == request.Operation && x.IdempotencyKey == request.RequestKey, ct);
        return await ResultAsync(command, command is not null, actor, ct);
    }

    private async Task<ProviderSetupCommandResult> ResultAsync(ProviderSetupCommand? command, bool replayed, ProviderSetupActor actor, CancellationToken ct)
    {
        Authorize();
        object current;
        try { current = new { projectionState = "Available", evaluatedAt = DateTimeOffset.UtcNow,
            actor, access = Access(actor), state = await StateAsync(actor, ct) }; }
        catch (Exception error) when (error is InvalidDataException or JsonException or System.Data.Common.DbException)
        {
            logger.LogError(error, "ProviderSetup.ProjectionUnavailable command={CommandId}", command?.Id);
            current = new { projectionState = "Unavailable", evaluatedAt = DateTimeOffset.UtcNow,
                actor, access = Access(actor), state = (object?)null,
                error = new { errorCode = "SETUP_PROJECTION_UNAVAILABLE", message = "The command outcome is retained; current state is unavailable.", suggestion = "Retry the state read; do not repeat the mutation." } };
        }
        return new(command is null ? "NotFound" : "Committed", replayed,
            command is null ? null : Read<ProviderSetupCommitOutcome>(command.OutcomeJson),
            command is null ? null : Element(command.HistoricalCommitSnapshotJson), current);
    }

    internal static async Task RequireLegacyCompletionAsync(AtoCopilotContext db, Guid provider, CancellationToken ct)
    {
        var draft = await db.Set<ProviderSetupDraft>().SingleOrDefaultAsync(x => x.ProviderId == provider, ct);
        if (draft is null) return;
        try { await RequireSourcesAsync(db, draft, [], ct); }
        catch (Exception error) when (error is ArgumentException or DbUpdateConcurrencyException)
        {
            throw new CspOnboardingIncompleteException(["Provider source disposition: resume setup to review the retained source request"]);
        }
    }

    private static async Task RequireSourcesAsync(AtoCopilotContext db, ProviderSetupDraft draft, IReadOnlyList<Guid> acknowledged, CancellationToken ct)
    {
        var sources = Part(Element(draft.DraftJson), "sources");
        if (Text(sources, "choice") is not ("Deferred" or "Intents"))
            throw new ArgumentException("Explicitly defer sources or retain an upload intent before completion.");
        if (Part(sources, "selection").ValueKind == JsonValueKind.Object)
            throw new ArgumentException("Upload or explicitly discard the unsubmitted file selection before completing setup.");
        var intents = await db.Set<CspPackageUploadIntent>().Where(x => x.ProviderId == draft.ProviderId && x.DraftId == draft.Id).ToListAsync(ct);
        var unresolved = new List<Guid>();
        foreach (var intent in intents)
            if (!await db.CspPackages.AnyAsync(x => x.ProviderId == draft.ProviderId && x.IdempotencyKey == intent.IdempotencyKey, ct))
                unresolved.Add(intent.Id);
        if (!unresolved.Order().SequenceEqual(acknowledged.Distinct().Order()))
            throw new DbUpdateConcurrencyException("Acknowledge each current unresolved source intent; receipt state remains unknown.");
    }

    private static void ValidateDraft(JsonElement draft)
    {
        if (draft.ValueKind != JsonValueKind.Object || draft.GetRawText().Length > 262144) throw new ArgumentException("Invalid or oversized setup draft.");
        if (Text(draft, "currentScreen") is not ("p-details" or "p-access" or "p-offering" or "p-sources" or "p-uncertain" or "p-review" or "p-ready"))
            throw new ArgumentException("Select a provider setup screen.");
        foreach (var section in new[] { "details", "securityContact", "firstOffering", "sources" })
            if (Part(draft, section).ValueKind != JsonValueKind.Object) throw new ArgumentException($"The {section} section is required.");
        foreach (var (name, max) in new[] { ("displayName", 64), ("legalEntityName", 256), ("serviceContactName", 256),
            ("serviceContactEmail", 254), ("supportPhone", 40), ("logoUrl", 2048) })
            if (Text(Part(draft, "details"), name)?.Length > max) throw new ArgumentException($"{name} exceeds {max} characters.");
        foreach (var section in new[] { "securityContact", "firstOffering", "sources" })
        {
            var value = Part(draft, section);
            if (Text(value, "choice") == "Deferred")
            {
                var reason = Text(Part(value, "deferral"), "reason");
                if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new ArgumentException("A bounded explicit deferral reason is required.");
            }
            if (Text(Part(draft, "securityContact"), "choice") is not ("Unspecified" or "ContactOnly" or "Deferred" or "ExistingIdentity")
                || Text(Part(draft, "firstOffering"), "choice") is not ("Unspecified" or "New" or "Existing" or "Deferred")
                || Text(Part(draft, "sources"), "choice") is not ("Unspecified" or "Selected" or "Intents" or "Deferred"))
                throw new ArgumentException("Invalid provider setup disposition.");
            var intentIds = Part(Part(draft, "sources"), "intentIds");
            if (intentIds.ValueKind != JsonValueKind.Array || intentIds.GetArrayLength() > 25
                || intentIds.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String || !value.TryGetGuid(out _)))
                throw new ArgumentException("Source intent IDs must be a bounded UUID array.");
            var contact = Part(draft, "securityContact");
            if (Text(contact, "displayName")?.Length > 256 || Text(contact, "email")?.Length > 254)
                throw new ArgumentException("Security contact details exceed the allowed length.");
        }
        if (Text(Part(draft, "securityContact"), "choice") == "ExistingIdentity")
            throw new ArgumentException("Identity discovery must be verified by an authorized identity workflow; save contact-only details instead.");
    }

    private static string Canonical(JsonElement element)
    {
        object? Normalize(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)
                .ToDictionary(x => x.Name, x => Normalize(x.Value), StringComparer.Ordinal),
            JsonValueKind.Array => value.EnumerateArray().Select(Normalize).ToArray(),
            _ => value
        };
        return Json(Normalize(element));
    }
}
