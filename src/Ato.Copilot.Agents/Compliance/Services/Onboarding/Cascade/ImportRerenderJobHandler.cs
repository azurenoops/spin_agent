using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.Cascade;

/// <summary>
/// Handler for <see cref="WizardJobType.ImportRerender"/> — re-runs eMASS or
/// SSP-PDF imports while preserving manual edits per spec edge cases
/// ("Replacing an eMASS export after user edits", "Replacing an SSP PDF
/// after manual field corrections"). v1 simply clears the dependency's
/// stale flag; the full re-import path is wired in features 008 / 022.
/// </summary>
public sealed class ImportRerenderJobHandler : IWizardJobHandler
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly ILogger<ImportRerenderJobHandler> _log;

    public ImportRerenderJobHandler(
        IDbContextFactory<AtoCopilotContext> factory,
        ILogger<ImportRerenderJobHandler> log)
    {
        _factory = factory;
        _log = log;
    }

    public WizardJobType JobType => WizardJobType.ImportRerender;

    public async Task ExecuteAsync(WizardJobEnvelope envelope, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<RerenderPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException("Empty payload.");
        await using var db = await _factory.CreateDbContextAsync(ct);

        var dep = await db.WizardArtifactDependencies
            .FirstOrDefaultAsync(d => d.Id == payload.DependencyId && d.TenantId == envelope.TenantId, ct);
        if (dep is null)
        {
            _log.LogWarning("ImportRerender: dependency {Id} not found", payload.DependencyId);
            return;
        }

        dep.IsStale = false;
        dep.StaleSince = null;
        dep.StaleReason = null;
        dep.LastReRunJobId = envelope.JobId;
        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "ImportRerender complete dep={Dep} dependentType={Type} dependent={DepId}",
            dep.Id, dep.DependentType, dep.DependentId);
    }

    public sealed record RerenderPayload(Guid DependencyId);
}
