using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Core.Interfaces.Compliance;

public interface ISystemDesignService
{
    Task<SystemDesignGraph> GetAsync(string systemId, CancellationToken ct = default);
    Task<SystemDesignGraph> SaveAsync(string systemId, SaveSystemDesignRequest request, CancellationToken ct = default);
    Task<SystemDesignGraph> SaveComponentScopeAsync(string systemId, SaveComponentScopeRequest request, CancellationToken ct = default);
    Task<SystemDesignGraph> ReviewAsync(string systemId, DesignReviewRequest request, CancellationToken ct = default);
    Task<SystemDesignGraph> ReconcileAsync(string systemId, DesignRevisionRequest request, CancellationToken ct = default);
    Task<SystemDesignGraph> BuildFromRecordedAsync(string systemId, DesignRevisionRequest request, CancellationToken ct = default);
    Task<SystemDesignGraph> DecideProposalAsync(string systemId, string proposalId, DesignProposalDecisionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DesignHistoryEntry>> GetHistoryAsync(string systemId, CancellationToken ct = default);
    Task<SystemDesignGraph> GetRevisionAsync(string systemId, long revision, CancellationToken ct = default);
    Task<ApprovedSystemDesign?> GetApprovedAsync(string systemId, CancellationToken ct = default);
    Task<DesignLayout> GetLayoutAsync(string systemId, string view, CancellationToken ct = default);
    Task<DesignLayout> SaveLayoutAsync(string systemId, SaveDesignLayoutRequest request, CancellationToken ct = default);
}
