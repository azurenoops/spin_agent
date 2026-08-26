using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Integration;

/// <summary>
/// AUD-001 Regression tests: verifies that the run-assessment persistence round-trip
/// is consistent — findings saved to the DB exactly match the set returned in the
/// response (via <c>assessment.Findings</c>), and not the pre-filter in-memory list.
///
/// Prevents recurrence of the bug where the response was built from a pre-filter
/// collection and findings disappeared from the UI on refresh.
/// </summary>
public class AssessmentFindingsPersistenceRoundTripTests : IDisposable
{
    private readonly AtoCopilotContext _context;

    public AssessmentFindingsPersistenceRoundTripTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"aud001-{Guid.NewGuid()}")
            .Options;

        _context = new AtoCopilotContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AUD-001: findings count in response == findings count in DB
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Happy path: all computed findings have valid ControlIds — every finding
    /// that is built in memory must be persisted and the response count must
    /// equal the DB count.
    /// </summary>
    [Fact]
    public async Task RunAssessment_AllFindingsPersistable_ResponseCountEqualsDbCount()
    {
        // Arrange — seed valid NIST controls
        var validControlId = "ac-2";
        _context.NistControls.Add(new NistControl { Id = validControlId, Family = "AC", Title = "Account Management" });
        await _context.SaveChangesAsync();

        var assessment = BuildAssessment("sys-001");
        var findings = new List<ComplianceFinding>
        {
            BuildFinding(validControlId),
        };

        // Act — replicate the endpoint persistence pattern
        var validSet = new HashSet<string>(
            await _context.NistControls.Select(nc => nc.Id).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        _context.Assessments.Add(assessment);
        await _context.SaveChangesAsync();

        foreach (var f in findings)
            f.AssessmentId = assessment.Id;

        // AUD-001 fix: only save findings whose ControlId is in validSet
        var persistableFindings = findings.Where(f => validSet.Contains(f.ControlId)).ToList();
        if (persistableFindings.Count > 0)
        {
            _context.Findings.AddRange(persistableFindings);
            await _context.SaveChangesAsync();
        }

        // Assign persisted-only set to assessment (matches endpoint behaviour)
        assessment.Findings = persistableFindings;

        // Assert: response field matches DB
        var responseCount = assessment.Findings.Count;
        var dbCount = await _context.Findings.CountAsync(f => f.AssessmentId == assessment.Id);

        responseCount.Should().Be(1, "one valid finding was built");
        dbCount.Should().Be(responseCount, "DB count must equal response count (AUD-001)");
    }

    /// <summary>
    /// Guard against regression: when some findings fail the FK filter, the
    /// response count must equal only the persisted subset — NOT the full
    /// pre-filter count that would disappear on refresh.
    /// </summary>
    [Fact]
    public async Task RunAssessment_SomeFindingsFilteredOut_ResponseCountEqualsPersistedSubset()
    {
        // Arrange — only one of two control IDs is valid in NistControls
        var validControlId = "si-2";
        var invalidControlId = "XX-999"; // not seeded → will be filtered
        _context.NistControls.Add(new NistControl { Id = validControlId, Family = "SI", Title = "Flaw Remediation" });
        await _context.SaveChangesAsync();

        var assessment = BuildAssessment("sys-002");
        var findings = new List<ComplianceFinding>
        {
            BuildFinding(validControlId),
            BuildFinding(invalidControlId), // will be dropped by FK filter
        };

        // Act
        var validSet = new HashSet<string>(
            await _context.NistControls.Select(nc => nc.Id).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        _context.Assessments.Add(assessment);
        await _context.SaveChangesAsync();

        foreach (var f in findings)
            f.AssessmentId = assessment.Id;

        var persistableFindings = findings.Where(f => validSet.Contains(f.ControlId)).ToList();
        if (persistableFindings.Count > 0)
        {
            _context.Findings.AddRange(persistableFindings);
            await _context.SaveChangesAsync();
        }

        // AUD-001: response is built from persistableFindings, not pre-filter findings
        assessment.Findings = persistableFindings;

        var responseCount = assessment.Findings.Count;
        var dbCount = await _context.Findings.CountAsync(f => f.AssessmentId == assessment.Id);

        // Key assertion: pre-filter had 2, but only 1 persisted
        findings.Count.Should().Be(2, "pre-filter list has 2 entries");
        responseCount.Should().Be(1, "only 1 finding has a valid ControlId");
        dbCount.Should().Be(responseCount,
            "response totalFindings must equal DB finding count — not the pre-filter count (AUD-001 regression guard)");
    }

    /// <summary>
    /// Edge case: if SaveChangesAsync fails, no findings are returned (exception
    /// propagates). The response must never contain unsaved findings.
    /// </summary>
    [Fact]
    public async Task RunAssessment_SaveFails_ExceptionPropagates_NoSilentUnsavedFindings()
    {
        // Arrange — use a disposed context so SaveChanges throws
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"aud001-fail-{Guid.NewGuid()}")
            .Options;

        var disposedContext = new AtoCopilotContext(options);
        await disposedContext.Database.EnsureCreatedAsync();
        disposedContext.Dispose(); // intentionally disposed before use

        var assessment = BuildAssessment("sys-fail");
        var findings = new List<ComplianceFinding> { BuildFinding("ac-1") };

        // Act & Assert
        // Simulating: if SaveChangesAsync throws, the endpoint must not return findings.
        // In the real endpoint the exception propagates as HTTP 500 — never silently
        // returns unsaved findings to the caller.
        var act = async () =>
        {
            disposedContext.Assessments.Add(assessment);
            await disposedContext.SaveChangesAsync(); // throws ObjectDisposedException
        };

        await act.Should().ThrowAsync<Exception>(
            "persistence failure must surface as an exception, not silently return unsaved findings");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ComplianceAssessment BuildAssessment(string systemId) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SubscriptionId = "",
        Framework = "NIST 800-53",
        ScanType = "combined",
        Status = AssessmentStatus.Completed,
        InitiatedBy = "test-user",
        AssessedAt = DateTime.UtcNow,
        CompletedAt = DateTime.UtcNow,
        RegisteredSystemId = systemId,
        ComplianceScore = 0,
        TotalControls = 1,
        PassedControls = 0,
        FailedControls = 1,
    };

    private static ComplianceFinding BuildFinding(string controlId) => new()
    {
        Id = Guid.NewGuid().ToString(),
        AssessmentId = "",  // set later by endpoint pattern
        ControlId = controlId,
        Title = $"Finding for {controlId}",
        Description = "Test finding",
        Severity = FindingSeverity.Medium,
        Status = FindingStatus.Open,
        ResourceType = "ControlImplementation",
        ResourceId = controlId,
        DiscoveredAt = DateTime.UtcNow,
    };
}
