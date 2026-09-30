using System.Globalization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.Monitoring;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed record SaveProviderMonitoringRuleRequest(long? ExpectedRevision, string Name, string Signal, Guid SourceId,
    MonitoringCondition Condition, int CadenceMinutes, string OwnerId, string Response, bool IsEnabled,
    string? ExpectedSourceRevision = null);
public sealed record ProviderMonitoringSource(Guid SourceId, string Signal, string Name, string CollectionHealth,
    string SourceRevision, string Field, string? Value, DateTimeOffset? SourceTimestamp, string SnapshotJson);
public sealed record ProviderMonitoringWorkspace(Guid OfferingId, string OfferingName,
    IReadOnlyList<ProviderMonitoringSource> Sources, IReadOnlyList<ProviderMonitoringRule> Rules,
    IReadOnlyList<ProviderMonitoringEvaluation> Evaluations);

/// <summary>Provider-owned rules over retained, reviewed facts. No cloud collection or mission mutations.</summary>
public sealed partial class ProviderMonitoringService(ProviderAuthorizationStore store)
{
    public async Task<ProviderMonitoringWorkspace> WorkspaceAsync(Guid offeringId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        return new(offeringId, offering.Name, await SourcesAsync(db, offering, ct),
            await db.Set<ProviderMonitoringRule>().AsNoTracking().Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offeringId).ToListAsync(ct),
            (await db.Set<ProviderMonitoringEvaluation>().AsNoTracking().Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offeringId)
                .ToListAsync(ct)).OrderByDescending(x => x.CreatedAt).ToList());
    }

    public Task<ProviderMonitoringRule> SaveAsync(Guid offeringId, Guid? ruleId, SaveProviderMonitoringRuleRequest input,
        string key, string actor, CancellationToken ct) =>
        store.WriteAsync(offeringId, $"MonitoringRule:{offeringId}:{ruleId}", key, input, actor, async (db, provider, offering) =>
        {
            if (input.Signal is not ("AuthorizationExpiry" or "AuthorizationWithdrawal" or "EvidenceFreshness" or "PublishedReleaseChange")
                || input.Response != "CreateProviderImpactReview" || input.SourceId == Guid.Empty || input.CadenceMinutes is < 1 or > 10080)
                throw new ArgumentException("Choose a retained provider signal, concrete source, provider-review response, and cadence of 1–10080 minutes.");
            var condition = MonitoringConditionEvaluator.Parse(Json(input.Condition));
            if (condition.Field != Field(input.Signal))
                throw new ArgumentException($"The {input.Signal} signal supplies only {Field(input.Signal)}.");
            if (input.Signal is "AuthorizationExpiry" or "EvidenceFreshness")
            {
                if (condition.Operator is not ("LessThanOrEqual" or "GreaterThanOrEqual") ||
                    !int.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days is < 0 or > 3650)
                    throw new ArgumentException("Date-age conditions require a numeric comparator and a 0–3650 day threshold.");
                condition = condition with { Value = days.ToString(CultureInfo.InvariantCulture) };
            }
            else
            {
                if (condition.Operator is not ("Equals" or "NotEquals") || !bool.TryParse(condition.Value, out var state))
                    throw new ArgumentException("State-change conditions require Equals/NotEquals with true or false.");
                condition = condition with { Value = state.ToString().ToLowerInvariant() };
            }
            var rule = ruleId.HasValue
                ? await db.Set<ProviderMonitoringRule>().SingleOrDefaultAsync(x => x.Id == ruleId && x.ProviderId == provider && x.OfferingId == offeringId, ct)
                    ?? throw new KeyNotFoundException("Rule not found in this offering.")
                : new ProviderMonitoringRule { ProviderId = provider, OfferingId = offeringId, CreatedBy = actor };
            if (ruleId.HasValue) Expected(rule, input.ExpectedRevision ?? 0);
            var source = await SourceAsync(db, offering!, input.Signal, input.SourceId, ct);
            if (source.SourceRevision.Length == 0 && (!ruleId.HasValue || input.SourceId != rule.SourceId))
                throw new KeyNotFoundException("Source not found in this offering.");
            if (input.IsEnabled && source.CollectionHealth != "Available")
                throw new ArgumentException("A reviewed, available source fact is required before enabling this rule.");
            if (input.ExpectedSourceRevision is not null && input.ExpectedSourceRevision != source.SourceRevision)
                throw new DbUpdateConcurrencyException("The selected source changed; reload and review it before saving.");
            if (ruleId.HasValue) rule.Revision++;
            else db.Add(rule);
            rule.Name = Text(input.Name, "rule name", 200);
            rule.OwnerId = Text(input.OwnerId, "provider owner", 254);
            rule.Signal = input.Signal;
            rule.SourceId = input.SourceId;
            rule.ConditionJson = Json(condition);
            rule.CadenceMinutes = input.CadenceMinutes;
            rule.Response = input.Response;
            rule.IsEnabled = input.IsEnabled;
            rule.BaselineJson = source.SnapshotJson;
            rule.BaselineHash = source.SourceRevision;
            rule.UpdatedBy = actor;
            rule.NextEvaluationUtcTicks = 0;
            db.Add(new ProviderMonitoringEvaluation
            {
                ProviderId = provider, OfferingId = offeringId, CreatedBy = actor, RuleId = rule.Id, RuleRevision = rule.Revision,
                ObservationHash = Hash("Configured"), Outcome = "Configured", CollectionHealth = source.CollectionHealth,
                RuleSnapshotJson = Json(rule), SourceSnapshotJson = Json(source)
            });
            return rule;
        }, ct);

    public async Task<ProviderMonitoringEvaluation> TestAsync(Guid offeringId, Guid ruleId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var rule = await RuleAsync(db, offering, ruleId, ct);
        return Evaluate(rule, await SourceAsync(db, offering, rule.Signal, rule.SourceId, ct), "read-only-test");
    }

    public Task<ProviderMonitoringEvaluation> EvaluateAsync(Guid offeringId, Guid ruleId, long expectedRevision,
        string key, string actor, CancellationToken ct) =>
        store.WriteAsync(offeringId, $"MonitoringEvaluate:{offeringId}:{ruleId}", key, new { ruleId, expectedRevision }, actor, async (db, _, offering) =>
        {
            var rule = await RuleAsync(db, offering!, ruleId, ct);
            Expected(rule, expectedRevision);
            var source = await SourceAsync(db, offering!, rule.Signal, rule.SourceId, ct);
            var evaluation = Evaluate(rule, source, actor);
            if (!rule.IsEnabled) return evaluation;
            rule.LastEvaluatedAt = DateTimeOffset.UtcNow;
            rule.NextEvaluationUtcTicks = DateTime.UtcNow.AddMinutes(rule.CadenceMinutes).Ticks;
            db.Entry(rule).Property(x => x.Revision).IsModified = true;
            var retained = await db.Set<ProviderMonitoringEvaluation>().SingleOrDefaultAsync(x =>
                x.ProviderId == rule.ProviderId && x.OfferingId == offeringId && x.RuleId == ruleId &&
                x.RuleRevision == rule.Revision && x.ObservationHash == evaluation.ObservationHash, ct);
            if (retained is not null) return retained;
            if (evaluation.Outcome == "Matched")
            {
                var review = await ProviderImpactService.QueueMonitoringReviewAsync(db, offering!, rule, evaluation, ct);
                evaluation.ImpactReviewId = review.Id;
            }
            db.Add(evaluation);
            return evaluation;
        }, ct);

    public async Task RunDueAsync(CancellationToken ct)
    {
        store.Authorize();
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        if (!await db.CspProfiles.AnyAsync(x => x.OnboardingState == Models.Tenancy.OnboardingState.Active, ct)) return;
        var provider = await store.ProviderAsync(db, ct);
        var nowTicks = DateTime.UtcNow.Ticks;
        var due = await db.Set<ProviderMonitoringRule>().AsNoTracking().Where(x =>
            x.ProviderId == provider && x.IsEnabled && x.NextEvaluationUtcTicks <= nowTicks).ToListAsync(ct);
        foreach (var rule in due)
            await EvaluateAsync(rule.OfferingId, rule.Id, rule.Revision,
                $"scheduled:{rule.Id:N}:{rule.Revision}:{DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(rule.CadenceMinutes).Ticks}",
                "system:provider-monitoring", ct);
    }

    private static async Task<ProviderMonitoringRule> RuleAsync(AtoCopilotContext db, ProviderOffering offering, Guid ruleId, CancellationToken ct) =>
        await db.Set<ProviderMonitoringRule>().SingleOrDefaultAsync(x => x.Id == ruleId && x.ProviderId == offering.ProviderId &&
            x.OfferingId == offering.Id, ct) ?? throw new KeyNotFoundException("Rule not found in this offering.");

    private static ProviderMonitoringEvaluation Evaluate(ProviderMonitoringRule rule, ProviderMonitoringSource source, string actor)
    {
        var value = rule.Signal == "PublishedReleaseChange" ? (rule.BaselineHash != source.SourceRevision).ToString().ToLowerInvariant() : source.Value;
        var alert = new ComplianceAlert { ChangeDetails = Json(new
        {
            property = Field(rule.Signal)[7..], oldValue = (string?)null, newValue = value
        }) };
        return new()
        {
            ProviderId = rule.ProviderId, OfferingId = rule.OfferingId, CreatedBy = actor, RuleId = rule.Id, RuleRevision = rule.Revision,
            ObservationHash = Hash(Json(new { source.SourceRevision, source.CollectionHealth, value })),
            Outcome = !rule.IsEnabled ? "Disabled" : source.CollectionHealth != "Available" ? "CollectionUnavailable"
                : MonitoringConditionEvaluator.Matches(rule.ConditionJson, alert) ? "Matched" : "NoMatch",
            CollectionHealth = source.CollectionHealth, RuleSnapshotJson = Json(rule), SourceSnapshotJson = Json(source with { Value = value })
        };
    }

    private static string Field(string signal) => signal switch
    {
        "AuthorizationExpiry" => "Change.daysUntilExpiry", "AuthorizationWithdrawal" => "Change.withdrawn",
        "EvidenceFreshness" => "Change.ageDays", "PublishedReleaseChange" => "Change.releaseChanged",
        _ => throw new ArgumentException("Unsupported provider source signal.")
    };
}
