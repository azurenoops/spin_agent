using System.Net;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class CspCapabilityNarrativeRegenerationEndpointTests :
    IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspCapabilityNarrativeRegenerationEndpointTests(
        MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BulkRegenerate_WithSubscribedCspCapability_CreatesAndRegeneratesMappedControls()
    {
        // Arrange
        var (systemId, capabilityId) = await SeedSubscribedCapabilityAsync(["AC-2", "AC-6"]);

        // Act
        using var response = await _client.PostAsync(
            $"/api/dashboard/systems/{systemId}/capabilities/{capabilityId}/bulk-regenerate",
            content: null);
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("totalControls").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("regenerated").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("failed").GetInt32().Should().Be(0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementations = await db.ControlImplementations
            .Where(implementation => implementation.RegisteredSystemId == systemId)
            .ToListAsync();
        implementations.Should().HaveCount(2);
        implementations.Should().OnlyContain(implementation =>
            implementation.SecurityCapabilityId == null &&
            implementation.IsAutoPopulated &&
            !string.IsNullOrWhiteSpace(implementation.Narrative));
    }

    [Fact]
    public async Task BulkRegenerate_WithDocumentSourceAndSubscribedCspCapability_UsesPreparedControl()
    {
        // Arrange
        var (systemId, capabilityId) = await SeedSubscribedCapabilityAsync(["AU-6"]);
        var sourceUrl = Uri.EscapeDataString("test-evidence-reference");
        await using (var preconditionScope = _factory.Services.CreateAsyncScope())
        {
            var preconditionDb = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<AtoCopilotContext>(preconditionScope.ServiceProvider);
            (await preconditionDb.ControlBaselines.AnyAsync(baseline => baseline.RegisteredSystemId == systemId))
                .Should().BeTrue();
        }

        // Act
        using var response = await _client.PostAsync(
            $"/api/dashboard/systems/{systemId}/capabilities/{capabilityId}/bulk-regenerate?sourceUrl={sourceUrl}",
            content: null);
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("totalControls").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("regenerated").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("failed").GetInt32().Should().Be(0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementation = await db.ControlImplementations
            .SingleAsync(candidate => candidate.RegisteredSystemId == systemId && candidate.ControlId == "AU-6");
        implementation.SecurityCapabilityId.Should().BeNull();
        implementation.Narrative.Should().Contain("Reference Sources Used:");
    }

    [Fact]
    public async Task BulkRegenerate_WithoutActiveCspSubscription_ReturnsNotFoundAndCreatesNothing()
    {
        // Arrange
        var (systemId, capabilityId) = await SeedSubscribedCapabilityAsync(["AC-2"], isSubscribed: false);

        // Act
        using var response = await _client.PostAsync(
            $"/api/dashboard/systems/{systemId}/capabilities/{capabilityId}/bulk-regenerate",
            content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
        document.RootElement.GetProperty("suggestion").GetString()
            .Should().Contain("active subscription");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        (await db.ControlImplementations.CountAsync(implementation =>
            implementation.RegisteredSystemId == systemId)).Should().Be(0);
    }

    private async Task<(string SystemId, Guid CapabilityId)> SeedSubscribedCapabilityAsync(
        string[] controlIds,
        bool isSubscribed = true)
    {
        _factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        await _factory.EnsureActiveCspProfileAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CapabilitySubscriptions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CapabilitySubscriptions" PRIMARY KEY,
                "RegisteredSystemId" TEXT NOT NULL,
                "CspInheritedCapabilityId" TEXT NOT NULL,
                "SubscribedBy" TEXT NOT NULL DEFAULT 'dashboard-user',
                "SubscribedAt" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS "IX_CapabilitySubscription_System_Capability"
                ON "CapabilitySubscriptions" ("RegisteredSystemId", "CspInheritedCapabilityId");
            """);
        var profileId = await db.CspProfiles
            .IgnoreQueryFilters()
            .Select(profile => profile.Id)
            .FirstAsync();
        var system = new RegisteredSystem
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            Name = $"Regeneration Test {Guid.NewGuid():N}",
            Acronym = "REGEN",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Government",
            CurrentRmfStep = RmfPhase.Implement,
            CreatedBy = "integration-test",
        };
        var component = new CspInheritedComponent
        {
            CspProfileId = profileId,
            Name = "Microsoft Entra ID P2",
            Description = "CSP identity governance component",
            ComponentType = CspComponentType.Identity,
            SourceFormat = SourceFormat.Manual,
            Status = CspInheritedComponentStatus.Published,
            ImportedBy = "integration-test",
        };
        var capability = new CspInheritedCapability
        {
            CspInheritedComponentId = component.Id,
            Name = "Access Reviews & Governance",
            Description = "CSP-managed identity governance",
            MappedNistControlIds = controlIds.ToList(),
            Status = CspInheritedCapabilityStatus.Mapped,
            CreatedBy = "integration-test",
        };
        db.AddRange(system, component, capability);
        db.ControlBaselines.Add(new ControlBaseline
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            RegisteredSystemId = system.Id,
            BaselineLevel = "Moderate",
            TotalControls = controlIds.Length,
            ControlIds = controlIds.ToList(),
            CreatedBy = "integration-test",
        });
        if (isSubscribed)
        {
            db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                RegisteredSystemId = system.Id,
                CspInheritedCapabilityId = capability.Id.ToString(),
                SubscribedBy = "integration-test",
                IsActive = true,
            });
        }
        await db.SaveChangesAsync();

        return (system.Id, capability.Id);
    }
}