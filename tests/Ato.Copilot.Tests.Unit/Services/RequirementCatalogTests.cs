using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public class RequirementCatalogTests
{
    private static string Fixture => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestData", "Requirements", "catalog.json"));

    [Fact]
    public void Parse_PreservesSourceIdentifiersAndExplicitHierarchy()
    {
        // Arrange
        var json = Fixture;

        // Act
        var catalog = RequirementCatalog.Parse(json);

        // Assert
        catalog.Version.Should().Be("test-1");
        catalog.Controls.Should().HaveCount(2);
        var parent = catalog.Controls.Single(x => x.DisplayId == "AC-11");
        parent.Id.Should().Be("parent-original");
        parent.Requirements.Select(x => x.Id).Should().Equal("requirement-alpha", "requirement-beta");
        parent.Requirements[0].Label.Should().Be("a.");
        parent.Parameters.Single().Id.Should().Be("lock-duration");
        var enhancement = catalog.Controls.Single(x => x.DisplayId == "AC-11(1)");
        enhancement.ParentId.Should().Be(parent.Id);
        enhancement.Requirements.Single().Id.Should().Be("concealment-statement");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"uuid\":\"catalog\"}")]
    [InlineData("{\"uuid\":\"catalog\",\"metadata\":{\"version\":\"1\"}}")]
    [InlineData("{\"uuid\":\"catalog\",\"metadata\":{\"version\":\"1\"},\"groups\":{}}")]
    public void Parse_MissingAuthoritativeDetails_RejectsSource(string json)
    {
        // Arrange
        Action act = () => RequirementCatalog.Parse(json);

        // Act / Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_DuplicateStatementIdentifier_RejectsAmbiguousMapping()
    {
        // Arrange
        var json = Fixture.Replace("requirement-beta", "requirement-alpha");

        // Act
        Action act = () => RequirementCatalog.Parse(json);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*duplicate*");
    }

    [Fact]
    public void Parse_DuplicateDisplayLabels_RejectsAmbiguousNavigation()
    {
        // Arrange
        var json = Fixture.Replace("AC-11(1)", "AC-11");

        // Act
        Action act = () => RequirementCatalog.Parse(json);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*duplicate display*");
    }

    [Fact]
    public void Parse_NestedGroupsAndRootControls_PreservesNonNistIdentifiers()
    {
        // Arrange
        const string json = """
            {"uuid":"test","metadata":{"version":"1"},"groups":[{"id":"outer","groups":[
            {"id":"inner","controls":[{"id":"arbitrary","title":"Demo","parts":[
            {"id":"statement","name":"statement","prose":"Text"}]}]}]}],
            "controls":[{"id":"root","title":"Root","parts":[]}]}
            """;

        // Act
        var catalog = RequirementCatalog.Parse(json);

        // Assert
        catalog.Controls.Select(x => x.Id).Should().BeEquivalentTo("arbitrary", "root");
        catalog.Controls.Single(x => x.Id == "arbitrary").Family.Should().Be("inner");
        catalog.Controls.Single(x => x.Id == "root").Requirements.Should().BeEmpty();
    }
}
