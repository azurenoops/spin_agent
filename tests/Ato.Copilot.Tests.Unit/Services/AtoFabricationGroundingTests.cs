using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Regression tests for Issue #641 / #685 — ATO document generator fabrication fix.
///
/// Issue #641 ("ATO documents fabricating facts") is the original trust-critical defect:
/// the generator was emitting ungrounded compliance claims (e.g. "MFA is configured",
/// "TLS 1.2+ enforced") that were not backed by any source record, and silently persisting
/// those documents as authoritative. An Authorizing Official seeing fabricated facts in an
/// SSP is an unacceptable compliance failure.
///
/// Issue #685 is the implementation tracking issue for the remediation. The fixes merged
/// in PR #792 (branch fix/685-ato-fabrication-grounding) cover all guardrails specified
/// by Banner's #641 remediation artifact:
///   1. Explicit systemId binding — no "first active system" guessing.
///   2. [SOURCE MISSING] emission — every field without a real DB source emits a marker.
///   3. Fail-loud grounding guard before persist — throws UNGROUNDED_CONTENT if any
///      [SOURCE MISSING] or [scaffold reference — unverified] marker reaches the persist path.
///   4. Pre-export grounding guard in SspService — adds GROUNDING_VIOLATION warnings;
///      callers must treat as hard block (HTTP 422).
///   5. Reviewer gate — AI-suggested narratives without ApprovedVersionId are marked
///      "[reviewer gate required]" so they cannot be silently exported as approved.
///
/// These tests fail without the guard (pre-fix behavior) and pass with it.
/// </summary>
public class AtoFabricationGroundingTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;
    private readonly SspService _sspService;
    private readonly DocumentGenerationService _docGenService;
    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly DbContextOptions<AtoCopilotContext> _dbOptions;

    public AtoFabricationGroundingTests()
    {
        var dbName = $"AtoFabricationGrounding_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();

        _dbOptions = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        _dbFactory = new TestDbContextFactory(_dbOptions);

        _sspService = new SspService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<SspService>>());

        _docGenService = new DocumentGenerationService(
            _dbFactory,
            Mock.Of<INistControlsService>(),
            Mock.Of<ILogger<DocumentGenerationService>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ─── #685 Fix 1: GenerateCustomerNarrativeTemplate no longer fabricates ──

    [Theory]
    [InlineData("AC")]
    [InlineData("IA")]
    [InlineData("SC")]
    [InlineData("AU")]
    [InlineData("CM")]
    [InlineData("SI")]
    public void GenerateCustomerNarrativeTemplate_NoFabricatedClaims_EmitsSourceMissingMarker(string family)
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test System",
            HostingEnvironment = "Azure Government"
        };
        var controlId = $"{family}-1";

        // Act
        var result = SspService.GenerateCustomerNarrativeTemplate(family, controlId, system);

        // Assert: no hardcoded implementation assertions
        result.Should().Contain("[SOURCE MISSING",
            $"template for {family} must emit [SOURCE MISSING] instead of fabricated claims (fix #685)");

        // These specific fabricated claims must NOT appear in ANY family template
        result.Should().NotContain("multi-factor authentication (MFA)",
            $"template for {family} must not assert MFA (fix #685 — fabricated claim)");
        result.Should().NotContain("CAC/PIV",
            $"template for {family} must not assert CAC/PIV (fix #685 — fabricated claim)");
        result.Should().NotContain("TLS 1.2+",
            $"template for {family} must not assert TLS 1.2+ (fix #685 — fabricated claim)");
    }

    [Fact]
    public void GenerateCustomerNarrativeTemplate_MissingHostingEnvironment_EmitsSourceMissingForField()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "No Env System",
            HostingEnvironment = "" // absent
        };

        var result = SspService.GenerateCustomerNarrativeTemplate("AC", "AC-1", system);

        result.Should().Contain("[SOURCE MISSING: HostingEnvironment]",
            "when HostingEnvironment is absent the template must emit a [SOURCE MISSING] marker, not a fabricated default");
    }

    [Fact]
    public void GenerateCustomerNarrativeTemplate_WithHostingEnvironment_UsesRealValue()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Real System",
            HostingEnvironment = "Azure Commercial"
        };

        var result = SspService.GenerateCustomerNarrativeTemplate("AC", "AC-1", system);

        result.Should().Contain("Azure Commercial",
            "template must use the system's actual hosting environment, not a hardcoded default");
        result.Should().NotContain("[SOURCE MISSING: HostingEnvironment]");
    }

    // ─── #685 Fix 2: GenerateDocumentAsync requires explicit systemId ────────

    [Fact]
    public async Task GenerateDocumentAsync_NullSystemId_ThrowsSystemIdRequired()
    {
        var act = () => _docGenService.GenerateDocumentAsync("SSP", systemId: null!);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*SYSTEM_ID_REQUIRED*");
    }

    [Fact]
    public async Task GenerateDocumentAsync_EmptySystemId_ThrowsSystemIdRequired()
    {
        var act = () => _docGenService.GenerateDocumentAsync("SSP", systemId: "");
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*SYSTEM_ID_REQUIRED*");
    }

    [Fact]
    public async Task GenerateDocumentAsync_UnknownSystemId_ThrowsSystemNotFound()
    {
        var act = () => _docGenService.GenerateDocumentAsync("SSP", systemId: "nonexistent-id-xyz");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SYSTEM_NOT_FOUND*");
    }

    [Fact]
    public async Task GenerateDocumentAsync_NoFirstActiveSystemFallback_DoesNotGuess()
    {
        // Seed an active system but call with a DIFFERENT (non-existent) systemId.
        // If the old "first active system" fallback were still in place, it would return
        // the seeded system instead of throwing. Fix #685 ensures it throws.
        _db.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "active-system-1",
            Name = "Some Active System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true
        });
        await _db.SaveChangesAsync();

        // Asking for a different, non-existent system must throw — not fall back to "active-system-1"
        var act = () => _docGenService.GenerateDocumentAsync("SSP", systemId: "completely-different-id");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SYSTEM_NOT_FOUND*",
                "the 'first active system' fallback must not silently bind to a different system (fix #685)");
    }

    // ─── #685 Fix 2: No hardcoded "Azure Government" in generated content ────

    [Fact]
    public async Task GenerateDocumentAsync_HostingEnvironment_SourcedFromSystem_NotHardcoded()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "On-Prem System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "On-Premises Data Center",
            CreatedBy = "test",
            IsActive = true
        };
        _db.RegisteredSystems.Add(system);
        await _db.SaveChangesAsync();

        // SSP with no assessment — only system facts matter here
        var doc = await _docGenService.GenerateDocumentAsync("SSP", system.Id);

        doc.Content.Should().Contain("On-Premises Data Center",
            "hosting environment must be sourced from the system record");
        // The hardcoded default "Azure Government" must NOT appear when the system is on-prem
        // (unless the system's actual record says Azure Government)
        doc.Content.Should().NotContain("Azure Government",
            "must not hardcode 'Azure Government' when system HostingEnvironment is different (fix #685)");
    }

    [Fact]
    public async Task GenerateDocumentAsync_MissingHostingEnvironment_GroundingGuardBlocksPersist()
    {
        // When HostingEnvironment is absent the document content contains [SOURCE MISSING] markers.
        // The grounding guard must catch those and throw UNGROUNDED_CONTENT before persist —
        // never silently produce and store a document with hardcoded "Azure Government" (fix #685).
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Missing Env System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "", // absent → [SOURCE MISSING] → UNGROUNDED_CONTENT throw
            CreatedBy = "test",
            IsActive = true
        };
        _db.RegisteredSystems.Add(system);
        await _db.SaveChangesAsync();

        // The grounding guard must block the persist and throw — not return a document
        // with fabricated "Azure Government" content (fix #685).
        var act = () => _docGenService.GenerateDocumentAsync("SSP", system.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNGROUNDED_CONTENT*",
                "when HostingEnvironment is absent the grounding guard must block persist, not silently emit 'Azure Government' (fix #685)");
    }

    // ─── #685 Fix 3: Grounding guard before persist blocks unresolved markers ─

    [Fact]
    public async Task GenerateDocumentAsync_WithSourceMissingInNarrative_GroundingGuardBlocksPersist()
    {
        // Seed a system with a ControlImplementation that has [SOURCE MISSING] in its narrative.
        // This simulates a template-scaffold narrative that was never resolved.
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Scaffold System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test",
            IsActive = true
        };
        _db.RegisteredSystems.Add(system);
        await _db.SaveChangesAsync();

        // The grounding guard in GenerateDocumentAsync should catch [SOURCE MISSING] in the
        // built document content before persisting.
        // Since the HostingEnvironment is set, SSP content won't have SOURCE MISSING from that path.
        // We test the boundary marker directly — make HostingEnvironment empty so the guard trips.
        var emptyEnvSystem = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Empty Env",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "", // triggers [SOURCE MISSING] in SSP content
            CreatedBy = "test",
            IsActive = true
        };
        _db.RegisteredSystems.Add(emptyEnvSystem);
        await _db.SaveChangesAsync();

        var act = () => _docGenService.GenerateDocumentAsync("SSP", emptyEnvSystem.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UNGROUNDED_CONTENT*",
                "persist must be blocked when the document contains [SOURCE MISSING] markers (fix #685)");
    }

    // ─── #685 Fix: SspService grounding guard flags unresolved scaffolds ─────

    [Fact]
    public async Task GenerateSspAsync_NarrativeWithSourceMissing_GroundingViolationAddedToWarnings()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Grounding Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government"
        };
        _db.RegisteredSystems.Add(system);

        // Add a control implementation with AiSuggested=true and no ApprovedVersionId
        var ci = new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            Narrative = "Some narrative with [SOURCE MISSING: describe the actual mechanism]",
            AiSuggested = true,
            ApprovedVersionId = null,
            AuthoredBy = "test"
        };
        _db.ControlImplementations.Add(ci);

        var baseline = new ControlBaseline
        {
            RegisteredSystemId = system.Id,
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-1" }
        };
        _db.ControlBaselines.Add(baseline);
        await _db.SaveChangesAsync();

        var doc = await _sspService.GenerateSspAsync(system.Id);

        doc.Warnings.Should().Contain(w => w.Contains("GROUNDING_VIOLATION"),
            "the grounding guard must add GROUNDING_VIOLATION warnings for narratives with [SOURCE MISSING] markers (fix #685)");
    }

    [Fact]
    public async Task GenerateSspAsync_ApprovedNarrativeWithoutSourceMissing_NoGroundingViolation()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Clean System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government"
        };
        _db.RegisteredSystems.Add(system);

        var ci = new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            Narrative = "The system implements AC-1 using Azure Active Directory with conditional access policies configured per the organization's security baseline.",
            AiSuggested = false,
            ApprovedVersionId = "some-approved-version-id",
            AuthoredBy = "reviewer@test.com"
        };
        _db.ControlImplementations.Add(ci);

        var baseline = new ControlBaseline
        {
            RegisteredSystemId = system.Id,
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-1" }
        };
        _db.ControlBaselines.Add(baseline);
        await _db.SaveChangesAsync();

        var doc = await _sspService.GenerateSspAsync(system.Id);

        doc.Warnings.Should().NotContain(w => w.Contains("GROUNDING_VIOLATION"),
            "a sourced, approved narrative should not trigger grounding violations (fix #685)");
    }

    // ─── #685 Fix: Reviewer gate — no silent Approved→Draft bypass ──────────

    [Fact]
    public async Task GenerateSspAsync_AiSuggestedNarrativeWithNoApproval_MarkedInDocument()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "AI Draft System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government"
        };
        _db.RegisteredSystems.Add(system);

        var ci = new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-2",
            Narrative = "This is an AI-suggested narrative about access control.",
            AiSuggested = true,
            IsAutoPopulated = false,
            ApprovedVersionId = null, // not approved
            AuthoredBy = "ai-system"
        };
        _db.ControlImplementations.Add(ci);

        var baseline = new ControlBaseline
        {
            RegisteredSystemId = system.Id,
            BaselineLevel = "Moderate",
            ControlIds = new List<string> { "AC-2" }
        };
        _db.ControlBaselines.Add(baseline);
        await _db.SaveChangesAsync();

        var doc = await _sspService.GenerateSspAsync(system.Id);

        // The document should contain the reviewer-gate marker, not silently render as approved
        doc.Content.Should().Contain("reviewer gate required",
            "AI-suggested narratives without approval must be marked as requiring reviewer gate (fix #685)");
    }

    // ─── Helper ─────────────────────────────────────────────────────────────

    private class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
        public Task<AtoCopilotContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AtoCopilotContext(_options));
    }
}
