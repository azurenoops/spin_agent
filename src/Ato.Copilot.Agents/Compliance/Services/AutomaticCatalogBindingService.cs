using System.Data;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>System-owned source association; does not author or approve compliance content.</summary>
public sealed class AutomaticCatalogBindingService(
    IDbContextFactory<AtoCopilotContext> factory,
    ITenantContextAccessor accessor,
    ILogger<AutomaticCatalogBindingService> logger) : IHostedService
{
    public const string NistRev5 = "NIST-800-53-R5";
    private const string Actor = "system:catalog-association";
    private const int BatchSize = 100;

    public Task StartAsync(CancellationToken cancellationToken) => BackfillAsync(cancellationToken: cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Internal lifecycle maintenance only; never exposed as an ordinary-user mutation endpoint.</summary>
    public async Task BackfillAsync(string? frameworkIdentifier = null, CancellationToken cancellationToken = default)
    {
        var offset = 0;
        while (true)
        {
            List<Target> targets;
            using (accessor.Push(new TenantContext(Guid.Empty, isCspAdmin: true)))
            {
                await using var routing = await factory.CreateDbContextAsync(cancellationToken);
                targets = await routing.ControlBaselines.AsNoTracking()
                    .Where(x => frameworkIdentifier == null || x.SourceFrameworkIdentifier == frameworkIdentifier
                        || x.SourceFrameworkIdentifier == null && frameworkIdentifier == NistRev5)
                    .OrderBy(x => x.Id).Skip(offset).Take(BatchSize)
                    .Select(x => new Target(x.Id, x.TenantId)).ToListAsync(cancellationToken);
            }
            foreach (var target in targets)
                await AssociateAsync(target.TenantId, target.Id, cancellationToken);
            if (targets.Count < BatchSize) return;
            offset += targets.Count;
        }
    }

    /// <summary>Associate one baseline using only its recorded framework and the retained reference source.</summary>
    public async Task AssociateAsync(Guid tenantId, string baselineId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A real tenant is required for catalog association.");
        using var tenantScope = accessor.Push(new TenantContext(tenantId));
        await using var db = await factory.CreateDbContextAsync(ct);
        var retry = false;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            if (retry) db.ChangeTracker.Clear();
            retry = true;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var baseline = await db.ControlBaselines.SingleOrDefaultAsync(x => x.Id == baselineId && x.TenantId == tenantId, token);
            if (baseline is null || baseline.RequirementCatalogBindingId is not null) return;

            // The sole legacy baseline writer selected the embedded NIST Rev. 5/SP800-53B baselines.
            if (baseline.SourceFrameworkIdentifier is null && baseline.BaselineLevel is "Low" or "Moderate" or "High")
                baseline.SourceFrameworkIdentifier = NistRev5;
            var framework = baseline.SourceFrameworkIdentifier is null ? null
                : await CatalogSourceReader.ReadFrameworkAsync(db,
                    x => x.Identifier == baseline.SourceFrameworkIdentifier && x.IsActive, token);
            var sourceUri = framework?.RequirementCatalogSourceUri ?? framework?.CatalogUrl;
            var issue = baseline.SourceFrameworkIdentifier is null
                ? "The baseline framework is not recorded. A system administrator must repair its source metadata."
                : framework?.RequirementCatalogJson is null || string.IsNullOrWhiteSpace(sourceUri)
                    ? $"The {baseline.SourceFrameworkIdentifier} reference source is unavailable. A platform administrator must load it; system association is automatic."
                    : null;
            RequirementCatalog? catalog = null;
            if (issue is null)
            {
                try { catalog = RequirementCatalog.Parse(framework!.RequirementCatalogJson!); }
                catch (Exception error) when (error is ArgumentException or JsonException)
                {
                    logger.LogError(error, "Invalid retained catalog source for baseline {BaselineId}", baseline.Id);
                    issue = "The recorded framework source is invalid. A platform administrator must repair the reference source.";
                }
                if (catalog is not null && baseline.ControlIds.Any(id =>
                    !catalog.Controls.Any(control => control.Id == id || control.DisplayId == id)))
                    issue = "Selected baseline identifiers do not resolve in the recorded framework source. A system administrator must repair the baseline metadata.";
            }
            if (issue is not null)
            {
                baseline.CatalogResolutionMessage = issue;
                logger.LogWarning("Automatic catalog association unavailable for baseline {BaselineId}: {Reason}", baseline.Id, issue);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return;
            }
            var binding = new BaselineCatalogBinding
            {
                TenantId = tenantId, ControlBaselineId = baseline.Id, FrameworkId = framework!.Id,
                FrameworkIdentifier = framework.Identifier, CatalogVersion = catalog!.Version,
                Publisher = framework.Publisher, SourceUri = sourceUri!, CatalogJson = framework.RequirementCatalogJson!,
                ContentHash = RequirementCoverageService.Hash(framework.RequirementCatalogJson!),
                BoundBy = Actor,
                Rationale = "Automatically associated the retained source for the baseline's framework. Historical source versions are not inferred; requirement mappings and narrative approvals are unchanged."
            };
            db.BaselineCatalogBindings.Add(binding);
            baseline.RequirementCatalogBindingId = binding.Id;
            baseline.CatalogResolutionMessage = null;
            baseline.CoverageRevision++;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            logger.LogInformation("Automatically associated baseline {BaselineId} with {Framework} version {Version}",
                baseline.Id, binding.FrameworkIdentifier, binding.CatalogVersion);
        }, ct);
    }

    private sealed record Target(string Id, Guid TenantId);
}
