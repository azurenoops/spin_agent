using System.Text;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public partial class NarrativeLibraryServiceTests
{
    [Fact]
    public async Task Proposal_MetadataOnlyEvidenceDoesNotBecomeAnExecutionObservation()
    {
        // Arrange
        await SeedAsync();
        var assessment = new ComplianceAssessment { TenantId = _tenantId, RegisteredSystemId = "system-1" };
        _db.Assessments.Add(assessment);
        _db.Evidence.Add(new() { TenantId = _tenantId, AssessmentId = assessment.Id, ControlId = "AC-2",
            Content = "", ContentHash = "synthetic-metadata-hash", EvidenceType = "ConfigurationExport" });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);

        // Act
        var proposal = await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);

        // Assert
        proposal.MissingEvidence.Should().Contain(item => item.Contains("implementation remains unknown", StringComparison.Ordinal));
        (await _db.ControlImplementations.SingleAsync()).ImplementationStatus.Should().Be(ImplementationStatus.Planned);
    }

    [Fact]
    public async Task Proposal_LegacyPersonAndObjectIdAliasesCannotReviewTheirOwnProposal()
    {
        // Arrange
        await SeedAsync();
        var person = new Ato.Copilot.Core.Models.Onboarding.Person { TenantId = _tenantId,
            DisplayName = "Reviewer", Email = "reviewer@example.invalid", EntraObjectId = Guid.NewGuid() };
        _db.Persons.Add(person);
        _db.OrganizationRoleAssignments.Add(new() { TenantId = _tenantId, Person = person, PersonId = person.Id,
            Role = Ato.Copilot.Core.Models.Onboarding.OrganizationRole.Issm });
        var proposal = new NarrativeProposal { TenantId = _tenantId, RegisteredSystemId = "system-1",
            ControlId = "AC-2", NarrativeType = "Policy", CreatedBy = person.EntraObjectId.Value.ToString(), DeduplicationKey = "legacy-self" };
        _db.NarrativeProposals.Add(proposal);
        await _db.SaveChangesAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());

        // Act
        var review = () => service.ReviewAsync("system-1", proposal.Id, person.Id.ToString(), 1, "RequestRevision", "Own proposal");

        // Assert
        await review.Should().ThrowAsync<UnauthorizedAccessException>();
        proposal.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task WorkspaceAccess_DenialOverridesLegacyAssignment()
    {
        // Arrange
        await SeedAsync();
        var personId = Guid.NewGuid();
        var tenant = new TenantContext(_tenantId) { IsWorkspaceRequest = true, PersonId = personId };
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(item => item.GetAccessAsync(_tenantId, personId, "system-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse("system-1", [],
                new(false, false, false, false, false, false, false, false, false)));
        var library = new NarrativeLibraryService(_db, tenant, access.Object);

        // Act
        var list = () => library.ListAsync("system-1", "author");

        // Assert
        await list.Should().ThrowAsync<UnauthorizedAccessException>();
        access.Verify(item => item.GetAccessAsync(_tenantId, personId, "system-1", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WorkspaceAccess_ReferenceAuthorCannotGenerateNarrativesWithoutNarrativeAuthorPermission()
    {
        // Arrange
        await SeedAsync();
        var personId = Guid.NewGuid();
        var tenant = new TenantContext(_tenantId) { IsWorkspaceRequest = true, PersonId = personId };
        var access = new Mock<ISystemWorkspaceAccessService>();
        access.Setup(item => item.GetAccessAsync(_tenantId, personId, "system-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse("system-1", ["MissionOwner"],
                new(true, true, false, false, false, false, false, false, false)));
        var library = new NarrativeLibraryService(_db, tenant, access.Object);
        var service = new NarrativeProposalService(_db, tenant, library, Mock.Of<IControlNarrativeService>());

        // Act
        var generate = () => service.GenerateAsync("system-1", "AC-2", "Policy", personId.ToString(), 1);
        var permissions = await library.GetAccessAsync("system-1", personId.ToString());

        // Assert
        permissions.CanAuthor.Should().BeTrue();
        permissions.CanGenerate.Should().BeFalse();
        await generate.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Proposal_StaleDraftIsNotAdvertisedAsReviewable()
    {
        // Arrange
        await SeedAsync();
        _db.RmfRoleAssignments.Add(new() { TenantId = _tenantId, RegisteredSystemId = "system-1",
            UserId = "reviewer", RmfRole = RmfRole.Issm });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Draft", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);
        (await _db.RegisteredSystems.SingleAsync()).HostingEnvironment = "Changed";
        await _db.SaveChangesAsync();

        // Act
        var response = await service.ListAsync("system-1", "reviewer");

        // Assert
        response.Single().IsStale.Should().BeTrue();
        response.Single().CanReview.Should().BeFalse();
    }

    [Fact]
    public async Task Draft_PrivateReferenceCannotBeEditedOrPublishedByAnotherSystemAuthor()
    {
        // Arrange
        await SeedAsync();
        _db.RmfRoleAssignments.Add(new() { TenantId = _tenantId, RegisteredSystemId = "system-1",
            UserId = "other-author", RmfRole = RmfRole.Isso });
        await _db.SaveChangesAsync();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("AC-2\nPolicy Narrative:\nPrivate draft."));
        var draft = await _service.ImportAsync("system-1", "author", "Private", "System", "system-1", "reference.txt", content);

        // Act
        var publish = () => _service.PublishAsync("system-1", draft.Id, "other-author", 1, draft.Passages, true);
        var edit = () => _service.UpdateDraftAsync("system-1", draft.Id, "other-author", 1, "System", "system-1", draft.Passages);

        // Assert
        await publish.Should().ThrowAsync<UnauthorizedAccessException>();
        await edit.Should().ThrowAsync<UnauthorizedAccessException>();
        (await _service.ListAsync("system-1", "other-author")).Should().BeEmpty();
    }

    [Fact]
    public async Task Draft_IncompleteMappingCanBeSavedButCannotBePublished()
    {
        // Arrange
        await SeedAsync();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("Unmapped claim"));
        var draft = await _service.ImportAsync("system-1", "author", "Reference", "System", "system-1", "reference.txt", content);

        // Act
        var edited = await _service.UpdateDraftAsync("system-1", draft.Id, "author", 1,
            "System", "system-1", [new(null, "Policy", "Corrected draft claim")]);
        var publish = () => _service.PublishAsync("system-1", draft.Id, "author", edited.Revision, edited.Passages, true);

        // Assert
        edited.Revision.Should().Be(2);
        edited.Passages.Single().ControlId.Should().BeNull();
        await publish.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Proposal_RevokedAuthorDuringModelCallCannotPersistResult()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                (await _db.RmfRoleAssignments.SingleAsync()).IsActive = false;
                await _db.SaveChangesAsync();
                return new GroundedNarrativeDraft("Discarded", [], []);
            });
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);

        // Act
        var generate = () => service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);

        // Assert
        await generate.Should().ThrowAsync<UnauthorizedAccessException>();
        (await _db.NarrativeProposals.CountAsync()).Should().Be(0);
        (await _db.ControlImplementations.SingleAsync()).TechnicalNarrative.Should().Be("Active technical");
    }

    [Fact]
    public async Task Proposal_ChangedSourceDuringModelCallCannotPersistResult()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                (await _db.RegisteredSystems.SingleAsync()).HostingEnvironment = "Changed while generating";
                await _db.SaveChangesAsync();
                return new GroundedNarrativeDraft("Discarded", [], []);
            });
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);

        // Act
        var generate = () => service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);

        // Assert
        await generate.Should().ThrowAsync<InvalidOperationException>().WithMessage("CONCURRENCY_CONFLICT:*");
        (await _db.NarrativeProposals.CountAsync()).Should().Be(0);
    }
}
