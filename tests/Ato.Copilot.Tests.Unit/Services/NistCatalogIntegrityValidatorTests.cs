using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Agents.Compliance.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class NistCatalogIntegrityValidatorTests
{
    private const string CatalogResourceName =
        "Ato.Copilot.Agents.Compliance.Resources.NIST_SP-800-53_rev5_catalog.json";

    private readonly NistCatalogIntegrityValidator _validator = new(
        Mock.Of<ILogger<NistCatalogIntegrityValidator>>());

    [Fact]
    public void Validate_BundledCatalog_ReturnsVerifiedBaseline()
    {
        // Arrange
        using var document = LoadBundledCatalog();

        // Act
        var result = _validator.Validate(document);

        // Assert
        result.IsValid.Should().BeTrue();
        result.SchemaValid.Should().BeTrue();
        result.OscalVersion.Should().Be("1.1.3");
        result.CatalogVersion.Should().Be("5.2.0");
        result.GroupCount.Should().Be(20);
        result.BaseControlCount.Should().Be(324);
        result.EnhancementCount.Should().Be(872);
        result.TotalControlCount.Should().Be(1196);
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Validate_SchemaInvalidCatalog_ReturnsViolation()
    {
        // Arrange
        using var document = JsonDocument.Parse("""{"catalog":{"uuid":"not-a-uuid"}}""");

        // Act
        var result = _validator.Validate(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.SchemaValid.Should().BeFalse();
        result.Violations.Should().Contain(violation => violation.Contains("schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TruncatedCatalog_ReturnsCountViolations()
    {
        // Arrange
        var root = LoadBundledCatalogNode();
        root["catalog"]!["groups"]!.AsArray().RemoveAt(0);
        using var document = JsonDocument.Parse(root.ToJsonString());

        // Act
        var result = _validator.Validate(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.SchemaValid.Should().BeTrue();
        result.GroupCount.Should().Be(19);
        result.Violations.Should().Contain(violation => violation.Contains("20 control families", StringComparison.Ordinal));
        result.TotalControlCount.Should().BeLessThan(1196);
    }

    [Fact]
    public void Validate_UnexpectedOscalVersion_ReturnsVersionViolation()
    {
        // Arrange
        var root = LoadBundledCatalogNode();
        root["catalog"]!["metadata"]!["oscal-version"] = "1.1.2";
        using var document = JsonDocument.Parse(root.ToJsonString());

        // Act
        var result = _validator.Validate(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.SchemaValid.Should().BeTrue();
        result.OscalVersion.Should().Be("1.1.2");
        result.Violations.Should().Contain(violation => violation.Contains("OSCAL version 1.1.3", StringComparison.Ordinal));
    }

    private static JsonDocument LoadBundledCatalog()
    {
        using var stream = OpenBundledCatalog();
        return JsonDocument.Parse(stream);
    }

    private static JsonNode LoadBundledCatalogNode()
    {
        using var stream = OpenBundledCatalog();
        return JsonNode.Parse(stream)!;
    }

    private static Stream OpenBundledCatalog()
    {
        return typeof(NistControlsService).Assembly.GetManifestResourceStream(CatalogResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{CatalogResourceName}' was not found.");
    }
}
