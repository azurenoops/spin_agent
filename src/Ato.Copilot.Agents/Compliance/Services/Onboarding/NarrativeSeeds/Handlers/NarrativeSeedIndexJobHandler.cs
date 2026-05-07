using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Agents.Compliance.Services.Onboarding.NarrativeSeeds.Handlers;

/// <summary>
/// Stub handler for <see cref="WizardJobType.NarrativeSeedIndex"/> jobs (T121).
/// In v1 this simply transitions the source <see cref="NarrativeSeedDocument"/>
/// from <c>Pending</c> → <c>Indexed</c>. Downstream feature 014 (citation-aware
/// narrative suggestions) will hook into this same job slot to register the
/// uploaded document with its retrieval index.
/// </summary>
public sealed class NarrativeSeedIndexJobHandler : IWizardJobHandler
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly IWizardProgressNotifier _notifier;
    private readonly ILogger<NarrativeSeedIndexJobHandler> _log;

    public NarrativeSeedIndexJobHandler(
        IDbContextFactory<AtoCopilotContext> factory,
        IWizardProgressNotifier notifier,
        ILogger<NarrativeSeedIndexJobHandler> log)
    {
        _factory = factory;
        _notifier = notifier;
        _log = log;
    }

    public WizardJobType JobType => WizardJobType.NarrativeSeedIndex;

    public async Task ExecuteAsync(WizardJobEnvelope envelope, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<NarrativeSeedIndexPayload>(envelope.PayloadJson)
            ?? throw new InvalidOperationException("Empty payload.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var doc = await db.NarrativeSeedDocuments
            .FirstOrDefaultAsync(d => d.Id == payload.DocumentId, ct)
            ?? throw new InvalidOperationException(
                $"NarrativeSeedDocument {payload.DocumentId} not found.");

        doc.IndexingStatus = NarrativeSeedIndexingStatus.Indexed;
        doc.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "NarrativeSeed indexed (stub) DocId={Doc} Tenant={Tenant}",
            doc.Id, doc.TenantId);
    }

    public sealed record NarrativeSeedIndexPayload(Guid DocumentId);
}
