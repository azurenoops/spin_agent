using System.Text;
using System.Xml.Linq;
using Ato.Copilot.Agents.Compliance.Services;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemDesignArtifactTests
{
    [Theory]
    [InlineData("context")]
    [InlineData("boundary")]
    [InlineData("network")]
    [InlineData("data-flow")]
    public void Render_DefaultViewsExcludeSourceOnlyBoxes_AndVersionArchitectureRecipe(string view)
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "Synthetic architecture", "baseline", 7,
            DateTimeOffset.UnixEpoch, "reviewer", null, new string('D', 64),
            [new("system", "Synthetic system", "InBoundary", "System"),
             new("app", "Synthetic application", "InBoundary", "Application"),
             .. new[] { "ProfileSection", "PpsEntry", "InformationType", "LeveragedAuthorization", "MonitoringObservation" }
                 .Select(kind => new SystemDesignDiagramNode(kind, $"SOURCE ONLY {kind}", "Undetermined", kind))],
            [new("flow", "system", "app", "Recorded application exchange", "TCP", "443", "TLS")]);

        // Act
        var artifact = SystemDesignDiagramRenderer.Render(source, view);
        var svg = XDocument.Parse(Encoding.UTF8.GetString(artifact.Content));

        // Assert
        svg.Descendants().Where(e => e.Attribute("data-node-id") != null)
            .Select(e => e.Attribute("data-node-id")!.Value).Should().BeEquivalentTo("system", "app");
        svg.Root!.Value.Should().NotContain("SOURCE ONLY")
            .And.Contain("Synthetic application").And.Contain("Recorded application exchange");
        artifact.Id.Should().Be(new PackageUuidRegistry("system").GetOrCreate("design-diagram",
            $"baseline:7:{view}:2").ToString());
        artifact.Description.Should().Contain("SVG recipe 2");
    }

    [Fact]
    public void Render_DraftMetadata_DoesNotClaimApprovalOrUseApprovalTimestamp()
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "DEMO <system>", "working-hash", 0,
            DateTimeOffset.UnixEpoch, "Not recorded", null, new string('C', 64),
            [new("system", "DEMO node", "Undetermined", "System")], [], "DRAFT / UNAPPROVED (NotStarted)");

        // Act
        var artifact = SystemDesignDiagramRenderer.Render(source, "context");
        var svg = XDocument.Parse(Encoding.UTF8.GetString(artifact.Content));

        // Assert
        artifact.Title.Should().Contain("DRAFT / UNAPPROVED");
        artifact.Description.Should().Contain("working").And.NotContain("baseline timestamp").And.NotContain("reviewer");
        svg.Root!.Value.Should().Contain("DRAFT / UNAPPROVED").And.Contain("NotStarted")
            .And.NotContain("1970-01-01").And.NotContain("Approved baseline");
    }

    [Fact]
    public void Render_IsStableEscapesLabelsAndIncludesApprovedMetadata()
    {
        // Arrange
        var source = new SystemDesignDiagramSource(
            "synthetic-system", "DEMO <system> & architecture", "approved-baseline", 3,
            new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), "synthetic-reviewer", "CUI",
            new string('A', 64),
            [new("node-1", "DEMO <script>alert(1)</script>", "InBoundary", "Application"),
             new("node-2", "DEMO external service", "OutOfBoundary", "ExternalSystem")],
            [new("flow-1", "node-1", "node-2", "Approved exchange", "TCP", "443", "TLS")]);

        // Act
        var first = SystemDesignDiagramRenderer.Render(source, "boundary");
        var second = SystemDesignDiagramRenderer.Render(source, "boundary");
        var document = XDocument.Parse(Encoding.UTF8.GetString(first.Content));

        // Assert
        first.Content.Should().Equal(second.Content);
        first.ContentHash.Should().Be(second.ContentHash);
        first.Id.Should().Be(second.Id);
        document.Descendants().Should().NotContain(x => x.Name.LocalName == "script" || x.Name.LocalName == "foreignObject");
        document.Descendants().Attributes().Should().NotContain(x =>
            x.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
            x.Name.LocalName == "href" || x.Name.LocalName == "src");
        document.Root!.Value.Should().Contain("DEMO <system> & architecture")
            .And.Contain("approved-baseline").And.Contain("synthetic-reviewer")
            .And.Contain("Approved").And.Contain("CUI").And.Contain("Legend")
            .And.Contain("2026-09-30").And.Contain(new string('A', 64));
    }

    [Theory]
    [InlineData("context")]
    [InlineData("boundary")]
    [InlineData("network")]
    [InlineData("data-flow")]
    public void Render_ContainsEveryRecordWithoutTruncatingLargeGraphs(string view)
    {
        // Arrange
        var source = new SystemDesignDiagramSource("synthetic-system", "DEMO large graph", "baseline", 1,
            DateTimeOffset.UnixEpoch, "reviewer", null, new string('B', 64),
            Enumerable.Range(1, 101).Select(i => new SystemDesignDiagramNode(
                $"node-{i}", $"DEMO component {i}", i == 101 ? "Undetermined" : "InBoundary", "Service")).ToArray(),
            [new("flow", "node-1", "node-101", "DEMO final flow", "TCP", "443", "TLS")]);

        // Act
        var result = SystemDesignDiagramRenderer.Render(source, view);
        var document = XDocument.Parse(Encoding.UTF8.GetString(result.Content));

        // Assert
        document.Root!.Value.Should().Contain("DEMO component 101").And.Contain("DEMO final flow");
        document.Descendants().Count(x => x.Attribute("data-node-id") != null).Should().Be(101);
        result.MediaType.Should().Be("image/svg+xml");
    }

    [Fact]
    public void Render_RejectsUnknownViewsAndDanglingEndpoints()
    {
        // Arrange
        var source = new SystemDesignDiagramSource("system", "DEMO", "baseline", 1,
            DateTimeOffset.UnixEpoch, "reviewer", null, new string('B', 64),
            [new("node", "DEMO component", "InBoundary", "Service")],
            [new("flow", "node", "missing", "DEMO exchange", null, null, null)]);

        // Act
        var unknown = () => SystemDesignDiagramRenderer.Render(source, "unknown");
        var dangling = () => SystemDesignDiagramRenderer.Render(source, "data-flow");

        // Assert
        unknown.Should().Throw<ArgumentException>();
        dangling.Should().Throw<InvalidOperationException>().WithMessage("*endpoint*");
    }
}
