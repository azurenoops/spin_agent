using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class PackageReadinessResponsibilityScopeTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Fact]
    public async Task NestedScopes_ReadResponsibilitiesWithExistingAmbientIdentity_WithoutChangingScopedIdentity()
    {
        // Arrange
        var (systemId, personId) = await SeedAsync();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantContext>();
            services.AddScoped<ITenantContext, TenantContext>();
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
        }));
        var identity = new TenantContext(MultiTenantWebApplicationFactory<McpProgram>.TenantAId)
        {
            PersonId = personId, IsWorkspaceRequest = true
        };
        var accessor = app.Services.GetRequiredService<ITenantContextAccessor>();
        using var request = accessor.Push(identity);
        await using var nested = app.Services.CreateAsyncScope();
        var scopedIdentity = nested.ServiceProvider.GetRequiredService<ITenantContext>();
        var db = nested.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.RegisteredSystems.AnyAsync(x => x.Id == systemId)).Should().BeTrue();
        scopedIdentity.EffectiveTenantId.Should().Be(Guid.Empty);
        scopedIdentity.PersonId.Should().BeNull();
        var readiness = app.Services.GetRequiredService<PackageReadinessService>();

        // Act
        var source = await readiness.ReadSourceAsync(systemId, new(PackagePurpose.InitialSubmission), personId.ToString(), default);
        var run = await readiness.ValidateAsync(systemId, new(PackagePurpose.InitialSubmission), personId.ToString(), default);

        // Assert
        source.State.Should().Be("Available", source.Reason);
        source.Hash.Should().HaveLength(64);
        run.Outcome.Should().Be("Blocked", run.FailureJson);
        PackageReadinessService.Checks(run).Should().Contain(x => x.Id == "responsibility" && x.Outcome == "FollowUp");
        accessor.Current.Should().BeSameAs(identity);
        identity.EffectiveTenantId.Should().Be(MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
        identity.PersonId.Should().Be(personId);
        identity.IsCspAdmin.Should().BeFalse();
        identity.ImpersonatedTenantId.Should().BeNull();
        scopedIdentity.EffectiveTenantId.Should().Be(Guid.Empty);
        scopedIdentity.PersonId.Should().BeNull();
        (await db.Set<CapabilityResponsibilityConfirmation>().CountAsync(x => x.RegisteredSystemId == systemId)).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AmbientForeignTenantOrUnassignedPerson_CannotReadReadinessSources(bool foreignTenant)
    {
        // Arrange
        var (systemId, personId) = await SeedAsync();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantContext>();
            services.AddScoped<ITenantContext, TenantContext>();
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
        }));
        var identity = new TenantContext(foreignTenant
            ? MultiTenantWebApplicationFactory<McpProgram>.TenantBId
            : MultiTenantWebApplicationFactory<McpProgram>.TenantAId)
        {
            PersonId = foreignTenant ? personId : Guid.NewGuid(), IsWorkspaceRequest = true
        };
        var accessor = app.Services.GetRequiredService<ITenantContextAccessor>();
        using var request = accessor.Push(identity);

        // Act
        var read = () => app.Services.GetRequiredService<PackageReadinessService>()
            .ReadSourceAsync(systemId, new(PackagePurpose.InitialSubmission), personId.ToString(), default);

        // Assert
        await read.Should().ThrowAsync<KeyNotFoundException>();
        accessor.Current.Should().BeSameAs(identity);
        identity.IsCspAdmin.Should().BeFalse();
    }

    private async Task<(string SystemId, Guid PersonId)> SeedAsync()
    {
        var tenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var personId = Guid.NewGuid();
        factory.ResetLegacyTenantContext(tenantId);
        factory.GetActiveContext().PersonId = personId;
        factory.GetActiveContext().IsWorkspaceRequest = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(new() { Id = personId, TenantId = tenantId, DisplayName = "Synthetic scoped ISSM", Email = $"{personId}@example.invalid" });
        db.OrganizationMemberships.Add(new()
        {
            TenantId = tenantId, PersonId = personId, DirectoryTenantId = Guid.NewGuid(),
            ObjectId = Guid.NewGuid(), GrantedBy = "test"
        });
        var system = new RegisteredSystem { TenantId = tenantId, Name = "Synthetic nested-scope readiness", CreatedBy = "test" };
        db.RegisteredSystems.Add(system);
        db.SystemRoleAssignments.Add(new() { TenantId = tenantId, RegisteredSystemId = system.Id, PersonId = personId, Role = OrganizationRole.Issm });
        db.ControlBaselines.Add(new()
        {
            TenantId = tenantId, RegisteredSystemId = system.Id, BaselineLevel = "Low", ControlIds = ["AC-1"], TotalControls = 1, CreatedBy = "test"
        });
        var profileId = await db.CspProfiles.Select(x => x.Id).FirstAsync();
        var component = new CspInheritedComponent { CspProfileId = profileId, Name = "Synthetic provider component", Status = CspInheritedComponentStatus.Published };
        var capability = new CspInheritedCapability
        {
            CspInheritedComponentId = component.Id, Name = "Synthetic mapped capability",
            Status = CspInheritedCapabilityStatus.Mapped, MappedNistControlIds = ["AC-1"]
        };
        db.AddRange(component, capability);
        db.CapabilitySubscriptions.Add(new()
        {
            RegisteredSystemId = system.Id, RoutingTenantId = tenantId, CspInheritedCapabilityId = capability.Id.ToString(),
            RoutingCapabilityId = capability.Id.ToString(), IsActive = true
        });
        await db.SaveChangesAsync();
        return (system.Id, personId);
    }
}
