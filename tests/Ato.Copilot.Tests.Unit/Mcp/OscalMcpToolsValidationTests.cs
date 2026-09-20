using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Mcp;

public sealed class OscalMcpToolsValidationTests
{
    private const string InvalidOscal = "{\"invalid\":true}";

    [Theory]
    [InlineData("ssp", "ssp")]
    [InlineData("sar", "assessment-results")]
    [InlineData("poam", "poam")]
    public async Task Export_WithSchemaInvalidDocument_BlocksOutput(
        string exportType,
        string schemaModelType)
    {
        // Arrange
        var schemaValidator = new Mock<IOscalSchemaValidationService>();
        schemaValidator
            .Setup(service => service.ValidateAsync(
                InvalidOscal,
                schemaModelType,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalSchemaValidationResult
            {
                IsValid = false,
                ModelType = schemaModelType,
                Violations =
                [
                    new OscalSchemaViolation
                    {
                        JsonPath = "$.metadata.oscal-version",
                        Message = "Required property is missing."
                    }
                ]
            });

        var sut = CreateSut(schemaValidator);

        // Act
        var result = exportType switch
        {
            "ssp" => await sut.OscalExportSsp("system-1"),
            "sar" => await sut.OscalExportSar("system-1"),
            "poam" => await sut.OscalExportPoam("system-1"),
            _ => throw new InvalidOperationException()
        };

        // Assert
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
        response.RootElement.GetProperty("errorCode").GetString()
            .Should().Be("OSCAL_SCHEMA_VALIDATION_FAILED");
        response.RootElement.TryGetProperty("oscalJson", out _).Should().BeFalse();
        schemaValidator.Verify(service => service.ValidateAsync(
            InvalidOscal,
            schemaModelType,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static OscalMcpTools CreateSut(Mock<IOscalSchemaValidationService> schemaValidator)
    {
        var sspExport = new Mock<IOscalSspExportService>();
        sspExport
            .Setup(service => service.ExportAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalExportResult(
                InvalidOscal,
                [],
                new OscalStatistics(0, 0, 0, 0, 0)));

        var sarExport = new Mock<IOscalSarExportService>();
        sarExport
            .Setup(service => service.ExportAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalSarExportResult(InvalidOscal, [], 0, 0, 0));

        var poamExport = new Mock<IOscalPoamExportService>();
        poamExport
            .Setup(service => service.ExportAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OscalPoamExportResult(InvalidOscal, [], 0, 0));

        return new OscalMcpTools(
            sspExport.Object,
            sarExport.Object,
            poamExport.Object,
            Mock.Of<IOscalSspImportService>(),
            schemaValidator.Object,
            Mock.Of<IFedRampSchematronService>(),
            Mock.Of<IEmassBridgeService>(),
            Mock.Of<ILogger<OscalMcpTools>>());
    }
}
