using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class CspPackageSqlServerSchemaTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task CatalogSchema_PreservesReviewedRows_AndUsesDatabaseRowVersions()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await CspInheritedCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var component = new CspInheritedComponent
        {
            CspProfileId = Guid.NewGuid(), Name = "Synthetic existing published contributor",
            Status = CspInheritedComponentStatus.Published
        };
        var capability = new CspInheritedCapability
        {
            CspInheritedComponent = component, Name = "Synthetic reviewed capability",
            Status = CspInheritedCapabilityStatus.Mapped, MappedBy = MappedBy.User,
            MappedNistControlIds = ["AU-2"]
        };
        db.AddRange(component, capability);
        await db.SaveChangesAsync();
        var version = component.RowVersion;

        // Act
        await CspInheritedCatalogSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        db.ChangeTracker.Clear();

        // Assert
        var retained = await db.CspInheritedComponents.SingleAsync();
        retained.RowVersion.Should().HaveCount(8).And.Equal(version!);
        retained.Status.Should().Be(CspInheritedComponentStatus.Published);
        var retainedCapability = await db.CspInheritedCapabilities.SingleAsync();
        retainedCapability.Status.Should().Be(CspInheritedCapabilityStatus.Mapped);
        retainedCapability.MappedNistControlIds.Should().Equal("AU-2");
    }

    [SkippableFact]
    public async Task AdditiveSchema_WithRetryStrategy_PreservesPackageLedger()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var package = new CspPackage { ProviderId = Guid.NewGuid(), IdempotencyKey = "retained", Name = "Retained package" };
        db.CspPackages.Add(package);
        db.CspPackageApprovals.Add(new CspPackageApproval { PackageId = package.Id });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.CspPackages DROP COLUMN AnalysisCheckpointJson");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.CspPackageApprovals DROP COLUMN CreatedVersion");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.CspPackageApprovals DROP COLUMN PublishedVersion");
        // Act
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        // Assert
        db.ChangeTracker.Clear();
        (await db.CspPackages.SingleAsync()).Name.Should().Be("Retained package");
        (await db.CspPackages.SingleAsync()).AnalysisCheckpointJson.Should().BeNull();
        var approval = await db.CspPackageApprovals.SingleAsync();
        approval.CreatedVersion.Should().Be(0);
        approval.PublishedVersion.Should().BeNull();
        db.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeTrue();
    }

    [SkippableFact]
    public async Task ProviderSetupSchema_WithSqlServerRetries_PreservesPrivateIntentUpgradeAndExactCommandReplay()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await TenantsAndOrganizationsSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CspPackageSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ProviderAuthorizationSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        db.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeTrue();
        var profile = new CspProfile
        {
            LegalEntityName = "Synthetic SQL provider", DisplayName = "Synthetic SQL provider",
            PrimarySupportEmail = "synthetic@example.invalid", OnboardingState = OnboardingState.Active,
            IdentityCompletedAt = DateTimeOffset.UtcNow, SupportCompletedAt = DateTimeOffset.UtcNow,
            ClassificationCompletedAt = DateTimeOffset.UtcNow, OnboardingCompletedAt = DateTimeOffset.UtcNow,
            CreatedBy = "synthetic-sql-fixture"
        };
        db.Add(profile);
        await db.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseSqlServer(db.Database.GetConnectionString(), sql => sql.EnableRetryOnFailure()).Options;
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(options));
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(x => x.IsCspAdmin).Returns(true);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var policy = new ProviderHandlingOptions
        {
            PolicyId = "synthetic-sql-policy", Version = "test-1", ApprovalReference = "Synthetic test reference",
            ValidUntil = DateTimeOffset.UtcNow.AddHours(1), AllowedClassifications = ["Unclassified"],
            SyntheticOnly = true, UploadsEnabled = true, AnalysisEnabled = true
        };
        var service = new ProviderSetupService(factory.Object, tenant.Object, Options.Create(policy),
            cache, NullLogger<ProviderSetupService>.Instance);
        var actor = new ProviderSetupActor("synthetic-directory", "synthetic-admin", "Synthetic SQL administrator");
        var draftInput = JsonSerializer.SerializeToElement(new
        {
            currentScreen = "p-details",
            details = new { displayName = "Retained draft name", legalEntityName = "", serviceContactEmail = "" },
            securityContact = new { choice = "Deferred", deferral = new { reason = "Reviewer later", ownerRole = "CSP.Admin" } },
            firstOffering = new { choice = "Deferred", deferral = new { reason = "Offering later", ownerRole = "CSP.Admin" } },
            sources = new { choice = "Deferred", intentIds = Array.Empty<Guid>(), deferral = new { reason = "Sources later", ownerRole = "CSP.Admin" } }
        });
        var request = new SaveProviderSetupDraft(0, draftInput);
        var key = Guid.NewGuid().ToString();
        var saved = await service.SaveAsync(request, key, actor, default);
        var draftId = saved.CommittedOutcome!.DraftId;
        var legacyIntent = new CspPackageUploadIntent
        {
            Id = Guid.NewGuid(), ProviderId = profile.Id, DraftId = draftId, EntryPoint = "Onboarding",
            IdempotencyKey = Guid.NewGuid().ToString(), IntentHash = new string('A', 64),
            IntentJson = """{"schemaVersion":1,"packageName":"Retained SQL source","entryPoint":"Onboarding","files":[]}""",
            CreatedBy = actor.ObjectId
        };
        var package = new CspPackage
        {
            ProviderId = profile.Id, Name = "Retained SQL source", IdempotencyKey = legacyIntent.IdempotencyKey,
            UploadIntentId = legacyIntent.Id, ContentHash = new string('B', 64), RequiresOfferingAssociation = true,
            HandlingPolicyVersion = "retained-policy-version",
            HandlingDeclarationJson = """{"classification":"Unclassified","markings":[],"containsOnlySyntheticData":true}"""
        };
        var original = new CspPackageEntry
        {
            PackageId = package.Id, IsOriginal = true, StableKey = new string('C', 64),
            FileName = "synthetic.txt", MediaType = "text/plain", Sha256 = new string('D', 64),
            ByteLength = 9, StorageKey = "synthetic-retained-artifact", ArchivePath = "synthetic.txt"
        };
        db.AddRange(legacyIntent, package, original);
        await db.SaveChangesAsync();
        var beforeDraft = await db.Set<ProviderSetupDraft>().AsNoTracking().SingleAsync();
        var beforeCommand = await db.Set<ProviderSetupCommand>().AsNoTracking().SingleAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.CspPackageUploadIntents ALTER COLUMN DraftId uniqueidentifier NOT NULL");

        // Act
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await ProviderSetupSchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        var replay = await service.SaveAsync(request, key, actor, default);
        var portalId = Guid.NewGuid();
        var portalInput = new ProviderUploadIntentInput(portalId, 1, "New SQL portal source", "ActivePortal",
            "Unassociated", null, null,
            [new(0, "new.txt", "text/plain", 1, new string('E', 64))],
            "test-1", new("Unclassified", [], true));
        await service.PrepareUploadAsync(new(0, portalInput), portalId.ToString(), actor, default);

        // Assert
        replay.Replayed.Should().BeTrue();
        replay.CommittedOutcome.Should().Be(saved.CommittedOutcome);
        db.ChangeTracker.Clear();
        var afterDraft = await db.Set<ProviderSetupDraft>().SingleAsync();
        afterDraft.DraftJson.Should().Be(beforeDraft.DraftJson);
        afterDraft.Revision.Should().Be(beforeDraft.Revision);
        (await db.Set<ProviderSetupCommand>().SingleAsync()).OutcomeJson.Should().Be(beforeCommand.OutcomeJson);
        var retainedIntent = await db.Set<CspPackageUploadIntent>().SingleAsync(x => x.Id == legacyIntent.Id);
        retainedIntent.DraftId.Should().Be(draftId);
        retainedIntent.IntentHash.Should().Be(new string('A', 64));
        retainedIntent.IntentJson.Should().Be(legacyIntent.IntentJson);
        (await db.Set<CspPackageUploadIntent>().SingleAsync(x => x.Id == portalId)).DraftId.Should().BeNull();
        (await db.Set<CspPackageUploadIntent>().CountAsync()).Should().Be(2);
        var retainedPackage = await db.CspPackages.SingleAsync();
        retainedPackage.ContentHash.Should().Be(new string('B', 64));
        retainedPackage.UploadIntentId.Should().Be(legacyIntent.Id);
        retainedPackage.OfferingId.Should().BeNull();
        retainedPackage.BoundaryRevisionId.Should().BeNull();
        var retainedOriginal = await db.CspPackageEntries.SingleAsync();
        retainedOriginal.Sha256.Should().Be(new string('D', 64));
        retainedOriginal.StorageKey.Should().Be("synthetic-retained-artifact");
        db.Database.CurrentTransaction.Should().BeNull();
    }
}
