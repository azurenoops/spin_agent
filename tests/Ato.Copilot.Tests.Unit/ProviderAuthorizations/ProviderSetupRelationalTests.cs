using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class ProviderSetupRelationalTests
{
    private static readonly ProviderSetupActor Actor = new("synthetic-directory", "synthetic-admin", "Synthetic administrator");

    [Fact]
    public async Task ConcurrentFirstDraft_SameKeyRetainsOneProviderDraftAndCommittedOutcome()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var request = new SaveProviderSetupDraft(0, Draft());
        var key = Guid.NewGuid().ToString();
        // Act
        var results = await Task.WhenAll(
            Task.Run(() => fixture.Service().SaveAsync(request, key, Actor, default)),
            Task.Run(() => fixture.Service().SaveAsync(request, key, Actor, default)));
        // Assert
        results[0].CommittedOutcome.Should().Be(results[1].CommittedOutcome);
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspProfiles.CountAsync()).Should().Be(1);
        (await db.Set<ProviderSetupDraft>().CountAsync()).Should().Be(1);
        (await db.Set<ProviderSetupCommand>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentFirstDraft_DifferentIntentCreatesNoSecondProvider()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var first = new SaveProviderSetupDraft(0, Draft("Synthetic first"));
        var other = new SaveProviderSetupDraft(0, Draft("Synthetic other"));
        async Task<bool> Save(SaveProviderSetupDraft value)
        {
            try { await fixture.Service().SaveAsync(value, Guid.NewGuid().ToString(), Actor, default); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        // Act
        var results = await Task.WhenAll(Task.Run(() => Save(first)), Task.Run(() => Save(other)));
        // Assert
        results.Count(value => value).Should().Be(1);
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspProfiles.CountAsync()).Should().Be(1);
        (await db.Set<ProviderSetupDraft>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FailedOfferingJournalCommit_RollsBackOfferingAndDraft_ThenExactRetryCreatesOnce()
    {
        // Arrange
        var failure = new FailCommandSave();
        await using var fixture = await Fixture.CreateAsync(failure);
        var save = await fixture.Service().SaveAsync(new(0, Draft(offering: true)), Guid.NewGuid().ToString(), Actor, default);
        var request = new CommitProviderSetup(save.CommittedOutcome!.CommittedDraftRevision, "FirstOffering",
            save.CommittedOutcome.CommittedProfileRevision);
        var key = Guid.NewGuid().ToString();
        failure.Enabled = true;
        // Act
        var failed = () => fixture.Service().CommitAsync(request, key, Actor, default);
        await failed.Should().ThrowAsync<IOException>();
        await using (var db = fixture.Factory.CreateDbContext())
        {
            (await db.Set<ProviderOffering>().CountAsync()).Should().Be(0);
            (await db.Set<ProviderSetupDraft>().SingleAsync()).Revision.Should().Be(request.ExpectedRevision);
        }
        failure.Enabled = false;
        var saved = await fixture.Service().CommitAsync(request, key, Actor, default);
        var replay = await fixture.Service().CommitAsync(request, key, Actor, default);
        // Assert
        saved.CommittedOutcome.Should().Be(replay.CommittedOutcome);
        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.Set<ProviderOffering>().CountAsync()).Should().Be(1);
        (await verify.Set<ProviderBoundaryRevision>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RepeatedAdditiveSchema_PreservesRowsHashesAndNullAssociations()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var saved = await fixture.Service().SaveAsync(new(0, Draft()), Guid.NewGuid().ToString(), Actor, default);
        await using var db = fixture.Factory.CreateDbContext();
        var draft = await db.Set<ProviderSetupDraft>().SingleAsync();
        var original = draft.DraftJson;
        // Act
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        await db.Entry(draft).ReloadAsync();
        draft.DraftJson.Should().Be(original);
        draft.Revision.Should().Be(saved.CommittedOutcome!.CommittedDraftRevision);
        (await db.Set<ProviderSetupCommand>().SingleAsync()).OutcomeJson.Should().Contain(saved.CommittedOutcome.CommandId.ToString());
        (await db.CspPackages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task KnownCommit_WithUnreadableCurrentProjection_ReplaysOutcomeWithoutMutation()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var key = Guid.NewGuid().ToString();
        var saved = await fixture.Service().SaveAsync(new(0, Draft()), key, Actor, default);
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var draft = await db.Set<ProviderSetupDraft>().SingleAsync();
            draft.DraftJson = "{invalid-json";
            await db.SaveChangesAsync();
        }
        // Act
        var replay = await fixture.Service().ReconcileAsync(new("SaveDraft", key), Actor, default);
        // Assert
        replay.Outcome.Should().Be("Committed");
        replay.CommittedOutcome.Should().Be(saved.CommittedOutcome);
        JsonSerializer.SerializeToElement(replay.Current).GetProperty("projectionState").GetString().Should().Be("Unavailable");
        await using var verify = fixture.Factory.CreateDbContext();
        (await verify.Set<ProviderSetupCommand>().CountAsync()).Should().Be(1);
        (await verify.Set<ProviderSetupDraft>().SingleAsync()).DraftJson.Should().Be("{invalid-json");
    }

    [Fact]
    public async Task LegacyMinimalSchema_UpgradeIsIdempotentAndPreservesOriginalData()
    {
        // Arrange
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE CspProfiles (Id TEXT NOT NULL PRIMARY KEY);
            CREATE TABLE CspPackages (Id TEXT NOT NULL PRIMARY KEY, ProviderId TEXT NOT NULL, ContentHash TEXT NOT NULL);
            CREATE TABLE ProviderOfferings (Id TEXT NOT NULL PRIMARY KEY);
            INSERT INTO CspProfiles(Id) VALUES ('11111111-1111-4111-8111-111111111111');
            INSERT INTO CspPackages(Id,ProviderId,ContentHash)
              VALUES ('22222222-2222-4222-8222-222222222222','11111111-1111-4111-8111-111111111111','retained-original-hash');
            """);
        // Act
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        (await db.Database.SqlQueryRaw<string>("SELECT ContentHash AS Value FROM CspPackages").SingleAsync())
            .Should().Be("retained-original-hash");
        (await db.Database.SqlQueryRaw<long>("SELECT SetupRevision AS Value FROM CspProfiles").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<long>("SELECT RequiresOfferingAssociation AS Value FROM CspPackages").SingleAsync()).Should().Be(0);
        (await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM CspPackages WHERE UploadIntentId IS NULL").SingleAsync()).Should().Be(1);
        (await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM ProviderSetupDrafts").SingleAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PriorRequiredDraftIntentSchema_UpgradePreservesHistoryAndPermitsIndependentPortalIntent()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service().SaveAsync(new(0, Draft()), Guid.NewGuid().ToString(), Actor, default);
        await using var db = fixture.Factory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE CspPackageUploadIntents;
            CREATE TABLE CspPackageUploadIntents (
                Id TEXT NOT NULL PRIMARY KEY, ProviderId TEXT NOT NULL, DraftId TEXT NOT NULL,
                IdempotencyKey TEXT NOT NULL, IntentHash TEXT NOT NULL, IntentJson TEXT NOT NULL,
                Revision INTEGER NOT NULL, CreatedAt TEXT NOT NULL, CreatedBy TEXT NOT NULL,
                UNIQUE(ProviderId,IdempotencyKey),
                FOREIGN KEY(ProviderId,DraftId) REFERENCES ProviderSetupDrafts(ProviderId,Id));
            INSERT INTO CspPackageUploadIntents(Id,ProviderId,DraftId,IdempotencyKey,IntentHash,IntentJson,Revision,CreatedAt,CreatedBy)
                SELECT '11111111-1111-4111-8111-111111111111',ProviderId,Id,'retained-key','retained-hash','retained-intent',3,CreatedAt,CreatedBy
                FROM ProviderSetupDrafts;
            """);
        // Act
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO CspPackageUploadIntents(Id,ProviderId,DraftId,EntryPoint,IdempotencyKey,IntentHash,IntentJson,Revision,CreatedAt,CreatedBy)
                SELECT '22222222-2222-4222-8222-222222222222',ProviderId,NULL,'ActivePortal','portal-key','portal-hash','portal-intent',1,CreatedAt,CreatedBy
                FROM ProviderSetupDrafts;
            """);
        // Assert
        (await db.Database.SqlQueryRaw<string>("SELECT IntentHash AS Value FROM CspPackageUploadIntents WHERE IdempotencyKey='retained-key'").SingleAsync())
            .Should().Be("retained-hash");
        (await db.Database.SqlQueryRaw<string>("SELECT EntryPoint AS Value FROM CspPackageUploadIntents WHERE IdempotencyKey='retained-key'").SingleAsync())
            .Should().Be("Onboarding");
        (await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM CspPackageUploadIntents WHERE DraftId IS NULL AND EntryPoint='ActivePortal'").SingleAsync())
            .Should().Be(1);
        (await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM CspPackageUploadIntents").SingleAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("InvalidScreen")]
    [InlineData("MissingDeferralReason")]
    [InlineData("InventedIdentity")]
    [InlineData("ForeignIntent")]
    [InlineData("OversizedName")]
    public async Task InvalidDraft_DoesNotCommitProviderOrPrivateState(string variation)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var node = System.Text.Json.Nodes.JsonNode.Parse(Draft().GetRawText())!;
        switch (variation)
        {
            case "InvalidScreen": node["currentScreen"] = "system-setup"; break;
            case "MissingDeferralReason": node["sources"]!["deferral"]!["reason"] = ""; break;
            case "InventedIdentity": node["securityContact"]!["choice"] = "ExistingIdentity"; break;
            case "ForeignIntent": node["sources"]!["choice"] = "Intents";
                node["sources"]!["intentIds"] = JsonSerializer.SerializeToNode(new[] { Guid.NewGuid() }); break;
            case "OversizedName": node["details"]!["displayName"] = new string('A', 65); break;
        }
        var request = new SaveProviderSetupDraft(0, JsonSerializer.SerializeToElement(node));
        // Act
        var save = () => fixture.Service().SaveAsync(request, Guid.NewGuid().ToString(), Actor, default);
        // Assert
        if (variation == "ForeignIntent") await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        else await save.Should().ThrowAsync<ArgumentException>();
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspProfiles.CountAsync()).Should().Be(0);
        (await db.Set<ProviderSetupDraft>().CountAsync()).Should().Be(0);
        (await db.Set<ProviderSetupCommand>().CountAsync()).Should().Be(0);
    }

    private static JsonElement Draft(string name = "Synthetic provider", bool offering = false) => JsonSerializer.SerializeToElement(new
    {
        currentScreen = "p-details",
        details = new { displayName = name, legalEntityName = "Synthetic operator", serviceContactEmail = "synthetic@example.invalid" },
        securityContact = new { choice = "Deferred", deferral = new { reason = "Reviewer later", ownerRole = "CSP.Admin" } },
        firstOffering = offering ? (object)new { choice = "New", name = "Synthetic SaaS", description = "",
            environments = Array.Empty<string>(), serviceDescription = new { environmentKind = "Other", environmentLabel = "Manual SaaS", serviceModel = "Software" } }
            : new { choice = "Deferred", deferral = new { reason = "Offering later", ownerRole = "CSP.Admin" } },
        sources = new { choice = "Deferred", intentIds = Array.Empty<Guid>(), deferral = new { reason = "Sources later", ownerRole = "CSP.Admin" } }
    });

    private sealed class FailCommandSave : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<ProviderSetupCommand>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Operation == "CommitFirstOffering"))
                throw new IOException("Synthetic journal failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"provider-setup-{Guid.NewGuid():N}.db");
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly Mock<ITenantContext> _tenant = new();
        public IDbContextFactory<AtoCopilotContext> Factory { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync(SaveChangesInterceptor? interceptor = null)
        {
            var fixture = new Fixture();
            var options = new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite($"Data Source={fixture._path};Pooling=False;Default Timeout=5");
            if (interceptor is not null) options.AddInterceptors(interceptor);
            fixture.Factory = new TestFactory(options.Options);
            fixture._tenant.SetupGet(x => x.IsCspAdmin).Returns(true);
            await using var db = fixture.Factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            return fixture;
        }
        public ProviderSetupService Service() => new(Factory, _tenant.Object, Options.Create(new ProviderHandlingOptions()),
            _cache, NullLogger<ProviderSetupService>.Instance);
        public ValueTask DisposeAsync()
        {
            _cache.Dispose();
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
    private sealed class TestFactory(DbContextOptions<AtoCopilotContext> options) : IDbContextFactory<AtoCopilotContext>
    {
        public AtoCopilotContext CreateDbContext() => new(options);
        public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
