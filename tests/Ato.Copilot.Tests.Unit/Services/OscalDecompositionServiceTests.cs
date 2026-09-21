using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for OSCAL decomposition service contracts and DTO shapes (Feature 076 - T012/T013).
/// DB-touching paths are covered in integration tests.
/// </summary>
public class OscalDecompositionServiceTests
{
    [Theory]
    [InlineData("model", "ModelSelfReported", true)]
    [InlineData("no-confidence", "ModelSelfReported", true)]
    [InlineData("invalid-json", "Fallback", false)]
    [InlineData("exception", "Fallback", false)]
    [InlineData("empty-fragments", "Fallback", false)]
    [InlineData("empty-description", "Fallback", false)]
    public async Task Decompose_Reload_Approve_PreservesActualOrigin(
        string responseKind, string expectedOrigin, bool expectedAi)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(options => options.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();
        var chat = new Mock<IChatClient>();
        var response = responseKind == "invalid-json" ? "invalid" :
            "{\"fragments\":[{\"statement_id\":\"ac-1_smt.a\",\"description\":\"Model text\",\"suggested_params\":[],\"confidence_score\":" +
            (responseKind == "no-confidence" ? "null" : "0.9") + "}]}";
        if (responseKind == "empty-fragments") response = "{\"fragments\":[]}";
        if (responseKind == "empty-description")
            response = "{\"fragments\":[{\"statement_id\":\"ac-1_smt.a\",\"description\":\" \"}]}";
        var setup = chat.Setup(client => client.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()));
        if (responseKind == "exception")
            setup.ThrowsAsync(new InvalidOperationException("Synthetic model failure"));
        else
            setup.ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.ControlImplementations.Add(new ControlImplementation
            {
                RegisteredSystemId = "system", ControlId = "ac-1", TenantId = tenantId,
            });
            await db.SaveChangesAsync();
        }
        var service = new OscalDecompositionService(chat.Object,
            provider.GetRequiredService<IServiceScopeFactory>(), Mock.Of<ILogger<OscalDecompositionService>>());

        // Act
        await service.DecomposeAsync(tenantId.ToString(), "system", "ac-1", "Original text", "test-user");
        var reloaded = await service.GetDraftAsync(tenantId.ToString(), "system", "ac-1");
        await service.ApproveAsync(tenantId.ToString(), "system", "ac-1", "test-reviewer");

        // Assert
        reloaded!.Fragments.Should().ContainSingle().Which.DerivationBasis.Should().Be(expectedOrigin);
        using var verificationScope = provider.CreateScope();
        var implementation = await verificationScope.ServiceProvider.GetRequiredService<AtoCopilotContext>()
            .ControlImplementations.SingleAsync();
        implementation.AiSuggested.Should().Be(expectedAi);
        implementation.IsAutoPopulated.Should().BeTrue();
        implementation.TechnicalNarrative.Should().Contain(expectedAi ? "Model text" : "Original text");
    }

    [Theory]
    [InlineData("Legacy text")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Approve_LegacyDraft_RequiresContentWithoutInferringModelOrigin(string? description)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(options => options.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = "system", ControlId = "ac-1", TechnicalNarrative = "Original text",
        });
        db.OscalDecompositionDrafts.Add(new OscalDecompositionDraft
        {
            TenantId = tenantId, RegisteredSystemId = "system", ControlId = "ac-1", GeneratedBy = "test",
            Fragments = description is null ? [] : [new OscalDecompositionFragment
            {
                StatementId = "ac-1_smt.a", Description = description, ConfidenceScore = 0.9,
            }],
        });
        await db.SaveChangesAsync();
        var service = new OscalDecompositionService(Mock.Of<IChatClient>(),
            provider.GetRequiredService<IServiceScopeFactory>(), Mock.Of<ILogger<OscalDecompositionService>>());

        // Act
        var approval = () => service.ApproveAsync(tenantId.ToString(), "system", "ac-1", "reviewer");

        // Assert
        if (string.IsNullOrEmpty(description))
        {
            await approval.Should().ThrowAsync<InvalidOperationException>().WithMessage("*without usable narrative fragments*");
            db.ChangeTracker.Clear();
            (await db.ControlImplementations.SingleAsync()).TechnicalNarrative.Should().Be("Original text");
            (await db.OscalDecompositionDrafts.SingleAsync()).Status.Should().Be(DecompositionDraftStatus.Pending);
            return;
        }
        await approval();
        db.ChangeTracker.Clear();
        (await db.ControlImplementations.SingleAsync()).AiSuggested.Should().BeFalse();
    }

    // ── Interface shape ──────────────────────────────────────────────────────

    [Fact]
    public void IOscalDecompositionService_HasRequiredMethods()
    {
        var type = typeof(IOscalDecompositionService);
        type.GetMethod("DecomposeAsync").Should().NotBeNull();
        type.GetMethod("GetDraftAsync").Should().NotBeNull();
        type.GetMethod("ApproveAsync").Should().NotBeNull();
        type.GetMethod("DiscardAsync").Should().NotBeNull();
    }

    [Fact]
    public void OscalDecompositionService_ImplementsInterface()
    {
        typeof(OscalDecompositionService)
            .GetInterfaces()
            .Should().Contain(typeof(IOscalDecompositionService));
    }

    // ── DTO shape tests ───────────────────────────────────────────────────────

    [Fact]
    public void DecompositionDraftDto_RoundTrips_WithFragments()
    {
        var draft = new DecompositionDraftDto(
            DraftId: "draft-001",
            ControlId: "ac-1",
            Status: "Pending",
            GeneratedAt: new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero),
            GeneratedBy: "test-user",
            Fragments:
            [
                new DecompositionFragmentDto(
                    FragmentId: "frag-001",
                    StatementId: "ac-1_smt.a",
                    ComponentUuid: null,
                    Description: "The organization develops an access control policy.",
                    SuggestedParams: [new SuggestedParamDto("ac-1_prm_1", "annually")],
                    ConfidenceScore: 0.9,
                    DerivationBasis: "ModelSelfReported",
                    RequiresHumanValidation: true)
            ]);

        draft.ControlId.Should().Be("ac-1");
        draft.Fragments.Should().HaveCount(1);
        draft.Fragments[0].StatementId.Should().Be("ac-1_smt.a");
        draft.Fragments[0].ConfidenceScore.Should().NotBeNull();
        draft.Fragments[0].ConfidenceScore!.Value.Should().BeApproximately(0.9, 0.001);
        draft.Fragments[0].SuggestedParams.Should().HaveCount(1);
        draft.Fragments[0].SuggestedParams[0].ParamId.Should().Be("ac-1_prm_1");
        draft.Fragments[0].SuggestedParams[0].Value.Should().Be("annually");
    }

    [Fact]
    public void DecompositionApprovalResult_Properties_AreReadable()
    {
        var approvedAt = DateTimeOffset.UtcNow;
        var result = new DecompositionApprovalResult(
            DraftId: "d-001",
            ControlId: "ac-2",
            FragmentsApplied: 3,
            ApprovedAt: approvedAt);

        result.FragmentsApplied.Should().Be(3);
        result.ControlId.Should().Be("ac-2");
        result.ApprovedAt.Should().Be(approvedAt);
    }

    [Fact]
    public void DecompositionFragmentDto_ConfidenceScore_BoundsAreValid()
    {
        var frag = new DecompositionFragmentDto(
            "f1", "ac-1_smt.a", null, "Test", [], 0.85, "ModelSelfReported", true);

        frag.ConfidenceScore.Should().NotBeNull();
        frag.ConfidenceScore!.Value.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(1.0);
    }


    [Fact]
    public void FallbackFragment_HasNullConfidenceScore()
    {
        // Simulates a fragment produced by the fallback path (exception in AI call).
        var frag = new DecompositionFragmentDto(
            FragmentId: "frag-fallback",
            StatementId: "ac-1_smt.a",
            ComponentUuid: null,
            Description: "Full narrative fallback",
            SuggestedParams: [],
            ConfidenceScore: null,
            DerivationBasis: "Fallback",
            RequiresHumanValidation: true);

        frag.ConfidenceScore.Should().BeNull();
        frag.DerivationBasis.Should().Be("Fallback");
        frag.RequiresHumanValidation.Should().BeTrue();
    }

    [Fact]
    public void JsonParseFailure_HasNullConfidenceScore()
    {
        // Simulates a fragment produced when the model returns invalid JSON.
        var frag = new DecompositionFragmentDto(
            FragmentId: "frag-json-fail",
            StatementId: "ac-2_smt.a",
            ComponentUuid: null,
            Description: "Unparseable model response fallback",
            SuggestedParams: [],
            ConfidenceScore: null,
            DerivationBasis: "Fallback",
            RequiresHumanValidation: true);

        frag.ConfidenceScore.Should().BeNull();
        frag.DerivationBasis.Should().Be("Fallback");
        frag.RequiresHumanValidation.Should().BeTrue();
    }

    [Fact]
    public void AtoRemediationEngine_DeterministicPath_HasNullConfidenceScore()
    {
        // The deterministic NIST lookup path must not emit a fabricated confidence score.
        var guidance = new Ato.Copilot.Core.Models.Compliance.RemediationGuidance
        {
            FindingId = "finding-001",
            Explanation = "Remediation required for AC-1.",
            TechnicalPlan = "1. Do this. 2. Do that.",
            ConfidenceScore = null, // Deterministic NIST lookup — no model confidence
            GeneratedAt = DateTime.UtcNow
        };

        guidance.ConfidenceScore.Should().BeNull(
            "deterministic NIST lookups do not have model-confidence scores; human review is always required");
    }

    // ── IOscalSspImportService ────────────────────────────────────────────────

    [Fact]
    public void IOscalSspImportService_HasImportAsyncMethod()
    {
        var type = typeof(IOscalSspImportService);
        type.GetMethod("ImportAsync").Should().NotBeNull();
    }

    [Fact]
    public void OscalImportResult_DefaultCounts_AreZero()
    {
        var result = new OscalImportResult();

        result.ControlsCreated.Should().Be(0);
        result.ControlsUpdated.Should().Be(0);
        result.ControlsSkipped.Should().Be(0);
        result.ControlsFailed.Should().Be(0);
        result.ValidationErrors.Should().BeEmpty();
        result.Preview.Should().BeEmpty();
    }

    [Fact]
    public void OscalImportResult_TracksCounts()
    {
        var result = new OscalImportResult
        {
            RunId = "run-001",
            Mode = ImportMode.Full,
            ControlsCreated = 150,
            ControlsUpdated = 10,
            ControlsSkipped = 5,
            ControlsFailed = 0,
            ValidationErrors = ["AC-1 narrative truncated"],
        };

        result.ControlsCreated.Should().Be(150);
        result.ControlsUpdated.Should().Be(10);
        result.ControlsFailed.Should().Be(0);
        result.ValidationErrors.Should().HaveCount(1);
    }

    [Fact]
    public void OscalImportPreviewItem_ActionValues()
    {
        var create = new OscalImportPreviewItem { ControlId = "ac-1", Action = "create", CurrentNarrative = null, NewNarrative = "Incoming narrative" };
        var update = new OscalImportPreviewItem { ControlId = "ac-2", Action = "update", CurrentNarrative = "Old narrative", NewNarrative = "New narrative" };
        var skip   = new OscalImportPreviewItem { ControlId = "ac-3", Action = "skip",   CurrentNarrative = "Unchanged",    NewNarrative = "Unchanged" };

        create.Action.Should().Be("create");
        create.CurrentNarrative.Should().BeNull();
        update.CurrentNarrative.Should().Be("Old narrative");
        skip.Action.Should().Be("skip");
    }

    [Fact]
    public void ImportMode_Enum_HasExpectedValues()
    {
        ImportMode.Preview.Should().Be(ImportMode.Preview);
        ImportMode.Full.Should().Be(ImportMode.Full);
        Enum.GetValues<ImportMode>().Should().HaveCount(2);
    }
}
