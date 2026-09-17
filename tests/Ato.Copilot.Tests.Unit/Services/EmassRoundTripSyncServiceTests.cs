using Ato.Copilot.Agents.Compliance.Services;
using System.Diagnostics;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class EmassRoundTripSyncServiceTests
{
    [Fact]
    public async Task ChangedFields_CreateConflictsWithoutChangingSpin()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var service = new EmassRoundTripSyncService(factory);
        await using var workbook = BuildControlWorkbook(
            systemName: "Updated System",
            implementationStatus: "Partially Implemented",
            narrative: "Updated narrative");

        // Act
        var result = await service.StartSyncAsync("system-1", workbook);

        // Assert
        result.ConflictsCreated.Should().Be(3);
        factory.Context.EmassConflicts.Should().HaveCount(3);
        var system = await factory.Context.RegisteredSystems.FindAsync("system-1");
        system!.Name.Should().Be("Original System");
        var implementation = await factory.Context.ControlImplementations.SingleAsync();
        implementation.ImplementationStatus.Should().Be(ImplementationStatus.Implemented);
        implementation.Narrative.Should().Be("Original narrative");
    }

    [Fact]
    public async Task DeletedEmassValue_CreatesConflictWithoutClearingSpin()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var service = new EmassRoundTripSyncService(factory);
        await using var workbook = BuildControlWorkbook(
            systemName: "Original System",
            implementationStatus: "Implemented",
            narrative: null);

        // Act
        var result = await service.StartSyncAsync("system-1", workbook);

        // Assert
        result.ConflictsCreated.Should().Be(1);
        var conflict = await factory.Context.EmassConflicts.SingleAsync();
        conflict.FieldName.Should().Be("ControlImplementation.Narrative");
        conflict.EmassValue.Should().BeNull();
        (await factory.Context.ControlImplementations.SingleAsync()).Narrative
            .Should().Be("Original narrative");
    }

    [Fact]
    public async Task PoamDeletedInSpin_CreatesDeletionConflict()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var service = new EmassRoundTripSyncService(factory);
        await using var workbook = BuildPoamWorkbook("Still present in eMASS");

        // Act
        var result = await service.StartSyncAsync("system-1", workbook);

        // Assert
        result.ConflictsCreated.Should().Be(1);
        var conflict = await factory.Context.EmassConflicts.SingleAsync();
        conflict.FieldName.Should().Be("PoamItem.Deletion");
        conflict.EmassValue.Should().Be("poam-1");
    }

    [Fact]
    public async Task IdenticalNormalizedFields_CreateNoConflicts()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var service = new EmassRoundTripSyncService(factory);
        await using var workbook = BuildControlWorkbook(
            systemName: " original system ",
            implementationStatus: "implemented",
            narrative: " original narrative ");

        // Act
        var result = await service.StartSyncAsync("system-1", workbook);

        // Assert
        result.ConflictsCreated.Should().Be(0);
        result.IdenticalFields.Should().BeGreaterThan(0);
        factory.Context.EmassConflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task ConflictResolution_OnlyAcceptEmassUpdatesSpin()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var service = new EmassRoundTripSyncService(factory);
        await using var workbook = BuildControlWorkbook(
            systemName: "Updated System",
            implementationStatus: "Partially Implemented",
            narrative: "Updated narrative");
        await service.StartSyncAsync("system-1", workbook);
        var conflicts = await factory.Context.EmassConflicts.OrderBy(conflict => conflict.FieldName).ToListAsync();
        var nameConflict = conflicts.Single(conflict => conflict.FieldName == "SystemInfo.SystemName");
        var narrativeConflict = conflicts.Single(conflict =>
            conflict.FieldName == "ControlImplementation.Narrative");

        // Act
        await service.ResolveConflictAsync(
            "system-1", nameConflict.Id,
            new ResolveConflictRequest(ConflictStatus.AcceptEmass), "isso@example.mil");
        await service.ResolveConflictAsync(
            "system-1", narrativeConflict.Id,
            new ResolveConflictRequest(ConflictStatus.KeepSpin), "isso@example.mil");

        // Assert
        (await factory.Context.RegisteredSystems.FindAsync("system-1"))!.Name
            .Should().Be("Updated System");
        (await factory.Context.ControlImplementations.SingleAsync()).Narrative
            .Should().Be("Original narrative");
        nameConflict.ResolvedBy.Should().Be("isso@example.mil");
        narrativeConflict.ConflictStatus.Should().Be(ConflictStatus.KeepSpin);
    }

    [Fact]
    public async Task ConflictResolution_PersistsStateAndAuditAcrossContexts()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"EmassSync_{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await SeedSystemAsync(factory);
        var service = new EmassRoundTripSyncService(factory);
        await using var workbook = BuildControlWorkbook(
            systemName: "Updated System",
            implementationStatus: "Implemented",
            narrative: "Original narrative");
        await service.StartSyncAsync("system-1", workbook);
        var conflictId = await factory.Context.EmassConflicts
            .Select(conflict => conflict.Id)
            .SingleAsync();

        // Act
        await service.ResolveConflictAsync(
            "system-1",
            conflictId,
            new ResolveConflictRequest(ConflictStatus.KeepSpin),
            "isso@example.mil");
        await using var reloadedContext = new AtoCopilotContext(options);
        var persistedConflict = await reloadedContext.EmassConflicts.SingleAsync();
        var persistedAudit = await reloadedContext.AuditLogs.SingleAsync();

        // Assert
        persistedConflict.ConflictStatus.Should().Be(ConflictStatus.KeepSpin);
        persistedConflict.ResolvedAt.Should().NotBeNull();
        persistedConflict.ResolvedBy.Should().Be("isso@example.mil");
        var unresolvedCount = await reloadedContext.EmassConflicts
            .CountAsync(conflict => conflict.ConflictStatus == ConflictStatus.Unresolved);
        unresolvedCount.Should().Be(0);
        persistedAudit.Action.Should().Be("EmassConflict.Resolve");
        persistedAudit.UserId.Should().Be("isso@example.mil");
        persistedAudit.AffectedResources.Should().Contain(["system-1", conflictId]);
    }

    [Fact]
    public async Task SecondSyncWithUnresolvedConflicts_RequiresAcknowledgment()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        var service = new EmassRoundTripSyncService(factory);
        await using var firstWorkbook = BuildControlWorkbook(
            systemName: "Updated System",
            implementationStatus: "Implemented",
            narrative: "Original narrative");
        await service.StartSyncAsync("system-1", firstWorkbook);
        await using var secondWorkbook = BuildControlWorkbook(
            systemName: "Another Name",
            implementationStatus: "Implemented",
            narrative: "Original narrative");

        // Act
        var act = () => service.StartSyncAsync("system-1", secondWorkbook);

        // Assert
        await act.Should().ThrowAsync<UnresolvedEmassConflictsException>();
    }

    [Fact]
    public async Task PoamWorkbook_CreatesConflictAndAcceptUpdatesOnlySelectedField()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        factory.Context.PoamItems.Add(new PoamItem
        {
            Id = "poam-1",
            RegisteredSystemId = "system-1",
            Weakness = "Original weakness",
            WeaknessSource = "Manual",
            SecurityControlNumber = "AC-2",
            PointOfContact = "ISSO",
            ScheduledCompletionDate = new DateTime(2026, 4, 1),
        });
        await factory.Context.SaveChangesAsync();
        var service = new EmassRoundTripSyncService(factory);
        await using var workbook = BuildPoamWorkbook("Updated weakness");

        // Act
        var result = await service.StartSyncAsync("system-1", workbook);

        // Assert
        result.ConflictsCreated.Should().Be(1);
        (await factory.Context.PoamItems.FindAsync("poam-1"))!.Weakness
            .Should().Be("Original weakness");
        var conflict = await factory.Context.EmassConflicts.SingleAsync();
        conflict.FieldName.Should().Be("PoamItem.Weakness");

        // Act
        await service.ResolveConflictAsync(
            "system-1", conflict.Id,
            new ResolveConflictRequest(ConflictStatus.AcceptEmass), "isso@example.mil");

        // Assert
        (await factory.Context.PoamItems.FindAsync("poam-1"))!.Weakness
            .Should().Be("Updated weakness");
        factory.Context.AuditLogs.Should().ContainSingle(entry =>
            entry.Action == "EmassConflict.Resolve" && entry.UserId == "isso@example.mil");
    }

    [Fact]
    public async Task LargeWorkbook_With400ControlsAnd150PoamItems_CompletesWithin30Seconds()
    {
        // Arrange
        var factory = CreateFactory();
        await SeedSystemAsync(factory);
        factory.Context.ControlImplementations.RemoveRange(factory.Context.ControlImplementations);
        factory.Context.ControlImplementations.AddRange(Enumerable.Range(1, 400).Select(index =>
            new ControlImplementation
            {
                Id = $"implementation-{index}",
                RegisteredSystemId = "system-1",
                ControlId = $"AC-{index}",
                ImplementationStatus = ImplementationStatus.Implemented,
                Narrative = $"Narrative {index}",
                AuthoredBy = "test",
            }));
        factory.Context.PoamItems.AddRange(Enumerable.Range(1, 150).Select(index =>
            new PoamItem
            {
                Id = $"poam-{index}",
                RegisteredSystemId = "system-1",
                Weakness = $"Weakness {index}",
                WeaknessSource = "Manual",
                SecurityControlNumber = $"AC-{index}",
                PointOfContact = "ISSO",
                ScheduledCompletionDate = new DateTime(2026, 4, 1),
            }));
        await factory.Context.SaveChangesAsync();
        var service = new EmassRoundTripSyncService(factory);
        await using var workbook = BuildLargeWorkbook();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await service.StartSyncAsync("system-1", workbook);

        // Assert
        stopwatch.Stop();
        result.ConflictsCreated.Should().Be(0);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    private static TestDbContextFactory CreateFactory()
    {
        var options = new DbContextOptionsBuilder<Ato.Copilot.Core.Data.Context.AtoCopilotContext>()
            .UseInMemoryDatabase($"EmassSync_{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static async Task SeedSystemAsync(TestDbContextFactory factory)
    {
        factory.Context.RegisteredSystems.Add(new RegisteredSystem
        {
            Id = "system-1",
            Name = "Original System",
            Acronym = "OS",
            DitprId = "DITPR-071",
            EmassId = "EMASS-071",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Test",
            CreatedBy = "test",
        });
        factory.Context.ControlImplementations.Add(new ControlImplementation
        {
            Id = "implementation-1",
            RegisteredSystemId = "system-1",
            ControlId = "AC-2",
            ImplementationStatus = ImplementationStatus.Implemented,
            Narrative = "Original narrative",
            AuthoredBy = "test",
        });
        await factory.Context.SaveChangesAsync();
    }

    private static MemoryStream BuildControlWorkbook(
        string systemName,
        string implementationStatus,
        string narrative)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Controls");
        var headers = new[]
        {
            "System Name", "System Acronym", "DITPR ID", "eMASS ID",
            "Control Identifier", "Control Name", "Control Family",
            "Implementation Status", "Implementation Narrative",
        };
        for (var index = 0; index < headers.Length; index++)
            worksheet.Cell(1, index + 1).Value = headers[index];

        worksheet.Cell(2, 1).Value = systemName;
        worksheet.Cell(2, 2).Value = "OS";
        worksheet.Cell(2, 3).Value = "DITPR-071";
        worksheet.Cell(2, 4).Value = "EMASS-071";
        worksheet.Cell(2, 5).Value = "AC-2";
        worksheet.Cell(2, 8).Value = implementationStatus;
        worksheet.Cell(2, 9).Value = narrative;

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildPoamWorkbook(string weakness)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("POAM");
        var headers = new[]
        {
            "System Name", "eMASS ID", "POA&M ID", "Weakness", "Weakness Source",
            "Point of Contact", "POC Email", "Security Control Number",
            "Scheduled Completion Date", "Resources Required", "Cost Estimate", "Status",
            "Completion Date", "Comments",
        };
        for (var index = 0; index < headers.Length; index++)
            worksheet.Cell(1, index + 1).Value = headers[index];

        worksheet.Cell(2, 1).Value = "Original System";
        worksheet.Cell(2, 2).Value = "EMASS-071";
        worksheet.Cell(2, 3).Value = "poam-1";
        worksheet.Cell(2, 4).Value = weakness;
        worksheet.Cell(2, 5).Value = "Manual";
        worksheet.Cell(2, 6).Value = "ISSO";
        worksheet.Cell(2, 8).Value = "AC-2";
        worksheet.Cell(2, 9).Value = "04/01/2026";
        worksheet.Cell(2, 12).Value = "Ongoing";

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildLargeWorkbook()
    {
        using var workbook = new XLWorkbook();
        var controls = workbook.Worksheets.Add("Controls");
        var controlHeaders = new[]
        {
            "System Name", "System Acronym", "DITPR ID", "eMASS ID",
            "Control Identifier", "Implementation Status", "Implementation Narrative",
        };
        for (var index = 0; index < controlHeaders.Length; index++)
            controls.Cell(1, index + 1).Value = controlHeaders[index];
        for (var row = 1; row <= 400; row++)
        {
            controls.Cell(row + 1, 1).Value = "Original System";
            controls.Cell(row + 1, 2).Value = "OS";
            controls.Cell(row + 1, 3).Value = "DITPR-071";
            controls.Cell(row + 1, 4).Value = "EMASS-071";
            controls.Cell(row + 1, 5).Value = $"AC-{row}";
            controls.Cell(row + 1, 6).Value = "Implemented";
            controls.Cell(row + 1, 7).Value = $"Narrative {row}";
        }

        var poam = workbook.Worksheets.Add("POAM");
        var poamHeaders = new[]
        {
            "System Name", "eMASS ID", "POA&M ID", "Weakness", "Weakness Source",
            "Point of Contact", "POC Email", "Security Control Number",
            "Scheduled Completion Date", "Resources Required", "Cost Estimate", "Status",
            "Completion Date", "Comments",
        };
        for (var index = 0; index < poamHeaders.Length; index++)
            poam.Cell(1, index + 1).Value = poamHeaders[index];
        for (var row = 1; row <= 150; row++)
        {
            poam.Cell(row + 1, 1).Value = "Original System";
            poam.Cell(row + 1, 2).Value = "EMASS-071";
            poam.Cell(row + 1, 3).Value = $"poam-{row}";
            poam.Cell(row + 1, 4).Value = $"Weakness {row}";
            poam.Cell(row + 1, 5).Value = "Manual";
            poam.Cell(row + 1, 6).Value = "ISSO";
            poam.Cell(row + 1, 8).Value = $"AC-{row}";
            poam.Cell(row + 1, 9).Value = "04/01/2026";
            poam.Cell(row + 1, 12).Value = "Ongoing";
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}