using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services;

/// <summary>Resolve retained ownership without guessing from shared subscriptions.</summary>
public static class RemediationScope
{
    public static async Task<string?> FindingSystemAsync(AtoCopilotContext db, ComplianceFinding finding, CancellationToken ct)
    {
        var owners = new List<string>();
        if (!string.IsNullOrEmpty(finding.AssessmentId))
        {
            var assessment = await db.Assessments.SingleOrDefaultAsync(a => a.Id == finding.AssessmentId && a.TenantId == finding.TenantId, ct);
            if (assessment is null) return null;
            if (!string.IsNullOrEmpty(assessment.RegisteredSystemId)) owners.Add(assessment.RegisteredSystemId);
        }
        if (!string.IsNullOrEmpty(finding.ImportRecordId))
        {
            var system = await db.ScanImportRecords.Where(a => a.Id == finding.ImportRecordId && a.TenantId == finding.TenantId)
                .Select(a => a.RegisteredSystemId).SingleOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(system)) return null;
            owners.Add(system);
        }
        return owners.Distinct(StringComparer.Ordinal).Count() == 1 ? owners[0] : null;
    }

    public static async Task<string?> TaskSystemAsync(AtoCopilotContext db, RemediationTask task, CancellationToken ct)
    {
        var owners = new List<string>();
        if (!string.IsNullOrEmpty(task.RegisteredSystemId)) owners.Add(task.RegisteredSystemId);
        if (!string.IsNullOrEmpty(task.FindingId))
        {
            var finding = await db.Findings.SingleOrDefaultAsync(f => f.Id == task.FindingId && f.TenantId == task.TenantId, ct);
            if (finding is null) return null;
            var system = await FindingSystemAsync(db, finding, ct);
            if (system is null) return null;
            owners.Add(system);
        }
        var board = await db.RemediationBoards.SingleOrDefaultAsync(b => b.Id == task.BoardId && b.TenantId == task.TenantId, ct);
        if (board is null) return null;
        if (!string.IsNullOrEmpty(board.AssessmentId))
        {
            var assessment = await db.Assessments.SingleOrDefaultAsync(a => a.Id == board.AssessmentId && a.TenantId == task.TenantId, ct);
            if (assessment is null) return null;
            if (!string.IsNullOrEmpty(assessment.RegisteredSystemId)) owners.Add(assessment.RegisteredSystemId);
        }
        return owners.Distinct(StringComparer.Ordinal).Count() == 1 ? owners[0] : null;
    }
}
