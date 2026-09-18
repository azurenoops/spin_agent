using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Configuration;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Observability;
using Ato.Copilot.Core.Interfaces.Compliance;
using System.Text.Json;

namespace Ato.Copilot.Tests.Unit.Observability;

/// <summary>
/// Unit tests for <see cref="NistControlsHealthCheck"/>: healthy (3/3 valid),
/// degraded (partial), unhealthy (exception or no controls).
/// </summary>
public class NistControlsHealthCheckTests
{
    private readonly Mock<INistControlsService> _nistServiceMock = new();
    private readonly Mock<ILogger<NistControlsHealthCheck>> _loggerMock = new();

    private NistControlsHealthCheck CreateHealthCheck(
        NistControlsOptions? options = null,
        NistCatalogIntegrityValidator? integrityValidator = null)
    {
        var opts = options ?? new NistControlsOptions { CacheDurationHours = 24 };
        return new NistControlsHealthCheck(
            _nistServiceMock.Object,
            Options.Create(opts),
            _loggerMock.Object,
            integrityValidator ?? new NistCatalogIntegrityValidator(Mock.Of<ILogger<NistCatalogIntegrityValidator>>()));
    }

    [Fact]
    public async Task CheckHealthAsync_AllControlsValid_ReturnsHealthy()
    {
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var healthCheck = CreateHealthCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("nist-controls", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("version");
        result.Data["version"].Should().Be("5.2.0");
        result.Data.Should().ContainKey("validTestControls");
        result.Data["validTestControls"].Should().Be("3/3");
        result.Data.Should().ContainKey("responseTimeMs");
    }

    [Fact]
    public async Task CheckHealthAsync_PartialControlsValid_ReturnsDegraded()
    {
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");
        // Only AC-3 is valid, SC-13 and AU-2 are not
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync("AC-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync("SC-13", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync("AU-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var healthCheck = CreateHealthCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("nist-controls", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["validTestControls"].Should().Be("1/3");
    }

    [Fact]
    public async Task CheckHealthAsync_NoControlsValid_ReturnsUnhealthy()
    {
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var healthCheck = CreateHealthCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("nist-controls", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_UnknownVersion_ReturnsUnhealthy()
    {
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("Unknown");
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var healthCheck = CreateHealthCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("nist-controls", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_Exception_ReturnsUnhealthy()
    {
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Catalog not loaded"));

        var healthCheck = CreateHealthCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("nist-controls", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
        result.Data.Should().ContainKey("error");
    }

    [Fact]
    public async Task CheckHealthAsync_IntegrityRejected_ReturnsUnhealthyWithIntegrityData()
    {
        // Arrange
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var validator = new NistCatalogIntegrityValidator(Mock.Of<ILogger<NistCatalogIntegrityValidator>>());
        using var invalidCatalog = JsonDocument.Parse("""{"catalog":{"uuid":"not-a-uuid"}}""");
        validator.Validate(invalidCatalog);
        var healthCheck = CreateHealthCheck(integrityValidator: validator);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("nist-controls", healthCheck, null, null)
        };

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data["integrityValid"].Should().Be(false);
        result.Data["schemaValid"].Should().Be(false);
        result.Data.Should().ContainKey("integrityViolations");
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesResponseTimeMs()
    {
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var healthCheck = CreateHealthCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("nist-controls", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Data.Should().ContainKey("responseTimeMs");
        ((long)result.Data["responseTimeMs"]).Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesCacheDurationHours()
    {
        _nistServiceMock.Setup(s => s.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("5.2.0");
        _nistServiceMock.Setup(s => s.ValidateControlIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var healthCheck = CreateHealthCheck(new NistControlsOptions { CacheDurationHours = 48 });
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("nist-controls", healthCheck, null, null)
        };

        var result = await healthCheck.CheckHealthAsync(context);

        result.Data.Should().ContainKey("cacheDurationHours");
        result.Data["cacheDurationHours"].Should().Be(48);
    }
}
