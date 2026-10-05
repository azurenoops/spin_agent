using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Interfaces.Workspaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemDesignServiceTests
{
    [Fact]
    public async Task ComponentScope_EmptyWordingProposalCannotSupplyProvenance()
    {
        // Arrange
        var id = Guid.NewGuid();
        await using (var db = new Ato.Copilot.Core.Data.Context.AtoCopilotContext(_options))
        {
            db.Add(new Ato.Copilot.Core.Models.Compliance.ResponsibilityDraft { Id = id, TenantId = _tenant,
                RegisteredSystemId = _system, ControlId = "CP-9",
                SuggestionJson = """{"values":{"customer":{"value":"","origin":"From system records","sourceIds":[]}},"questions":[],"conflicts":[]}""" });
            await db.SaveChangesAsync();
        }
        var service = Service(workspace: ScopeWorkspace().Object);
        // Act
        var save = () => service.SaveComponentScopeAsync(_system,
            new(0, "provider", "backup", "source-1", "Included", "area", "Human wording") { WordingDraftId = id, WordingDraftRevision = 1 });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>().WithMessage("*no proposed system-use wording*");
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Fact]
    public async Task ComponentScope_SourceRemovalIdentifiesAffectedDraftWithoutReplacingIt()
    {
        // Arrange
        var workspace = ScopeWorkspace();
        var service = Service(workspace: workspace.Object);
        await service.SaveComponentScopeAsync(_system, new(0, "provider", "backup", "source-1", "Included", "area", "Retained use"));
        workspace.Setup(x => x.GetSystemComponentPlacementsAsync(_tenant, _system, "provider", "backup",
            It.IsAny<SystemSecurityCapabilityAccess>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Source no longer available"));
        // Act
        var graph = await service.GetAsync(_system);
        // Assert
        graph.SourcesStale.Should().BeTrue();
        graph.Gaps.Should().Contain(x => x.Explanation.Contains("source for Azure Backup is unavailable"));
        graph.ComponentScopes.Single().Usage.Should().Be("Retained use");
        graph.ComponentScopes.Single().SourceRevision.Should().Be("source-1");
    }

    [Fact]
    public async Task ComponentScope_OrphanWordingRevisionIsNotAccepted()
    {
        // Arrange
        var service = Service(workspace: ScopeWorkspace().Object);
        // Act
        var save = () => service.SaveComponentScopeAsync(_system,
            new(0, "provider", "backup", "source-1", "NeedsConfirmation", null, "Retained notes") { WordingDraftRevision = 7 });
        // Assert
        await save.Should().ThrowAsync<ArgumentException>().WithMessage("*wording draft identifier*");
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Fact]
    public async Task ComponentScope_ChangedWordingProposalMustBeComparedBeforeSaving()
    {
        // Arrange
        var id = Guid.NewGuid();
        await using (var db = new Ato.Copilot.Core.Data.Context.AtoCopilotContext(_options))
        {
            db.Add(new Ato.Copilot.Core.Models.Compliance.ResponsibilityDraft { Id = id, TenantId = _tenant,
                RegisteredSystemId = _system, ControlId = "CP-9", Revision = 2 });
            await db.SaveChangesAsync();
        }
        var service = Service(workspace: ScopeWorkspace().Object);
        // Act
        var save = () => service.SaveComponentScopeAsync(_system,
            new(0, "provider", "backup", "source-1", "Included", "area", "Human correction") { WordingDraftId = id, WordingDraftRevision = 1 });
        // Assert
        await save.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException>().WithMessage("*proposal changed*");
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Fact]
    public async Task ComponentScope_UnavailableSourceCannotBecomeNewIncludedUse()
    {
        // Arrange
        var workspace = ScopeWorkspace();
        workspace.Setup(x => x.GetSystemComponentPlacementsAsync(_tenant, _system, "provider", "backup",
            It.IsAny<SystemSecurityCapabilityAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemComponentPlacementOptions("provider", "backup", "source-1", "relationship",
                false, "Source unavailable", [new("area", "Mission API")], []) { ComponentName = "Azure Backup", SourceAvailable = false });
        var service = Service(workspace: workspace.Object);
        // Act
        var save = () => service.SaveComponentScopeAsync(_system, new(0, "provider", "backup", "source-1", "Included", "area", "Unverified use"));
        // Assert
        await save.Should().ThrowAsync<ArgumentException>().WithMessage("*unavailable*");
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComponentScope_PreservesAIProvenanceAndHumanCorrections(bool foreignDraft)
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var suggestion = new Ato.Copilot.Core.Models.Compliance.ResponsibilityDraftSuggestion(new() {
            ["scope"] = new("Suggested service use", "AI proposed", ["published-v7"], SourceHash: "pinned-hash"),
            ["customer"] = new("Recorded duty", "From system records", ["system"])
        }, [], []);
        await using (var db = new Ato.Copilot.Core.Data.Context.AtoCopilotContext(_options))
        {
            if (foreignDraft) db.RegisteredSystems.Add(new() { Id = "another-system", TenantId = _tenant, Name = "Other synthetic system" });
            db.Add(new Ato.Copilot.Core.Models.Compliance.ResponsibilityDraft { Id = draftId, TenantId = _tenant,
                RegisteredSystemId = foreignDraft ? "another-system" : _system, ControlId = "CP-9", SourceHash = "pinned-hash",
                SuggestionJson = System.Text.Json.JsonSerializer.Serialize(suggestion, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) });
            await db.SaveChangesAsync();
        }
        var service = Service(workspace: ScopeWorkspace().Object);
        // Act
        var save = () => service.SaveComponentScopeAsync(_system,
            new(0, "provider", "backup", "source-1", "Included", "area", "Human corrected service use") {
                WordingDraftId = draftId, WordingDraftRevision = 1 });
        // Assert
        if (foreignDraft) await save.Should().ThrowAsync<KeyNotFoundException>();
        else
        {
            var result = await save();
            result.ComponentScopes.Single().WordingBasis!.SourceHash.Should().Be("pinned-hash");
            result.ComponentScopes.Single().WordingBasis!.OriginalWording.Should().Be("Suggested service use");
            result.ComponentScopes.Single().WordingBasis!.UserEdited.Should().BeTrue();
            result.ComponentScopes.Single().Usage.Should().Be("Human corrected service use");
        }
    }

    [Theory]
    [InlineData("Included", "area")]
    [InlineData("Excluded", null)]
    public async Task ComponentScope_UsesIndependentReviewAndPreservesBaselineThroughLaterDrafts(string decision, string? area)
    {
        // Arrange
        await SeedReviewedSourcesAsync();
        var workspace = ScopeWorkspace();
        var service = Service(workspace: workspace.Object);
        var initial = await service.GetAsync(_system);
        var ready = await service.SaveAsync(_system, Save(initial) with {
            Nodes = initial.Nodes.Select(node => node.Kind == "Component" ? node with { BoundaryDisposition = "InBoundary" } : node).ToArray() });
        var draft = await service.SaveComponentScopeAsync(_system,
            new(ready.Revision, "provider", "backup", "source-1", decision, area, "Human-reviewed service usage"));
        var submitted = await service.ReviewAsync(_system, new(draft.Revision, "submit", "Review service use"));
        // Act
        var selfReview = () => service.ReviewAsync(_system, new(submitted.Revision, "approve", "Self-review"));
        await selfReview.Should().ThrowAsync<UnauthorizedAccessException>();
        var approved = await Service(_reviewer, workspace: workspace.Object).ReviewAsync(_system,
            new(submitted.Revision, "approve", "Independent service-use review"));
        var derived = await service.ReviewAsync(_system, new(approved.Revision, "derive_draft", "New service-use question"));
        var changed = await service.SaveComponentScopeAsync(_system,
            new(derived.Revision, "provider", "backup", "source-1", "NeedsConfirmation", null, "New unresolved question"));
        var retained = await service.GetApprovedAsync(_system);
        // Assert
        approved.ComponentScopes.Single().Decision.Should().Be(decision);
        retained!.Graph.ComponentScopes.Single().Decision.Should().Be(decision);
        retained.Graph.ComponentScopes.Single().Usage.Should().Be("Human-reviewed service usage");
        changed.ComponentScopes.Single().Decision.Should().Be("NeedsConfirmation");
        changed.BaselineChanges.Should().Contain(x => x.RecordId == "component-use:provider:backup");
    }

    private Mock<IWorkspaceOperationsService> ScopeWorkspace(string revision = "source-1")
    {
        var workspace = new Mock<IWorkspaceOperationsService>();
        workspace.Setup(x => x.GetSystemComponentPlacementsAsync(_tenant, _system, "provider", "backup",
            It.IsAny<SystemSecurityCapabilityAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemComponentPlacementOptions("provider", "backup", revision, "relationship",
                true, null, [new("area", "Mission API")], []) { ComponentName = "Azure Backup", SourceAvailable = true });
        return workspace;
    }

    [Theory]
    [InlineData("Included", "area")]
    [InlineData("Excluded", null)]
    [InlineData("NeedsConfirmation", null)]
    public async Task ComponentScope_SavesUseDraftWithoutChangingCanonicalPlacements(string decision, string? area)
    {
        // Arrange
        var workspace = ScopeWorkspace();
        var service = Service(workspace: workspace.Object);
        var request = new SaveComponentScopeRequest(0, "provider", "backup", "source-1", decision, area,
            "User-corrected usage; workloads not verified.");
        // Act
        var graph = await service.SaveComponentScopeAsync(_system, request);
        var reloaded = await service.GetAsync(_system);
        // Assert
        graph.GovernanceStatus.Should().Be("Draft");
        reloaded.ComponentScopes.Should().ContainSingle().Which.Should().BeEquivalentTo(graph.ComponentScopes.Single());
        graph.ComponentScopes.Single().Decision.Should().Be(decision);
        workspace.Verify(x => x.AssignSystemComponentPlacementAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AssignSystemComponentPlacementRequest>(),
            It.IsAny<SystemSecurityCapabilityAccess>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Included", null, "source-1")]
    [InlineData("Included", "foreign-area", "source-1")]
    [InlineData("Invented", null, "source-1")]
    [InlineData("Excluded", "area", "source-1")]
    [InlineData("Included", "area", "old-source")]
    public async Task ComponentScope_RejectsInvalidOrStaleRecords(string decision, string? area, string source)
    {
        // Arrange
        var service = Service(workspace: ScopeWorkspace().Object);
        // Act
        var save = () => service.SaveComponentScopeAsync(_system,
            new(0, "provider", "backup", source, decision, area, "Retained note"));
        // Assert
        await save.Should().ThrowAsync<Exception>();
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Fact]
    public async Task ComponentScope_RetainsCorrectionsDuringGeneralDesignSaveAndDetectsSourceChange()
    {
        // Arrange
        var workspace = ScopeWorkspace();
        var service = Service(workspace: workspace.Object);
        var graph = await service.SaveComponentScopeAsync(_system,
            new(0, "provider", "backup", "source-1", "Included", "area", "Human correction"));
        // Act
        graph = await service.SaveAsync(_system, Save(graph));
        workspace.Setup(x => x.GetSystemComponentPlacementsAsync(_tenant, _system, "provider", "backup",
            It.IsAny<SystemSecurityCapabilityAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemComponentPlacementOptions("provider", "backup", "source-2", "relationship",
                true, null, [new("area", "Mission API")], []) { ComponentName = "Azure Backup", SourceAvailable = true });
        var stale = await service.GetAsync(_system);
        // Assert
        graph.ComponentScopes.Single().Usage.Should().Be("Human correction");
        stale.SourcesStale.Should().BeTrue();
        stale.Gaps.Should().Contain(x => x.Id.StartsWith("ComponentScopeSource:"));
        stale.ComponentScopes.Single().SourceRevision.Should().Be("source-1");
    }

    [Fact]
    public async Task ComponentScope_RejectsDeniedEditorAndForeignTenantBeforeSourceRead()
    {
        // Arrange
        var workspace = ScopeWorkspace();
        var request = new SaveComponentScopeRequest(0, "provider", "backup", "source-1", "Excluded", null, "Not used");
        // Act
        var denied = () => Service(edit: false, workspace: workspace.Object).SaveComponentScopeAsync(_system, request);
        var foreign = () => Service(tenantId: Guid.NewGuid(), workspace: workspace.Object).SaveComponentScopeAsync(_system, request);
        // Assert
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        workspace.VerifyNoOtherCalls();
    }
}
