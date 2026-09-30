using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class InventoryPackageReadinessTests
{
    [Theory]
    [InlineData(PackagePurpose.InitialSubmission, "Blocking", true)]
    [InlineData(PackagePurpose.Legacy, "FollowUp", false)]
    public async Task EmptyRegister_IsNotACompletedInventory(PackagePurpose purpose, string outcome, bool required)
    {
        // Arrange
        var service = new Mock<IInventoryService>();
        service.Setup(x => x.CheckCompletenessAsync("system", default)).ReturnsAsync(new InventoryCompleteness
        { SystemId = "system", TotalItems = 0, IsComplete = true, CompletenessScore = 100 });
        // Act
        var checks = await InventoryPackageReadiness.EvaluateAsync(service.Object, "system", purpose, NullLogger.Instance, default);
        // Assert
        checks[0].Outcome.Should().Be(outcome);
        checks[0].Required.Should().Be(required);
        checks[0].Why.Should().Contain("No active");
    }

    [Fact]
    public async Task StandaloneManagedSoftware_DoesNotRequirePhysicalHardware()
    {
        // Arrange
        var service = new Mock<IInventoryService>();
        service.Setup(x => x.CheckCompletenessAsync("system", default)).ReturnsAsync(new InventoryCompleteness
        { SystemId = "system", TotalItems = 1, SoftwareCount = 1, IsComplete = true, CompletenessScore = 100 });
        // Act
        var checks = await InventoryPackageReadiness.EvaluateAsync(service.Object, "system", PackagePurpose.InitialSubmission, NullLogger.Instance, default);
        // Assert
        checks[0].Outcome.Should().Be("Passed");
        checks.Should().NotContain(x => x.Outcome == "Blocking");
    }

    [Fact]
    public async Task MissingFields_BlockButHardwareCoverageRemainsAFollowUp()
    {
        // Arrange
        var service = new Mock<IInventoryService>();
        service.Setup(x => x.CheckCompletenessAsync("system", default)).ReturnsAsync(new InventoryCompleteness
        {
            SystemId = "system", TotalItems = 1, HardwareCount = 1,
            ItemsWithMissingFields = [new InventoryIssue { ItemId = "item", ItemName = "Storage", MissingFields = ["Manufacturer"] }],
            HardwareWithoutSoftware = ["item"],
        });
        // Act
        var checks = await InventoryPackageReadiness.EvaluateAsync(service.Object, "system", PackagePurpose.InitialSubmission, NullLogger.Instance, default);
        // Assert
        checks[0].Outcome.Should().Be("Blocking");
        checks[0].Why.Should().Contain("Manufacturer");
        checks.Should().Contain(x => x.Id == "inventory-coverage" && x.Outcome == "FollowUp" && !x.Required);
    }

    [Fact]
    public async Task UnavailableInventory_DoesNotBecomeAnEmptyPass()
    {
        // Arrange
        var service = new Mock<IInventoryService>();
        service.Setup(x => x.CheckCompletenessAsync("system", default)).ThrowsAsync(new InvalidOperationException("Unavailable"));
        // Act
        var checks = await InventoryPackageReadiness.EvaluateAsync(service.Object, "system", PackagePurpose.InitialSubmission, NullLogger.Instance, default);
        // Assert
        checks.Should().ContainSingle().Which.Outcome.Should().Be("Unavailable");
        checks[0].Required.Should().BeTrue();
    }
}
