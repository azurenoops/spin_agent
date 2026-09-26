using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Fixture = Ato.Copilot.Tests.Unit.PackageImports.CspPackageServiceTests.Fixture;

namespace Ato.Copilot.Tests.Unit.PackageImports;

public sealed class CspPackageReviewRecoveryTests
{
    [Fact]
    public async Task ReviewState_FreshBlockedPreviewIsCurrentButStillIneligible()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        var preview = await fixture.PreviewAsync(package.PackageId);
        preview.Blockers.Should().NotBeEmpty();
        // Act
        var recovered = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        // Assert
        recovered.Preview.Should().BeEquivalentTo(preview);
        recovered.PreviewIsStale.Should().BeFalse();
        var approve = () => fixture.Service.ApproveAsync(package.PackageId,
            new(preview.PreviewId, preview.PreviewHash, preview.Revision), "reviewer", default);
        await approve.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task ReviewState_UsesPublicationOrderRatherThanPreviewCreationOrder()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var normal = Fixture.Analysis(inputs);
                return Task.FromResult(normal with
                {
                    Candidates = [normal.Candidates[0], normal.Candidates[0] with { Key = "second-component", Name = "Second component" }]
                });
            });
        var package = await fixture.AnalyzeAsync();
        await fixture.ReviewAllAsync(package.PackageId);
        var status = await fixture.Service.GetAsync(package.PackageId, default);
        var components = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "Component", null, default)).Items;
        var first = await fixture.Service.PreviewAsync(package.PackageId,
            new(status.Revision, [new(components[0].CandidateId, components[0].Revision)]), "reviewer", default);
        var second = await fixture.Service.PreviewAsync(package.PackageId,
            new(status.Revision, [new(components[1].CandidateId, components[1].Revision)]), "reviewer", default);
        var firstDecision = new PackageDecisionRequest(first.PreviewId, first.PreviewHash, first.Revision);
        var secondDecision = new PackageDecisionRequest(second.PreviewId, second.PreviewHash, second.Revision);
        await fixture.Service.ApproveAsync(package.PackageId, firstDecision, "reviewer", default);
        await fixture.Service.ApproveAsync(package.PackageId, secondDecision, "reviewer", default);
        await fixture.Service.PublishAsync(package.PackageId, secondDecision, "second", "reviewer", default);
        var lastPublication = await fixture.Service.PublishAsync(package.PackageId, firstDecision, "first", "reviewer", default);
        // Act
        var recovered = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        // Assert
        recovered.Preview!.PreviewId.Should().Be(second.PreviewId);
        recovered.Publication.Should().BeEquivalentTo(lastPublication);
        recovered.Publication!.Records.Single().CandidateId.Should().Be(components[0].CandidateId);
    }

    [Fact]
    public async Task Reference_ExplicitMetadataCanBeReviewed_WithoutClaimingIncompletePackageAnalysisSucceeded()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        AddReference(fixture, incomplete: true);
        var package = await fixture.ReceiveAsync("partial-reference", "Synthetic cited reference with unresolved surrounding text");
        var analyze = () => fixture.Service.ProcessSynchronouslyAsync(package.PackageId, default);
        await analyze.Should().ThrowAsync<Ato.Copilot.Core.Services.PackageImports.PackageAnalysisException>();
        var candidate = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "AuthorizationReference", null, default)).Items.Single();
        // Act
        var reviewed = await fixture.Service.EditAsync(package.PackageId, candidate.CandidateId, Fixture.Edit(candidate), "reviewer", default);
        // Assert
        reviewed.ReviewState.Should().Be("Reviewed");
        var status = await fixture.Service.GetAsync(package.PackageId, default);
        status.ProcessingState.Should().Be("NeedsAttention");
        status.Coverage.Unsupported.Should().Be(1);
        status.PublicationState.Should().Be("Unpublished");
    }

    [Fact]
    public async Task ReviewState_RecoversPersistedPreviewApprovalAndPublicationIndependently()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        var empty = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        empty.Preview.Should().BeNull();
        empty.Publication.Should().BeNull();
        empty.PreviewIsStale.Should().BeFalse();
        await fixture.ReviewAllAsync(package.PackageId);
        var preview = await fixture.PreviewAsync(package.PackageId);
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        var savedPreview = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        savedPreview.Preview.Should().BeEquivalentTo(preview);
        savedPreview.PreviewIsStale.Should().BeFalse();
        await fixture.Service.ApproveAsync(package.PackageId, decision, "reviewer", default);
        var savedApproval = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        savedApproval.Preview!.State.Should().Be("Approved");
        savedApproval.PreviewIsStale.Should().BeFalse();
        var published = await fixture.Service.PublishAsync(package.PackageId, decision, "published", "reviewer", default);
        var savedPublication = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        savedPublication.Publication.Should().BeEquivalentTo(published);
        savedPublication.Preview!.State.Should().Be("Published");
        var newerPreview = await fixture.PreviewAsync(package.PackageId);
        // Act
        var recovered = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        // Assert
        recovered.Preview!.PreviewId.Should().Be(newerPreview.PreviewId);
        recovered.Publication.Should().BeEquivalentTo(published);
        recovered.PreviewIsStale.Should().BeFalse();
        recovered.Preview.Blockers.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("edited")]
    [InlineData("impact")]
    public async Task ReviewState_RecomputesStalenessWithoutChangingPersistedDecision(string change)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        await fixture.ReviewAllAsync(package.PackageId);
        var preview = await fixture.PreviewAsync(package.PackageId);
        await fixture.Service.ApproveAsync(package.PackageId,
            new(preview.PreviewId, preview.PreviewHash, preview.Revision), "reviewer", default);
        if (change == "edited")
        {
            var component = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "Component", null, default)).Items.Single();
            await fixture.Service.EditAsync(package.PackageId, component.CandidateId,
                Fixture.Edit(component) with { Name = "Changed candidate" }, "reviewer", default);
        }
        else
        {
            await using var db = fixture.Factory.CreateDbContext();
            if (change == "expired")
                (await db.CspPackageApprovals.SingleAsync()).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            else
                db.CspInheritedComponents.Add(new()
                {
                    CspProfileId = fixture.ProviderId, Name = "Synthetic component",
                    Status = Ato.Copilot.Core.Models.Tenancy.CspInheritedComponentStatus.Published
                });
            await db.SaveChangesAsync();
        }
        // Act
        var recovered = await fixture.Service.ReviewStateAsync(package.PackageId, default);
        // Assert
        recovered.Preview!.PreviewId.Should().Be(preview.PreviewId);
        recovered.Preview.PreviewHash.Should().Be(preview.PreviewHash);
        recovered.Preview.Revision.Should().Be(preview.Revision);
        recovered.PreviewIsStale.Should().BeTrue();
        recovered.Publication.Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ReviewState_DeniesSubscriberAndImpersonation(bool admin, bool impersonated)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        fixture.Tenant.SetupGet(x => x.IsCspAdmin).Returns(admin);
        fixture.Tenant.SetupGet(x => x.ImpersonatedTenantId).Returns(impersonated ? Guid.NewGuid() : null);
        // Act
        var read = () => fixture.Service.ReviewStateAsync(package.PackageId, default);
        // Assert
        await read.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Reference_StaysPrivate_PreservesMetadataForLegacyEdit_AndCannotPublish()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        AddReference(fixture);
        var package = await fixture.AnalyzeAsync();
        var reference = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "AuthorizationReference", null, default)).Items.Single();
        reference.AuthorizationReference.Should().BeEquivalentTo(Reference());
        reference.ReviewState.Should().Be("NeedsReview");
        // Act
        await fixture.Service.EditAsync(package.PackageId, reference.CandidateId, Fixture.Edit(reference), "reviewer", default);
        var recorded = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "AuthorizationReference", "Reviewed", default)).Items.Single();
        var status = await fixture.Service.GetAsync(package.PackageId, default);
        var preview = await fixture.Service.PreviewAsync(package.PackageId,
            new(status.Revision, [new(recorded.CandidateId, recorded.Revision)]), "reviewer", default);
        // Assert
        recorded.AuthorizationReference.Should().BeEquivalentTo(Reference());
        recorded.Citations.Should().NotBeEmpty();
        preview.Blockers.Should().Contain(x => x.Contains("supporting analysis"));
        await using var db = fixture.Factory.CreateDbContext();
        (await db.CspInheritedComponents.CountAsync()).Should().Be(0);
        (await db.AuthorizationDecisions.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("issuer")]
    [InlineData("dates")]
    [InlineData("name")]
    public async Task Reference_InvalidMetadata_IsRejectedWithoutChangingRevision(string invalid)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        AddReference(fixture);
        var package = await fixture.AnalyzeAsync();
        var reference = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "AuthorizationReference", null, default)).Items.Single();
        var metadata = Reference();
        metadata = invalid switch
        {
            "reference" => metadata with { Reference = new string('x', 2001) },
            "issuer" => metadata with { Issuer = new string('x', 501) },
            "name" => metadata,
            _ => metadata with { ExpiresAt = metadata.IssuedAt!.Value.AddDays(-1) }
        };
        // Act
        var edit = () => fixture.Service.EditAsync(package.PackageId, reference.CandidateId,
            Fixture.Edit(reference) with
            {
                Name = invalid == "name" ? new string('x', 2001) : reference.Name,
                AuthorizationReference = metadata
            }, "reviewer", default);
        // Assert
        await edit.Should().ThrowAsync<ArgumentException>();
        (await fixture.Service.GetAsync(package.PackageId, default)).Revision.Should().Be(package.Revision);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("ComponentType")]
    public async Task ReferenceValidationChange_DoesNotRelaxInventoryValidation(string field)
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        var package = await fixture.AnalyzeAsync();
        var component = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "Component", null, default)).Items.Single();
        var input = JsonSerializer.SerializeToNode(Fixture.Edit(component))
            ?? throw new InvalidOperationException("The fixture request did not serialize.");
        input[field] = field == "Name" ? new string('x', 257) : null;
        var request = input.Deserialize<EditPackageCandidateRequest>()
            ?? throw new InvalidOperationException("The fixture request did not deserialize.");

        // Act
        var edit = () => fixture.Service.EditAsync(package.PackageId, component.CandidateId, request, "reviewer", default);

        // Assert
        await edit.Should().ThrowAsync<ArgumentException>();
        (await fixture.Service.GetAsync(package.PackageId, default)).Revision.Should().Be(package.Revision);
    }

    [Fact]
    public async Task Reference_EditingInvalidatesApproval_AndExcludedEvidenceCannotRemainReviewed()
    {
        // Arrange
        await using var fixture = await Fixture.CreateAsync();
        AddReference(fixture);
        var package = await fixture.AnalyzeAsync();
        await fixture.ReviewAllAsync(package.PackageId);
        var candidates = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, null, null, default)).Items;
        var status = await fixture.Service.GetAsync(package.PackageId, default);
        var preview = await fixture.Service.PreviewAsync(package.PackageId, new(status.Revision,
            candidates.Where(x => x.Type != "AuthorizationReference").Select(x => new PackageSelection(x.CandidateId, x.Revision)).ToArray()), "reviewer", default);
        await fixture.Service.ApproveAsync(package.PackageId, new(preview.PreviewId, preview.PreviewHash, preview.Revision), "reviewer", default);
        var reference = candidates.Single(x => x.Type == "AuthorizationReference");
        await fixture.Service.EditAsync(package.PackageId, reference.CandidateId,
            Fixture.Edit(reference) with { AuthorizationReference = Reference() with { Issuer = "Reviewed issuer" } }, "reviewer", default);
        (await fixture.Service.ReviewStateAsync(package.PackageId, default)).PreviewIsStale.Should().BeTrue();
        var source = (await fixture.Service.EntriesAsync(package.PackageId, 1, 25, default)).Items.Single();
        // Act
        await fixture.Service.ExcludeAsync(package.PackageId, source.EntryId, new(source.Revision, "Excluded source"), "reviewer", default);
        // Assert
        (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "AuthorizationReference", "Reviewed", default)).Items.Should().BeEmpty();
        var excludedReference = (await fixture.Service.CandidatesAsync(package.PackageId, 1, 25, "AuthorizationReference", null, default)).Items.Single();
        var review = () => fixture.Service.EditAsync(package.PackageId, excludedReference.CandidateId,
            Fixture.Edit(excludedReference), "reviewer", default);
        await review.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private static PackageAuthorizationReference Reference() =>
        new("Synthetic stated reference", "Synthetic issuer", DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2027-01-01T00:00:00Z"));

    private static void AddReference(Fixture fixture, bool incomplete = false) =>
        fixture.Analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<IReadOnlyList<CspPackageAnalysisInput>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CspPackageAnalysisInput> inputs, CancellationToken _) =>
            {
                var result = Fixture.Analysis(inputs);
                var metadata = Reference();
                var reference = new CspPackageCandidateDraft("reference", CspPackageCandidateKind.AuthorizationReference,
                    "Synthetic stated reference", "Stated metadata only", null, null, null, null, [], [], result.Candidates[0].Citations)
                {
                    AuthorizationReference = new(metadata.Reference, metadata.Issuer, metadata.IssuedAt, metadata.ExpiresAt)
                };
                return Task.FromResult(result with
                {
                    Candidates = [.. result.Candidates, reference],
                    Entries = result.Entries.Select(x => x with { AnalysisComplete = !incomplete }).ToArray(),
                    Coverage = result.Coverage with { AnalysisComplete = !incomplete }
                });
            });
}
