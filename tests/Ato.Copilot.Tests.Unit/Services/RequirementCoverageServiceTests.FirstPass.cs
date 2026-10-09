using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using System.Text.Json;
using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class RequirementCoverageServiceTests
{
    private async Task<(RequirementCoverageService Service, Mock<IControlNarrativeService> Generator)> FirstPassFixtureAsync()
    {
        _db.ControlImplementations.Add(new() { TenantId = _tenantId, RegisteredSystemId = _systemId, ControlId = "AC-11",
            PolicyNarrative = "Existing declared policy", ApprovalStatus = SspSectionStatus.Draft });
        _db.SystemProfileSections.Add(new() { TenantId = _tenantId, RegisteredSystemId = _systemId, SectionType = ProfileSectionType.MissionAndPurpose,
            DraftContent = """{"missionStatement":"Recorded mission support","businessPurpose":"Recorded operational records","secret":"do-not-send-secret"}""" });
        await _db.SaveChangesAsync();
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(g => g.GenerateRequirementFirstPassAsync(It.IsAny<RequirementFirstPassContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequirementFirstPassContext context, CancellationToken _) => new RequirementFirstPassSuggestion(
                [new(context.Requirements[0].Id, "First-pass policy from recorded mission support; review needed.",
                    [context.Sources.Single(s => s.Kind == "SystemProfile").Id], "Based on the recorded Mission source.")],
                [], ["Record an explicit supporting parameter value."], []));
        var service = new RequirementCoverageService(_db, _tenant.Object, _access.Object, NullLogger<RequirementCoverageService>.Instance,
            generator.Object, new EphemeralDataProtectionProvider());
        return (service, generator);
    }

    [Fact]
    public async Task FirstPass_IsNonPersistentGroundedAndPreservesProofInExplicitUnreviewedSave()
    {
        // Arrange
        var (service, generator) = await FirstPassFixtureAsync();
        var before = await service.ReadAsync(_systemId, "AC-11", default);
        RequirementFirstPassContext? captured = null;
        generator.Setup(g => g.GenerateRequirementFirstPassAsync(It.IsAny<RequirementFirstPassContext>(), It.IsAny<CancellationToken>()))
            .Callback<RequirementFirstPassContext, CancellationToken>((context, _) => captured = context)
            .ReturnsAsync((RequirementFirstPassContext context, CancellationToken _) => new(
                [new(context.Requirements[0].Id, "Source-backed editable draft", [context.Sources.Single(s => s.Kind == "SystemProfile").Id], "Recorded mission source")], [], [], []));
        // Act
        var draft = await service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion!.Value, before.BaselineRevision, "Policy"));
        // Assert
        captured.Should().NotBeNull();
        captured!.Sources.Single(s => s.Kind == "SystemProfile").Content.Should().Contain("Recorded mission support").And.NotContain("do-not-send-secret");
        (await _db.ControlImplementations.SingleAsync()).RequirementCoverageJson.Should().BeNull();
        (await _db.NarrativeVersions.CountAsync()).Should().Be(0);
        // Act
        await service.SaveMappingsAsync(_systemId, "AC-11", new(before.NarrativeVersion.Value,
            draft.Responses.Select(r => new RequirementResponse(r.StatementId, "Policy", r.Response, [])).ToArray(), new Dictionary<string, string>(), draft.Token), "author", default);
        var saved = await service.ReadAsync(_systemId, "AC-11", default);
        // Assert
        saved.FirstPass!.ContextHash.Should().Be(draft.ContextHash);
        saved.FirstPass.Sources.Should().Contain(s => s.Kind == "SystemProfile");
        saved.Requirements[0].Reviewed.Should().BeFalse();
        (await _db.ControlImplementations.SingleAsync()).ApprovedRequirementCoverageJson.Should().BeNull();
    }

    [Fact]
    public async Task FirstPass_RejectsViewerForeignScopeAndTamperedProvenance()
    {
        // Arrange
        var (service, generator) = await FirstPassFixtureAsync();
        var before = await service.ReadAsync(_systemId, "AC-11", default);
        SetPermissions(true, false, false, false);
        // Act
        var viewer = () => service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion!.Value, before.BaselineRevision, "Policy"));
        // Assert
        await viewer.Should().ThrowAsync<UnauthorizedAccessException>();
        generator.Verify(g => g.GenerateRequirementFirstPassAsync(It.IsAny<RequirementFirstPassContext>(), It.IsAny<CancellationToken>()), Times.Never);
        // Arrange
        SetPermissions(true, true, true, true);
        // Act
        var forged = () => service.SaveMappingsAsync(_systemId, "AC-11", new(before.NarrativeVersion!.Value, [], new Dictionary<string, string>(), "forged-proof"), "author", default);
        // Assert
        await forged.Should().ThrowAsync<ArgumentException>();
        // Arrange
        var draft = await service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion!.Value, before.BaselineRevision, "Policy"));
        _tenant.SetupGet(t => t.PersonId).Returns(Guid.NewGuid());
        // Act
        var anotherAuthor = () => service.SaveMappingsAsync(_systemId, "AC-11",
            new(before.NarrativeVersion.Value, [], new Dictionary<string, string>(), draft.Token), "another-author", default);
        // Assert
        await anotherAuthor.Should().ThrowAsync<UnauthorizedAccessException>();
        // Arrange
        _tenant.SetupGet(t => t.EffectiveTenantId).Returns(Guid.NewGuid());
        // Act
        var foreign = () => service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion.Value, before.BaselineRevision, "Policy"));
        // Assert
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task FirstPass_RechecksSourcesAndAuthorPermissionAfterGeneration()
    {
        // Arrange
        var (service, generator) = await FirstPassFixtureAsync();
        var before = await service.ReadAsync(_systemId, "AC-11", default);
        generator.Setup(g => g.GenerateRequirementFirstPassAsync(It.IsAny<RequirementFirstPassContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequirementFirstPassContext context, CancellationToken _) => {
                SetPermissions(true, false, false, false);
                return new RequirementFirstPassSuggestion([new(context.Requirements[0].Id, "Source-backed draft",
                    [context.Sources[0].Id], "Declared context")], [], [], []);
            });
        // Act
        var prepare = () => service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion!.Value, before.BaselineRevision, "Policy"));
        // Assert
        await prepare.Should().ThrowAsync<UnauthorizedAccessException>();
        (await _db.ControlImplementations.SingleAsync()).RequirementCoverageJson.Should().BeNull();
    }

    [Fact]
    public async Task FirstPass_RetainsPolicyAndTechnicalAssistanceWithoutLosingHumanEdits()
    {
        // Arrange
        var (service, _) = await FirstPassFixtureAsync();
        var before = await service.ReadAsync(_systemId, "AC-11", default);
        var policy = await service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion!.Value, before.BaselineRevision, "Policy"));
        var technical = await service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion.Value, before.BaselineRevision, "Technical"));
        // Act
        await service.SaveMappingsAsync(_systemId, "AC-11", new(before.NarrativeVersion.Value,
            [new(policy.Responses[0].StatementId, "Policy", "Human-edited policy draft", []),
                new(technical.Responses[0].StatementId, "Technical", "Human-edited technical draft", [])],
            new Dictionary<string, string>(), FirstPassTokens: [policy.Token, technical.Token]), "author", default);
        var saved = await service.ReadAsync(_systemId, "AC-11", default);
        // Assert
        saved.FirstPasses!.Select(p => p.Kind).Should().BeEquivalentTo("Policy", "Technical");
        saved.Requirements[0].Responses.Select(r => r.Response).Should().BeEquivalentTo("Human-edited policy draft", "Human-edited technical draft");
        saved.Requirements[0].Reviewed.Should().BeFalse();
    }

    [Fact]
    public async Task FirstPass_SourceChangesBeforeSaveAreConflicts_NotSilentStaleApplication()
    {
        // Arrange
        var (service, _) = await FirstPassFixtureAsync();
        var before = await service.ReadAsync(_systemId, "AC-11", default);
        var draft = await service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion!.Value, before.BaselineRevision, "Policy"));
        var profile = await _db.SystemProfileSections.SingleAsync();
        profile.DraftContent = """{"missionStatement":"Changed mission source"}""";
        await _db.SaveChangesAsync();
        // Act
        var save = () => service.SaveMappingsAsync(_systemId, "AC-11", new(before.NarrativeVersion.Value, [], new Dictionary<string, string>(), draft.Token), "author", default);
        // Assert
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*sources changed*");
        (await _db.ControlImplementations.SingleAsync()).RequirementCoverageJson.Should().BeNull();
    }

    [Fact]
    public async Task FirstPass_ChangedSourceDuringModelCallDoesNotReturnAStaleSuggestion()
    {
        // Arrange
        var (service, generator) = await FirstPassFixtureAsync();
        var before = await service.ReadAsync(_systemId, "AC-11", default);
        generator.Setup(g => g.GenerateRequirementFirstPassAsync(It.IsAny<RequirementFirstPassContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (RequirementFirstPassContext context, CancellationToken ct) => {
                var profile = await _db.SystemProfileSections.SingleAsync(ct);
                profile.DraftContent = """{"missionStatement":"Source changed while model was running"}""";
                await _db.SaveChangesAsync(ct);
                return new RequirementFirstPassSuggestion([new(context.Requirements[0].Id, "Old source draft", [context.Sources[0].Id], "Old context")], [], [], []);
            });
        // Act
        var generate = () => service.GenerateFirstPassAsync(_systemId, "AC-11", new(before.NarrativeVersion!.Value, before.BaselineRevision, "Policy"));
        // Assert
        await generate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*sources changed during generation*");
    }
    [Fact]
    public async Task FirstPass_IncludesEnteredPrivacyAndRetainedPolicies_NotForeignProfileOrChangedLibraryValues()
    {
        // Arrange
        var (service, generator) = await FirstPassFixtureAsync();
        _db.SystemComponents.Add(new() { Id = "policy-firstpass", TenantId = _tenantId, Name = "Changed policy library name",
            ComponentType = ComponentType.Policy, Description = "Changed current library content" });
        var retained = new PolicySourceDto("policy-firstpass", "Retained policy name", "Recorded authority description", "Law",
            "Active", null, "retained-v1", "Retained source", null, false, []);
        _db.ComponentSystemAssignments.Add(new() { TenantId = _tenantId, RegisteredSystemId = _systemId,
            SystemComponentId = retained.Id, PolicySourceRevision = retained.Revision, PolicySourceSnapshotJson = JsonSerializer.Serialize(retained),
            PolicyRationale = "Recorded policy applicability rationale" });
        _db.PrivacyImpactAssessments.Add(new() { TenantId = _tenantId, RegisteredSystemId = _systemId,
            PurposeOfCollection = "Recorded collection purpose", IntendedUse = "Recorded processing use", RetentionPeriod = "Recorded retention" });
        var foreign = new RegisteredSystem { TenantId = Guid.NewGuid(), Name = "Foreign first-pass system" };
        _db.RegisteredSystems.Add(foreign);
        _db.SystemProfileSections.Add(new() { TenantId = foreign.TenantId, RegisteredSystemId = foreign.Id,
            SectionType = ProfileSectionType.MissionAndPurpose, DraftContent = """{"missionStatement":"foreign-do-not-use"}""" });
        await _db.SaveChangesAsync();
        RequirementFirstPassContext? captured = null;
        generator.Setup(g => g.GenerateRequirementFirstPassAsync(It.IsAny<RequirementFirstPassContext>(), It.IsAny<CancellationToken>()))
            .Callback<RequirementFirstPassContext, CancellationToken>((context, _) => captured = context)
            .ReturnsAsync((RequirementFirstPassContext context, CancellationToken _) => new(
                [new(context.Requirements[0].Id, "Editable sourced draft", [context.Sources[0].Id], "Recorded context")], [], [], []));
        var detail = await service.ReadAsync(_systemId, "AC-11", default);
        // Act
        await service.GenerateFirstPassAsync(_systemId, "AC-11", new(detail.NarrativeVersion!.Value, detail.BaselineRevision, "Policy"));
        // Assert
        captured!.Sources.Single(s => s.Kind == "PrivacyAssessment").Content.Should().Contain("Recorded processing use");
        captured.Sources.Single(s => s.Kind == "RetainedPolicy").Content.Should().Contain("Recorded authority description").And.NotContain("Changed current library content");
        captured.Sources.Should().NotContain(s => s.Content.Contains("foreign-do-not-use"));
        captured.Sources.Single(s => s.Kind == "RetainedPolicy").RecordedValues.Should().NotContain(retained.Name);
    }
}
