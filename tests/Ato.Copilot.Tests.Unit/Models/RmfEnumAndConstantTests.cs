using Xunit;
using FluentAssertions;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Unit.Models;

/// <summary>
/// Tests for Feature 015 RMF enums, ComplianceFrameworks helpers,
/// and ComplianceRoles constants added for persona-driven workflows.
/// </summary>
public class RmfEnumAndConstantTests
{
    // ─── RmfStep Enum ───────────────────────────────────────────────────

    [Fact]
    public void RmfPhase_HasSevenSteps()
    {
        Enum.GetValues<RmfPhase>().Should().HaveCount(7);
    }

    [Theory]
    [InlineData(RmfPhase.Prepare, 0)]
    [InlineData(RmfPhase.Categorize, 1)]
    [InlineData(RmfPhase.Select, 2)]
    [InlineData(RmfPhase.Implement, 3)]
    [InlineData(RmfPhase.Assess, 4)]
    [InlineData(RmfPhase.Authorize, 5)]
    [InlineData(RmfPhase.Monitor, 6)]
    public void RmfPhase_EnumValues_AreCorrectlyOrdered(RmfPhase step, int expected)
    {
        ((int)step).Should().Be(expected);
    }

    // ─── SystemType Enum ────────────────────────────────────────────────

    [Fact]
    public void SystemType_HasThreeValues()
    {
        Enum.GetValues<SystemType>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(SystemType.MajorApplication)]
    [InlineData(SystemType.Enclave)]
    [InlineData(SystemType.PlatformIt)]
    public void SystemType_ContainsExpectedValue(SystemType type)
    {
        Enum.IsDefined(type).Should().BeTrue();
    }

    // ─── MissionCriticality Enum ────────────────────────────────────────

    [Fact]
    public void MissionCriticality_HasThreeValues()
    {
        Enum.GetValues<MissionCriticality>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(MissionCriticality.MissionCritical)]
    [InlineData(MissionCriticality.MissionEssential)]
    [InlineData(MissionCriticality.MissionSupport)]
    public void MissionCriticality_ContainsExpectedValue(MissionCriticality criticality)
    {
        Enum.IsDefined(criticality).Should().BeTrue();
    }

    // ─── AzureCloudEnvironment Enum ─────────────────────────────────────

    [Fact]
    public void AzureCloudEnvironment_HasFourValues()
    {
        Enum.GetValues<AzureCloudEnvironment>().Should().HaveCount(4);
    }

    [Theory]
    [InlineData(AzureCloudEnvironment.Commercial)]
    [InlineData(AzureCloudEnvironment.Government)]
    [InlineData(AzureCloudEnvironment.GovernmentAirGappedIl5)]
    [InlineData(AzureCloudEnvironment.GovernmentAirGappedIl6)]
    public void AzureCloudEnvironment_ContainsExpectedValue(AzureCloudEnvironment env)
    {
        Enum.IsDefined(env).Should().BeTrue();
    }

    // ─── ImpactValue Enum ───────────────────────────────────────────────

    [Theory]
    [InlineData(ImpactValue.Low, 0)]
    [InlineData(ImpactValue.Moderate, 1)]
    [InlineData(ImpactValue.High, 2)]
    public void ImpactValue_EnumValues_HaveCorrectIntValues(ImpactValue impact, int expected)
    {
        ((int)impact).Should().Be(expected);
    }

    [Fact]
    public void ImpactValue_HasThreeValues()
    {
        Enum.GetValues<ImpactValue>().Should().HaveCount(3);
    }

    // ─── RmfRole Enum ───────────────────────────────────────────────────

    [Fact]
    public void RmfRole_HasFiveValues()
    {
        Enum.GetValues<RmfRole>().Should().HaveCount(6);
    }

    [Theory]
    [InlineData(RmfRole.SystemOwner)]
    [InlineData(RmfRole.Isso)]
    [InlineData(RmfRole.Issm)]
    [InlineData(RmfRole.Sca)]
    [InlineData(RmfRole.AuthorizingOfficial)]
    [InlineData(RmfRole.MissionOwner)]
    public void RmfRole_ContainsExpectedValue(RmfRole role)
    {
        Enum.IsDefined(role).Should().BeTrue();
    }

    // ─── TailoringAction Enum ───────────────────────────────────────────

    [Fact]
    public void TailoringAction_HasTwoValues()
    {
        Enum.GetValues<TailoringAction>().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(TailoringAction.Added)]
    [InlineData(TailoringAction.Removed)]
    public void TailoringAction_ContainsExpectedValue(TailoringAction action)
    {
        Enum.IsDefined(action).Should().BeTrue();
    }

    // ─── InheritanceType Enum ───────────────────────────────────────────

    [Fact]
    public void InheritanceType_HasThreeValues()
    {
        Enum.GetValues<InheritanceType>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(InheritanceType.Inherited)]
    [InlineData(InheritanceType.Shared)]
    [InlineData(InheritanceType.Customer)]
    public void InheritanceType_ContainsExpectedValue(InheritanceType inheritance)
    {
        Enum.IsDefined(inheritance).Should().BeTrue();
    }

    // ─── ImplementationStatus Enum ──────────────────────────────────────

    [Fact]
    public void ImplementationStatus_HasFourValues()
    {
        Enum.GetValues<ImplementationStatus>().Should().HaveCount(4);
    }

    [Theory]
    [InlineData(ImplementationStatus.Implemented)]
    [InlineData(ImplementationStatus.PartiallyImplemented)]
    [InlineData(ImplementationStatus.Planned)]
    [InlineData(ImplementationStatus.NotApplicable)]
    public void ImplementationStatus_ContainsExpectedValue(ImplementationStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }

    // ─── EffectivenessDetermination Enum ────────────────────────────────

    [Fact]
    public void EffectivenessDetermination_HasTwoValues()
    {
        Enum.GetValues<EffectivenessDetermination>().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(EffectivenessDetermination.Satisfied)]
    [InlineData(EffectivenessDetermination.OtherThanSatisfied)]
    public void EffectivenessDetermination_ContainsExpectedValue(EffectivenessDetermination determination)
    {
        Enum.IsDefined(determination).Should().BeTrue();
    }

    // ─── CatSeverity Enum ───────────────────────────────────────────────

    [Fact]
    public void CatSeverity_HasThreeValues()
    {
        Enum.GetValues<CatSeverity>().Should().HaveCount(3);
    }

    [Theory]
    [InlineData(CatSeverity.CatI)]
    [InlineData(CatSeverity.CatII)]
    [InlineData(CatSeverity.CatIII)]
    public void CatSeverity_ContainsExpectedValue(CatSeverity severity)
    {
        Enum.IsDefined(severity).Should().BeTrue();
    }

    // ─── AuthorizationDecisionType Enum ─────────────────────────────────

    [Fact]
    public void AuthorizationDecisionType_HasFourValues()
    {
        Enum.GetValues<AuthorizationDecisionType>().Should().HaveCount(4);
    }

    [Theory]
    [InlineData(AuthorizationDecisionType.Ato)]
    [InlineData(AuthorizationDecisionType.AtoWithConditions)]
    [InlineData(AuthorizationDecisionType.Iatt)]
    [InlineData(AuthorizationDecisionType.Dato)]
    public void AuthorizationDecisionType_ContainsExpectedValue(AuthorizationDecisionType decision)
    {
        Enum.IsDefined(decision).Should().BeTrue();
    }

    // ─── PoamStatus Enum ────────────────────────────────────────────────

    [Fact]
    public void PoamStatus_HasFourValues()
    {
        Enum.GetValues<PoamStatus>().Should().HaveCount(4);
    }

    [Theory]
    [InlineData(PoamStatus.Ongoing)]
    [InlineData(PoamStatus.Completed)]
    [InlineData(PoamStatus.Delayed)]
    [InlineData(PoamStatus.RiskAccepted)]
    public void PoamStatus_ContainsExpectedValue(PoamStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }

    // ─── ComplianceRoles.AuthorizingOfficial ────────────────────────────

    [Fact]
    public void ComplianceRoles_AuthorizingOfficial_HasCorrectValue()
    {
        ComplianceRoles.AuthorizingOfficial.Should().Be("Compliance.AuthorizingOfficial");
    }

    [Fact]
    public void ComplianceRoles_AuthorizingOfficial_FollowsNamingConvention()
    {
        ComplianceRoles.AuthorizingOfficial.Should().StartWith("Compliance.");
    }

    // ─── ComplianceFrameworks Baseline Counts ───────────────────────────

    [Theory]
    [InlineData("Low", 131)]
    [InlineData("Moderate", 325)]
    [InlineData("High", 421)]
    public void GetBaselineControlCount_ReturnsCorrectCount(string level, int expected)
    {
        ComplianceFrameworks.GetBaselineControlCount(level).Should().Be(expected);
    }

    [Theory]
    [InlineData("low", 131)]
    [InlineData("MODERATE", 325)]
    [InlineData("high", 421)]
    public void GetBaselineControlCount_IsCaseInsensitive(string level, int expected)
    {
        ComplianceFrameworks.GetBaselineControlCount(level).Should().Be(expected);
    }

    [Fact]
    public void GetBaselineControlCount_UnknownLevel_ReturnsZero()
    {
        ComplianceFrameworks.GetBaselineControlCount("Unknown").Should().Be(0);
    }

    // ─── ComplianceFrameworks RMF Step Display Names ────────────────────

    [Theory]
    [InlineData(RmfPhase.Prepare, "Step 0 — Prepare")]
    [InlineData(RmfPhase.Categorize, "Step 1 — Categorize")]
    [InlineData(RmfPhase.Select, "Step 2 — Select")]
    [InlineData(RmfPhase.Implement, "Step 3 — Implement")]
    [InlineData(RmfPhase.Assess, "Step 4 — Assess")]
    [InlineData(RmfPhase.Authorize, "Step 5 — Authorize")]
    [InlineData(RmfPhase.Monitor, "Step 6 — Monitor")]
    public void GetStepDisplayName_ReturnsExpectedFormat(RmfPhase step, string expected)
    {
        ComplianceFrameworks.GetStepDisplayName(step).Should().Be(expected);
    }

    [Theory]
    [InlineData(RmfPhase.Prepare, 0)]
    [InlineData(RmfPhase.Categorize, 1)]
    [InlineData(RmfPhase.Select, 2)]
    [InlineData(RmfPhase.Implement, 3)]
    [InlineData(RmfPhase.Assess, 4)]
    [InlineData(RmfPhase.Authorize, 5)]
    [InlineData(RmfPhase.Monitor, 6)]
    public void GetStepNumber_ReturnsExpectedNumber(RmfPhase step, int expected)
    {
        ComplianceFrameworks.GetStepNumber(step).Should().Be(expected);
    }

    // ─── ComplianceFrameworks FIPS 199 Notation ─────────────────────────

    [Fact]
    public void FormatFips199Notation_ReturnsCorrectFormat()
    {
        var result = ComplianceFrameworks.FormatFips199Notation(
            "MySystem", ImpactValue.Low, ImpactValue.Moderate, ImpactValue.High);

        result.Should().Be("SC MySystem = {(confidentiality, LOW), (integrity, MODERATE), (availability, HIGH)}");
    }

    [Fact]
    public void FormatFips199Notation_AllLow_ReturnsAllLow()
    {
        var result = ComplianceFrameworks.FormatFips199Notation(
            "Test", ImpactValue.Low, ImpactValue.Low, ImpactValue.Low);

        result.Should().Contain("(confidentiality, LOW)")
            .And.Contain("(integrity, LOW)")
            .And.Contain("(availability, LOW)");
    }

    // ─── ComplianceFrameworks Impact Level Derivation ────────────────────

    [Theory]
    [InlineData(ImpactValue.Low, false, null, "IL2")]
    [InlineData(ImpactValue.Moderate, false, null, "IL4")]
    [InlineData(ImpactValue.High, false, null, "IL5")]
    [InlineData(ImpactValue.Low, true, null, "IL2")]
    [InlineData(ImpactValue.Moderate, true, null, "IL4")]
    [InlineData(ImpactValue.High, true, null, "IL5")]
    public void DeriveImpactLevel_NonClassified_ReturnsExpected(
        ImpactValue highWaterMark, bool isNss, string? classified, string expected)
    {
        ComplianceFrameworks.DeriveImpactLevel(highWaterMark, isNss, classified).Should().Be(expected);
    }

    [Theory]
    [InlineData("Secret")]
    [InlineData("TopSecret")]
    [InlineData("SECRET")]
    public void DeriveImpactLevel_Classified_ReturnsIL6(string classified)
    {
        ComplianceFrameworks.DeriveImpactLevel(ImpactValue.High, true, classified).Should().Be("IL6");
    }

    // ─── ComplianceFrameworks Baseline Level Derivation ──────────────────

    [Theory]
    [InlineData(ImpactValue.Low, "Low")]
    [InlineData(ImpactValue.Moderate, "Moderate")]
    [InlineData(ImpactValue.High, "High")]
    public void DeriveBaselineLevel_ReturnsExpected(ImpactValue highWaterMark, string expected)
    {
        ComplianceFrameworks.DeriveBaselineLevel(highWaterMark).Should().Be(expected);
    }

    // ─── ComplianceFrameworks Control Family Names ──────────────────────

    [Theory]
    [InlineData("AC", "Access Control")]
    [InlineData("AU", "Audit and Accountability")]
    [InlineData("CA", "Assessment, Authorization, and Monitoring")]
    [InlineData("CM", "Configuration Management")]
    [InlineData("IA", "Identification and Authentication")]
    [InlineData("IR", "Incident Response")]
    [InlineData("SC", "System and Communications Protection")]
    [InlineData("SI", "System and Information Integrity")]
    [InlineData("SR", "Supply Chain Risk Management")]
    public void ControlFamilyNames_ContainsExpectedFamilies(string key, string expectedName)
    {
        ComplianceFrameworks.ControlFamilyNames.Should().ContainKey(key);
        ComplianceFrameworks.ControlFamilyNames[key].Should().Be(expectedName);
    }

    [Fact]
    public void ControlFamilyNames_Has20Families()
    {
        ComplianceFrameworks.ControlFamilyNames.Should().HaveCount(20);
    }

    // ─── ComplianceFrameworks ExtractControlFamily ──────────────────────

    [Theory]
    [InlineData("AC-1", "AC")]
    [InlineData("AU-6(3)", "AU")]
    [InlineData("SC-7", "SC")]
    [InlineData("SI-4(14)", "SI")]
    [InlineData("RA-5", "RA")]
    [InlineData("CM-2(7)", "CM")]
    public void ExtractControlFamily_ReturnsCorrectPrefix(string controlId, string expected)
    {
        ComplianceFrameworks.ExtractControlFamily(controlId).Should().Be(expected);
    }

    [Fact]
    public void ExtractControlFamily_EmptyInput_ReturnsEmpty()
    {
        ComplianceFrameworks.ExtractControlFamily("").Should().BeEmpty();
    }

    [Theory]
    [InlineData("INVALID", "INVALID")]
    [InlineData("A", "A")]
    public void ExtractControlFamily_NoDash_ReturnsFullString(string controlId, string expected)
    {
        ComplianceFrameworks.ExtractControlFamily(controlId).Should().Be(expected);
    }
}
