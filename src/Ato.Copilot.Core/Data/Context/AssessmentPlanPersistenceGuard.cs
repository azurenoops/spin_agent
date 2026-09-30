using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Context;

internal static class AssessmentPlanPersistenceGuard
{
    internal static async Task ValidateAsync(AtoCopilotContext db, CancellationToken ct)
    {
        var plans = db.ChangeTracker.Entries<SecurityAssessmentPlan>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted).ToList();
        foreach (var entry in plans)
        {
            if (entry.OriginalValues.GetValue<SapStatus>(nameof(SecurityAssessmentPlan.Status)) == SapStatus.Finalized)
                throw new InvalidOperationException("Finalized assessment plans are immutable.");
        }

        var childPlanIds = db.ChangeTracker.Entries<SapControlEntry>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .SelectMany(e => e.State == EntityState.Added
                ? new[] { e.Entity.SecurityAssessmentPlanId }
                : new[] { e.Entity.SecurityAssessmentPlanId, e.OriginalValues.GetValue<string>(nameof(SapControlEntry.SecurityAssessmentPlanId)) })
            .Concat(db.ChangeTracker.Entries<SapTeamMember>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .SelectMany(e => e.State == EntityState.Added
                    ? new[] { e.Entity.SecurityAssessmentPlanId }
                    : new[] { e.Entity.SecurityAssessmentPlanId, e.OriginalValues.GetValue<string>(nameof(SapTeamMember.SecurityAssessmentPlanId)) }))
            .Distinct().ToList();
        var newPlanIds = db.ChangeTracker.Entries<SecurityAssessmentPlan>()
            .Where(e => e.State == EntityState.Added).Select(e => e.Entity.Id).ToHashSet();
        childPlanIds.RemoveAll(newPlanIds.Contains);
        if (childPlanIds.Count > 0 && await db.SecurityAssessmentPlans.AsNoTracking()
            .AnyAsync(p => childPlanIds.Contains(p.Id) && p.Status == SapStatus.Finalized, ct))
            throw new InvalidOperationException("Finalized assessment plan controls and team members are immutable.");

        // Even legacy child-only writers must participate in the parent's revision check.
        foreach (var id in childPlanIds)
        {
            var parent = db.ChangeTracker.Entries<SecurityAssessmentPlan>().SingleOrDefault(e => e.Entity.Id == id);
            if (parent is null)
            {
                var sap = await db.SecurityAssessmentPlans.SingleOrDefaultAsync(p => p.Id == id, ct);
                if (sap is null) continue;
                parent = db.Entry(sap);
            }
            if (parent.Entity.Status == SapStatus.Finalized)
                throw new InvalidOperationException("Finalized assessment plan controls and team members are immutable.");
            if (parent.State == EntityState.Unchanged)
            {
                parent.Entity.Revision++;
                parent.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
        foreach (var entry in plans.Where(e => e.State == EntityState.Modified))
        {
            var original = entry.OriginalValues.GetValue<long>(nameof(SecurityAssessmentPlan.Revision));
            if (entry.Entity.Revision == original) entry.Entity.Revision++;
        }
    }
}
