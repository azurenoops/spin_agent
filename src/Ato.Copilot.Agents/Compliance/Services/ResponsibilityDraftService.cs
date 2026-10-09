using System.Data;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class ResponsibilityDraftService(AtoCopilotContext db,
    ICapabilityResponsibilityService responsibilities, ISystemEnvironmentService environments,
    NarrativeLibraryService library, IResponsibilityDraftGenerator generator,
    ILogger<ResponsibilityDraftService> logger)
{
    public async Task<ResponsibilityDraftContext> GetForEnvironmentAsync(string systemId, string controlId,
        Guid? capabilityId, CancellationToken ct)
    {
        controlId = Control(controlId);
        await responsibilities.AuthorizeAsync(systemId, false, ct);
        var environment = await environments.ListAsync(systemId, ct);
        var assigned = environment.ProviderScopes.Where(x => x.State != "Removed").ToArray();
        var matches = assigned.Where(scope => scope.PublishedDuties.Capabilities.Any(capability =>
            (!capabilityId.HasValue || capability.CapabilityId == capabilityId)
            && MapsControl(capability, controlId))).ToArray();
        if (matches.Length == 0)
            matches = assigned.Where(scope => scope.PublishedDuties.State != "Available"
                || scope.PublishedDuties.Capabilities.Count == 0
                || capabilityId.HasValue && scope.PublishedDuties.Capabilities.Any(x => x.CapabilityId == capabilityId)).ToArray();
        var context = await GetAsync(systemId, controlId, matches.Length == 1 ? matches[0].AssignmentId : null, ct);
        return matches.Length > 1 ? context with {
            EnvironmentScopeIssue = "Several recorded provider contexts may apply to this control. This first pass uses system and environment records without choosing a provider. Verify provider-reliant allocation against the named contributions before confirming.",
        } : context;
    }

    public async Task<ResponsibilityDraftContext> GetAsync(string systemId, string controlId, Guid? scopeId, CancellationToken ct)
    {
        controlId = Control(controlId);
        var captured = await CaptureAsync(systemId, controlId, scopeId, ct);
        var key = scopeId?.ToString() ?? "";
        var row = await db.Set<ResponsibilityDraft>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == captured.TenantId && x.RegisteredSystemId == systemId && x.ControlId == controlId && x.ScopeKey == key, ct);
        return new(systemId, controlId, captured.BaselineId, scopeId,
            await responsibilities.AuthorizeAsync(systemId, false, ct), captured.Hash, captured.Scopes,
            captured.Sources, captured.Values, captured.Questions, captured.Conflicts,
            row is null ? null : await ResponseAsync(row, captured.Hash, ct));
    }

    public async Task<ResponsibilityDraftContext> PrepareAsync(string systemId, string controlId,
        PrepareResponsibilityDraftRequest request, string actor, CancellationToken ct)
    {
        Actor(actor);
        await responsibilities.AuthorizeAsync(systemId, true, ct);
        controlId = Control(controlId);
        var captured = await CaptureAsync(systemId, controlId, request.ScopeId, ct);
        var key = request.ScopeId?.ToString() ?? "";
        var row = await db.Set<ResponsibilityDraft>().SingleOrDefaultAsync(x => x.TenantId == captured.TenantId
            && x.RegisteredSystemId == systemId && x.ControlId == controlId && x.ScopeKey == key, ct);
        Expected(row?.Revision ?? 0, request.ExpectedRevision);
        var initial = row is null;
        var result = new ResponsibilityDraftSuggestion(captured.Values, captured.Questions, captured.Conflicts);
        string? failure = null;
        try
        {
            if (request.Generate)
                result = EnforceSources(captured, await generator.GenerateAsync(controlId, captured.Sources, ct));
        }
        catch (ResponsibilityGenerationException error)
        {
            logger.LogWarning("Responsibility first pass failed for system {SystemId}, control {ControlId}", systemId, controlId);
            failure = error.Message;
        }
        await responsibilities.AuthorizeAsync(systemId, true, ct);
        if ((await CaptureAsync(systemId, controlId, request.ScopeId, ct)).Hash != captured.Hash)
            throw new ResponsibilityReviewConflictException("Source records changed during preparation. Your prior draft was preserved.");
        result = result with { Values = result.Values.ToDictionary(x => x.Key, x => x.Value with { SourceHash = captured.Hash }) };
        if (initial)
        {
            row = new ResponsibilityDraft { TenantId = captured.TenantId, RegisteredSystemId = systemId,
                ControlId = controlId, ScopeKey = key, PreparedBy = actor, SourceHash = captured.Hash,
                SourceJson = Serialize(captured.Sources), ValuesJson = Serialize(result.Values),
                SuggestionJson = Serialize(result) };
            db.Add(row);
        }
        else
        {
            row!.Revision++;
            if (failure is null)
            {
                row.SourceHash = captured.Hash;
                row.SourceJson = Serialize(captured.Sources);
                row.SuggestionJson = Serialize(result);
                row.Status = "ComparisonRequired";
                row.ReviewedAt = null; row.ReviewedBy = null;
            }
        }
        row!.GenerationState = failure is not null ? "Failed" : request.Generate ? "Prepared" : "NotRequested";
        row.GenerationError = failure;
        if (failure is null && request.Generate) row.GeneratedAt = DateTimeOffset.UtcNow;
        AddHistory(row, actor, failure is null ? "SuggestionPrepared" : "GenerationFailed", includeSource: true,
            attemptSource: failure is not null ? captured : null);
        await NarrativePersistence.SaveAsync(db, ct);
        return await GetAsync(systemId, controlId, request.ScopeId, ct);
    }

    public async Task<ResponsibilityDraftResponse> SaveAsync(string systemId, Guid id,
        SaveResponsibilityDraftRequest request, string actor, CancellationToken ct)
    {
        Actor(actor);
        await responsibilities.AuthorizeAsync(systemId, true, ct);
        var row = await OwnedAsync(systemId, id, ct);
        Expected(row.Revision, request.ExpectedRevision);
        ValidateValues(request.Values);
        var current = Read<Dictionary<string, ResponsibilityDraftValue>>(row.ValuesJson);
        var suggested = Read<ResponsibilityDraftSuggestion>(row.SuggestionJson).Values;
        var next = request.Values.ToDictionary(x => x.Key, x =>
        {
            var basis = request.ApplySuggestion && suggested.TryGetValue(x.Key, out var suggestion) && suggestion.Value == x.Value
                ? suggestion : current.GetValueOrDefault(x.Key) ?? new("", "From system records", [], SourceHash: row.SourceHash);
            return basis with { Value = x.Value.Trim(), UserEdited = basis.UserEdited || basis.Value != x.Value.Trim() };
        });
        row.ValuesJson = Serialize(next);
        row.Revision++;
        if (request.ApplySuggestion || row.Status == "Accepted") row.Status = "Proposed";
        row.ReviewedAt = null; row.ReviewedBy = null;
        AddHistory(row, actor, request.ApplySuggestion ? "SuggestionCompared" : "DraftEdited");
        await NarrativePersistence.SaveAsync(db, ct);
        return await ResponseAsync(row, (await CaptureAsync(systemId, row.ControlId, Scope(row), ct)).Hash, ct);
    }

    public Task<ResponsibilityDraftResponse> ConfirmAsync(string systemId, Guid id,
        ConfirmResponsibilityDraftRequest request, string actor, CancellationToken ct) =>
        db.Database.CurrentTransaction is null
            ? db.Database.CreateExecutionStrategy().ExecuteAsync(() => ConfirmAttemptAsync(systemId, id, request, actor, ct))
            : ConfirmAttemptAsync(systemId, id, request, actor, ct);

    private async Task<ResponsibilityDraftResponse> ConfirmAttemptAsync(string systemId, Guid id,
        ConfirmResponsibilityDraftRequest request, string actor, CancellationToken ct)
    {
        Actor(actor);
        if (db.Database.CurrentTransaction is null) db.ChangeTracker.Clear();
        await responsibilities.AuthorizeAsync(systemId, true, ct);
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var row = await OwnedAsync(systemId, id, ct);
        Expected(row.Revision, request.ExpectedRevision);
        var captured = await CaptureAsync(systemId, row.ControlId, Scope(row), ct);
        if (row.Status != "Proposed" || row.SourceHash != request.SourceHash || captured.Hash != row.SourceHash)
            throw new ResponsibilityReviewConflictException("Draft or sources changed. Compare a refreshed suggestion before confirming.");
        if (!request.ProviderCoverageVerified || !request.CustomerDutiesReviewed
            || string.IsNullOrWhiteSpace(request.ReviewNotes) || request.ReviewNotes.Length > 2000)
            throw new ArgumentException("Review coverage, local duties and a 1-2000 character rationale before confirming.");
        var values = Read<Dictionary<string, ResponsibilityDraftValue>>(row.ValuesJson);
        var allocation = values["allocation"].Value;
        if (allocation == "NeedsConfirmation") throw new ArgumentException("Resolve the allocation before confirmation.");
        if (string.IsNullOrWhiteSpace(values["basis"].Value)
            || allocation is "Inherited" or "Shared" && string.IsNullOrWhiteSpace(values["providerDuties"].Value)
            || allocation == "Inherited" && new[] { "scope", "exclusions", "source" }.Any(key => string.IsNullOrWhiteSpace(values[key].Value)))
            throw new ArgumentException("Complete the allocation basis, applicable provider duties, scope, exclusions and supporting source before confirming.");
        var input = new CapabilityResponsibilityAllocation(row.ControlId, allocation,
            Empty(values["provider"].Value), Empty(values["customer"].Value));
        if (Scope(row) is { } scopeId)
        {
            var subscriptions = await (from adoption in db.Set<Ato.Copilot.Core.Models.ProviderAuthorizations.CapabilityAdoptionSnapshot>()
                join subscription in db.CapabilitySubscriptions on adoption.SubscriptionId equals subscription.Id
                where adoption.TenantId == row.TenantId && adoption.SystemId == systemId && adoption.AssignmentId == scopeId
                    && subscription.RegisteredSystemId == systemId && subscription.IsActive && subscription.CurrentAdoptionSnapshotId == adoption.Id
                select subscription.Id).ToArrayAsync(ct);
            var preview = await responsibilities.PreviewAsync(systemId, ct);
            var targets = preview.Items.Where(x => x.ControlId == row.ControlId && subscriptions.Contains(x.SubscriptionId))
                .Select(x => x.CapabilityId).Distinct().ToArray();
            if (targets.Length == 0) throw new ResponsibilityReviewConflictException("Adopt the applicable capability through the existing scope workflow before confirming provider inheritance.");
            foreach (var capabilityId in targets)
            {
                preview = await responsibilities.PreviewAsync(systemId, ct);
                var source = preview.Items.Single(x => x.ControlId == row.ControlId && x.CapabilityId == capabilityId);
                await responsibilities.ConfirmAsync(systemId, capabilityId, new(preview.BaselineId!,
                    source.SourceRevision, source.ReviewRevision, [input], true, true, request.ReviewNotes), actor, ct);
            }
        }
        else await responsibilities.ConfirmSystemAllocationAsync(systemId, captured.BaselineId, input, actor, ct);
        var acceptedSource = await CaptureAsync(systemId, row.ControlId, Scope(row), ct);
        if (NonAllocationInputs(captured) != NonAllocationInputs(acceptedSource))
            throw new ResponsibilityReviewConflictException("Source inputs changed during confirmation. No responsibility change was committed.");
        row.SourceHash = acceptedSource.Hash;
        row.SourceJson = Serialize(acceptedSource.Sources);
        row.Status = "Accepted"; row.Revision++;
        row.ReviewedAt = DateTimeOffset.UtcNow; row.ReviewedBy = actor;
        AddHistory(row, actor, "ResponsibilityConfirmed", includeSource: true, request.ReviewNotes);
        await NarrativePersistence.SaveAsync(db, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await ResponseAsync(row, row.SourceHash, ct);
    }

    public async Task<object> HistoryAsync(string systemId, Guid id, int offset, CancellationToken ct)
    {
        await responsibilities.AuthorizeAsync(systemId, false, ct);
        if (offset < 0) throw new ArgumentException("History offset must not be negative.");
        var row = await OwnedAsync(systemId, id, ct);
        return await db.Set<ResponsibilityDraftHistory>().AsNoTracking().Where(x => x.TenantId == row.TenantId && x.DraftId == id)
            .OrderByDescending(x => x.Revision).Skip(offset).Take(20).ToListAsync(ct);
    }
    private async Task<ResponsibilityDraft> OwnedAsync(string systemId, Guid id, CancellationToken ct)
    {
        var owner = await db.RegisteredSystems.Where(x => x.Id == systemId).Select(x => x.TenantId).SingleAsync(ct);
        return await db.Set<ResponsibilityDraft>().SingleOrDefaultAsync(x =>
            x.Id == id && x.TenantId == owner && x.RegisteredSystemId == systemId, ct)
            ?? throw new KeyNotFoundException("Responsibility draft not found.");
    }
    private async Task<ResponsibilityDraftResponse> ResponseAsync(ResponsibilityDraft row, string currentHash, CancellationToken ct)
    {
        var history = await db.Set<ResponsibilityDraftHistory>().AsNoTracking()
            .Where(x => x.TenantId == row.TenantId && x.DraftId == row.Id).OrderByDescending(x => x.Revision).Take(20)
            .Select(x => new { x.Id, x.Revision, x.Action, x.Actor, x.At, x.SourceHash }).ToListAsync(ct);
        return new(row.Id, row.Revision, row.Status, row.SourceHash, row.SourceHash != currentHash,
            row.GenerationState, row.GenerationError, row.PreparedAt, row.GeneratedAt, row.PreparedBy,
            row.ReviewedBy, row.ReviewedAt, Read<Dictionary<string, ResponsibilityDraftValue>>(row.ValuesJson),
            Read<ResponsibilityDraftSuggestion>(row.SuggestionJson), Read<List<ResponsibilityDraftSource>>(row.SourceJson),
            JsonSerializer.SerializeToElement(history, Json));
    }
    private void AddHistory(ResponsibilityDraft row, string actor, string action, bool includeSource = false,
        string? reviewNotes = null, Captured? attemptSource = null) =>
        db.Add(new ResponsibilityDraftHistory { TenantId = row.TenantId, DraftId = row.Id, Revision = row.Revision,
            Actor = actor, Action = action, SourceHash = attemptSource?.Hash ?? row.SourceHash, ValuesJson = row.ValuesJson,
            SourceJson = includeSource ? attemptSource is null ? row.SourceJson : Serialize(attemptSource.Sources) : null,
            ReviewNotes = reviewNotes });
    private static void ValidateValues(Dictionary<string, string> values)
    {
        if (values is null || values.Count != ResponsibilityDraftGenerator.FieldNames.Length
            || ResponsibilityDraftGenerator.FieldNames.Any(x => !values.TryGetValue(x, out var value)
                || value is null || value.Length > (x == "provider" ? 200 : 2000))
            || !ResponsibilityDraftGenerator.IsAllocation(values["allocation"]))
            throw new ArgumentException("Supply all responsibility fields within their length limits and a supported allocation.");
    }
    private static void Expected(long actual, long expected)
    {
        if (actual != expected) throw new ResponsibilityReviewConflictException("The proposed draft changed. Reload and compare before saving.");
    }
    private static Guid? Scope(ResponsibilityDraft row) => row.ScopeKey.Length == 0 ? null : Guid.Parse(row.ScopeKey);
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Actor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200) throw new ArgumentException("A verified reviewer identity is required.");
    }
}
