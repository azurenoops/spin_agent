using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services.Onboarding;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Emass.Handlers;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Jobs;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// Integration test asserting that <see cref="EmassCommitJobHandler"/> writes a
/// <see cref="WizardArtifactDependency"/> link from the
/// <see cref="EmassImportSession"/> (source) to each created
/// <see cref="Ato.Copilot.Core.Models.Compliance.RegisteredSystem"/> (dependent),
/// providing the foundation for the FR-094 cascade-replace flow (T074).
/// </summary>
public class EmassImportDependencyTests
{
    [Fact]
    public async Task CommitJobHandler_LinksSessionToCreatedSystems()
    {
        var dbName = $"EmassDeps_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var factory = new TestDbContextFactory(options);

        var notifier = new Mock<IWizardProgressNotifier>();
        notifier
            .Setup(n => n.PublishAsync(It.IsAny<WizardJobStatusEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jobRunnerMock = new Mock<IWizardJobRunner>();
        var dependencies = new WizardArtifactDependencyService(
            factory, jobRunnerMock.Object, NullLogger<WizardArtifactDependencyService>.Instance);

        var handler = new EmassCommitJobHandler(
            factory, dependencies, notifier.Object,
            NullLogger<EmassCommitJobHandler>.Instance);

        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var commitJobId = Guid.NewGuid();
        var preview = new EmassParseResult(
            new[]
            {
                new EmassParsedSystem("SYS-A", "Acme Portal", 100, 5, null),
                new EmassParsedSystem("SYS-B", "Acme Gateway", 80, 2, null),
            },
            "Xlsx");

        await using (var db = factory.CreateDbContext())
        {
            db.EmassImportSessions.Add(new EmassImportSession
            {
                Id = sessionId,
                TenantId = tenantId,
                OriginalFileName = "fixture.xlsx",
                StorageBlobKey = "key",
                ContentChecksumSha256 = "sha-fixture",
                Status = EmassImportStatus.Parsed,
                Preview = JsonSerializer.Serialize(preview),
            });
            db.WizardJobStatuses.Add(new WizardJobStatus
            {
                Id = commitJobId,
                TenantId = tenantId,
                JobType = WizardJobType.EmassCommit,
                Status = WizardJobState.Queued,
            });
            await db.SaveChangesAsync();
        }

        var payload = new EmassCommitJobPayload(
            sessionId,
            new[]
            {
                new EmassCommitInstruction("SYS-A", EmassCommitDecision.Merge),
                new EmassCommitInstruction("SYS-B", EmassCommitDecision.Merge),
            });

        await handler.ExecuteAsync(
            new WizardJobEnvelope(commitJobId, tenantId, WizardJobType.EmassCommit, Guid.NewGuid(),
                JsonSerializer.Serialize(payload)),
            CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var deps = await verify.WizardArtifactDependencies
            .Where(d => d.TenantId == tenantId
                     && d.SourceArtifactType == ArtifactSourceKind.EmassImportSession
                     && d.SourceArtifactId == sessionId
                     && d.DependentType == ArtifactDependentKind.RegisteredSystem)
            .ToListAsync();

        deps.Should().HaveCount(2, "each merged system must have a dependency link to the source session");
        deps.Should().AllSatisfy(d => d.SourceVersionTag.Should().Be("sha-fixture"));

        var systems = await verify.RegisteredSystems.ToListAsync();
        systems.Should().Contain(s => s.Name == "Acme Portal");
        systems.Should().Contain(s => s.Name == "Acme Gateway");

        var session = await verify.EmassImportSessions.FirstAsync(s => s.Id == sessionId);
        session.Status.Should().Be(EmassImportStatus.Imported);

        var job = await verify.WizardJobStatuses.FirstAsync(j => j.Id == commitJobId);
        job.Status.Should().Be(WizardJobState.Succeeded);
        job.Result.Should().NotBeNullOrEmpty();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
