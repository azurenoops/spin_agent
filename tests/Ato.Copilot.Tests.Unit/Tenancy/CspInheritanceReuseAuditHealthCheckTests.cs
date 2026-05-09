using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// T218 [US9]: Verifies <see cref="CspInheritanceReuseAuditHealthCheck"/> enforces
/// FR-110 — exactly one DI registration per FR-110 service interface — and uses
/// string-based reflection lookup so unregistered interfaces are no-ops (the
/// check then begins enforcing them automatically once T198 / T204 / T206 land).
/// </summary>
public class CspInheritanceReuseAuditHealthCheckTests
{
    private static CspInheritanceReuseAuditHealthCheck NewCheck(IServiceCollection services)
        => new CspInheritanceReuseAuditHealthCheck(
            new ServiceRegistrationSnapshot(services),
            NullLogger<CspInheritanceReuseAuditHealthCheck>.Instance);

    [Fact]
    public async Task GivenNoRegistrations_WhenStartAsync_ThenDoesNotThrow()
    {
        // Arrange — empty service collection: every FR-110 interface is missing
        var services = new ServiceCollection();
        var check = NewCheck(services);

        // Act
        var act = async () => await check.StartAsync(CancellationToken.None);

        // Assert — string-based lookup must no-op against missing types so
        // unregistered interfaces (e.g. ICspAtoDocumentParser before T198 lands)
        // do not crash the host
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GivenSingleRegistrationOfEveryService_WhenStartAsync_ThenDoesNotThrow()
    {
        // Arrange — one descriptor per FR-110 interface; the check must accept this
        var services = new ServiceCollection();
        services.AddSingleton<ICapabilityMappingService>(_ => null!);
        services.AddSingleton<IControlNarrativeService>(_ => null!);
        services.AddSingleton<IEvidenceArtifactService>(_ => null!);
        services.AddSingleton<IOrgInheritanceService>(_ => null!);

        var check = NewCheck(services);

        // Act
        var act = async () => await check.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GivenDuplicateRegistration_WhenStartAsync_ThenThrowsInvalidOperation()
    {
        // Arrange — two descriptors for IControlNarrativeService → FR-110 violation
        var services = new ServiceCollection();
        services.AddSingleton<IControlNarrativeService>(_ => null!);
        services.AddSingleton<IControlNarrativeService>(_ => null!);

        var check = NewCheck(services);

        // Act
        var act = async () => await check.StartAsync(CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("FR-110");
        ex.Which.Message.Should().Contain("IControlNarrativeService");
    }

    [Fact]
    public async Task GivenDuplicateRegistrationOfTenancyInterface_WhenStartAsync_ThenThrows()
    {
        // Arrange — verify the Tenancy-namespace interface is also enforced
        var services = new ServiceCollection();
        services.AddSingleton<ICspAtoDocumentParser>(_ => null!);
        services.AddSingleton<ICspAtoDocumentParser>(_ => null!);

        var check = NewCheck(services);

        // Act
        var act = async () => await check.StartAsync(CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("FR-110");
        ex.Which.Message.Should().Contain("ICspAtoDocumentParser");
    }

    [Fact]
    public async Task GivenMultipleDuplicates_WhenStartAsync_ThenThrowsListingAllOffenders()
    {
        // Arrange — two FR-110 interfaces over-registered simultaneously
        var services = new ServiceCollection();
        services.AddSingleton<ICapabilityMappingService>(_ => null!);
        services.AddSingleton<ICapabilityMappingService>(_ => null!);
        services.AddSingleton<IEvidenceArtifactService>(_ => null!);
        services.AddSingleton<IEvidenceArtifactService>(_ => null!);

        var check = NewCheck(services);

        // Act
        var act = async () => await check.StartAsync(CancellationToken.None);

        // Assert — both offenders must be reported in the same exception
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("ICapabilityMappingService");
        ex.Which.Message.Should().Contain("IEvidenceArtifactService");
    }

    [Fact]
    public async Task GivenSnapshotIsImmutable_WhenServicesMutatedAfterSnapshot_ThenSnapshotUnaffected()
    {
        // Arrange — snapshot taken first; services mutated later must not be reflected
        var services = new ServiceCollection();
        services.AddSingleton<IControlNarrativeService>(_ => null!);
        var check = NewCheck(services);

        // Act — add a duplicate AFTER the snapshot was taken
        services.AddSingleton<IControlNarrativeService>(_ => null!);
        var act = async () => await check.StartAsync(CancellationToken.None);

        // Assert — snapshot is a point-in-time copy; later mutations are invisible
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public Task StopAsync_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var check = NewCheck(services);
        return check.StopAsync(CancellationToken.None);
    }
}
