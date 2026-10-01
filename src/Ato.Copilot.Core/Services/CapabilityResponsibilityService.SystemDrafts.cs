using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services;

public sealed partial class CapabilityResponsibilityService
{
    public async Task ConfirmSystemAllocationAsync(string systemId, string baselineId,
        CapabilityResponsibilityAllocation allocation, string actor, CancellationToken ct = default)
    {
        await MutateAsync(systemId, actor, async () =>
        {
            var state = await LoadAsync(systemId, ct);
            var input = Normalize(allocation);
            if (state.Baseline?.Id != baselineId || !state.Baseline.ControlIds.Contains(input.ControlId, StringComparer.OrdinalIgnoreCase))
                throw new ResponsibilityReviewConflictException("The system baseline changed.");
            if (input.InheritanceType is not ("Inherited" or "Shared" or "Customer")
                || input.Provider?.Length > 200 || input.CustomerResponsibility?.Length > 2000
                || input.InheritanceType is "Inherited" or "Shared" && string.IsNullOrWhiteSpace(input.Provider)
                || input.InheritanceType is "Shared" or "Customer" && string.IsNullOrWhiteSpace(input.CustomerResponsibility))
                throw new ArgumentException("Provide an explicit allocation, provider where applicable, and required customer duties.");
            var baseline = state.Baseline;
            var row = baseline.Inheritances.SingleOrDefault(x => x.ControlId == input.ControlId);
            var audit = new InheritanceAuditEntry { ControlBaselineId = baseline.Id, ControlId = input.ControlId,
                Actor = actor, ChangeSource = InheritanceChangeSource.Manual, PreviousInheritanceType = row?.InheritanceType.ToString(),
                PreviousProvider = row?.Provider, PreviousCustomerResponsibility = row?.CustomerResponsibility };
            if (row is null)
            {
                row = new ControlInheritance { TenantId = TenantId, ControlBaselineId = baseline.Id, ControlId = input.ControlId };
                db.ControlInheritances.Add(row);
            }
            row.InheritanceType = Enum.Parse<InheritanceType>(input.InheritanceType);
            row.Provider = input.Provider; row.CustomerResponsibility = input.CustomerResponsibility;
            row.DesignationSource = "Manual"; row.SetBy = actor; row.SetAt = DateTime.UtcNow;
            audit.ControlInheritanceId = row.Id; audit.NewInheritanceType = input.InheritanceType;
            audit.NewProvider = input.Provider; audit.NewCustomerResponsibility = input.CustomerResponsibility;
            db.InheritanceAuditEntries.Add(audit);
            await db.SaveChangesAsync(ct);
            var rows = await db.ControlInheritances.Where(x => x.TenantId == TenantId && x.ControlBaselineId == baseline.Id).ToListAsync(ct);
            baseline.InheritedControls = rows.Count(x => x.InheritanceType == InheritanceType.Inherited);
            baseline.SharedControls = rows.Count(x => x.InheritanceType == InheritanceType.Shared);
            baseline.CustomerControls = rows.Count(x => x.InheritanceType == InheritanceType.Customer);
            baseline.ModifiedAt = DateTime.UtcNow;
            AddActivity(systemId, row.Id, actor, "ResponsibilityDraftConfirmed",
                $"Reviewed system responsibility for {input.ControlId}; narrative and implementation status preserved.");
            return true;
        }, ct);
    }
}
