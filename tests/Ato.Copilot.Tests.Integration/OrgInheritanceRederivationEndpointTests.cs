using System.Net;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class OrgInheritanceRederivationEndpointTests :
    IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public OrgInheritanceRederivationEndpointTests(
        MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Derive_RemovesReferencedStaleDefaultWithoutLosingManualOverride()
    {
        // Arrange
        _factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        await _factory.EnsureActiveCspProfileAsync();
        var (defaultId, orgDerivedBaselineId, overrideBaselineId, foreignDefaultId, foreignDesignationId) =
            await SeedReferencedStaleDefaultAsync();

        // Act
        using var response = await _client.PostAsync(
            "/api/dashboard/inheritance/org-defaults/derive",
            content: null);
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("removedCount").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("affectedSystems").GetInt32().Should().Be(2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        (await db.OrgInheritanceDefaults.AnyAsync(item => item.Id == defaultId)).Should().BeFalse();
        (await db.ControlInheritances.AnyAsync(item =>
            item.ControlBaselineId == orgDerivedBaselineId)).Should().BeFalse();
        var remaining = await db.ControlInheritances
            .SingleAsync(item => item.ControlBaselineId == overrideBaselineId);
        remaining.DesignationSource.Should().Be("Manual");
        remaining.OrgInheritanceDefaultId.Should().BeNull();
        remaining.InheritanceType.Should().Be(InheritanceType.Shared);
        remaining.Provider.Should().Be("System Provider");
        remaining.CustomerResponsibility.Should().Be("Operate system-specific safeguards");
        (await db.InheritanceAuditEntries.AnyAsync(item =>
            item.ControlBaselineId == orgDerivedBaselineId &&
            item.ControlId == "ORG-CASCADE")).Should().BeTrue();
        (await db.InheritanceAuditEntries.AnyAsync(item =>
            item.ControlBaselineId == overrideBaselineId &&
            item.ControlId == "ORG-CASCADE")).Should().BeTrue();
        (await db.OrgInheritanceDefaults.IgnoreQueryFilters()
            .AnyAsync(item => item.Id == foreignDefaultId)).Should().BeTrue();
        (await db.ControlInheritances.IgnoreQueryFilters()
            .AnyAsync(item => item.Id == foreignDesignationId)).Should().BeTrue();
    }

    private async Task<(
        string DefaultId,
        string OrgDerivedBaselineId,
        string OverrideBaselineId,
        string ForeignDefaultId,
        string ForeignDesignationId)> SeedReferencedStaleDefaultAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var suffix = Guid.NewGuid().ToString("N");
        var controlId = $"ZZ-{suffix[..8]}";
        var orgDerivedSystem = new RegisteredSystem
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            Id = $"system-{suffix}",
            Name = $"Re-derivation Test {suffix}",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Government",
            CreatedBy = "integration-test",
        };
        var orgDerivedBaseline = new ControlBaseline
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            Id = $"baseline-{suffix}",
            RegisteredSystemId = orgDerivedSystem.Id,
            BaselineLevel = "Moderate",
            TotalControls = 1,
            ControlIds = [controlId],
            CreatedBy = "integration-test",
        };
        var overrideSystem = new RegisteredSystem
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            Id = $"override-system-{suffix}",
            Name = $"Override Re-derivation Test {suffix}",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Government",
            CreatedBy = "integration-test",
        };
        var overrideBaseline = new ControlBaseline
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            Id = $"override-baseline-{suffix}",
            RegisteredSystemId = overrideSystem.Id,
            BaselineLevel = "Moderate",
            TotalControls = 1,
            ControlIds = [controlId],
            CreatedBy = "integration-test",
        };
        var staleDefault = new OrgInheritanceDefault
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            Id = $"default-{suffix}",
            ControlId = controlId,
            InheritanceType = InheritanceType.Inherited,
            Provider = "Retired Provider",
            SourceCapabilityIds = "retired-capability",
            SourceCapabilityNames = "Retired Capability",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow.AddDays(-1),
        };
        var foreignSystem = new RegisteredSystem
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId,
            Id = $"foreign-system-{suffix}",
            Name = $"Foreign Re-derivation Test {suffix}",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Government",
            CreatedBy = "integration-test",
        };
        var foreignBaseline = new ControlBaseline
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId,
            Id = $"foreign-baseline-{suffix}",
            RegisteredSystemId = foreignSystem.Id,
            BaselineLevel = "Moderate",
            TotalControls = 1,
            ControlIds = ["AC-2"],
            CreatedBy = "integration-test",
        };
        var foreignDefault = new OrgInheritanceDefault
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId,
            Id = $"foreign-default-{suffix}",
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Foreign Provider",
            SourceCapabilityIds = "foreign-capability",
            SourceCapabilityNames = "Foreign Capability",
            MappingRole = CapabilityMappingRole.Primary,
            DerivedAt = DateTime.UtcNow.AddDays(-1),
        };
        var foreignDesignation = new ControlInheritance
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId,
            ControlBaselineId = foreignBaseline.Id,
            ControlId = "AC-2",
            InheritanceType = InheritanceType.Inherited,
            Provider = "Foreign Provider",
            DesignationSource = "OrgDerived",
            OrgInheritanceDefaultId = foreignDefault.Id,
            SetBy = "system",
        };
        db.AddRange(
            orgDerivedSystem,
            orgDerivedBaseline,
            overrideSystem,
            overrideBaseline,
            staleDefault,
            foreignSystem,
            foreignBaseline,
            foreignDefault);
        db.ControlInheritances.AddRange(
            new ControlInheritance
            {
                TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                ControlBaselineId = orgDerivedBaseline.Id,
                ControlId = controlId,
                InheritanceType = InheritanceType.Inherited,
                Provider = "Retired Provider",
                DesignationSource = "OrgDerived",
                OrgInheritanceDefaultId = staleDefault.Id,
                SetBy = "system",
            },
            new ControlInheritance
            {
                TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                ControlBaselineId = overrideBaseline.Id,
                ControlId = controlId,
                InheritanceType = InheritanceType.Shared,
                Provider = "System Provider",
                CustomerResponsibility = "Operate system-specific safeguards",
                DesignationSource = "Manual",
                OrgInheritanceDefaultId = staleDefault.Id,
                SetBy = "reviewer",
            },
            foreignDesignation);
        await db.SaveChangesAsync();

        return (
            staleDefault.Id,
            orgDerivedBaseline.Id,
            overrideBaseline.Id,
            foreignDefault.Id,
            foreignDesignation.Id);
    }
}
