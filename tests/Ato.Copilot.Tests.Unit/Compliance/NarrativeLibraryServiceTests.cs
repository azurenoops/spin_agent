using System.Text;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Compliance;

public partial class NarrativeLibraryServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IDisposable _scope;
    private readonly AtoCopilotContext _db;
    private readonly NarrativeLibraryService _service;
    private readonly Guid _tenantId = Guid.NewGuid();

    public NarrativeLibraryServiceTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddDbContext<AtoCopilotContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _provider = services.BuildServiceProvider();
        var tenant = new TenantContext(_tenantId);
        _scope = ((TenantContextAccessor)_provider.GetRequiredService<ITenantContextAccessor>()).Push(tenant);
        _db = _provider.GetRequiredService<AtoCopilotContext>();
        _service = new NarrativeLibraryService(_db, tenant);
    }

    private async Task SeedAsync()
    {
        _db.RegisteredSystems.Add(new RegisteredSystem { Id = "system-1", TenantId = _tenantId, Name = "Synthetic system" });
        _db.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = "system-1", TenantId = _tenantId, UserId = "author", RmfRole = RmfRole.SystemOwner,
        });
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = "system-1", TenantId = _tenantId, ControlId = "AC-2", AuthoredBy = "seed",
            PolicyNarrative = "Active policy", TechnicalNarrative = "Active technical", ApprovalStatus = SspSectionStatus.Approved,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Access_UsesActiveAssignmentsAndFailsClosedAfterRevocation()
    {
        // Arrange
        await SeedAsync();
        // Act
        var access = await _service.GetAccessAsync("system-1", "author");
        // Assert
        access.CanAuthor.Should().BeTrue();
        access.CanPublishShared.Should().BeFalse();
        access.TenantId.Should().Be(_tenantId);
        access.SystemName.Should().Be("Synthetic system");
        (await _db.RmfRoleAssignments.SingleAsync()).IsActive = false;
        await _db.SaveChangesAsync();
        var revoked = () => _service.GetAccessAsync("system-1", "author");
        await revoked.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ImportAndPublish_PreservesClaimsWithoutChangingImplementation()
    {
        // Arrange
        await SeedAsync();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("AC-2\nPolicy Narrative:\nReview quarterly."));
        var draft = await _service.ImportAsync("system-1", "author", "Access policy", "System", "system-1", "reference.txt", content);
        // Act
        var published = await _service.PublishAsync("system-1", draft.Id, "author", draft.Revision, draft.Passages, true);
        // Assert
        published.IsPublished.Should().BeTrue();
        published.Version.Should().Be(1);
        published.PublishedBy.Should().Be("author");
        (await _db.Set<NarrativeReferencePublication>().SingleAsync()).Status.Should().Be("Pending");
        (await _db.ControlImplementations.SingleAsync()).PolicyNarrative.Should().Be("Active policy");
        (await _db.EvidenceArtifacts.CountAsync()).Should().Be(0);
        var rewrite = () => _service.PublishAsync("system-1", draft.Id, "author", published.Revision, draft.Passages, true);
        await rewrite.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false, "AC-2", "Policy")]
    [InlineData(true, "UNKNOWN-99", "Policy")]
    [InlineData(true, "AC-2", "Unknown")]
    public async Task Publish_UnreviewedOrInvalidMapping_IsRejected(bool reviewed, string controlId, string type)
    {
        // Arrange
        await SeedAsync();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("Unmapped language"));
        var draft = await _service.ImportAsync("system-1", "author", "Reference", "System", "system-1", "reference.txt", content);
        // Act
        var action = () => _service.PublishAsync("system-1", draft.Id, "author", draft.Revision,
            [new NarrativeReferencePassage(controlId, type, "Imported claim")], reviewed);
        // Assert
        await action.Should().ThrowAsync<ArgumentException>();
        (await _service.ListAsync("system-1", "author")).Should().OnlyContain(item => !item.IsPublished);
    }

    [Fact]
    public async Task Import_UnassignedAndSharedScopeAuthors_AreDenied()
    {
        // Arrange
        await SeedAsync();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("Claim"));
        // Act
        var unassigned = () => _service.ImportAsync("system-1", "stranger", "Reference", "System", "system-1", "reference.txt", content);
        var shared = () => _service.ImportAsync("system-1", "author", "Reference", "Organization", _tenantId.ToString(), "reference.txt", content);
        // Assert
        await unassigned.Should().ThrowAsync<UnauthorizedAccessException>();
        await shared.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task List_CrossTenantSystem_IsHidden()
    {
        // Arrange
        await SeedAsync();
        var foreign = new NarrativeLibraryService(_db, new TenantContext(Guid.NewGuid()));
        // Act
        var action = () => foreign.ListAsync("system-1", "author");
        // Assert
        await action.Should().ThrowAsync<KeyNotFoundException>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public async Task Proposal_GenerationPreservesActiveContentAndDeduplicates()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed technical", ["Imported claim is unverified"], ["Access review evidence missing"]));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        // Act
        var first = await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);
        var repeat = await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);
        // Assert
        repeat.Id.Should().Be(first.Id);
        first.BeforeContent.Should().Be("Active technical");
        first.ProposedContent.Should().Be("Proposed technical");
        (await _db.ControlImplementations.SingleAsync()).TechnicalNarrative.Should().Be("Active technical");
        generator.Verify(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrganizationAdministrator_PublicationAuthorityDoesNotGrantProposalReview()
    {
        // Arrange
        await SeedAsync();
        var administrator = new Ato.Copilot.Core.Models.Onboarding.Person
        { TenantId = _tenantId, DisplayName = "Administrator", Email = "admin@example.invalid", EntraObjectId = Guid.NewGuid() };
        _db.Persons.Add(administrator);
        _db.OrganizationRoleAssignments.Add(new()
        {
            TenantId = _tenantId, PersonId = administrator.Id, Person = administrator,
            Role = Ato.Copilot.Core.Models.Onboarding.OrganizationRole.Administrator
        });
        var proposal = new NarrativeProposal { TenantId = _tenantId, RegisteredSystemId = "system-1",
            ControlId = "AC-2", NarrativeType = "Technical", CreatedBy = "author", BaseVersion = 1, Revision = 1 };
        _db.NarrativeProposals.Add(proposal);
        await _db.SaveChangesAsync();
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, Mock.Of<IControlNarrativeService>());

        // Act
        var review = () => service.ReviewAsync("system-1", proposal.Id, administrator.EntraObjectId.Value.ToString(),
            1, "RequestRevision", "Not authorized by administrator status.");

        // Assert
        await review.Should().ThrowAsync<UnauthorizedAccessException>();
        proposal.Status.Should().Be("Draft");
        (await _db.ControlImplementations.SingleAsync()).TechnicalNarrative.Should().Be("Active technical");
    }

    [Fact]
    public async Task Proposal_TechnicalStateChangeDoesNotStalePolicy()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var policy = await service.GenerateAsync("system-1", "AC-2", "Policy", "author", 1);
        var technical = await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);
        // Act
        (await _db.RegisteredSystems.SingleAsync()).HostingEnvironment = "Changed environment";
        await _db.SaveChangesAsync();
        var proposals = await service.ListAsync("system-1", "author");
        // Assert
        proposals.Single(item => item.Id == policy.Id).IsStale.Should().BeFalse();
        proposals.Single(item => item.Id == technical.Id).IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task Proposal_OnlyRelevantAssessmentEvidenceChangesTechnicalFreshness()
    {
        // Arrange
        await SeedAsync();
        var assessment = new ComplianceAssessment { TenantId = _tenantId, RegisteredSystemId = "system-1" };
        _db.Assessments.Add(assessment);
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);
        var observation = new ComplianceEvidence { TenantId = _tenantId, AssessmentId = assessment.Id,
            ControlId = "AU-6", Content = "Synthetic observation", EvidenceType = "ConfigurationExport" };
        _db.Evidence.Add(observation);
        await _db.SaveChangesAsync();
        // Act
        var unrelated = await service.ListAsync("system-1", "author");
        observation.ControlId = "AC-2";
        await _db.SaveChangesAsync();
        var relevant = await service.ListAsync("system-1", "author");
        // Assert
        unrelated.Single().IsStale.Should().BeFalse();
        relevant.Single().IsStale.Should().BeTrue();
        var updated = await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);
        updated.Provenance.GetProperty("observedEvidence").EnumerateArray().Single().GetProperty("Content")
            .GetString().Should().Be("Synthetic observation");
    }

    [Fact]
    public async Task Proposal_DoesNotApproveAnUnreviewedCompanionNarrative()
    {
        // Arrange
        await SeedAsync();
        var implementation = await _db.ControlImplementations.SingleAsync();
        implementation.ApprovalStatus = SspSectionStatus.Draft;
        _db.RmfRoleAssignments.Add(new RmfRoleAssignment
        { TenantId = _tenantId, RegisteredSystemId = "system-1", UserId = "reviewer", RmfRole = RmfRole.Issm });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Reviewed policy", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var proposal = await service.GenerateAsync("system-1", "AC-2", "Policy", "author", 1);
        // Act
        await service.ReviewAsync("system-1", proposal.Id, "reviewer", 1, "Approve", "Policy reviewed");
        // Assert
        implementation.PolicyNarrative.Should().Be("Reviewed policy");
        implementation.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        implementation.ApprovedVersionId.Should().BeNull();
    }

    [Fact]
    public async Task Proposal_ConcurrentNarrativeVersionIsStaleEvenWithoutSourceChanges()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        await service.GenerateAsync("system-1", "AC-2", "Policy", "author", 1);
        (await _db.ControlImplementations.SingleAsync()).CurrentVersion++;
        await _db.SaveChangesAsync();
        // Act
        var proposals = await service.ListAsync("system-1", "author");
        // Assert
        proposals.Single().IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task Proposal_AcceptanceReplacesOnlyReviewedHalfOfApprovedBaseline()
    {
        // Arrange
        await SeedAsync();
        var implementation = await _db.ControlImplementations.SingleAsync();
        var approved = new NarrativeVersion { TenantId = _tenantId, ControlImplementationId = implementation.Id,
            VersionNumber = 1, Status = SspSectionStatus.Approved, SnapshotJson = NarrativeContentSnapshot.Capture(implementation) };
        _db.NarrativeVersions.Add(approved);
        implementation.ApprovedVersionId = approved.Id;
        implementation.TechnicalNarrative = "Unapproved working technical";
        implementation.ApprovalStatus = SspSectionStatus.Draft;
        implementation.CurrentVersion = 2;
        _db.RmfRoleAssignments.Add(new() { TenantId = _tenantId, RegisteredSystemId = "system-1",
            UserId = "reviewer", RmfRole = RmfRole.Issm });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Reviewed new policy", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var proposal = await service.GenerateAsync("system-1", "AC-2", "Policy", "author", 2);

        // Act
        await service.ReviewAsync("system-1", proposal.Id, "reviewer", 1, "Approve", "Review only policy");
        var activeApproved = await _db.NarrativeVersions.SingleAsync(item => item.Id == implementation.ApprovedVersionId);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<NarrativeContentSnapshot>(activeApproved.SnapshotJson!);

        // Assert
        snapshot!.PolicyNarrative.Should().Be("Reviewed new policy");
        snapshot.TechnicalNarrative.Should().Be("Active technical");
        implementation.TechnicalNarrative.Should().Be("Unapproved working technical");
        implementation.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        approved.SnapshotJson.Should().Contain("Active policy");
        (await _db.NarrativeReviews.SingleAsync()).ReviewerComments.Should().Be("Review only policy");
    }

    [Fact]
    public async Task PolicyGroundingUsesControlDefinitionAndScopedDeclaredOwnerIndependently()
    {
        // Arrange
        await SeedAsync();
        _db.NistControls.Add(new() { Id = "ac-2", Title = "Account management", Description = "Manage account lifecycle." });
        var capability = new SecurityCapability { Id = "capability-1", TenantId = _tenantId, Name = "Access",
            Owner = "Original responsible role" };
        _db.SecurityCapabilities.Add(capability);
        _db.CapabilityControlMappings.Add(new() { TenantId = _tenantId, SecurityCapabilityId = capability.Id,
            RegisteredSystemId = "system-1", ControlId = "AC-2" });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(item => item.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var policy = await service.GenerateAsync("system-1", "AC-2", "Policy", "author", 1);
        var technical = await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);

        // Act
        capability.Owner = "Replacement responsible role";
        await _db.SaveChangesAsync();
        var current = await service.ListAsync("system-1", "author");

        // Assert
        policy.Provenance.GetProperty("controlDefinition").GetProperty("Description").GetString()
            .Should().Be("Manage account lifecycle.");
        policy.Provenance.GetProperty("declaredControlOwners")[0].GetProperty("Owner").GetString()
            .Should().Be("Original responsible role");
        current.Single(item => item.Id == policy.Id).IsStale.Should().BeTrue();
        current.Single(item => item.Id == technical.Id).IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task Proposal_UsesPersistedResponsibilityAndReportsUnknownObservations()
    {
        // Arrange
        await SeedAsync();
        var baseline = new ControlBaseline { TenantId = _tenantId, RegisteredSystemId = "system-1" };
        _db.ControlBaselines.Add(baseline);
        _db.ControlInheritances.Add(new() { TenantId = _tenantId, ControlBaselineId = baseline.Id,
            ControlId = "AC-2", InheritanceType = InheritanceType.Shared, Provider = "Synthetic provider",
            CustomerResponsibility = "Review customer accounts", SetBy = "seed" });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);

        // Act
        var policy = await service.GenerateAsync("system-1", "AC-2", "Policy", "author", 1);
        var technical = await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);

        // Assert
        policy.Provenance.GetProperty("declaredResponsibilities").GetArrayLength().Should().Be(1);
        technical.Provenance.GetProperty("declaredResponsibilities")[0].GetProperty("CustomerResponsibility")
            .GetString().Should().Be("Review customer accounts");
        technical.MissingEvidence.Should().Contain(item => item.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Proposal_ScopesComponentsAndBoundariesToApplicableCapabilityMapping()
    {
        // Arrange
        await SeedAsync();
        _db.SecurityCapabilities.Add(new() { Id = "capability-1", TenantId = _tenantId, Name = "Access" });
        _db.SystemCapabilityLinks.Add(new() { TenantId = _tenantId, RegisteredSystemId = "system-1", SecurityCapabilityId = "capability-1" });
        _db.CapabilityControlMappings.Add(new() { TenantId = _tenantId, RegisteredSystemId = "system-1",
            SecurityCapabilityId = "capability-1", ControlId = "AC-2", AuthorizationBoundaryDefinitionId = "boundary-1" });
        _db.AuthorizationBoundaryDefinitions.AddRange(
            new() { Id = "boundary-1", TenantId = _tenantId, RegisteredSystemId = "system-1", Name = "Applicable" },
            new() { Id = "boundary-2", TenantId = _tenantId, RegisteredSystemId = "system-1", Name = "Unrelated" });
        _db.SystemComponents.AddRange(
            new() { Id = "component-1", TenantId = _tenantId, RegisteredSystemId = "system-1", Name = "Included" },
            new() { Id = "component-2", TenantId = _tenantId, RegisteredSystemId = "system-1", Name = "Excluded" });
        _db.ComponentCapabilityLinks.AddRange(
            new() { TenantId = _tenantId, SecurityCapabilityId = "capability-1", SystemComponentId = "component-1" },
            new() { TenantId = _tenantId, SecurityCapabilityId = "capability-1", SystemComponentId = "component-2" });
        _db.BoundaryComponentAssignments.AddRange(
            new() { TenantId = _tenantId, SystemComponentId = "component-1", AuthorizationBoundaryDefinitionId = "boundary-1" },
            new() { TenantId = _tenantId, SystemComponentId = "component-2", AuthorizationBoundaryDefinitionId = "boundary-1",
                IsInScope = false, ExclusionRationale = "Not part of control implementation" });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);

        // Act
        var technical = await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);

        // Assert
        technical.Provenance.GetProperty("declaredComponents").EnumerateArray()
            .Select(item => item.GetProperty("Id").GetString()).Should().Equal("component-1");
        technical.Provenance.GetProperty("declaredBoundaries").EnumerateArray()
            .Select(item => item.GetProperty("Id").GetString()).Should().Equal("boundary-1");
    }

    [Fact]
    public async Task DraftMapping_CanBeCorrectedAndMovedBeforePublication()
    {
        // Arrange
        await SeedAsync();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("Unmapped reference"));
        var draft = await _service.ImportAsync("system-1", "author", "Reference", "System", "system-1", "reference.txt", content);
        _db.SecurityCapabilities.Add(new SecurityCapability { TenantId = _tenantId, Id = "capability-1", Name = "Access management" });
        var person = new Ato.Copilot.Core.Models.Onboarding.Person
        { TenantId = _tenantId, DisplayName = "Publisher", Email = "publisher@example.invalid" };
        _db.Persons.Add(person);
        _db.OrganizationRoleAssignments.Add(new() { TenantId = _tenantId, Person = person, PersonId = person.Id,
            Role = Ato.Copilot.Core.Models.Onboarding.OrganizationRole.Issm });
        await _db.SaveChangesAsync();

        // Act
        var updated = await _service.UpdateDraftAsync("system-1", draft.Id, person.Id.ToString(), 1,
            "Capability", "capability-1", [new("ac-2", "Policy", "Reviewed reference claim")]);
        var stale = () => _service.UpdateDraftAsync("system-1", draft.Id, person.Id.ToString(), 1,
            "Organization", _tenantId.ToString(), updated.Passages);
        var published = await _service.PublishAsync("system-1", updated.Id, person.Id.ToString(),
            updated.Revision, updated.Passages, true);

        // Assert
        updated.Revision.Should().Be(2);
        updated.Scope.Should().Be("Capability");
        updated.Passages.Single().ControlId.Should().Be("AC-2");
        published.IsPublished.Should().BeTrue();
        await stale.Should().ThrowAsync<InvalidOperationException>();
        (await _db.NarrativeReferences.SingleAsync()).OriginalPassagesJson.Should().Contain("Unmapped reference");
    }

    [Fact]
    public async Task Proposal_ApprovedStateBecomesStaleAfterManualSameHalfEdit()
    {
        // Arrange
        await SeedAsync();
        _db.RmfRoleAssignments.Add(new() { TenantId = _tenantId, RegisteredSystemId = "system-1",
            UserId = "reviewer", RmfRole = RmfRole.Issm });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Accepted policy", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var proposal = await service.GenerateAsync("system-1", "AC-2", "Policy", "author", 1);
        await service.ReviewAsync("system-1", proposal.Id, "reviewer", 1, "Approve", null);

        // Act
        var implementation = await _db.ControlImplementations.SingleAsync();
        implementation.PolicyNarrative = "Manual replacement";
        implementation.CurrentVersion++;
        await _db.SaveChangesAsync();
        var proposals = await service.ListAsync("system-1", "author");

        // Assert
        proposals.Single().IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task Proposal_UnrelatedSystemComponentDoesNotChangeTechnicalState()
    {
        // Arrange
        await SeedAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);

        // Act
        _db.SystemComponents.Add(new SystemComponent
        { TenantId = _tenantId, RegisteredSystemId = "system-1", Name = "Unrelated component", CreatedBy = "seed" });
        await _db.SaveChangesAsync();
        var proposals = await service.ListAsync("system-1", "author");

        // Assert
        proposals.Single().IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task Proposal_RepeatedObservationAndCollectionTimesDoNotChangeTechnicalState()
    {
        // Arrange
        await SeedAsync();
        var assessment = new ComplianceAssessment { TenantId = _tenantId, RegisteredSystemId = "system-1" };
        _db.Assessments.Add(assessment);
        var evidence = new ComplianceEvidence { TenantId = _tenantId, AssessmentId = assessment.Id,
            ControlId = "AC-2", ResourceId = "synthetic-resource", Content = "Observed configuration", EvidenceType = "ConfigurationExport" };
        _db.Evidence.Add(evidence);
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("Proposed", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        await service.GenerateAsync("system-1", "AC-2", "Technical", "author", 1);

        // Act
        evidence.CollectedAt = evidence.CollectedAt.AddDays(1);
        _db.Evidence.Add(new ComplianceEvidence { TenantId = _tenantId, AssessmentId = assessment.Id,
            ControlId = "AC-2", ResourceId = evidence.ResourceId, Content = evidence.Content, EvidenceType = evidence.EvidenceType });
        await _db.SaveChangesAsync();
        var proposals = await service.ListAsync("system-1", "author");

        // Assert
        proposals.Single().IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task Proposal_ApprovalRequiresSeparateReviewerAndPreservesOtherHalf()
    {
        // Arrange
        await SeedAsync();
        _db.RmfRoleAssignments.Add(new RmfRoleAssignment
        { TenantId = _tenantId, RegisteredSystemId = "system-1", UserId = "reviewer", RmfRole = RmfRole.Issm });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(service => service.GenerateGroundedDraftAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroundedNarrativeDraft("New policy", [], []));
        var service = new NarrativeProposalService(_db, new TenantContext(_tenantId), _service, generator.Object);
        var proposal = await service.GenerateAsync("system-1", "AC-2", "Policy", "author", 1);
        // Act
        var self = () => service.ReviewAsync("system-1", proposal.Id, "author", 1, "Approve", "Reviewed");
        await self.Should().ThrowAsync<UnauthorizedAccessException>();
        await service.ReviewAsync("system-1", proposal.Id, "reviewer", 1, "Approve", "Reviewed");
        // Assert
        var active = await _db.ControlImplementations.SingleAsync();
        active.PolicyNarrative.Should().Be("New policy");
        active.TechnicalNarrative.Should().Be("Active technical");
        active.ImplementationStatus.Should().Be(ImplementationStatus.Planned);
        active.CurrentVersion.Should().Be(2);
        (await _db.NarrativeVersions.CountAsync()).Should().Be(2);
        active.ApprovedVersionId.Should().NotBeNull();
    }
}