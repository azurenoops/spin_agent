using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class SystemDesignServiceTests
{
    private const string ScopeComponentId = "648e6076-8215-4b7e-87e0-f1e12067a9bc";

    [Fact]
    public async Task ComponentScope_SourceRemovalKeepsSavedUseAndReportsStaleness()
    {
        // Arrange
        var workspace = await ScopeWorkspace();
        var service = Service(workspace: workspace.Object);
        await service.SaveComponentScopeAsync(_system,
            new(0, "local", ScopeComponentId, "source-1", "Included", "area", "Retained use"));
        workspace.Setup(x => x.GetSystemComponentPlacementsAsync(_tenant, _system, "local", ScopeComponentId,
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
    public async Task ComponentScope_RejectsEmptyProposalAndOrphanRevisionWithoutWritingHistory()
    {
        // Arrange
        var id = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Add(new ResponsibilityDraft
            {
                Id = id,
                TenantId = _tenant,
                RegisteredSystemId = _system,
                ControlId = "CP-9",
                SuggestionJson = """{"values":{"customer":{"value":"","origin":"From system records","sourceIds":[]}},"questions":[],"conflicts":[]}"""
            });
            await db.SaveChangesAsync();
        }
        var service = Service(workspace: (await ScopeWorkspace()).Object);

        // Act
        var emptyProposal = () => service.SaveComponentScopeAsync(_system,
            new(0, "local", ScopeComponentId, "source-1", "Included", "area", "Human wording")
            { WordingDraftId = id, WordingDraftRevision = 1 });
        var orphanRevision = () => service.SaveComponentScopeAsync(_system,
            new(0, "local", ScopeComponentId, "source-1", "NeedsConfirmation", null, "Retained note")
            { WordingDraftRevision = 7 });

        // Assert
        await emptyProposal.Should().ThrowAsync<ArgumentException>().WithMessage("*no proposed system-use wording*");
        await orphanRevision.Should().ThrowAsync<ArgumentException>().WithMessage("*wording draft identifier*");
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Fact]
    public async Task ComponentScope_RejectsChangedProposalRevisionBeforePersistingUse()
    {
        // Arrange
        var id = Guid.NewGuid();
        await using (var db = new AtoCopilotContext(_options))
        {
            db.Add(new ResponsibilityDraft
            {
                Id = id,
                TenantId = _tenant,
                RegisteredSystemId = _system,
                ControlId = "CP-9",
                Revision = 2
            });
            await db.SaveChangesAsync();
        }
        var service = Service(workspace: (await ScopeWorkspace()).Object);

        // Act
        var save = () => service.SaveComponentScopeAsync(_system,
            new(0, "local", ScopeComponentId, "source-1", "Included", "area", "Human correction")
            { WordingDraftId = id, WordingDraftRevision = 1 });

        // Assert
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>().WithMessage("*proposal changed*");
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Fact]
    public async Task ComponentScope_UnavailableSourceCannotBecomeIncludedUse()
    {
        // Arrange
        var workspace = await ScopeWorkspace(available: false);
        var service = Service(workspace: workspace.Object);

        // Act
        var save = () => service.SaveComponentScopeAsync(_system,
            new(0, "local", ScopeComponentId, "source-1", "Included", "area", "Unverified use"));

        // Assert
        await save.Should().ThrowAsync<ArgumentException>().WithMessage("*unavailable*");
        (await service.GetHistoryAsync(_system)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Included", "area")]
    [InlineData("Excluded", null)]
    [InlineData("NeedsConfirmation", null)]
    public async Task ComponentScope_SavesSystemUseWithoutChangingCanonicalPlacements(string decision, string? area)
    {
        // Arrange
        var workspace = await ScopeWorkspace();
        var service = Service(workspace: workspace.Object);
        var request = new SaveComponentScopeRequest(0, "local", ScopeComponentId, "source-1", decision, area,
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

    [Fact]
    public async Task ComponentScope_RejectsDeniedAndForeignTenantBeforeReadingSource()
    {
        // Arrange
        var workspace = await ScopeWorkspace();
        var request = new SaveComponentScopeRequest(0, "local", ScopeComponentId, "source-1", "Excluded", null, "Not used");

        // Act
        var denied = () => Service(edit: false, workspace: workspace.Object).SaveComponentScopeAsync(_system, request);
        var foreign = () => Service(tenantId: Guid.NewGuid(), workspace: workspace.Object).SaveComponentScopeAsync(_system, request);

        // Assert
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        workspace.VerifyNoOtherCalls();
    }

    private async Task<Mock<IWorkspaceOperationsService>> ScopeWorkspace(bool available = true)
    {
        await using (var db = new AtoCopilotContext(_options))
        {
            db.SystemComponents.Add(new()
            {
                Id = ScopeComponentId,
                TenantId = _tenant,
                RegisteredSystemId = _system,
                Name = "Azure Backup",
                ComponentType = ComponentType.Thing,
                Status = available ? ComponentStatus.Active : ComponentStatus.Decommissioned
            });
            await db.SaveChangesAsync();
        }
        var workspace = new Mock<IWorkspaceOperationsService>();
        workspace.Setup(x => x.GetSystemComponentPlacementsAsync(_tenant, _system, "local", ScopeComponentId,
            It.IsAny<SystemSecurityCapabilityAccess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemComponentPlacementOptions("local", ScopeComponentId, "source-1", "relationship",
                true, null, [new("area", "Mission API")], []));
        return workspace;
    }
}
