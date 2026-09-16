using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Compliance;

/// <summary>Classifies migration-backfilled evidence that has not been manually tagged.</summary>
public sealed class EvidenceNarrativeBulkClassifierJob
{
    private readonly IDbContextFactory<AtoCopilotContext> _dbContextFactory;
    private readonly IEvidenceNarrativeClassifier _classifier;
    private readonly ILogger<EvidenceNarrativeBulkClassifierJob> _logger;

    public EvidenceNarrativeBulkClassifierJob(
        IDbContextFactory<AtoCopilotContext> dbContextFactory,
        IEvidenceNarrativeClassifier classifier,
        ILogger<EvidenceNarrativeBulkClassifierJob> logger)
    {
        _dbContextFactory = dbContextFactory;
        _classifier = classifier;
        _logger = logger;
    }

    /// <summary>Classifies pending artifacts in the current tenant context.</summary>
    public async Task<EvidenceNarrativeBulkClassifierResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var artifacts = await context.EvidenceArtifacts
            .Where(artifact => artifact.NarrativeType == EvidenceNarrativeType.Combined
                && artifact.ManuallyTaggedBy == null
                && artifact.AutoTagRationale == null)
            .ToListAsync(cancellationToken);

        var policy = 0;
        var technical = 0;
        var combined = 0;

        foreach (var artifact in artifacts)
        {
            var classification = await _classifier.ClassifyAsync(artifact, cancellationToken);
            artifact.NarrativeType = classification.Type;
            artifact.AutoTagRationale = classification.Rationale;

            switch (classification.Type)
            {
                case EvidenceNarrativeType.Policy:
                    policy++;
                    break;
                case EvidenceNarrativeType.Technical:
                    technical++;
                    break;
                case EvidenceNarrativeType.Combined:
                    combined++;
                    break;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Evidence narrative classification completed: {Policy} tagged Policy, {Technical} Technical, {Combined} Combined (low-confidence)",
            policy, technical, combined);

        return new EvidenceNarrativeBulkClassifierResult(artifacts.Count, policy, technical, combined);
    }
}

public sealed record EvidenceNarrativeBulkClassifierResult(
    int Processed,
    int Policy,
    int Technical,
    int Combined);