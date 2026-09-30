using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services.Roles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class OscalGeneratedSchemaTests
{
    [Fact]
    public async Task RealGenerator_WithRecordedPrerequisites_PassesBundledSspSchemaWithoutInventingLinks()
    {
        // Arrange
        var database = Guid.NewGuid().ToString();
        using var services = new ServiceCollection().AddDbContextFactory<AtoCopilotContext>(o => o.UseInMemoryDatabase(database))
            .AddSingleton<IUnifiedRoleReader, UnifiedRoleReader>().BuildServiceProvider();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenant = Guid.NewGuid();
        var system = new RegisteredSystem
        {
            TenantId = tenant, Name = "DEMO schema mission", Description = "DEMO documented mission",
            OperationalStatus = OperationalStatus.UnderDevelopment
        };
        var person = new Person { TenantId = tenant, DisplayName = "DEMO assigned ISSM", Email = "schema@example.invalid" };
        var categorization = new SecurityCategorization { RegisteredSystemId = system.Id, TenantId = tenant };
        categorization.InformationTypes.Add(new InformationType
        {
            Name = "DEMO event records", Category = "DEMO audit events", Sp80060Id = "D.1.1",
            ConfidentialityImpact = ImpactValue.Moderate, IntegrityImpact = ImpactValue.Moderate, AvailabilityImpact = ImpactValue.Low
        });
        var boundary = new AuthorizationBoundaryDefinition
        {
            TenantId = tenant, RegisteredSystemId = system.Id, Name = "DEMO boundary", Description = "DEMO explicit boundary scope"
        };
        var component = new SystemComponent
        {
            TenantId = tenant, RegisteredSystemId = system.Id, ComponentType = ComponentType.Thing,
            Name = "DEMO audit processor", Description = "DEMO event processing component", Status = ComponentStatus.Active
        };
        db.AddRange(system, person, categorization, boundary, component,
            new SystemRoleAssignment { TenantId = tenant, RegisteredSystemId = system.Id, PersonId = person.Id, Role = OrganizationRole.Issm },
            new BoundaryComponentAssignment { TenantId = tenant, SystemComponentId = component.Id, AuthorizationBoundaryDefinitionId = boundary.Id, IsInScope = true },
            new ControlBaseline { TenantId = tenant, RegisteredSystemId = system.Id, BaselineLevel = "Moderate", ControlIds = ["AU-2"], TotalControls = 1 },
            new ControlImplementation
            {
                TenantId = tenant, RegisteredSystemId = system.Id, ControlId = "AU-2",
                PolicyNarrative = "DEMO recorded event policy", TechnicalNarrative = "DEMO recorded event collection",
                ImplementationStatus = ImplementationStatus.Implemented
            },
            new SystemProfileSection
            {
                TenantId = tenant, RegisteredSystemId = system.Id, SectionType = ProfileSectionType.MissionAndPurpose,
                GovernanceStatus = SspSectionStatus.UnderReview, DraftContent = "DEMO approved purpose\nSecond paragraph."
            });
        await db.SaveChangesAsync();
        await new SystemProfileService(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SystemProfileService>.Instance)
            .BatchApproveSectionsAsync(system.Id, "DEMO reviewer", RmfRole.Issm);
        var exporter = new OscalSspExportService(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OscalSspExportService>.Instance);
        var validator = new OscalSchemaValidationService(Mock.Of<IEmassExportService>(), Mock.Of<IOscalSapExportService>(),
            NullLogger<OscalSchemaValidationService>.Instance);

        // Act
        var generated = await exporter.ExportAsync(system.Id);
        var validation = await validator.ValidateAsync(generated.OscalJson, "ssp");

        // Assert
        validation.IsValid.Should().BeTrue(string.Join("\n", validation.Violations.Select(v => $"{v.JsonPath}: {v.Message}")));
        using var document = JsonDocument.Parse(generated.OscalJson);
        var ssp = document.RootElement.GetProperty("system-security-plan");
        ssp.GetProperty("system-characteristics").GetProperty("system-ids")[0].GetProperty("id").GetString().Should().Be(system.Id);
        generated.OscalJson.Should().Contain("DEMO assigned ISSM").And.Contain("DEMO audit processor")
            .And.Contain("DEMO recorded event policy").And.Contain("DEMO explicit boundary scope");
        ssp.GetProperty("control-implementation").GetProperty("implemented-requirements")[0].TryGetProperty("by-components", out _)
            .Should().BeFalse("no persisted control-to-component relationship was recorded");
        ssp.TryGetProperty("back-matter", out _).Should().BeFalse("an empty optional back-matter collection must be omitted");
        var invalid = System.Text.Json.Nodes.JsonNode.Parse(generated.OscalJson)!;
        invalid["system-security-plan"]!["system-characteristics"]!.AsObject().Remove("system-name");
        var diagnostic = await validator.ValidateAsync(invalid.ToJsonString(), "ssp");
        diagnostic.IsValid.Should().BeFalse();
        diagnostic.Violations.Should().NotContain(v => v.JsonPath.EndsWith("/identifier-type")
            || v.JsonPath.EndsWith("/components/0/type"), "successful anyOf alternatives are not validation violations");
    }
}
