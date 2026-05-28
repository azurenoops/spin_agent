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
/// Integration coverage for the manual-create surface added on top of US9
/// (Feature 048): a CSP-Admin can create a CSP-inherited component and
/// add capabilities to it WITHOUT going through the
/// <c>POST /import</c> multipart pipeline.
/// </summary>
/// <remarks>
/// <para>Endpoints under test:</para>
/// <list type="bullet">
///   <item><c>POST /api/csp/inherited-components</c> — JSON body, returns 200 with
///         <see cref="SourceFormat.Manual"/> and
///         <see cref="CspInheritedComponentStatus.Published"/>.</item>
///   <item><c>POST /api/csp/inherited-components/{componentId}/capabilities</c> —
///         JSON body, returns 200 with
///         <see cref="MappedBy.User"/> and
///         <see cref="CspInheritedCapabilityStatus.Mapped"/>.</item>
/// </list>
/// <para>Both endpoints are gated to <c>CSP.Admin</c> per FR-106; Mission
/// Owners must receive <c>403 FORBIDDEN_NOT_CSP_ADMIN</c>.</para>
/// </remarks>
[Collection("Tenancy")]
public class CspInheritedComponentManualCreateTests
    : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private const string BaseUrl = "/api/csp/inherited-components";

    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspInheritedComponentManualCreateTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ─── POST /api/csp/inherited-components (CSP-Admin) ─────────────────────

    [Fact]
    public async Task Post_Component_AsCspAdmin_Returns200_WithManualSourceAndPublishedStatus()
    {
        // Arrange
        SetCspAdmin(true);
        var payload = JsonContent.Create(new
        {
            name = "Manually Created Service",
            description = "A capability authored by the CSP admin without an ATO PDF.",
            componentType = "Service",
        });

        // Act
        var resp = await _client.PostAsync(BaseUrl, payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "FR-106 requires CSP.Admin to be able to author CSP-inherited components manually.");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");
        data.GetProperty("name").GetString().Should().Be("Manually Created Service");
        data.GetProperty("componentType").GetString().Should().Be("Service");
        data.GetProperty("sourceFormat").GetString().Should().Be("Manual",
            "manual-create rows MUST mark provenance as SourceFormat.Manual");
        data.GetProperty("status").GetString().Should().Be("Published",
            "manual-create skips Draft because there is no extraction step to defer publishing for");
    }

    [Fact]
    public async Task Post_Component_AsMissionOwner_Returns403_ForbiddenNotCspAdmin()
    {
        // Arrange
        SetCspAdmin(false);
        var payload = JsonContent.Create(new
        {
            name = "Mission Owner Attempt",
            description = "should be blocked",
            componentType = "Service",
        });

        // Act
        var resp = await _client.PostAsync(BaseUrl, payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Post_Component_AsCspAdmin_WithEmptyName_Returns422_ValidationFailed()
    {
        // Arrange
        SetCspAdmin(true);
        var payload = JsonContent.Create(new
        {
            name = "   ",
            description = "non-empty desc",
            componentType = "Service",
        });

        // Act
        var resp = await _client.PostAsync(BaseUrl, payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Post_Component_AsCspAdmin_WithUnknownComponentType_Returns422_ValidationFailed()
    {
        // Arrange
        SetCspAdmin(true);
        var payload = JsonContent.Create(new
        {
            name = "Bad Type",
            description = "desc",
            componentType = "BogusType",
        });

        // Act
        var resp = await _client.PostAsync(BaseUrl, payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    // ─── POST /api/csp/inherited-components/{id}/capabilities (CSP-Admin) ────

    [Fact]
    public async Task Post_Capability_AsCspAdmin_Default_Returns200_WithNeedsReviewStatus()
    {
        // Arrange — Feature 050 FR-001: the new default is NeedsReview.
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("Component For Caps");
        var payload = JsonContent.Create(new
        {
            name = "Manual Capability",
            description = "Authored by CSP-Admin.",
            mappedNistControlIds = new[] { "AC-2", "AC-2(1)" },
        });

        // Act
        var resp = await _client.PostAsync($"{BaseUrl}/{componentId}/capabilities", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("success");
        var data = body.GetProperty("data");
        data.GetProperty("name").GetString().Should().Be("Manual Capability");
        data.GetProperty("status").GetString().Should().Be("NeedsReview",
            "Feature 050 FR-001 — manual-create capabilities are vetted by default; "
            + "creator must explicitly opt in to mark Mapped on create.");
        data.GetProperty("mappedBy").GetString().Should().Be("User",
            "manual-create rows MUST be MappedBy=User so a future remap respects them.");
        data.GetProperty("reviewedBy").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("reviewedAt").ValueKind.Should().Be(JsonValueKind.Null);

        // Verify exactly one Created history row was written.
        var capabilityId = data.GetProperty("id").GetGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var history = await db.CapabilityHistoryEvents
            .IgnoreQueryFilters()
            .Where(h => h.CapabilityId == capabilityId)
            .ToListAsync();
        history.Should().HaveCount(1);
        history[0].EventType.Should().Be(CapabilityHistoryEventType.Created);
        history[0].MetadataJson.Should().BeNull();
    }

    [Fact]
    public async Task Post_Capability_AsCspAdmin_WithMarkMappedImmediately_Returns200_WithMappedStatus_AndTwoHistoryRows()
    {
        // Arrange — Feature 050 FR-001 override.
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("Component For Override");
        var payload = JsonContent.Create(new
        {
            name = "Manual Capability Mapped Immediately",
            description = "Authored by CSP-Admin who already verified the mapping.",
            mappedNistControlIds = new[] { "AC-2" },
            markMappedImmediately = true,
        });

        // Act
        var resp = await _client.PostAsync($"{BaseUrl}/{componentId}/capabilities", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("Mapped");
        data.GetProperty("reviewedBy").GetString().Should().NotBeNullOrEmpty();
        data.GetProperty("reviewedAt").ValueKind.Should().Be(JsonValueKind.String);
        data.GetProperty("reviewerNote").GetString().Should().Be("Mapped on create by creator.");

        // Verify two history rows: Created + Reviewed.
        var capabilityId = data.GetProperty("id").GetGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var history = await db.CapabilityHistoryEvents
            .IgnoreQueryFilters()
            .Where(h => h.CapabilityId == capabilityId)
            .ToListAsync();
        history.Should().HaveCount(2);
        history.Select(h => h.EventType).Should().Contain(new[]
        {
            CapabilityHistoryEventType.Created,
            CapabilityHistoryEventType.Reviewed,
        });
        var created = history.Single(h => h.EventType == CapabilityHistoryEventType.Created);
        created.MetadataJson.Should().NotBeNull();
        created.MetadataJson.Should().Contain("markedMappedImmediately");
    }

    [Fact]
    public async Task Post_Capability_AsCspAdmin_WithMarkMappedImmediatelyFalse_Returns200_WithNeedsReviewStatus()
    {
        // Arrange — explicit false matches absence.
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("Component For ExplicitFalse");
        var payload = JsonContent.Create(new
        {
            name = "Explicit False",
            description = "x",
            mappedNistControlIds = new[] { "AC-2" },
            markMappedImmediately = false,
        });

        // Act
        var resp = await _client.PostAsync($"{BaseUrl}/{componentId}/capabilities", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("status").GetString().Should().Be("NeedsReview");
    }

    [Fact]
    public async Task Post_Capability_AsMissionOwner_Returns403_ForbiddenNotCspAdmin()
    {
        // Arrange
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("Component For 403 Cap");
        SetCspAdmin(false);
        var payload = JsonContent.Create(new
        {
            name = "Should Be Blocked",
            description = "x",
            mappedNistControlIds = new[] { "AC-2" },
        });

        // Act
        var resp = await _client.PostAsync($"{BaseUrl}/{componentId}/capabilities", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("FORBIDDEN_NOT_CSP_ADMIN");
    }

    [Fact]
    public async Task Post_Capability_AsCspAdmin_OnUnknownComponent_Returns404_NotFound()
    {
        // Arrange
        SetCspAdmin(true);
        var unknownId = Guid.NewGuid();
        var payload = JsonContent.Create(new
        {
            name = "Cap On Unknown Component",
            description = "x",
            mappedNistControlIds = new[] { "AC-2" },
        });

        // Act
        var resp = await _client.PostAsync($"{BaseUrl}/{unknownId}/capabilities", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Post_Capability_AsCspAdmin_WithEmptyControlIds_Returns422_ValidationFailed()
    {
        // Arrange
        SetCspAdmin(true);
        var componentId = await SeedComponentAsync("Component For Validation Cap");
        var payload = JsonContent.Create(new
        {
            name = "Bad Cap",
            description = "x",
            mappedNistControlIds = Array.Empty<string>(),
        });

        // Act
        var resp = await _client.PostAsync($"{BaseUrl}/{componentId}/capabilities", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("errorCode").GetString()
            .Should().Be("VALIDATION_FAILED");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private void SetCspAdmin(bool isCspAdmin)
    {
        var ctx = _factory.GetActiveContext();
        ctx.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        ctx.IsCspAdmin = isCspAdmin;
        ctx.ImpersonatedTenantId = null;
        ctx.Status = TenantStatus.Active;
    }

    private async Task<Guid> SeedComponentAsync(string name)
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
            Status = CspInheritedComponentStatus.Published,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }
}
