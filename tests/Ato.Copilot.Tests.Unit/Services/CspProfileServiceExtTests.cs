using System.Text.Json;
using FluentAssertions;
using Xunit;
using Ato.Copilot.Mcp.Services;
using static Ato.Copilot.Mcp.Services.CspProfileService;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Tests CspProfile deserialization: services[] format, flat controls[], and mixed.
/// </summary>
public class CspProfileServiceExtTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserialize_ServicesFormat_PopulatesServicesAndFlatControls()
    {
        var json = """
        {
          "profileId": "test-profile",
          "name": "Test Profile",
          "provider": "Azure",
          "baselineLevel": "high",
          "description": "Test",
          "version": "1.0",
          "services": [
            {
              "name": "Service A",
              "category": "Identity",
              "description": "Auth service",
              "controls": [
                { "controlId": "ac-2", "inheritanceType": "Inherited" },
                { "controlId": "ac-3", "inheritanceType": "Shared", "customerResponsibility": "Configure policies" }
              ]
            },
            {
              "name": "Service B",
              "category": "Monitoring",
              "description": "Logging service",
              "controls": [
                { "controlId": "au-2", "inheritanceType": "Inherited" }
              ]
            }
          ]
        }
        """;

        var profile = JsonSerializer.Deserialize<CspProfile>(json, JsonOpts)!;

        profile.Services.Should().HaveCount(2);
        profile.Services[0].Name.Should().Be("Service A");
        profile.Services[0].Controls.Should().HaveCount(2);
        profile.Services[1].Controls.Should().HaveCount(1);

        // After constructor-style flattening, Controls should be populated
        // (Note: the constructor does this at load time in CspProfileService;
        //  here we just verify deserialization and manual flattening)
        if (profile.Services.Count > 0)
        {
            profile.Controls = profile.Services.SelectMany(s => s.Controls).ToList();
        }
        profile.Controls.Should().HaveCount(3);
        profile.Controls[0].ControlId.Should().Be("ac-2");
        profile.Controls[2].ControlId.Should().Be("au-2");
    }

    [Fact]
    public void Deserialize_FlatControlsOnly_NoServices()
    {
        var json = """
        {
          "profileId": "flat-profile",
          "name": "Flat Profile",
          "provider": "AWS",
          "baselineLevel": "moderate",
          "description": "Legacy flat format",
          "version": "1.0",
          "controls": [
            { "controlId": "ac-1", "inheritanceType": "Customer", "customerResponsibility": "Create policy" },
            { "controlId": "ac-2", "inheritanceType": "Inherited" }
          ]
        }
        """;

        var profile = JsonSerializer.Deserialize<CspProfile>(json, JsonOpts)!;

        profile.Services.Should().BeEmpty();
        profile.Controls.Should().HaveCount(2);
        profile.Controls[0].ControlId.Should().Be("ac-1");
        profile.Controls[0].InheritanceType.Should().Be("Customer");
        profile.Controls[0].CustomerResponsibility.Should().Be("Create policy");
    }

    [Fact]
    public void Deserialize_ServicesFormat_ControlsParsedCorrectly()
    {
        var json = """
        {
          "profileId": "detail-test",
          "name": "Detail Test",
          "provider": "Azure",
          "baselineLevel": "high",
          "description": "Test control fields",
          "version": "1.0",
          "services": [
            {
              "name": "Key Vault",
              "category": "Cryptography",
              "description": "Key management",
              "controls": [
                { "controlId": "sc-12", "inheritanceType": "Inherited" },
                { "controlId": "sc-13", "inheritanceType": "Shared", "customerResponsibility": "Select algorithms" }
              ]
            }
          ]
        }
        """;

        var profile = JsonSerializer.Deserialize<CspProfile>(json, JsonOpts)!;

        var controls = profile.Services[0].Controls;
        controls[0].InheritanceType.Should().Be("Inherited");
        controls[0].CustomerResponsibility.Should().BeNullOrEmpty();
        controls[1].InheritanceType.Should().Be("Shared");
        controls[1].CustomerResponsibility.Should().Be("Select algorithms");
    }

    [Fact]
    public void Deserialize_EmptyServicesAndControls_HandlesGracefully()
    {
        var json = """
        {
          "profileId": "empty",
          "name": "Empty Profile",
          "provider": "Test",
          "baselineLevel": "low",
          "description": "No controls",
          "version": "0.1"
        }
        """;

        var profile = JsonSerializer.Deserialize<CspProfile>(json, JsonOpts)!;

        profile.Services.Should().BeEmpty();
        profile.Controls.Should().BeEmpty();
    }

    [Fact]
    public void FlattenLogic_ServicesPopulated_FlattensIntoControls()
    {
        var profile = new CspProfile
        {
            ProfileId = "flatten-test",
            Name = "Flatten Test",
            Provider = "Azure",
            Services = new List<CspService>
            {
                new()
                {
                    Name = "SvcA",
                    Category = "Cat1",
                    Description = "Desc",
                    Controls = new List<ProfileControlMapping>
                    {
                        new() { ControlId = "ac-1", InheritanceType = "Inherited" },
                        new() { ControlId = "ac-2", InheritanceType = "Shared" },
                    }
                },
                new()
                {
                    Name = "SvcB",
                    Category = "Cat2",
                    Description = "Desc2",
                    Controls = new List<ProfileControlMapping>
                    {
                        new() { ControlId = "au-1", InheritanceType = "Customer" },
                    }
                },
            }
        };

        // Simulate the flatten logic from CspProfileService constructor
        if (profile.Services.Count > 0)
        {
            profile.Controls = profile.Services.SelectMany(s => s.Controls).ToList();
        }

        profile.Controls.Should().HaveCount(3);
        profile.Controls.Select(c => c.ControlId).Should().BeEquivalentTo("ac-1", "ac-2", "au-1");
    }

    [Fact]
    public void FlattenLogic_NoServices_KeepsExistingControls()
    {
        var profile = new CspProfile
        {
            ProfileId = "no-flatten",
            Name = "No Flatten",
            Provider = "AWS",
            Controls = new List<ProfileControlMapping>
            {
                new() { ControlId = "ia-1", InheritanceType = "Inherited" },
            }
        };

        // Simulate the flatten logic — should NOT overwrite existing Controls
        if (profile.Services.Count > 0)
        {
            profile.Controls = profile.Services.SelectMany(s => s.Controls).ToList();
        }

        profile.Controls.Should().HaveCount(1);
        profile.Controls[0].ControlId.Should().Be("ia-1");
    }
}
