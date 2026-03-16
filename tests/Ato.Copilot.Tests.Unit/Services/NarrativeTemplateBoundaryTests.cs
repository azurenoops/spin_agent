using FluentAssertions;
using Xunit;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Tests.Unit.Services;

public class NarrativeTemplateBoundaryTests
{
    private readonly NarrativeTemplateService _sut = new();

    [Fact]
    public void GenerateCompositeNarrative_SingleMapping_ReturnsSimpleNarrative()
    {
        var mappings = new List<BoundaryMappingContext>
        {
            new("MFA", "Entra ID", "Enforces multi-factor authentication", "Production"),
        };

        var result = _sut.GenerateCompositeNarrative("AC-2", "Account Management", mappings);

        // Should produce the same output as GenerateNarrative (passthrough)
        result.Should().Contain("MFA");
        result.Should().Contain("Entra ID");
        result.Should().Contain("AC-2");
        result.Should().NotContain("through the following capabilities");
        result.Should().NotContain("Within the");
    }

    [Fact]
    public void GenerateCompositeNarrative_MultipleBoundaries_IncludesAllSections()
    {
        var mappings = new List<BoundaryMappingContext>
        {
            new("MFA", "Entra ID", "MFA for all users", null), // org-wide
            new("Smart Card", "PIV Provider", "CAC-based auth", "Production"),
            new("YubiKey", "Yubico", "Hardware token", "Dev/Test"),
        };

        var result = _sut.GenerateCompositeNarrative("IA-2", "Identification and Authentication", mappings);

        result.Should().Contain("through the following capabilities");
        result.Should().Contain("Organization-Wide: MFA using Entra ID");
        result.Should().Contain("Within the Production boundary: Smart Card using PIV Provider");
        result.Should().Contain("Within the Dev/Test boundary: YubiKey using Yubico");
    }

    [Fact]
    public void GenerateCompositeNarrative_OrgWideOnly_WithMultiple_ShowsComposite()
    {
        var mappings = new List<BoundaryMappingContext>
        {
            new("MFA", "Entra ID", "MFA for all users", null),
            new("PAM", "CyberArk", "Privileged access management", null),
        };

        var result = _sut.GenerateCompositeNarrative("AC-2", "Account Management", mappings);

        result.Should().Contain("through the following capabilities");
        result.Should().Contain("Organization-Wide: MFA");
        result.Should().Contain("Organization-Wide: PAM");
    }

    [Fact]
    public void GenerateCompositeNarrative_EmptyMappings_ReturnsEmpty()
    {
        var result = _sut.GenerateCompositeNarrative("AC-2", "Account Management", []);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateCompositeNarrative_IncludesFamilyContext()
    {
        var mappings = new List<BoundaryMappingContext>
        {
            new("Firewall", "Palo Alto", "Network segmentation", null),
            new("WAF", "Azure WAF", "Web app firewall", "DMZ"),
        };

        var result = _sut.GenerateCompositeNarrative("SC-7", "Boundary Protection", mappings);

        // SC family context
        result.Should().Contain("system and communications protection");
    }

    [Fact]
    public void GenerateCompositeNarrative_BoundaryAlphabeticalOrder()
    {
        var mappings = new List<BoundaryMappingContext>
        {
            new("Cap Z", "Vendor Z", "Desc Z", "Zebra"),
            new("Cap A", "Vendor A", "Desc A", "Alpha"),
        };

        var result = _sut.GenerateCompositeNarrative("AC-1", "Policy and Procedures", mappings);

        var alphaPos = result.IndexOf("Within the Alpha boundary", StringComparison.Ordinal);
        var zebraPos = result.IndexOf("Within the Zebra boundary", StringComparison.Ordinal);
        alphaPos.Should().BeLessThan(zebraPos, "boundaries should be sorted alphabetically");
    }
}
