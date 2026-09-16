using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class EmassExportReadinessServiceTests
{
    [Fact]
    public async Task MissingIdentifiersAndCategorization_ReturnsBlockingGaps()
    {
        // Arrange
        var factory = CreateFactory();
        factory.Context.RegisteredSystems.Add(CreateSystem("system-1"));
        await factory.Context.SaveChangesAsync();
        var service = new EmassExportReadinessService(factory);

        // Act
        var result = await service.CheckReadinessAsync("system-1");

        // Assert
        result.IsReady.Should().BeFalse();
        result.Gaps.Where(gap => gap.Severity == ReadinessGapSeverity.Blocking)
            .Select(gap => gap.FieldName)
            .Should().BeEquivalentTo("DitprId", "EmassId", "SecurityCategorization");
    }

    [Fact]
    public async Task MissingApprovedSspSection_IsAdvisoryAndDoesNotBlock()
    {
        // Arrange
        var factory = CreateFactory();
        AddReadySystemData(factory, includeApprovedSection: false);
        await factory.Context.SaveChangesAsync();
        var service = new EmassExportReadinessService(factory);

        // Act
        var result = await service.CheckReadinessAsync("system-1");

        // Assert
        result.IsReady.Should().BeTrue();
        result.Gaps.Should().ContainSingle(gap =>
            gap.FieldName == "Ssp.ApprovedSections" &&
            gap.Severity == ReadinessGapSeverity.Advisory);
    }

    [Fact]
    public async Task CompleteSystem_IsReadyWithoutGaps()
    {
        // Arrange
        var factory = CreateFactory();
        AddReadySystemData(factory, includeApprovedSection: true);
        await factory.Context.SaveChangesAsync();
        var service = new EmassExportReadinessService(factory);

        // Act
        var result = await service.CheckReadinessAsync("system-1");

        // Assert
        result.IsReady.Should().BeTrue();
        result.Gaps.Should().BeEmpty();
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PoamWithoutScheduledCompletionDate_IsAdvisoryAndDoesNotBlock()
    {
        // Arrange
        var factory = CreateFactory();
        AddReadySystemData(factory, includeApprovedSection: true);
        factory.Context.PoamItems.Add(new PoamItem
        {
            RegisteredSystemId = "system-1",
            Weakness = "Unscheduled remediation",
            WeaknessSource = "Manual",
            SecurityControlNumber = "AC-2",
            PointOfContact = "ISSO",
        });
        await factory.Context.SaveChangesAsync();
        var service = new EmassExportReadinessService(factory);

        // Act
        var result = await service.CheckReadinessAsync("system-1");

        // Assert
        result.IsReady.Should().BeTrue();
        result.Gaps.Should().ContainSingle(gap =>
            gap.FieldName == "Poam.ScheduledCompletionDate" &&
            gap.Severity == ReadinessGapSeverity.Advisory);
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<Ato.Copilot.Core.Data.Context.AtoCopilotContext>()
            .UseInMemoryDatabase($"EmassReadiness_{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static RegisteredSystem CreateSystem(string id) => new()
    {
        Id = id,
        Name = "Readiness Test System",
        SystemType = SystemType.MajorApplication,
        MissionCriticality = MissionCriticality.MissionEssential,
        HostingEnvironment = "Test",
        CreatedBy = "test",
    };

    private static void AddReadySystemData(
        TestDbContextFactory factory,
        bool includeApprovedSection)
    {
        var system = CreateSystem("system-1");
        system.DitprId = "DITPR-071";
        system.EmassId = "EMASS-071";
        factory.Context.RegisteredSystems.Add(system);

        var categorization = new SecurityCategorization
        {
            RegisteredSystemId = system.Id,
            CategorizedBy = "test",
        };
        categorization.InformationTypes.Add(new InformationType
        {
            Sp80060Id = "D.1.1",
            Name = "Test Information",
            ConfidentialityImpact = ImpactValue.Moderate,
            IntegrityImpact = ImpactValue.Moderate,
            AvailabilityImpact = ImpactValue.Low,
        });
        factory.Context.SecurityCategorizations.Add(categorization);

        if (includeApprovedSection)
        {
            factory.Context.SspSections.Add(new SspSection
            {
                RegisteredSystemId = system.Id,
                SectionNumber = 1,
                SectionTitle = "System Identification",
                Status = SspSectionStatus.Approved,
                AuthoredBy = "test",
            });
        }
    }
}