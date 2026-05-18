using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy.Csp;

/// <summary>
/// T192 [US9]: validates the capability-review flow:
/// <list type="number">
///   <item><c>PATCH /api/csp/inherited-components/{componentId}/capabilities/{capabilityId}/review</c>
///         transitions a <see cref="CspInheritedCapabilityStatus.NeedsReview"/>
///         row to <see cref="CspInheritedCapabilityStatus.Mapped"/>;</item>
///   <item>persists <c>MappedNistControlIds</c> and <c>ReviewerNote</c>;</item>
///   <item>stamps <c>ReviewedBy</c> + <c>ReviewedAt</c>;</item>
///   <item>emits an <see cref="AuditLogEntry"/> with
///         <c>Action = "CspInheritedCapability.Review"</c>;</item>
///   <item>returns <c>409</c> if the capability is already
///         <see cref="CspInheritedCapabilityStatus.Mapped"/>.</item>
/// </list>
/// </summary>
/// <remarks>
/// RED until T205 + T208 are implemented (review endpoint + service).
/// </remarks>
[Collection("Tenancy")]
public class CspInheritedCapabilityReviewTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private const string BaseUrl = "/api/csp/inherited-components";

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;
    private readonly Guid _componentId;
    private readonly Guid _needsReviewCapabilityId;
    private readonly Guid _alreadyMappedCapabilityId;

    public CspInheritedCapabilityReviewTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        // CSP-Admin caller — review is gated to CSP.Admin. The reviewer
        // identity stamped on ReviewedBy / AuditLogEntry.UserId is taken
        // from the authenticated HTTP principal by the production code, not
        // from TenantContext, so we only assert it is non-empty below.
        var ctx = factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = true;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;

        _componentId = Guid.NewGuid();
        _needsReviewCapabilityId = Guid.NewGuid();
        _alreadyMappedCapabilityId = Guid.NewGuid();

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Patch_Review_NeedsReview_Returns200_TransitionsToMapped_AndEmitsAudit()
    {
        // Arrange
        var url = $"{BaseUrl}/{_componentId}/capabilities/{_needsReviewCapabilityId}/review";
        var payload = JsonContent.Create(new
        {
            mappedNistControlIds = new[] { "AC-2", "AC-2(1)" },
            reviewerNote = "Operator confirmed mapping; AI was uncertain.",
        });

        // Act
        var resp = await _client.PatchAsync(url, payload);

        // Assert — status + envelope shape.
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var capability = body.GetProperty("data");
        capability.GetProperty("id").GetGuid().Should().Be(_needsReviewCapabilityId);
        capability.GetProperty("status").GetString().Should().Be("Mapped");
        capability.GetProperty("mappedBy").GetString().Should().Be("User");
        capability.GetProperty("reviewedBy").GetString().Should().NotBeNullOrEmpty();

        // Assert — DB-side persisted state (read straight from the row).
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var row = await db.CspInheritedCapabilities
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == _needsReviewCapabilityId);
        row.Status.Should().Be(CspInheritedCapabilityStatus.Mapped);
        row.MappedBy.Should().Be(MappedBy.User);
        row.ReviewedBy.Should().NotBeNullOrEmpty();
        row.ReviewedAt.Should().NotBeNull();
        row.ReviewerNote.Should().Be("Operator confirmed mapping; AI was uncertain.");
        row.MappedNistControlIds.Should().BeEquivalentTo(new[] { "AC-2", "AC-2(1)" });

        // Assert — audit row with the canonical Action and the prior + new
        // control list captured (FR-105).
        var auditRow = await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.Action == "CspInheritedCapability.Review")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();
        auditRow.Should().NotBeNull("review must emit an AuditLogEntry per FR-105");
        auditRow!.Details.Should().Contain(_needsReviewCapabilityId.ToString());
        auditRow.AffectedControls.Should().Contain("AC-2");
        auditRow.AffectedControls.Should().Contain("AC-2(1)");
    }

    [Fact]
    public async Task Patch_Review_AlreadyMapped_Returns409()
    {
        // Arrange
        var url = $"{BaseUrl}/{_componentId}/capabilities/{_alreadyMappedCapabilityId}/review";
        var payload = JsonContent.Create(new
        {
            mappedNistControlIds = new[] { "AC-3" },
            reviewerNote = "second pass",
        });

        // Act
        var resp = await _client.PatchAsync(url, payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "review is only valid on NeedsReview rows; re-mapping uses the parent component PATCH");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("error");
    }

    // ───────────────────────────── Helpers ───────────────────────────────

    private async Task SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var profileId = await db.Set<CspProfile>().IgnoreQueryFilters()
            .Select(p => p.Id)
            .FirstOrDefaultAsync();
        if (profileId == Guid.Empty)
        {
            profileId = Guid.NewGuid();
        }

        // Use EF Add+SaveChanges so SQLite Guid-TEXT casing matches what
        // the production code persists (raw-SQL interpolation lowercases
        // Guid.ToString() and SQLite's case-sensitive TEXT comparison
        // breaks JOIN-by-Guid). See Feature 048 T207 audit.
        if (!await db.CspInheritedComponents.AnyAsync(c => c.Id == _componentId))
        {
            db.CspInheritedComponents.Add(new CspInheritedComponent
            {
                Id = _componentId,
                CspProfileId = profileId,
                Name = "Component-T192",
                Description = "desc",
                ComponentType = CspComponentType.Service,
                SourceFormat = SourceFormat.Pdf,
                Status = CspInheritedComponentStatus.Published,
                ImportedAt = DateTimeOffset.UtcNow,
                ImportedBy = "seed",
            });
        }
        if (!await db.CspInheritedCapabilities.AnyAsync(c => c.Id == _needsReviewCapabilityId))
        {
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = _needsReviewCapabilityId,
                CspInheritedComponentId = _componentId,
                Name = "Needs-Review-Cap",
                Description = "desc",
                MappedNistControlIds = new List<string>(),
                MappingConfidence = 0.42,
                Status = CspInheritedCapabilityStatus.NeedsReview,
                MappedBy = MappedBy.AI,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "seed",
            });
        }
        if (!await db.CspInheritedCapabilities.AnyAsync(c => c.Id == _alreadyMappedCapabilityId))
        {
            db.CspInheritedCapabilities.Add(new CspInheritedCapability
            {
                Id = _alreadyMappedCapabilityId,
                CspInheritedComponentId = _componentId,
                Name = "Already-Mapped-Cap",
                Description = "desc",
                MappedNistControlIds = new List<string> { "AC-1" },
                MappingConfidence = 0.95,
                Status = CspInheritedCapabilityStatus.Mapped,
                MappedBy = MappedBy.AI,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "seed",
            });
        }
        await db.SaveChangesAsync();
    }
}
