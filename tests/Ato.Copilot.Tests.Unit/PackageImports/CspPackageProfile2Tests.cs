using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageProfile2Tests
{
    [Fact]
    public async Task ProfileSchema_CreatesAndUpgradesBothProvidersWithoutResettingHistory()
    {
        // Arrange
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        var expected = new Dictionary<string, string[]>
        {
            ["CspPackages"] = ["OfferingId", "PackageVersionId", "BoundaryRevisionId", "AnalysisProfileVersion",
                "TargetAnalysisProfileVersion", "FamilyCoverageJson", "AnalysisOperationsJson"],
            ["CspPackageEntries"] = ["AnalysisProfileVersion", "FamilyCoverageJson"],
            ["CspPackageCandidates"] = ["ClaimJson", "AnalysisProfileVersion"]
        };
        // Act
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var package = new CspPackage
        {
            ProviderId = Guid.NewGuid(), IdempotencyKey = "retained", Revision = 7, Version = 12,
            ProcessingState = "NeedsAttention", PublicationState = "Published",
            AnalysisCheckpointJson = """{"Version":1,"AnalysisProfileVersion":1}"""
        };
        db.CspPackages.Add(package);
        db.CspPackageEntries.Add(new() { PackageId = package.Id, StableKey = "source", SegmentsJson = """["retained evidence"]""" });
        db.CspPackageCandidates.Add(new()
        {
            PackageId = package.Id, StableKey = "claim", ReviewState = "Reviewed", Revision = 4,
            PayloadJson = """{"Name":"Human-reviewed name"}"""
        });
        db.CspPackageApprovals.Add(new()
        {
            PackageId = package.Id, Revision = 7, CreatedVersion = 11, PublishedVersion = 12,
            State = "Published", SnapshotJson = """{"retained":"approval"}"""
        });
        db.CspPackageAudits.Add(new() { PackageId = package.Id, Action = "Published", Detail = "Retained history" });
        await db.SaveChangesAsync();
        var history = await SavedHistoryAsync(db);
        // Assert
        foreach (var (table, columns) in expected)
        {
            var actual = await db.Database.SqlQuery<string>($"SELECT name AS Value FROM pragma_table_info({table})").ToListAsync();
            actual.Should().Contain(columns);
            foreach (var column in columns)
            {
                var dropColumn = $"ALTER TABLE {table} DROP COLUMN {column}";
                await db.Database.ExecuteSqlRawAsync(dropColumn);
            }
        }
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        foreach (var (table, columns) in expected)
        {
            var actual = await db.Database.SqlQuery<string>($"SELECT name AS Value FROM pragma_table_info({table})").ToListAsync();
            actual.Should().Contain(columns);
            foreach (var column in columns)
            {
                string.Join('\n', CspPackageSchemaAdditions.Scripts(true)).Should()
                    .Contain($"COL_LENGTH(N'dbo.{table}', N'{column}')");
                CspPackageSchemaAdditions.Scripts(true).Single(script => script.StartsWith($"IF OBJECT_ID(N'dbo.{table}',"))
                    .Should().Contain(column);
            }
        }
        (await SavedHistoryAsync(db)).Should().Be(history);
        foreach (var table in expected.Keys)
        {
            var profileQuery = $"SELECT AnalysisProfileVersion AS Value FROM {table}";
            (await db.Database.SqlQueryRaw<int>(profileQuery).SingleAsync()).Should().Be(1);
        }
        (await db.Database.SqlQueryRaw<int?>("SELECT TargetAnalysisProfileVersion AS Value FROM CspPackages").SingleAsync()).Should().BeNull();
        (await db.Database.SqlQueryRaw<string>("SELECT FamilyCoverageJson AS Value FROM CspPackages").SingleAsync()).Should().Be("{}");
        (await db.Database.SqlQueryRaw<string>("SELECT AnalysisOperationsJson AS Value FROM CspPackages").SingleAsync()).Should().Be("[]");
        (await db.Database.SqlQueryRaw<string>("SELECT FamilyCoverageJson AS Value FROM CspPackageEntries").SingleAsync()).Should().Be("[]");
        (await db.Database.SqlQueryRaw<string?>("SELECT ClaimJson AS Value FROM CspPackageCandidates").SingleAsync()).Should().BeNull();

        await db.Database.ExecuteSqlAsync($"UPDATE CspPackages SET AnalysisProfileVersion = 2, TargetAnalysisProfileVersion = 2, FamilyCoverageJson = {"""{"retained":[]}"""}, AnalysisOperationsJson = {"""["completed"]"""}");
        await db.Database.ExecuteSqlRawAsync("UPDATE CspPackageEntries SET AnalysisProfileVersion = 2, FamilyCoverageJson = '[\"retained\"]'");
        await db.Database.ExecuteSqlAsync($"UPDATE CspPackageCandidates SET AnalysisProfileVersion = 2, ClaimJson = {"""{"retained":true}"""}");
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        foreach (var table in expected.Keys)
        {
            var profileQuery = $"SELECT AnalysisProfileVersion AS Value FROM {table}";
            (await db.Database.SqlQueryRaw<int>(profileQuery).SingleAsync()).Should().Be(2);
        }
        (await db.Database.SqlQueryRaw<int?>("SELECT TargetAnalysisProfileVersion AS Value FROM CspPackages").SingleAsync()).Should().Be(2);
        (await db.Database.SqlQueryRaw<string>("SELECT FamilyCoverageJson AS Value FROM CspPackages").SingleAsync()).Should().Be("""{"retained":[]}""");
        (await db.Database.SqlQueryRaw<string>("SELECT AnalysisOperationsJson AS Value FROM CspPackages").SingleAsync()).Should().Be("""["completed"]""");
        (await db.Database.SqlQueryRaw<string>("SELECT FamilyCoverageJson AS Value FROM CspPackageEntries").SingleAsync()).Should().Be("""["retained"]""");
        (await db.Database.SqlQueryRaw<string>("SELECT ClaimJson AS Value FROM CspPackageCandidates").SingleAsync()).Should().Be("""{"retained":true}""");
        (await SavedHistoryAsync(db)).Should().Be(history);
    }

    private static async Task<string> SavedHistoryAsync(AtoCopilotContext db)
    {
        db.ChangeTracker.Clear();
        return JsonSerializer.Serialize(new
        {
            Packages = await db.CspPackages.ToListAsync(),
            Entries = await db.CspPackageEntries.ToListAsync(),
            Candidates = await db.CspPackageCandidates.ToListAsync(),
            Approvals = await db.CspPackageApprovals.ToListAsync(),
            Audits = await db.CspPackageAudits.ToListAsync()
        });
    }

    [Fact]
    public async Task Worker_PersistsTypedClaimAndProfileWithPrivateEvidence()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) => Task.FromResult(Profile2(inputs)));
        var receipt = await fixture.ReceiveAsync("claim", "synthetic supporting quote");
        // Act
        await fixture.Service.ProcessSynchronouslyAsync(receipt.PackageId, default);
        var candidate = (await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default)).Items.Single();
        // Assert
        var wire = JsonSerializer.SerializeToElement(candidate);
        wire.TryGetProperty("Claim", out var claim).Should().BeTrue("the typed claim must survive persistence and projection");
        claim.GetProperty("AssessmentFinding").GetProperty("Observation").GetString().Should().Be("synthetic supporting quote");
        wire.GetProperty("AnalysisProfileVersion").GetInt32().Should().Be(2);
        candidate.ReviewState.Should().Be("NeedsReview");
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspPackageEntries.SingleAsync()).SegmentsJson.Should().Contain("synthetic supporting quote");
    }

    [Theory]
    [InlineData("none", 2)]
    [InlineData("legacy-entry", 1)]
    [InlineData("legacy-checkpoint", 1)]
    [InlineData("missing-checkpoint", null)]
    [InlineData("missing-entry", null)]
    [InlineData("missing-draft", null)]
    [InlineData("quote", null)]
    [InlineData("type", null)]
    [InlineData("key", null)]
    public async Task Candidates_ProjectsOnlyMatchingRetainedProfile_WithoutRewritingHistory(string change, int? expectedProfile)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) => Task.FromResult(Profile2(inputs)));
        var receipt = await fixture.AnalyzeAsync();
        string history;
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var row = await db.CspPackageCandidates.SingleAsync();
            var payload = JsonNode.Parse(row.PayloadJson)!.AsObject();
            payload.Remove("AnalysisProfileVersion");
            payload["Name"] = "Human-reviewed name";
            row.ReviewState = "Reviewed";
            row.Revision = 4;
            var package = await db.CspPackages.SingleAsync();
            var checkpoint = JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(package.AnalysisCheckpointJson!)!;
            switch (change)
            {
                case "legacy-entry": checkpoint = checkpoint with { Entries = checkpoint.Entries.Select(x => x with { AnalysisProfileVersion = 1 }).ToArray() }; break;
                case "legacy-checkpoint": checkpoint = checkpoint with { AnalysisProfileVersion = 1 }; break;
                case "missing-entry": checkpoint = checkpoint with { Entries = [] }; break;
                case "missing-draft": checkpoint = checkpoint with { Candidates = [] }; break;
                case "quote": payload["Citations"]![0]!["Quote"] = "different quote"; break;
                case "type": payload["Type"] = "BoundaryClaim"; break;
                case "key": row.StableKey = "different-key"; break;
            }
            row.PayloadJson = payload.ToJsonString();
            package.AnalysisCheckpointJson = change == "missing-checkpoint" ? null : JsonSerializer.Serialize(checkpoint);
            await db.SaveChangesAsync();
            history = await SavedHistoryAsync(db);
        }
        // Act
        var candidate = (await fixture.Service.CandidatesAsync(receipt.PackageId, 1, 25, null, null, default)).Items.Single();
        // Assert
        var wire = JsonSerializer.SerializeToElement(candidate);
        wire.TryGetProperty("AnalysisProfileVersion", out var version).Should().Be(expectedProfile.HasValue);
        if (expectedProfile.HasValue) version.GetInt32().Should().Be(expectedProfile.Value);
        candidate.Name.Should().Be("Human-reviewed name");
        candidate.ReviewState.Should().Be("Reviewed");
        candidate.Revision.Should().Be(4);
        await using var verify = fixture.Factory.CreateDbContext();
        (await SavedHistoryAsync(verify)).Should().Be(history);
    }

    internal static CspPackageAnalysisResult Profile2(IReadOnlyList<CspPackageAnalysisInput> inputs)
    {
        var original = Fixture.Analysis(inputs);
        var coverage = Enum.GetValues<CspPackageClaimFamily>().Select(x =>
            new CspPackageFamilyCoverage(x, CspPackageFamilyCoverageStatus.Analyzed)).ToArray();
        var result = original with
        {
            AnalysisProfileVersion = 2,
            Entries = original.Entries.Select(x => x with { AnalysisProfileVersion = 2, FamilyCoverage = coverage }).ToArray(),
            FamilyCoverage = new Dictionary<string, IReadOnlyList<CspPackageFamilyCoverage>> { ["root"] = coverage },
            Candidates = [original.Candidates[0] with
            {
                Key = "finding", Kind = CspPackageCandidateKind.AssessmentFinding,
                Claim = new()
                {
                    AssessmentFinding = new() { Observation = "synthetic supporting quote" },
                    FieldSources = [new("assessmentFinding.observation", [0])]
                }
            }]
        };
        return Fixture.WithCheckpoint(result, inputs) with
        {
            Checkpoint = Fixture.WithCheckpoint(result, inputs).Checkpoint! with
            { AnalysisProfileVersion = 2, FamilyCoverage = result.FamilyCoverage }
        };
    }
}
