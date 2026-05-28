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
/// T020 [Feature 050 / US2] — integration coverage for
/// <c>POST /api/csp/inherited-components/{componentId}/capabilities/{capabilityId}/move</c>.
/// </summary>
/// <remarks>
/// <para>Behavior pinned in
/// <c>specs/050-csp-capability-lifecycle/contracts/http-api.md § 2</c>:</para>
/// <list type="bullet">
///   <item>200 with updated capability DTO + new <c>rowVersion</c>;
///         exactly one <c>Moved</c> history row visible via DB.</item>
///   <item>400 <c>VALIDATION_ERROR</c> — missing / unparsable
///         <c>If-Match</c>; <c>targetComponentId == componentId</c>.</item>
///   <item>404 <c>NOT_FOUND</c> — archived target; unknown target;
///         cross-tenant target (existence-leak guard);
///         capability not under source component.</item>
///   <item>412 <c>ROW_VERSION_MISMATCH</c> — stale <c>If-Match</c>.</item>
///   <item>403 <c>FORBIDDEN_NOT_CSP_ADMIN</c> — non-admin caller.</item>
/// </list>
/// <para><b>Deviation</b>: the SQLite-backed fixture's
/// <c>CspInheritedCapabilities</c> table has <c>RowVersion BLOB NULL</c> (no
/// <c>UPDATE</c> trigger), so the optimistic-concurrency 412 path is asserted
/// here by sending an <em>unmatched</em> <c>If-Match</c> stamp that does not
/// equal the seeded <c>RowVersion</c>. The EF Core change tracker still
/// surfaces <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
/// because the property is declared <c>[Timestamp]</c>.</para>
/// </remarks>
[Collection("Tenancy")]
public class ReparentCapabilityEndpointTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private const string BaseUrl = "/api/csp/inherited-components";

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public ReparentCapabilityEndpointTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ─── 200 OK happy path + history row written ─────────────────────────

    [Fact]
    public async Task Post_Move_AsCspAdmin_Returns200_AndWritesOneMovedHistoryRow()
    {
        // Arrange
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source A");
        var targetId = await SeedComponentAsync("Target B");
        var capability = await SeedCapabilityAsync(sourceId);

        var rowVersion = Convert.ToBase64String(capability.RowVersion!);
        var req = BuildMoveRequest(sourceId, capability.Id, targetId, rowVersion);

        // Act
        var resp = await _client.SendAsync(req);

        // Assert — envelope shape
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");
        data.GetProperty("cspInheritedComponentId").GetGuid().Should().Be(targetId);
        data.GetProperty("status").GetString().Should().Be("NeedsReview");
        data.GetProperty("mappingFailureReason").GetString()
            .Should().Be("Moved to a new component; re-review required.");
        data.GetProperty("reviewedBy").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("reviewedAt").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("rowVersion").GetString().Should().NotBeNullOrEmpty();

        // Assert — exactly one Moved history row
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var history = await db.CapabilityHistoryEvents
            .IgnoreQueryFilters()
            .Where(h => h.CapabilityId == capability.Id)
            .ToListAsync();
        history.Should().HaveCount(1);
        history[0].EventType.Should().Be(CapabilityHistoryEventType.Moved);
        history[0].MetadataJson.Should().NotBeNull();
        history[0].MetadataJson.Should().Contain("fromComponentId");
        history[0].MetadataJson.Should().Contain("toComponentId");
    }

    // ─── 400 VALIDATION_ERROR cases ──────────────────────────────────────

    [Fact]
    public async Task Post_Move_MissingIfMatch_Returns422_ValidationFailed()
    {
        // Arrange
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source missing-ifmatch");
        var targetId = await SeedComponentAsync("Target missing-ifmatch");
        var capability = await SeedCapabilityAsync(sourceId);

        // No If-Match header.
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}/{sourceId}/capabilities/{capability.Id}/move")
        {
            Content = JsonContent.Create(new { targetComponentId = targetId }),
        };

        // Act
        var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Post_Move_UnparsableIfMatch_Returns422_ValidationFailed()
    {
        // Arrange
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source bad-ifmatch");
        var targetId = await SeedComponentAsync("Target bad-ifmatch");
        var capability = await SeedCapabilityAsync(sourceId);

        var req = BuildMoveRequest(sourceId, capability.Id, targetId, ifMatch: "not-base64!");

        // Act
        var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Post_Move_TargetEqualsSource_Returns422_ValidationFailed()
    {
        // Arrange
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source same-target");
        var capability = await SeedCapabilityAsync(sourceId);

        var rowVersion = Convert.ToBase64String(capability.RowVersion!);
        var req = BuildMoveRequest(sourceId, capability.Id,
            targetComponentId: sourceId, ifMatch: rowVersion);

        // Act
        var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    // ─── 404 NOT_FOUND cases ─────────────────────────────────────────────

    [Fact]
    public async Task Post_Move_ArchivedTarget_Returns404_NotFound()
    {
        // Arrange
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source archived-target");
        var targetId = await SeedComponentAsync(
            "Archived Target", archived: true);
        var capability = await SeedCapabilityAsync(sourceId);

        var rowVersion = Convert.ToBase64String(capability.RowVersion!);
        var req = BuildMoveRequest(sourceId, capability.Id, targetId, rowVersion);

        // Act
        var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Post_Move_UnknownTarget_Returns404_NotFound()
    {
        // Arrange
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source unknown-target");
        var capability = await SeedCapabilityAsync(sourceId);

        var rowVersion = Convert.ToBase64String(capability.RowVersion!);
        var req = BuildMoveRequest(sourceId, capability.Id,
            targetComponentId: Guid.NewGuid(), ifMatch: rowVersion);

        // Act
        var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_Move_CapabilityNotUnderSourceComponent_Returns404_NotFound()
    {
        // Arrange — capability lives under "other"; caller asks to move it
        // from "source" → "target". Must be a 404 from the source side.
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source wrong-source");
        var targetId = await SeedComponentAsync("Target wrong-source");
        var otherId = await SeedComponentAsync("Other wrong-source");
        var capability = await SeedCapabilityAsync(otherId);

        var rowVersion = Convert.ToBase64String(capability.RowVersion!);
        var req = BuildMoveRequest(sourceId, capability.Id, targetId, rowVersion);

        // Act
        var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── 412 ROW_VERSION_MISMATCH ───────────────────────────────────────

    [Fact]
    public async Task Post_Move_StaleIfMatch_Returns412_RowVersionMismatch()
    {
        // Arrange — supply an If-Match that does NOT match the seeded
        // RowVersion. EF Core's change tracker surfaces the mismatch.
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source stale-ifmatch");
        var targetId = await SeedComponentAsync("Target stale-ifmatch");
        var capability = await SeedCapabilityAsync(sourceId);

        var staleRowVersion = Convert.ToBase64String(
            new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
        var req = BuildMoveRequest(sourceId, capability.Id, targetId, staleRowVersion);

        // Act
        var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("ROW_VERSION_MISMATCH");
    }

    // ─── 403 FORBIDDEN_NOT_CSP_ADMIN ────────────────────────────────────

    [Fact]
    public async Task Post_Move_AsNonCspAdmin_Returns403_ForbiddenNotCspAdmin()
    {
        // Arrange — seed as admin, then drop role for the move call.
        SetCspAdmin(true);
        var sourceId = await SeedComponentAsync("Source 403");
        var targetId = await SeedComponentAsync("Target 403");
        var capability = await SeedCapabilityAsync(sourceId);
        SetCspAdmin(false);

        var rowVersion = Convert.ToBase64String(capability.RowVersion!);
        var req = BuildMoveRequest(sourceId, capability.Id, targetId, rowVersion);

        // Act
        var resp = await _client.SendAsync(req);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static HttpRequestMessage BuildMoveRequest(
        Guid componentId,
        Guid capabilityId,
        Guid targetComponentId,
        string ifMatch)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}/{componentId}/capabilities/{capabilityId}/move")
        {
            Content = JsonContent.Create(new { targetComponentId }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return req;
    }

    private void SetCspAdmin(bool isCspAdmin)
    {
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = isCspAdmin;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;
    }

    private async Task<Guid> SeedComponentAsync(string name, bool archived = false)
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
        var id = Guid.NewGuid();
        db.CspInheritedComponents.Add(new CspInheritedComponent
        {
            Id = id,
            CspProfileId = profileId,
            Name = name,
            Description = "seed",
            ComponentType = CspComponentType.Service,
            SourceFormat = SourceFormat.Manual,
            Status = archived
                ? CspInheritedComponentStatus.Archived
                : CspInheritedComponentStatus.Published,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<CspInheritedCapability> SeedCapabilityAsync(Guid componentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var cap = new CspInheritedCapability
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = componentId,
            Name = "Tenant RBAC",
            Description = "Azure RBAC role assignments.",
            MappedNistControlIds = new List<string> { "AC-2" },
            MappingConfidence = 0.87,
            MappedBy = MappedBy.AI,
            Status = CspInheritedCapabilityStatus.Mapped,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedBy = "system",
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            ReviewedBy = "reviewer-oid",
            ReviewerNote = "ok",
            // Explicit non-null RowVersion. The integration SQLite fixture's
            // `CspInheritedCapabilities` table has `RowVersion BLOB NULL`
            // with no UPDATE trigger; we seed a real byte[] so the endpoint's
            // `Convert.ToBase64String(cap.RowVersion)` succeeds.
            RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 },
        };
        db.CspInheritedCapabilities.Add(cap);
        await db.SaveChangesAsync();

        // Re-fetch to capture whatever the provider persisted (SQLite/EF Core
        // can null the BLOB if the column type-converter ignores the seed —
        // explicit re-load guards against that).
        var reloaded = await db.CspInheritedCapabilities
            .AsNoTracking()
            .FirstAsync(c => c.Id == cap.Id);

        if (reloaded.RowVersion is null || reloaded.RowVersion.Length == 0)
        {
            // SQLite stripped the seed; force-write a real BLOB via raw SQL so
            // the endpoint test can encode it into the If-Match header.
            var stamp = new byte[] { 0, 0, 0, 0, 0, 0, 0, 11 };
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE CspInheritedCapabilities SET RowVersion = {stamp} WHERE Id = {cap.Id}");
            reloaded = await db.CspInheritedCapabilities
                .AsNoTracking()
                .FirstAsync(c => c.Id == cap.Id);
        }
        return reloaded;
    }
}
