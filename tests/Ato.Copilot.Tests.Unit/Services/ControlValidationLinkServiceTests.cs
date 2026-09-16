using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ControlValidationLinkServiceTests
{
    [Fact]
    public async Task UpsertScanLink_DuplicateUpdatesSingleRow_AndOtherTenantCannotReadIt()
    {
        // Arrange
        var tenantA = new TenantContext(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var tenantB = new TenantContext(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        ITenantContext currentTenant = tenantA;
        var accessor = new Mock<ITenantContextAccessor>();
        accessor.SetupGet(candidate => candidate.Current).Returns(() => currentTenant);
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"ControlValidationService_{Guid.NewGuid():N}")
            .Options;
        var contextFactory = new TestDbContextFactory(options, accessor.Object);
        var context = contextFactory.Context;
        context.ControlImplementations.Add(new ControlImplementation
        {
            TenantId = tenantA.TenantId,
            Id = "implementation-a",
            RegisteredSystemId = "system-a",
            ControlId = "AC-2",
            AuthoredBy = "test",
        });
        await context.SaveChangesAsync();
        var service = new ControlValidationLinkService(contextFactory, accessor.Object);

        // Act
        var created = await service.UpsertScanLinkAsync("system-a", "AC-2", "scan-1/finding-1", "First description");
        var createdAgain = await service.UpsertScanLinkAsync("system-a", "AC-2", "scan-1/finding-1", "Updated description");
        var links = await service.GetLinksAsync("system-a", "AC-2");

        // Assert
        created.Should().BeTrue();
        createdAgain.Should().BeFalse();
        links.Should().ContainSingle();
        links[0].Description.Should().Be("Updated description");
        links[0].IsAutomated.Should().BeTrue();
        links[0].ValidatedAt.Should().NotBeNull();

        // Act
        currentTenant = tenantB;
        var tenantBService = new ControlValidationLinkService(contextFactory, accessor.Object);
        var act = () => tenantBService.GetLinksAsync("system-a", "AC-2");

        // Assert
        await act.Should().ThrowAsync<ControlImplementationNotFoundException>();
    }
}