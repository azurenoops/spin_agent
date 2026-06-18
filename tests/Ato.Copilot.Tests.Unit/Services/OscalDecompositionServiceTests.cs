using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for OSCAL decomposition service contracts and DTO shapes (Feature 076 - T012/T013).
/// DB-touching paths are covered in integration tests.
/// </summary>
public class OscalDecompositionServiceTests
{
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
                    ConfidenceScore: 0.9)
            ]);

        draft.ControlId.Should().Be("ac-1");
        draft.Fragments.Should().HaveCount(1);
        draft.Fragments[0].StatementId.Should().Be("ac-1_smt.a");
        draft.Fragments[0].ConfidenceScore.Should().BeApproximately(0.9, 0.001);
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
            "f1", "ac-1_smt.a", null, "Test", [], 0.85);

        frag.ConfidenceScore.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(1.0);
    }

    // ── IOscalSspImportService ────────────────────────────────────────────────

    [Fact]
    public void IOscalSspImportService_HasRequiredMethods()
    {
        var type = typeof(IOscalSspImportService);
        type.GetMethod("PreviewAsync").Should().NotBeNull();
        type.GetMethod("ImportAsync").Should().NotBeNull();
    }

    [Fact]
    public void OscalImportPreview_SchemaInvalid_CarriesErrors()
    {
        var preview = new OscalImportPreview(
            SchemaValid: false,
            ValidationErrors: ["Missing required field: system-id"],
            ValidationWarnings: [],
            DetectedOscalVersion: "1.1.2",
            Counts: new OscalImportEntityCounts(0, 0, 0, 0, 0),
            ControlSummaries: []);

        preview.SchemaValid.Should().BeFalse();
        preview.ValidationErrors.Should().HaveCount(1);
        preview.ValidationErrors[0].Should().Contain("system-id");
    }

    [Fact]
    public void OscalImportRunResult_TracksCounts()
    {
        var result = new OscalImportRunResult(
            ImportRunId: "run-001",
            SystemId: "sys-001",
            ImportedAt: DateTimeOffset.UtcNow,
            ImportedBy: "user-001",
            SchemaValid: true,
            ControlsCreated: 150,
            ControlsUpdated: 10,
            ControlsSkipped: 5,
            ControlsFailed: 0,
            Warnings: ["AC-1 narrative truncated"],
            Errors: []);

        result.ControlsCreated.Should().Be(150);
        result.ControlsUpdated.Should().Be(10);
        result.ControlsFailed.Should().Be(0);
        result.SchemaValid.Should().BeTrue();
        result.Warnings.Should().HaveCount(1);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void OscalControlImportSummary_ActionValues()
    {
        var create = new OscalControlImportSummary("ac-1", "create", null, "Incoming narrative");
        var update = new OscalControlImportSummary("ac-2", "update", "Old narrative", "New narrative");
        var skip = new OscalControlImportSummary("ac-3", "skip", "Unchanged", "Unchanged");

        create.Action.Should().Be("create");
        create.ExistingNarrative.Should().BeNull();
        update.ExistingNarrative.Should().Be("Old narrative");
        skip.Action.Should().Be("skip");
    }

    [Fact]
    public void OscalImportEntityCounts_AllPropertiesAccessible()
    {
        var counts = new OscalImportEntityCounts(
            ControlsToCreate: 50,
            ControlsToUpdate: 10,
            ControlsToSkip: 5,
            ComponentsToCreate: 3,
            InventoryItemsToCreate: 12);

        counts.ControlsToCreate.Should().Be(50);
        counts.ComponentsToCreate.Should().Be(3);
        counts.InventoryItemsToCreate.Should().Be(12);
    }
}
