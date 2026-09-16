using System.Diagnostics;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public sealed class EvidenceNarrativeBulkClassifierJobTests
{
    [Fact]
    public async Task RunAsync_BackfilledArtifacts_ClassifiesOnceAndPreservesManualTags()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"EvidenceNarrativeBulk_{Guid.NewGuid()}")
            .Options;
        var factory = new TestDbContextFactory(options);
        factory.Context.EvidenceArtifacts.AddRange(
            Artifact("policy", "Account Policy.pdf"),
            Artifact("scan", "scan-results.csv", ArtifactCategory.ScanResult),
            Artifact("manual", "manually-tagged.pdf", manuallyTaggedBy: "reviewer"));
        await factory.Context.SaveChangesAsync();
        var job = new EvidenceNarrativeBulkClassifierJob(
            factory,
            new EvidenceNarrativeClassifier(),
            NullLogger<EvidenceNarrativeBulkClassifierJob>.Instance);

        // Act
        var firstRun = await job.RunAsync();
        var secondRun = await job.RunAsync();
        factory.Context.ChangeTracker.Clear();
        var artifacts = await factory.Context.EvidenceArtifacts
            .ToDictionaryAsync(artifact => artifact.Id);

        // Assert
        firstRun.Processed.Should().Be(2);
        firstRun.Policy.Should().Be(1);
        firstRun.Technical.Should().Be(1);
        secondRun.Processed.Should().Be(0);
        artifacts["scan"].NarrativeType.Should().Be(EvidenceNarrativeType.Technical);
        artifacts["policy"].NarrativeType.Should().Be(EvidenceNarrativeType.Policy);
        artifacts["manual"].ManuallyTaggedBy.Should().Be("reviewer");
    }

    [Fact]
    public async Task RunAsync_OneThousandArtifacts_CompletesWithinOneSecond()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlite(connection)
            .Options;
        var factory = new TestDbContextFactory(options);
        await factory.Context.Database.EnsureCreatedAsync();
        factory.Context.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "system-1",
            Name = "Classifier Performance System",
        });
        factory.Context.EvidenceArtifacts.AddRange(Enumerable.Range(0, 1000)
            .Select(index => Artifact(index.ToString(), $"Policy-{index}.pdf")));
        await factory.Context.SaveChangesAsync();
        var job = new EvidenceNarrativeBulkClassifierJob(
            factory,
            new EvidenceNarrativeClassifier(),
            NullLogger<EvidenceNarrativeBulkClassifierJob>.Instance);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await job.RunAsync();
        stopwatch.Stop();

        // Assert
        result.Processed.Should().Be(1000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    private static EvidenceArtifact Artifact(
        string id,
        string fileName,
        ArtifactCategory category = ArtifactCategory.Other,
        string? manuallyTaggedBy = null) => new()
    {
        Id = id,
        RegisteredSystemId = "system-1",
        FileName = fileName,
        NarrativeType = EvidenceNarrativeType.Combined,
        ArtifactCategory = category,
        ManuallyTaggedBy = manuallyTaggedBy,
    };
}