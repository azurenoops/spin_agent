using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Queries;

public static class AssessmentPlanQueries
{
    public static IQueryable<SecurityAssessmentPlan> WithRetainedDetails(
        this IQueryable<SecurityAssessmentPlan> plans) =>
        plans.AsSplitQuery().Include(plan => plan.ControlEntries).Include(plan => plan.TeamMembers);

    public static async Task<List<SecurityAssessmentPlan>> LoadRetainedDetailsAsync(
        this IQueryable<SecurityAssessmentPlan> plans, CancellationToken cancellationToken = default)
    {
        var loaded = await plans.WithRetainedDetails().ToListAsync(cancellationToken);
        await plans.VerifyRetainedRevisionsAsync(loaded, cancellationToken);
        return loaded;
    }

    public static async Task VerifyRetainedRevisionsAsync(this IQueryable<SecurityAssessmentPlan> plans,
        IReadOnlyCollection<SecurityAssessmentPlan> loaded, CancellationToken cancellationToken = default)
    {
        if (loaded.Count == 0) return;
        var revisions = await plans.Select(plan => new { plan.Id, plan.Revision })
            .ToDictionaryAsync(plan => plan.Id, plan => plan.Revision, cancellationToken);
        // Child writes participate in the parent revision, so split reads cannot mix revisions.
        if (revisions.Count != loaded.Count || loaded.Any(plan =>
            !revisions.TryGetValue(plan.Id, out var revision) || revision != plan.Revision))
            throw new DbUpdateConcurrencyException("The assessment plan changed while it was being read. Refresh and retry.");
    }

    public static IOrderedQueryable<SecurityAssessmentPlan> OrderWorkingFirst(
        this IQueryable<SecurityAssessmentPlan> plans) =>
        // Numeric CASE avoids SQL Server's Boolean/XOR translation of string-converted enums.
        plans.OrderByDescending(plan => plan.Status == SapStatus.Draft ? 1 : 0)
            .ThenByDescending(plan => plan.GeneratedAt)
            .ThenBy(plan => plan.Id);
}
