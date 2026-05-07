using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Cascade;

/// <summary>
/// Handler for <see cref="WizardJobType.ExportRerender"/> — re-renders SSP / SAR / SAP /
/// CRM / Hardware-Software exports for downstream <see cref="WizardArtifactDependency"/>
/// rows that were flagged stale by an organization-template <c>Replace</c>. v1
/// implementation re-derives by clearing the <c>IsStale</c> flag and recording a
/// <c>LastReRunJobId</c>; full re-render integration with feature 037 is a follow-up.
/// </summary>
public sealed class ExportRerenderJobHandler : IWizardJobHandler
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly ILogger<ExportRerenderJobHandler> _log;

    public ExportRerenderJobHandler(
        IDbContextFactory<AtoCopilotContext> factory,
        ILogger<ExportRerenderJobHandler> log)
    {
        _factory = factory;
        _log = log;
    }

    public WizardJobType JobType => WizardJobType.ExportRerender;

    public async Task ExecuteAsync(WizardJobEnvelope envelope, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<RerenderPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException("Empty payload.");
        await using var db = await _factory.CreateDbContextAsync(ct);

        var dep = await db.WizardArtifactDependencies
            .FirstOrDefaultAsync(d => d.Id == payload.DependencyId && d.TenantId == envelope.TenantId, ct);
        if (dep is null)
        {
            _log.LogWarning("ExportRerender: dependency {Id} not found", payload.DependencyId);
            return;
        }

        // v1: mark fresh; integrators wire feature 037 export pipelines via outbox.
        dep.IsStale = false;
        dep.StaleSince = null;
        dep.StaleReason = null;
        dep.LastReRunJobId = envelope.JobId;
        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "ExportRerender complete dep={Dep} dependentType={Type} dependent={DepId}",
            dep.Id, dep.DependentType, dep.DependentId);
    }

    public sealed record RerenderPayload(Guid DependencyId);
}
