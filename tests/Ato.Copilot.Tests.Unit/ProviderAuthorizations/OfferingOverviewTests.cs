using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Store = Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;
using Fixture = Ato.Copilot.Tests.Unit.ProviderAuthorizations.OfferingBoundaryOverviewTests.Fixture;

namespace Ato.Copilot.Tests.Unit.ProviderAuthorizations;

public sealed class OfferingOverviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Decisions_FilterCurrentProviderDecisionsBeforePaging_WithGlobalCountsAndExistingProjection(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        await using (var db = fixture.Db())
        {
            for (var i = 0; i < 15; i++)
            {
                var record = new ProviderAuthorizationRecord
                {
                    ProviderId = fixture.Provider, OfferingId = fixture.Offering, Revision = i + 10
                };
                var kind = i < 12 ? "ProviderDecision" : "InheritedMicrosoftReference";
                var body = new CreateProviderDecisionRequest(7, fixture.SourceBoundary, [], kind, $"Decision {i}",
                    "Synthetic issuer", "Source decision", "2020-01-01", null, null, "NoExpiryStated", "Source scope", ["Condition"], []);
                var revision = new ProviderAuthorizationRevision
                {
                    ProviderId = fixture.Provider, OfferingId = fixture.Offering, RecordId = record.Id,
                    BoundaryRevisionId = fixture.SourceBoundary, Revision = 2,
                    SnapshotJson = Store.Json(body), SnapshotHash = $"hash-{i}",
                    MetadataReviewState = i < 4 ? "Recorded" : i < 8 ? "Unconfirmed" : "Rejected"
                };
                record.CurrentRevisionId = revision.Id;
                db.AddRange(record, revision, new ProviderAuthorizationRevision
                {
                    ProviderId = fixture.Provider, OfferingId = fixture.Offering, RecordId = record.Id,
                    BoundaryRevisionId = fixture.SourceBoundary, Revision = 1, SnapshotJson = Store.Json(body),
                    MetadataReviewState = "Recorded"
                });
                if (i == 0)
                    db.Add(new ProviderAuthorizationLifecycleEvent
                    {
                        ProviderId = fixture.Provider, OfferingId = fixture.Offering, RecordId = record.Id,
                        AuthorizationRevisionId = revision.Id, Kind = "Withdrawn", EffectiveOn = "2020-02-01"
                    });
                if (i == 1)
                    db.Add(new ProviderAuthorizationImpactReview
                    {
                        ProviderId = fixture.Provider, OfferingId = fixture.Offering, Disposition = "AcceptForPublication",
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                        ContextJson = Store.Json(new ProviderPublicationContextMaterial(fixture.Offering, 7,
                            fixture.SourceBoundary, "", null, null,
                            [new(record.Id, revision.Id, revision.SnapshotHash, "")], [], []))
                    });
            }
            await db.SaveChangesAsync();
        }
        var existing = await fixture.Service.DecisionsAsync(fixture.Offering, 2, 10, null, default, "ProviderDecision");

        // Act
        var result = await fixture.Service.OverviewAsync(fixture.Offering, 2, 1, 10, default);
        var first = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 100, default);

        // Assert
        result.OfferingRevision.Should().Be(7);
        result.Authorizations.Should().BeEquivalentTo(new
        {
            Items = existing.Items, Page = 2, PageSize = 10, Total = 12, Recorded = 4, Unconfirmed = 4, Rejected = 4
        });
        first.Authorizations.Items.Single(x => x.Reference == "Decision 0").CurrentStanding.Should().Be("Withdrawn");
        first.Authorizations.Items.Single(x => x.Reference == "Decision 1").ImpactReviewRequired.Should().BeFalse();
        first.Authorizations.Items.Should().OnlyContain(x => x.Revision >= 10);
        result.Packages.Total.Should().Be(0);
        result.Hosting.Should().BeEquivalentTo(new OfferingOverviewHosting(null, false, 0, 0, 0));
        fixture.PackageService.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Packages_UseRetainedAndDirectLinks_ExactReceiptStatus_GlobalCountsAndDeterministicPages(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var receipts = new List<CspPackage>();
        var versionId = Guid.NewGuid();
        await using (var db = fixture.Db())
        {
            for (var i = 0; i < 12; i++)
            {
                var row = new CspPackage
                {
                    ProviderId = fixture.Provider, OfferingId = i == 0 ? null : fixture.Offering,
                    BoundaryRevisionId = i == 0 ? null : fixture.SourceBoundary,
                    IdempotencyKey = Guid.NewGuid().ToString(), Name = $"Package {i}", Revision = 4,
                    CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddDays(i),
                    ProcessingState = i == 11 ? "NeedsAttention" : i == 10 ? "Failed" : "Received",
                    PublicationState = "PartiallyPublished", LastError = i == 11 ? "Synthetic retained error" : null,
                    AnalysisCheckpointJson = """{"AnalysisProgress":{"CompletedSegments":2,"TotalSegments":3,"ModelCalls":4,"ModelCallLimit":8,"ContinuingAutomatically":true}}"""
                };
                receipts.Add(row);
                db.Add(row);
                db.Add(new CspPackageCandidate { PackageId = row.Id, Type = "Component", ReviewState = "NeedsReview" });
            }
            db.Add(new ProviderPackageVersion
            {
                Id = versionId, ProviderId = fixture.Provider, OfferingId = fixture.Offering,
                PackageId = receipts[0].Id, BoundaryRevisionId = fixture.SourceBoundary, SeriesId = Guid.NewGuid(), Version = 2
            });
            var secondVersionId = Guid.NewGuid();
            receipts[1].PackageVersionId = secondVersionId;
            db.Add(new ProviderPackageVersion
            {
                Id = secondVersionId, ProviderId = fixture.Provider, OfferingId = fixture.Offering,
                PackageId = receipts[1].Id, BoundaryRevisionId = fixture.SourceBoundary, SeriesId = Guid.NewGuid(), Version = 1
            });
            foreach (var state in new[] { "Pending", "Processed", "Unsupported", "Unreadable", "Failed", "Excluded" })
                db.Add(new CspPackageEntry { PackageId = receipts[11].Id, StableKey = state, Status = state });
            db.AddRange(new CspPackage { ProviderId = fixture.Provider, OfferingId = fixture.OtherOffering, Name = "Other offering",
                    IdempotencyKey = Guid.NewGuid().ToString() },
                new CspPackage { ProviderId = fixture.Provider, Name = "Unlinked", IdempotencyKey = Guid.NewGuid().ToString() },
                new CspPackage { ProviderId = Guid.NewGuid(), OfferingId = fixture.Offering, Name = "Foreign",
                    IdempotencyKey = Guid.NewGuid().ToString() });
            await db.SaveChangesAsync();
        }

        // Act
        var first = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);
        var second = await fixture.Service.OverviewAsync(fixture.Offering, 1, 2, 10, default);
        var beyond = await fixture.Service.OverviewAsync(fixture.Offering, 1, 3, 10, default);

        // Assert
        first.Packages.Total.Should().Be(12);
        first.Packages.NeedsAttention.Should().Be(2);
        first.Packages.Processing.Should().Be(10);
        first.Packages.AwaitingReview.Should().Be(12);
        first.Packages.Items.Select(x => x.Package.PackageId).Should().Equal(receipts.Skip(2).Reverse().Select(x => x.Id));
        first.Packages.Items[0].Package.Should().BeEquivalentTo(new PackageStatus(receipts[11].Id, receipts[11].Id,
            receipts[11].Name, 4, "NeedsAttention", "PartiallyPublished", new(6, 1, 1, 1, 1, 1, 1),
            receipts[11].LastError, receipts[11].CreatedAt, receipts[11].UpdatedAt, null,
            new CspPackageAnalysisProgress(2, 3, 4, 8, false)));
        first.Packages.Items[0].AwaitingReview.Should().Be(1);
        first.Packages.Items[0].AuthorizationDetails.Should().Be(0);
        second.Packages.Items.Should().HaveCount(2);
        second.Packages.Items[0].Package.Association.Should().Be(new PackageOfferingAssociation(fixture.Offering,
            receipts[1].PackageVersionId!.Value, fixture.SourceBoundary));
        second.Packages.Items[1].Should().BeEquivalentTo(new
        {
            PackageVersionId = (Guid?)versionId, Version = (int?)2, BoundaryRevisionId = (Guid?)fixture.SourceBoundary
        });
        second.Packages.Items[1].Package.Association.Should().BeNull("the exact legacy receipt must not be fabricated");
        second.Packages.Items[1].Package.AnalysisProgress!.ContinuingAutomatically.Should().BeTrue();
        beyond.Packages.Items.Should().BeEmpty();
        beyond.Packages.Total.Should().Be(12);
        fixture.PackageService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreferredReview_IsGlobal_ClaimsBeforeLegacy_NeedsReviewBeforeReviewed_ThenNewest()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var oldest = await fixture.AddCandidateAsync("Older pending", type: "AuthorizationDecisionClaim");
        var expected = await fixture.AddCandidateAsync("Newer pending", type: "AuthorizationDecisionClaim");
        await fixture.AddCandidateAsync("New reviewed", "Reviewed", "AuthorizationDecisionClaim");
        await fixture.AddCandidateAsync("New legacy", type: "AuthorizationReference");
        await fixture.AddCandidateAsync("Rejected", "Rejected", "AuthorizationDecisionClaim");
        await using (var db = fixture.Db())
        {
            (await db.CspPackages.SingleAsync(x => x.Id == oldest.PackageId)).CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
            var package = await db.CspPackages.SingleAsync(x => x.Id == expected.PackageId);
            package.CreatedAt = DateTimeOffset.UtcNow.AddDays(-1);
            package.Name = "Retained authorization package";
            db.Add(new CspPackageCandidate { PackageId = expected.PackageId, Type = "AuthorizationReference", ReviewState = "Reviewed" });
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 1, default);
        var all = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 100, default);

        // Assert
        result.Packages.Items.Should().NotContain(x => x.Package.PackageId == expected.PackageId);
        result.Packages.PreferredAuthorizationReview.Should().Be(new OfferingAuthorizationReviewTarget(
            expected.PackageId, "Retained authorization package", "AuthorizationDecisionClaim"));
        result.Packages.AwaitingReview.Should().Be(3);
        all.Packages.Items.Single(x => x.Package.PackageId == expected.PackageId).AuthorizationDetails.Should().Be(2);
        all.Packages.Items.Sum(x => x.AuthorizationDetails).Should().Be(5);
    }

    [Theory]
    [InlineData("NeedsReview")]
    [InlineData("Reviewed")]
    public async Task PreferredReview_FallsBackToLegacy_WhenClaimsAreRejected(string state)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCandidateAsync("Rejected claim", "Rejected", "AuthorizationDecisionClaim");
        var source = await fixture.AddCandidateAsync("Legacy", state, "AuthorizationReference");
        // Act
        var result = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);
        // Assert
        result.Packages.PreferredAuthorizationReview!.PackageId.Should().Be(source.PackageId);
        result.Packages.PreferredAuthorizationReview.Type.Should().Be("AuthorizationReference");
    }

    [Fact]
    public async Task Capabilities_ReuseIdentityDeduplication_AndKeepReviewedApprovedPublishedArchivedDistinct()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCandidateAsync("Proposal");
        await fixture.AddCandidateAsync("Reviewed", "Reviewed");
        await fixture.AddCandidateAsync("Approved", "Approved");
        await fixture.AddCandidateAsync("Rejected", "Rejected");
        var published = await fixture.AddCandidateAsync("Published", "Published");
        await fixture.AddPublishedAsync("Published", published);
        await fixture.AddPublishedAsync("Archived", status: CspInheritedCapabilityStatus.Archived);
        // Act
        var result = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 1, default);
        // Assert
        result.Capabilities.Should().Be(new OfferingOverviewCapabilities(3, 1, 1, 1, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosting_UsesExactCurrentRevision_DistinctAssociatedSystems_NotAssignments(bool sqlite)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync(sqlite);
        var first = await fixture.AddAssignmentAsync();
        var duplicate = await fixture.AddAssignmentAsync(systemId: first.SystemId);
        var pending = await fixture.AddAssignmentAsync();
        var stale = await fixture.AddAssignmentAsync();
        await fixture.AddAssignmentAsync();
        var missingSystem = await fixture.AddAssignmentAsync(includeSystem: false);
        await fixture.AddAssignmentAsync();
        await fixture.AddAssignmentAsync(offeringId: fixture.OtherOffering);
        await fixture.AddRelationshipAsync(first, "SeparateMissionBoundary", false);
        await fixture.AddRelationshipAsync(duplicate, "SeparateMissionBoundary", false);
        await fixture.AddRelationshipAsync(pending, "Undetermined", true);
        await fixture.AddRelationshipAsync(stale, "SeparateMissionBoundary", false, 0);
        await fixture.AddRelationshipAsync(missingSystem, "SeparateMissionBoundary", false);
        await using (var db = fixture.Db())
        {
            var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == fixture.Offering);
            offering.CurrentHostingScopeRevisionId = fixture.HostingScope;
            var scope = await db.Set<ProviderHostingScopeRevision>().SingleAsync(x => x.Id == fixture.HostingScope);
            scope.SnapshotJson = Store.Json(new CreateProviderHostingScopeRequest(7, null, "Exact retained hosting", [fixture.Scope], [], []));
            db.Add(new ProviderHostingScopeRevision
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, Revision = 99,
                SnapshotJson = Store.Json(new CreateProviderHostingScopeRequest(7, null, "Newer but not current", [], [], []))
            });
            await db.SaveChangesAsync();
        }
        // Act
        var result = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 1, default);
        // Assert
        result.Hosting.Should().Be(new OfferingOverviewHosting("Exact retained hosting", true, 1, 7, 2));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("provider")]
    [InlineData("offering")]
    [InlineData("system")]
    public async Task Hosting_DoesNotCountForeignRelationshipEvenWithMatchingAssignmentId(string foreign)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var assignment = await fixture.AddAssignmentAsync();
        await using (var db = fixture.Db())
        {
            db.Add(new MissionProviderRelationshipReview
            {
                ProviderId = foreign == "provider" ? Guid.NewGuid() : fixture.Provider,
                OfferingId = foreign == "offering" ? fixture.OtherOffering : fixture.Offering,
                TenantId = foreign == "tenant" ? Guid.NewGuid() : assignment.TargetTenantId,
                SystemId = foreign == "system" ? Guid.NewGuid().ToString() : assignment.SystemId,
                AssignmentId = assignment.Id, AssignmentRevision = assignment.Revision,
                State = "SeparateMissionBoundary", ReviewRequired = false
            });
            await db.SaveChangesAsync();
        }
        // Act
        var result = await fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);
        // Assert
        result.Hosting.AssignmentCount.Should().Be(1);
        result.Hosting.AssociatedSystemCount.Should().Be(0);
    }

    [Fact]
    public async Task GlobalPackageCounts_HaveNoAcquisitionCap_AndTimestampTiesUseIdentity()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Db())
        {
            for (var i = 0; i < 105; i++)
            {
                var receipt = new CspPackage
                {
                    ProviderId = fixture.Provider, OfferingId = fixture.Offering, BoundaryRevisionId = fixture.SourceBoundary,
                    IdempotencyKey = Guid.NewGuid().ToString(), CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
                };
                db.Add(receipt);
                db.Add(new CspPackageCandidate { PackageId = receipt.Id, Type = "AuthorizationDecisionClaim" });
            }
            await db.SaveChangesAsync();
        }
        await using var before = fixture.Db();
        var expected = await before.CspPackages.OrderBy(x => x.Id).Skip(100).Select(x => x.Id).ToListAsync();
        // Act
        var result = await fixture.Service.OverviewAsync(fixture.Offering, 11, 11, 10, default);
        // Assert
        result.Packages.Should().BeEquivalentTo(new { Page = 11, PageSize = 10, Total = 105, Processing = 105, AwaitingReview = 105 });
        result.Packages.Items.Select(x => x.Package.PackageId).Should().Equal(expected);
        result.Packages.PreferredAuthorizationReview.Should().NotBeNull();
        result.Authorizations.Items.Should().BeEmpty();
        await using var after = fixture.Db();
        (await after.Set<ProviderAuthorizationAudit>().CountAsync()).Should().Be(0);
        (await after.CspPackageAudits.CountAsync()).Should().Be(0);
        (await after.Set<ProviderOffering>().SingleAsync(x => x.Id == fixture.Offering)).Revision.Should().Be(7);
        fixture.PackageService.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 1, 10)]
    [InlineData(1, 0, 10)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 101)]
    [InlineData(int.MaxValue, 1, 100)]
    [InlineData(1, int.MaxValue, 100)]
    public async Task InvalidPaging_IsRejected(int authorizationPage, int packagePage, int size)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        // Act
        var action = () => fixture.Service.OverviewAsync(fixture.Offering, authorizationPage, packagePage, size, default);
        // Assert
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("support")]
    public async Task NonProviderContext_IsDenied(string kind)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Tenant.IsCspAdmin = kind != "tenant";
        fixture.Tenant.ImpersonatedTenantId = kind == "support" ? fixture.Customer : null;
        // Act
        var action = () => fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);
        // Assert
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ForeignOffering_IsNotDisclosed()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        await using (var db = fixture.Db())
        {
            db.Add(new ProviderOffering { Id = id, OfferingId = id, ProviderId = Guid.NewGuid(), Name = "Private" });
            await db.SaveChangesAsync();
        }
        // Act
        var action = () => fixture.Service.OverviewAsync(id, 1, 1, 10, default);
        // Assert
        await action.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("missing-hosting")]
    [InlineData("foreign-hosting")]
    [InlineData("corrupt-hosting")]
    [InlineData("missing-decision")]
    [InlineData("missing-version")]
    public async Task InconsistentRetainedContext_FailsInsteadOfInventingEmptyData(string kind)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Db())
        {
            var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == fixture.Offering);
            if (kind.EndsWith("hosting"))
            {
                offering.CurrentHostingScopeRevisionId = kind == "missing-hosting" ? Guid.NewGuid() : fixture.HostingScope;
                if (kind == "foreign-hosting")
                {
                    var foreign = new ProviderHostingScopeRevision { ProviderId = Guid.NewGuid(), OfferingId = fixture.OtherOffering };
                    db.Add(foreign);
                    offering.CurrentHostingScopeRevisionId = foreign.Id;
                }
            }
            else if (kind == "missing-decision")
                db.Add(new ProviderAuthorizationRecord
                {
                    ProviderId = fixture.Provider, OfferingId = fixture.Offering, CurrentRevisionId = Guid.NewGuid()
                });
            else
                db.Add(new CspPackage
                {
                    ProviderId = fixture.Provider, OfferingId = fixture.Offering, PackageVersionId = Guid.NewGuid(),
                    BoundaryRevisionId = fixture.SourceBoundary, IdempotencyKey = Guid.NewGuid().ToString()
                });
            await db.SaveChangesAsync();
        }
        // Act
        var action = () => fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);
        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("missing-receipt")]
    [InlineData("foreign-receipt")]
    [InlineData("version-overflow")]
    [InlineData("conflicting-offering")]
    [InlineData("conflicting-boundary")]
    public async Task InvalidVersionLinks_FailWithoutLeakingOrDroppingPackages(string kind)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Db())
        {
            var receipt = new CspPackage
            {
                ProviderId = kind == "foreign-receipt" ? Guid.NewGuid() : fixture.Provider,
                OfferingId = kind == "conflicting-offering" ? fixture.OtherOffering : fixture.Offering,
                BoundaryRevisionId = kind == "conflicting-boundary" ? Guid.NewGuid() : fixture.SourceBoundary
            };
            if (kind != "missing-receipt") db.Add(receipt);
            db.Add(new ProviderPackageVersion
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, PackageId = receipt.Id,
                BoundaryRevisionId = fixture.SourceBoundary,
                Version = kind == "version-overflow" ? (long)int.MaxValue + 1 : 1
            });
            await db.SaveChangesAsync();
        }
        // Act
        var action = () => fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);
        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task InvalidRetainedDecisionDate_IsDataFailure_NotClientValidation()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Db())
        {
            var record = new ProviderAuthorizationRecord { ProviderId = fixture.Provider, OfferingId = fixture.Offering };
            var revision = new ProviderAuthorizationRevision
            {
                ProviderId = fixture.Provider, OfferingId = fixture.Offering, RecordId = record.Id,
                BoundaryRevisionId = fixture.SourceBoundary, MetadataReviewState = "Recorded",
                SnapshotJson = Store.Json(new CreateProviderDecisionRequest(7, fixture.SourceBoundary, [], "ProviderDecision",
                    "Retained reference", "Issuer", "External decision", null, "invalid", null,
                    "NoExpiryStated", "Source scope", [], []))
            };
            record.CurrentRevisionId = revision.Id;
            db.AddRange(record, revision);
            await db.SaveChangesAsync();
        }
        // Act
        var action = () => fixture.Service.OverviewAsync(fixture.Offering, 1, 1, 10, default);
        // Assert
        await action.Should().ThrowAsync<InvalidDataException>();
    }
}
