using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class EmassWorkflowStatusServiceTests
{
    [Fact]
    public async Task NoCompletedPackage_ReturnsNeverExported()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var service = CreateService(factory);

        // Act
        var result = await service.GetStatusAsync("system-1");

        // Assert
        result.OverallStatus.Should().Be(EmassWorkflowOverallStatus.NeverExported);
        result.LastExportedAt.Should().BeNull();
    }

    [Fact]
    public async Task UnresolvedConflict_TakesPriorityOverPendingExport()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var exportedAt = DateTimeOffset.UtcNow.AddDays(-1);
        factory.Context.AuthorizationPackages.Add(CreateCompletedPackage(exportedAt));
        factory.Context.ControlImplementations.Add(CreateControl(DateTime.UtcNow));
        factory.Context.EmassConflicts.Add(new EmassConflict
        {
            RegisteredSystemId = "system-1",
            SyncBatchId = "batch-1",
            EntityType = "SystemInfo",
            FieldName = "SystemInfo.SystemName",
            SpinValue = "Old",
            EmassValue = "New",
            DetectedAt = DateTimeOffset.UtcNow,
        });
        await factory.Context.SaveChangesAsync();
        var service = CreateService(factory);

        // Act
        var result = await service.GetStatusAsync("system-1");

        // Assert
        result.OverallStatus.Should().Be(EmassWorkflowOverallStatus.HasConflicts);
        result.UnresolvedConflictCount.Should().Be(1);
        result.LastSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ControlModifiedAfterExport_ReturnsPendingExportCount()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var exportedAt = DateTimeOffset.UtcNow.AddDays(-1);
        factory.Context.AuthorizationPackages.Add(CreateCompletedPackage(exportedAt));
        factory.Context.ControlImplementations.Add(CreateControl(DateTime.UtcNow));
        await factory.Context.SaveChangesAsync();
        var service = CreateService(factory);

        // Act
        var result = await service.GetStatusAsync("system-1");

        // Assert
        result.OverallStatus.Should().Be(EmassWorkflowOverallStatus.PendingExport);
        result.ExportSummary.Single(summary => summary.Category == "Controls")
            .PendingCount.Should().Be(1);
    }

    private static EmassWorkflowStatusService CreateService(TestDbContextFactory factory)
    {
        var readiness = new Mock<IEmassExportReadinessService>();
        readiness.Setup(service => service.CheckReadinessAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string systemId, CancellationToken _) =>
                new EmassExportReadinessResult(
                    systemId, true, Array.Empty<ReadinessGap>(), DateTimeOffset.UtcNow));
        return new EmassWorkflowStatusService(factory, readiness.Object);
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<Ato.Copilot.Core.Data.Context.AtoCopilotContext>()
            .UseInMemoryDatabase($"EmassStatus_{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static async Task SeedSystemAsync(TestDbContextFactory factory)
    {
        factory.Context.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "system-1",
            Name = "Status Test System",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Test",
            CreatedBy = "test",
        });
        await factory.Context.SaveChangesAsync();
    }

    private static AuthorizationPackage CreateCompletedPackage(DateTimeOffset completedAt) => new()
    {
        Id = "package-1",
        RegisteredSystemId = "system-1",
        Status = PackageStatus.Completed,
        GeneratedBy = "test",
        GeneratedAt = completedAt.AddMinutes(-5),
        CompletedAt = completedAt,
        ExpiresAt = completedAt.AddDays(30),
        TotalArtifactCount = 3,
    };

    private static ControlImplementation CreateControl(DateTime modifiedAt) => new()
    {
        RegisteredSystemId = "system-1",
        ControlId = "AC-2",
        AuthoredBy = "test",
        AuthoredAt = modifiedAt.AddDays(-2),
        ModifiedAt = modifiedAt,
    };
}