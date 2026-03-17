using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

/// <summary>
/// Unit tests for OscalSspExportService OSCAL 1.1.2 builders (Feature 022 — T036).
/// Tests internal static builder methods directly for isolation.
/// </summary>
public class OscalSspExportServiceTests
{
    // ─── BuildMetadata ───────────────────────────────────────────────────────

    [Fact]
    public void BuildMetadata_WithRoles_IncludesRolesPartiesResponsibleParties()
    {
        var system = MakeSystem();
        var roles = new List<RmfRoleAssignment>
        {
            MakeRole(RmfRole.AuthorizingOfficial, "u1", "Alice Adams"),
            MakeRole(RmfRole.Isso, "u2", "Bob Baker")
        };
        var partyMap = new Dictionary<string, string> { ["u1"] = "uuid-a", ["u2"] = "uuid-b" };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildMetadata(system, roles, partyMap, warnings);

        result["title"].Should().Be("Test System System Security Plan");
        result["oscal-version"].Should().Be("1.1.2");
        result["version"].Should().Be("1.0");

        var oscalRoles = result["roles"] as List<Dictionary<string, object>>;
        oscalRoles.Should().NotBeNull();
        oscalRoles!.Should().Contain(r => (string)r["id"] == "authorizing-official");
        oscalRoles.Should().Contain(r => (string)r["id"] == "information-system-security-officer");

        var parties = result["parties"] as List<Dictionary<string, object>>;
        parties.Should().NotBeNull();
        parties!.Should().HaveCount(2);
        parties.Should().Contain(p => (string)p["uuid"] == "uuid-a" && (string)p["name"] == "Alice Adams");

        var rp = result["responsible-parties"] as List<Dictionary<string, object>>;
        rp.Should().NotBeNull();
        rp!.Should().Contain(r => (string)r["role-id"] == "authorizing-official");

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void BuildMetadata_NoRoles_EmptyArraysAndWarning()
    {
        var system = MakeSystem();
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildMetadata(
            system, new List<RmfRoleAssignment>(), new Dictionary<string, string>(), warnings);

        result["roles"].Should().BeEquivalentTo(Array.Empty<object>());
        result["parties"].Should().BeEquivalentTo(Array.Empty<object>());
        result["responsible-parties"].Should().BeEquivalentTo(Array.Empty<object>());
        warnings.Should().ContainSingle().Which.Should().Contain("No RMF role assignments");
    }

    // ─── BuildImportProfile ──────────────────────────────────────────────────

    [Theory]
    [InlineData("low", OscalSspExportService.ProfileUriLow)]
    [InlineData("moderate", OscalSspExportService.ProfileUriModerate)]
    [InlineData("high", OscalSspExportService.ProfileUriHigh)]
    [InlineData("Low", OscalSspExportService.ProfileUriLow)]
    [InlineData("Moderate", OscalSspExportService.ProfileUriModerate)]
    public void BuildImportProfile_BaselineLevel_CorrectUri(string level, string expectedUri)
    {
        var baseline = new ControlBaseline
        {
            Id = "b1",
            RegisteredSystemId = "sys1",
            BaselineLevel = level
        };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildImportProfile(baseline, warnings);

        result["href"].Should().Be(expectedUri);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void BuildImportProfile_NoBaseline_OmitsHrefWithWarning()
    {
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildImportProfile(null, warnings);

        result.Should().NotContainKey("href");
        warnings.Should().ContainSingle().Which.Should().Contain("No control baseline");
    }

    [Fact]
    public void BuildImportProfile_UnrecognizedLevel_OmitsHrefWithWarning()
    {
        var baseline = new ControlBaseline
        {
            Id = "b1",
            RegisteredSystemId = "sys1",
            BaselineLevel = "unknown"
        };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildImportProfile(baseline, warnings);

        result.Should().NotContainKey("href");
        warnings.Should().ContainSingle().Which.Should().Contain("Unrecognized baseline level");
    }

    // ─── BuildSystemCharacteristics ──────────────────────────────────────────

    [Fact]
    public void BuildSystemCharacteristics_WithCategorization_IncludesImpactLevels()
    {
        var system = MakeSystem();
        var categorization = new SecurityCategorization
        {
            Id = "sc1",
            RegisteredSystemId = "sys1",
            InformationTypes = new List<InformationType>
            {
                new()
                {
                    Id = "it1",
                    SecurityCategorizationId = "sc1",
                    Name = "Financial Data",
                    Sp80060Id = "D.5.1",
                    ConfidentialityImpact = ImpactValue.Moderate,
                    IntegrityImpact = ImpactValue.Moderate,
                    AvailabilityImpact = ImpactValue.Low
                }
            }
        };
        var sectionMap = new Dictionary<int, SspSection>();
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildSystemCharacteristics(
            system, categorization, sectionMap, warnings);

        result["system-name"].Should().Be("Test System");
        result["system-name-short"].Should().Be("TS");
        result["security-sensitivity-level"].Should().Be("moderate");

        var impactLevel = result["security-impact-level"] as Dictionary<string, string>;
        impactLevel.Should().NotBeNull();
        impactLevel!["security-objective-confidentiality"].Should().Be("moderate");
        impactLevel["security-objective-integrity"].Should().Be("moderate");
        impactLevel["security-objective-availability"].Should().Be("low");

        result.Should().ContainKey("system-information");
        result.Should().ContainKey("authorization-boundary");

        var status = result["status"] as Dictionary<string, string>;
        status.Should().NotBeNull();
        status!["state"].Should().Be("operational");
    }

    [Fact]
    public void BuildSystemCharacteristics_NoCategorization_PlaceholderWithWarning()
    {
        var system = MakeSystem();
        system.OperationalStatus = null;
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildSystemCharacteristics(
            system, null, new Dictionary<int, SspSection>(), warnings);

        result["security-sensitivity-level"].Should().Be("not-yet-determined");

        var impactLevel = result["security-impact-level"] as Dictionary<string, string>;
        impactLevel!["security-objective-confidentiality"].Should().Be("low");

        warnings.Should().ContainSingle().Which.Should().Contain("No security categorization");
    }

    [Fact]
    public void BuildSystemCharacteristics_WithSections_IncludesBoundaryAndNetworkArch()
    {
        var system = MakeSystem();
        var sectionMap = new Dictionary<int, SspSection>
        {
            [6] = new() { Id = "s6", SectionNumber = 6, RegisteredSystemId = "sys1", Content = "Network architecture description" },
            [7] = new() { Id = "s7", SectionNumber = 7, RegisteredSystemId = "sys1", Content = "Data flow description" },
            [11] = new() { Id = "s11", SectionNumber = 11, RegisteredSystemId = "sys1", Content = "Boundary description" }
        };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildSystemCharacteristics(
            system, null, sectionMap, warnings);

        var boundary = result["authorization-boundary"] as Dictionary<string, string>;
        boundary!["description"].Should().Be("Boundary description");

        var netArch = result["network-architecture"] as Dictionary<string, string>;
        netArch!["description"].Should().Be("Network architecture description");

        var dataFlow = result["data-flow"] as Dictionary<string, string>;
        dataFlow!["description"].Should().Be("Data flow description");
    }

    [Theory]
    [InlineData(OperationalStatus.Operational, "operational")]
    [InlineData(OperationalStatus.UnderDevelopment, "under-development")]
    [InlineData(OperationalStatus.Disposed, "disposition")]
    [InlineData(OperationalStatus.MajorModification, "under-major-modification")]
    public void BuildSystemCharacteristics_OperationalStatusMapping(OperationalStatus status, string expected)
    {
        var system = MakeSystem();
        system.OperationalStatus = status;
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildSystemCharacteristics(
            system, null, new Dictionary<int, SspSection>(), warnings);

        var statusObj = result["status"] as Dictionary<string, string>;
        statusObj!["state"].Should().Be(expected);
    }

    // ─── BuildSystemImplementation ───────────────────────────────────────────

    [Fact]
    public void BuildSystemImplementation_WithComponentsAndUsers_CorrectStructure()
    {
        var boundaries = new List<AuthorizationBoundary>
        {
            new()
            {
                Id = "ab1",
                RegisteredSystemId = "sys1",
                ResourceId = "/subscriptions/sub1/vm1",
                ResourceType = "Microsoft.Compute/virtualMachines",
                ResourceName = "AppServer1",
                IsInBoundary = true
            }
        };
        var roles = new List<RmfRoleAssignment>
        {
            MakeRole(RmfRole.SystemOwner, "u1", "Jane Doe")
        };
        var componentMap = new Dictionary<string, string> { ["/subscriptions/sub1/vm1"] = "comp-uuid-1" };
        var partyMap = new Dictionary<string, string> { ["u1"] = "party-uuid-1" };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildSystemImplementation(
            boundaries, roles, null, componentMap, partyMap, warnings);

        // Users
        var users = result["users"] as List<Dictionary<string, object>>;
        users.Should().HaveCount(1);
        var user = users![0];
        user["uuid"].Should().Be("party-uuid-1");
        var roleIds = user["role-ids"] as string[];
        roleIds.Should().Contain("system-owner");

        // Components
        var components = result["components"] as List<Dictionary<string, object>>;
        components.Should().HaveCount(1);
        components![0]["uuid"].Should().Be("comp-uuid-1");
        components[0]["type"].Should().Be("this-system"); // virtualMachines maps to this-system
        components[0]["title"].Should().Be("AppServer1");

        // Inventory items
        var inventory = result["inventory-items"] as List<Dictionary<string, object>>;
        inventory.Should().HaveCount(1);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void BuildSystemImplementation_NoBoundaries_EmptyComponentsAndInventory()
    {
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildSystemImplementation(
            new List<AuthorizationBoundary>(),
            new List<RmfRoleAssignment>(),
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            warnings);

        result["components"].Should().BeEquivalentTo(Array.Empty<object>());
        result["inventory-items"].Should().BeEquivalentTo(Array.Empty<object>());
        result["users"].Should().BeEquivalentTo(Array.Empty<object>());
    }

    [Fact]
    public void BuildSystemImplementation_WithInheritanceProviders_IncludesLeveragedAuthorizations()
    {
        var baseline = new ControlBaseline
        {
            Id = "b1",
            RegisteredSystemId = "sys1",
            BaselineLevel = "moderate",
            Inheritances = new List<ControlInheritance>
            {
                new()
                {
                    Id = "inh1",
                    ControlBaselineId = "b1",
                    ControlId = "AC-1",
                    InheritanceType = InheritanceType.Inherited,
                    Provider = "AWS GovCloud"
                }
            }
        };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildSystemImplementation(
            new List<AuthorizationBoundary>(),
            new List<RmfRoleAssignment>(),
            baseline,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            warnings);

        result.Should().ContainKey("leveraged-authorizations");
        var leveraged = result["leveraged-authorizations"] as List<Dictionary<string, object>>;
        leveraged.Should().HaveCount(1);
        leveraged![0]["title"].Should().Be("AWS GovCloud FedRAMP Authorization");
    }

    // ─── BuildControlImplementation ──────────────────────────────────────────

    [Fact]
    public void BuildControlImplementation_WithImplementations_CreatesRequirements()
    {
        var system = MakeSystem();
        var implementations = new List<ControlImplementation>
        {
            new()
            {
                Id = "ci1",
                RegisteredSystemId = "sys1",
                ControlId = "AC-1",
                Narrative = "Access control policy established.",
                ImplementationStatus = ImplementationStatus.Implemented
            },
            new()
            {
                Id = "ci2",
                RegisteredSystemId = "sys1",
                ControlId = "AU-2",
                Narrative = "Audit events configured.",
                ImplementationStatus = ImplementationStatus.PartiallyImplemented
            }
        };
        var componentMap = new Dictionary<string, string> { ["res1"] = "comp-uuid-1" };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildControlImplementation(
            system, implementations, null, componentMap, new List<Deviation>(), warnings);

        result["description"].Should().Be("Control implementation narratives for Test System.");

        var reqs = result["implemented-requirements"] as List<Dictionary<string, object>>;
        reqs.Should().HaveCount(2);

        var ac1 = reqs!.First(r => (string)r["control-id"] == "ac-1");
        ac1["description"].Should().Be("Access control policy established.");
        var ac1Props = ac1["props"] as Dictionary<string, string>[];
        ac1Props![0]["value"].Should().Be("implemented");

        var au2 = reqs.First(r => (string)r["control-id"] == "au-2");
        var au2Props = au2["props"] as Dictionary<string, string>[];
        au2Props![0]["value"].Should().Be("partial");

        // by-components cross-refs present when componentMap is not empty
        ac1.Should().ContainKey("by-components");

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void BuildControlImplementation_NoImplementations_EmptyWithWarning()
    {
        var system = MakeSystem();
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildControlImplementation(
            system, new List<ControlImplementation>(), null,
            new Dictionary<string, string>(), new List<Deviation>(), warnings);

        result["implemented-requirements"].Should().BeEquivalentTo(Array.Empty<object>());
        warnings.Should().ContainSingle().Which.Should().Contain("No control implementation narratives");
    }

    [Fact]
    public void BuildControlImplementation_WithInheritance_IncludesResponsibleRoles()
    {
        var system = MakeSystem();
        var implementations = new List<ControlImplementation>
        {
            new()
            {
                Id = "ci1",
                RegisteredSystemId = "sys1",
                ControlId = "AC-1",
                Narrative = "Inherited from provider",
                ImplementationStatus = ImplementationStatus.Implemented
            }
        };
        var baseline = new ControlBaseline
        {
            Id = "b1",
            RegisteredSystemId = "sys1",
            BaselineLevel = "moderate",
            Inheritances = new List<ControlInheritance>
            {
                new()
                {
                    Id = "inh1",
                    ControlBaselineId = "b1",
                    ControlId = "AC-1",
                    InheritanceType = InheritanceType.Inherited,
                    Provider = "Azure"
                }
            }
        };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildControlImplementation(
            system, implementations, baseline,
            new Dictionary<string, string>(), new List<Deviation>(), warnings);

        var reqs = result["implemented-requirements"] as List<Dictionary<string, object>>;
        reqs.Should().HaveCount(1);
        reqs![0].Should().ContainKey("responsible-roles");

        var respRoles = reqs[0]["responsible-roles"] as Dictionary<string, string>[];
        respRoles![0]["role-id"].Should().Be("provider");
    }

    // ─── BuildBackMatter ─────────────────────────────────────────────────────

    [Fact]
    public void BuildBackMatter_WithInterconnectionsAndContingencyPlan_ResourcesPresent()
    {
        var interconnections = new List<SystemInterconnection>
        {
            new()
            {
                Id = "ic1",
                RegisteredSystemId = "sys1",
                TargetSystemName = "Partner System",
                InterconnectionType = InterconnectionType.Direct,
                Status = InterconnectionStatus.Active,
                Agreements = new List<InterconnectionAgreement>
                {
                    new()
                    {
                        Id = "a1",
                        SystemInterconnectionId = "ic1",
                        Title = "ISA Agreement",
                        AgreementType = AgreementType.Isa,
                        DocumentReference = "https://docs.example.com/isa.pdf"
                    }
                }
            }
        };
        var contingencyPlan = new ContingencyPlanReference
        {
            Id = "cp1",
            RegisteredSystemId = "sys1",
            DocumentTitle = "ISCP",
            DocumentLocation = "https://docs.example.com/iscp.pdf",
            DocumentVersion = "2.0"
        };
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildBackMatter(interconnections, contingencyPlan, new List<Deviation>(), warnings);

        var resources = result["resources"] as List<Dictionary<string, object>>;
        resources.Should().HaveCount(2);

        // ISA resource
        var isa = resources!.First(r => ((string)r["title"]).Contains("ISA"));
        isa["description"].Should().BeOfType<string>().Which.Should().Contain("Partner System");
        var isaRlinks = isa["rlinks"] as Dictionary<string, string>[];
        isaRlinks![0]["href"].Should().Be("https://docs.example.com/isa.pdf");

        // Contingency plan resource
        var cp = resources.First(r => (string)r["title"] == "ISCP");
        cp["description"].Should().BeOfType<string>().Which.Should().Contain("2.0");
        var cpRlinks = cp["rlinks"] as Dictionary<string, string>[];
        cpRlinks![0]["href"].Should().Be("https://docs.example.com/iscp.pdf");
    }

    [Fact]
    public void BuildBackMatter_NoResources_EmptyList()
    {
        var warnings = new List<string>();

        var result = OscalSspExportService.BuildBackMatter(
            new List<SystemInterconnection>(), null, new List<Deviation>(), warnings);

        var resources = result["resources"] as List<Dictionary<string, object>>;
        resources.Should().BeEmpty();
    }

    // ─── OSCAL Version Constant ──────────────────────────────────────────────

    [Fact]
    public void OscalVersion_Is_1_1_2()
    {
        OscalSspExportService.OscalVersion.Should().Be("1.1.2");
    }

    // ─── Profile URI Constants ───────────────────────────────────────────────

    [Fact]
    public void ProfileUriConstants_AreValidUsnistgovUrls()
    {
        OscalSspExportService.ProfileUriLow.Should().Contain("LOW-baseline_profile.json");
        OscalSspExportService.ProfileUriModerate.Should().Contain("MODERATE-baseline_profile.json");
        OscalSspExportService.ProfileUriHigh.Should().Contain("HIGH-baseline_profile.json");
    }

    // ─── JSON Kebab-Case ─────────────────────────────────────────────────────

    [Fact]
    public void BuildMetadata_SerializesWithKebabCase()
    {
        var system = MakeSystem();
        var roles = new List<RmfRoleAssignment>
        {
            MakeRole(RmfRole.SystemOwner, "u1", "Test User")
        };
        var partyMap = new Dictionary<string, string> { ["u1"] = "uuid-1" };
        var warnings = new List<string>();

        var metadata = OscalSspExportService.BuildMetadata(system, roles, partyMap, warnings);

        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
            WriteIndented = false
        };
        var json = JsonSerializer.Serialize(metadata, opts);

        // Verify kebab-case is applied by the serializer options
        json.Should().Contain("oscal-version");
        json.Should().Contain("1.1.2");
        json.Should().Contain("responsible-parties");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static RegisteredSystem MakeSystem() => new()
    {
        Id = "sys1",
        Name = "Test System",
        Acronym = "TS",
        Description = "A test system",
        SystemType = SystemType.MajorApplication,
        OperationalStatus = OperationalStatus.Operational,
        MissionCriticality = MissionCriticality.MissionEssential,
        DitprId = "DITPR-001",
        EmassId = "eMASS-001"
    };

    private static RmfRoleAssignment MakeRole(RmfRole role, string userId, string displayName) => new()
    {
        Id = $"role-{userId}",
        RegisteredSystemId = "sys1",
        RmfRole = role,
        UserId = userId,
        UserDisplayName = displayName,
        IsActive = true
    };
}
