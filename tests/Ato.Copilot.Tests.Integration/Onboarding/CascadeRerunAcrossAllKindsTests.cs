using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Cascade;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.NarrativeSeeds.Handlers;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// SC-013 — admin re-run cascade clears the stale flag for downstream
/// dependents originating from each of the four wizard source kinds
/// (Template, eMASS import, SSP PDF import, Narrative Seed).
/// </summary>
public class CascadeRerunAcrossAllKindsTests
{
    [Theory]
    [InlineData(ArtifactSourceKind.Template, ArtifactDependentKind.SspExport)]
    [InlineData(ArtifactSourceKind.EmassImportSession, ArtifactDependentKind.RegisteredSystem)]
    [InlineData(ArtifactSourceKind.SspPdfImportSession, ArtifactDependentKind.RegisteredSystem)]
    [InlineData(ArtifactSourceKind.NarrativeSeedDocument, ArtifactDependentKind.NarrativeSuggestion)]
    public async Task Rerun_ClearsStaleFlagForEverySourceKind(
        ArtifactSourceKind sourceKind, ArtifactDependentKind dependentKind)
    {
        var tid = Guid.NewGuid();
        var depId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"Cascade_{Guid.NewGuid()}")
            .Options;
        var factory = new InMemoryFactory(options);

        await using (var db = factory.CreateDbContext())
        {
            db.WizardArtifactDependencies.Add(new WizardArtifactDependency
            {
                Id = depId, TenantId = tid,
                SourceArtifactType = sourceKind, SourceArtifactId = Guid.NewGuid(),
                SourceVersionTag = "v1",
                DependentType = dependentKind, DependentId = Guid.NewGuid(),
                IsStale = true, StaleSince = DateTimeOffset.UtcNow,
                StaleReason = "Source replaced",
            });
            await db.SaveChangesAsync();
        }

        var envelope = new WizardJobEnvelope(
            jobId, tid,
            sourceKind == ArtifactSourceKind.Template
                ? WizardJobType.ExportRerender
                : WizardJobType.ImportRerender,
            Guid.NewGuid(),
            $"{{\"DependencyId\":\"{depId:D}\"}}");

        if (sourceKind == ArtifactSourceKind.Template)
        {
            var handler = new ExportRerenderJobHandler(
                factory, NullLogger<ExportRerenderJobHandler>.Instance);
            await handler.ExecuteAsync(envelope, CancellationToken.None);
        }
        else
        {
            var handler = new ImportRerenderJobHandler(
                factory, NullLogger<ImportRerenderJobHandler>.Instance);
            await handler.ExecuteAsync(envelope, CancellationToken.None);
        }

        await using (var db = factory.CreateDbContext())
        {
            var dep = await db.WizardArtifactDependencies.SingleAsync();
            dep.IsStale.Should().BeFalse();
            dep.StaleSince.Should().BeNull();
            dep.LastReRunJobId.Should().Be(jobId);
        }
    }

    private sealed class InMemoryFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public InMemoryFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
