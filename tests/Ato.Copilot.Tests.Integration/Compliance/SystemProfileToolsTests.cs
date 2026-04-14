using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

/// <summary>
/// Integration tests for Feature 046 — System Profile MCP Tools.
/// T019: End-to-end tool execution through real service + in-memory EF Core.
/// Covers all 7 tools: get profile, save section, submit/withdraw, review,
/// batch approve, completeness, and save business context.
/// </summary>
public class SystemProfileToolsTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;

    private readonly ComplianceGetSystemProfileTool _getProfileTool;
    private readonly ComplianceSaveProfileSectionTool _saveSectionTool;
    private readonly ComplianceSubmitProfileSectionTool _submitTool;
    private readonly ComplianceReviewProfileSectionTool _reviewTool;
    private readonly ComplianceBatchApproveProfileTool _batchApproveTool;
    private readonly ComplianceGetProfileCompletenessTool _completenessTool;
    private readonly ComplianceSaveBusinessContextTool _businessContextTool;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private const string MoUserId = "mo-user";
    private const string IssmUserId = "issm-user";

    public SystemProfileToolsTests()
    {
        var dbName = $"ProfileToolsInt_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var profileService = new SystemProfileService(
            scopeFactory, Mock.Of<ILogger<SystemProfileService>>());

        _getProfileTool = new ComplianceGetSystemProfileTool(
            profileService, Mock.Of<ILogger<ComplianceGetSystemProfileTool>>());
        _saveSectionTool = new ComplianceSaveProfileSectionTool(
            profileService, Mock.Of<ILogger<ComplianceSaveProfileSectionTool>>());
        _submitTool = new ComplianceSubmitProfileSectionTool(
            profileService, Mock.Of<ILogger<ComplianceSubmitProfileSectionTool>>());
        _reviewTool = new ComplianceReviewProfileSectionTool(
            profileService, Mock.Of<ILogger<ComplianceReviewProfileSectionTool>>());
        _batchApproveTool = new ComplianceBatchApproveProfileTool(
            profileService, Mock.Of<ILogger<ComplianceBatchApproveProfileTool>>());
        _completenessTool = new ComplianceGetProfileCompletenessTool(
            profileService, Mock.Of<ILogger<ComplianceGetProfileCompletenessTool>>());
        _businessContextTool = new ComplianceSaveBusinessContextTool(
            profileService, Mock.Of<ILogger<ComplianceSaveBusinessContextTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    // ─── Seed Helpers ────────────────────────────────────────────────────────

    private async Task<RegisteredSystem> SeedSystemWithRolesAsync()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Profile Integration Test System",
            Acronym = "PITS",
            Description = "Integration test system",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "test-setup"
        };
        _db.RegisteredSystems.Add(system);

        _db.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = system.Id,
            UserId = MoUserId,
            UserDisplayName = "Mission Owner",
            RmfRole = RmfRole.MissionOwner,
            AssignedBy = "test-setup",
            IsActive = true
        });
        _db.RmfRoleAssignments.Add(new RmfRoleAssignment
        {
            RegisteredSystemId = system.Id,
            UserId = IssmUserId,
            UserDisplayName = "ISSM User",
            RmfRole = RmfRole.Issm,
            AssignedBy = "test-setup",
            IsActive = true
        });

        await _db.SaveChangesAsync();
        return system;
    }

    private static string GetStatus(JsonDocument doc) =>
        doc.RootElement.GetProperty("status").GetString()!;

    private static string GetErrorCode(JsonDocument doc) =>
        doc.RootElement.GetProperty("errorCode").GetString()!;

    private static JsonElement GetData(JsonDocument doc) =>
        doc.RootElement.GetProperty("data");

    // ═══════════════════════════════════════════════════════════════════════
    // Tool 1: compliance_get_system_profile
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetProfile_EmptySystem_Returns6NotStartedEntries()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _getProfileTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");

        var data = GetData(doc);
        data.GetProperty("systemId").GetString().Should().Be(system.Id);

        var sections = data.GetProperty("sections");
        sections.GetArrayLength().Should().Be(6);

        // All sections should be NotStarted
        foreach (var section in sections.EnumerateArray())
        {
            section.GetProperty("governanceStatus").GetString().Should().Be("NotStarted");
        }
    }

    [Fact]
    public async Task GetProfile_WithMissionOwner_IncludesInfo()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _getProfileTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id
        });

        var doc = JsonDocument.Parse(result);
        var data = GetData(doc);
        var mo = data.GetProperty("missionOwner");
        mo.GetProperty("userId").GetString().Should().Be(MoUserId);
        mo.GetProperty("displayName").GetString().Should().Be("Mission Owner");
    }

    [Fact]
    public async Task GetProfile_SystemNotFound_ReturnsError()
    {
        var result = await _getProfileTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "nonexistent-id"
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("SYSTEM_NOT_FOUND");
    }

    [Fact]
    public async Task GetProfile_MissingSystemId_ReturnsError()
    {
        var result = await _getProfileTool.ExecuteAsync(new Dictionary<string, object?>());

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("INVALID_INPUT");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tool 2: compliance_save_profile_section
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveSection_HappyPath_CreatesDraft()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "MissionAndPurpose",
            ["content"] = "{\"mission\":\"Protect national defense data\"}",
            ["user_id"] = MoUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");

        var data = GetData(doc);
        data.GetProperty("sectionType").GetString().Should().Be("MissionAndPurpose");
        data.GetProperty("governanceStatus").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task SaveSection_InvalidSectionType_ReturnsError()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "BogusSection",
            ["content"] = "{}",
            ["user_id"] = MoUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("INVALID_INPUT");
    }

    [Fact]
    public async Task SaveSection_Unauthorized_ReturnsError()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "MissionAndPurpose",
            ["content"] = "{}",
            ["user_id"] = "random-user"
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("UNAUTHORIZED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tool 3: compliance_submit_profile_section (submit + withdraw)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Submit_HappyPath_TransitionsToUnderReview()
    {
        var system = await SeedSystemWithRolesAsync();

        // First save a draft
        await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "MissionAndPurpose",
            ["content"] = "{\"mission\":\"test\"}",
            ["user_id"] = MoUserId
        });

        // Submit
        var result = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_types"] = "MissionAndPurpose",
            ["user_id"] = MoUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");

        var data = GetData(doc);
        var submitted = data.GetProperty("submittedSections");
        submitted.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Submit_NoSections_ReturnsError()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["user_id"] = MoUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("NO_SUBMITTABLE_SECTIONS");
    }

    [Fact]
    public async Task Withdraw_HappyPath_TransitionsToDraft()
    {
        var system = await SeedSystemWithRolesAsync();

        // Save and submit
        await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "DataTypes",
            ["content"] = "{\"types\":\"PII\"}",
            ["user_id"] = MoUserId
        });
        await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_types"] = "DataTypes",
            ["user_id"] = MoUserId
        });

        // Withdraw
        var result = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["action"] = "withdraw",
            ["section_types"] = "DataTypes",
            ["user_id"] = MoUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");

        var data = GetData(doc);
        data.GetProperty("withdrawnSections").GetArrayLength().Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tool 4: compliance_review_profile_section (approve + reject)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Review_Approve_SetsApproved()
    {
        var system = await SeedSystemWithRolesAsync();

        // Save → submit
        await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "UsersAndAccess",
            ["content"] = "{\"users\":\"admins\"}",
            ["user_id"] = MoUserId
        });
        await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_types"] = "UsersAndAccess",
            ["user_id"] = MoUserId
        });

        // Approve
        var result = await _reviewTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "UsersAndAccess",
            ["decision"] = "approve",
            ["user_id"] = IssmUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");
        GetData(doc).GetProperty("newStatus").GetString().Should().Be("Approved");
    }

    [Fact]
    public async Task Review_RequestRevision_SetsNeedsRevision()
    {
        var system = await SeedSystemWithRolesAsync();

        await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "EnvironmentAndDeployment",
            ["content"] = "{\"env\":\"Azure\"}",
            ["user_id"] = MoUserId
        });
        await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_types"] = "EnvironmentAndDeployment",
            ["user_id"] = MoUserId
        });

        var result = await _reviewTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "EnvironmentAndDeployment",
            ["decision"] = "request_revision",
            ["comments"] = "Add DR info",
            ["user_id"] = IssmUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");
        GetData(doc).GetProperty("newStatus").GetString().Should().Be("NeedsRevision");
    }

    [Fact]
    public async Task Review_InvalidDecision_ReturnsError()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _reviewTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "MissionAndPurpose",
            ["decision"] = "maybe",
            ["user_id"] = IssmUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("INVALID_INPUT");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tool 5: compliance_batch_approve_profile
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BatchApprove_ApprovesAllUnderReview()
    {
        var system = await SeedSystemWithRolesAsync();

        // Save and submit two sections
        foreach (var type in new[] { "MissionAndPurpose", "DataTypes" })
        {
            await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["system_id"] = system.Id,
                ["section_type"] = type,
                ["content"] = $"{{\"test\":\"{type}\"}}",
                ["user_id"] = MoUserId
            });
        }
        await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["user_id"] = MoUserId
        });

        // Batch approve
        var result = await _batchApproveTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["user_id"] = IssmUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");
        GetData(doc).GetProperty("approvedCount").GetInt32().Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tool 6: compliance_get_profile_completeness
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Completeness_EmptyProfile_0Percent()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _completenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");

        var data = GetData(doc);
        data.GetProperty("totalSections").GetInt32().Should().Be(5);
        data.GetProperty("approvedPercentage").GetInt32().Should().Be(0);
        data.GetProperty("isProfileComplete").GetBoolean().Should().BeFalse();
        data.GetProperty("missionOwnerAssigned").GetBoolean().Should().BeTrue();
        data.GetProperty("missionOwnerName").GetString().Should().Be("Mission Owner");
    }

    [Fact]
    public async Task Completeness_SystemNotFound_ReturnsError()
    {
        var result = await _completenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = "nonexistent"
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("SYSTEM_NOT_FOUND");
    }

    [Fact]
    public async Task Completeness_MissionOwnerCanAccess_FR017()
    {
        var system = await SeedSystemWithRolesAsync();

        // The completeness tool doesn't require any specific role — verify MO can call it
        var result = await _completenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");
    }

    [Fact]
    public async Task GetProfile_MissionOwnerCanAccess_FR017()
    {
        var system = await SeedSystemWithRolesAsync();

        // The get_system_profile tool doesn't require any specific role — verify MO can call it
        var result = await _getProfileTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tool 7: compliance_save_business_context
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveBusinessContext_HappyPath()
    {
        var system = await SeedSystemWithRolesAsync();
        _db.ControlImplementations.Add(new ControlImplementation
        {
            RegisteredSystemId = system.Id,
            ControlId = "AC-1",
            ImplementationStatus = ImplementationStatus.Planned
        });
        await _db.SaveChangesAsync();

        var result = await _businessContextTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["control_id"] = "AC-1",
            ["content"] = "Our access control procedures ensure...",
            ["user_id"] = MoUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("success");
        GetData(doc).GetProperty("controlId").GetString().Should().Be("AC-1");
        GetData(doc).GetProperty("authoredBy").GetString().Should().Be(MoUserId);
    }

    [Fact]
    public async Task SaveBusinessContext_ControlNotFound_ReturnsError()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _businessContextTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["control_id"] = "NONEXISTENT-1",
            ["content"] = "test",
            ["user_id"] = MoUserId
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("CONTROL_NOT_FOUND");
    }

    [Fact]
    public async Task SaveBusinessContext_MissingContent_ReturnsError()
    {
        var system = await SeedSystemWithRolesAsync();

        var result = await _businessContextTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["control_id"] = "AC-1"
        });

        var doc = JsonDocument.Parse(result);
        GetStatus(doc).Should().Be("error");
        GetErrorCode(doc).Should().Be("INVALID_INPUT");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End-to-end: Full lifecycle through tools
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullLifecycle_SaveSubmitReviewCompleteness()
    {
        var system = await SeedSystemWithRolesAsync();

        // 1. Save all 5 mandatory sections
        var mandatorySections = new[]
        {
            "MissionAndPurpose", "UsersAndAccess", "EnvironmentAndDeployment",
            "DataTypes", "PortsProtocolsAndServices"
        };

        foreach (var type in mandatorySections)
        {
            var saveResult = await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["system_id"] = system.Id,
                ["section_type"] = type,
                ["content"] = $"{{\"field\":\"{type} content\"}}",
                ["user_id"] = MoUserId
            });
            JsonDocument.Parse(saveResult).RootElement.GetProperty("status").GetString()
                .Should().Be("success", because: $"saving {type} should succeed");
        }

        // 2. Submit all sections
        var submitResult = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["user_id"] = MoUserId
        });
        var submitDoc = JsonDocument.Parse(submitResult);
        GetStatus(submitDoc).Should().Be("success");
        GetData(submitDoc).GetProperty("submittedSections").GetArrayLength().Should().Be(5);

        // 3. Batch approve all
        var approveResult = await _batchApproveTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["user_id"] = IssmUserId
        });
        var approveDoc = JsonDocument.Parse(approveResult);
        GetStatus(approveDoc).Should().Be("success");
        GetData(approveDoc).GetProperty("approvedCount").GetInt32().Should().Be(5);

        // 4. Verify completeness = 100%
        var completenessResult = await _completenessTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id
        });
        var completenessDoc = JsonDocument.Parse(completenessResult);
        GetStatus(completenessDoc).Should().Be("success");
        GetData(completenessDoc).GetProperty("approvedPercentage").GetInt32().Should().Be(100);
        GetData(completenessDoc).GetProperty("isProfileComplete").GetBoolean().Should().BeTrue();

        // 5. Verify profile overview shows all approved
        var profileResult = await _getProfileTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id
        });
        var profileDoc = JsonDocument.Parse(profileResult);
        GetStatus(profileDoc).Should().Be("success");
        var sections = GetData(profileDoc).GetProperty("sections");

        foreach (var section in sections.EnumerateArray())
        {
            var sType = section.GetProperty("sectionType").GetString();
            if (mandatorySections.Contains(sType))
            {
                section.GetProperty("governanceStatus").GetString().Should().Be("Approved");
            }
            else
            {
                section.GetProperty("governanceStatus").GetString().Should().Be("NotStarted");
            }
        }
    }

    [Fact]
    public async Task FullLifecycle_SubmitWithdrawResubmit()
    {
        var system = await SeedSystemWithRolesAsync();

        // Save
        await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_type"] = "MissionAndPurpose",
            ["content"] = "{\"mission\":\"test\"}",
            ["user_id"] = MoUserId
        });

        // Submit
        await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_types"] = "MissionAndPurpose",
            ["user_id"] = MoUserId
        });

        // Withdraw via submit tool (action=withdraw)
        var withdrawResult = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["action"] = "withdraw",
            ["section_types"] = "MissionAndPurpose",
            ["user_id"] = MoUserId
        });
        var withdrawDoc = JsonDocument.Parse(withdrawResult);
        GetStatus(withdrawDoc).Should().Be("success");
        GetData(withdrawDoc).GetProperty("withdrawnSections").GetArrayLength().Should().Be(1);

        // Re-submit
        var resubmitResult = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = system.Id,
            ["section_types"] = "MissionAndPurpose",
            ["user_id"] = MoUserId
        });
        var resubmitDoc = JsonDocument.Parse(resubmitResult);
        GetStatus(resubmitDoc).Should().Be("success");
        GetData(resubmitDoc).GetProperty("submittedSections").GetArrayLength().Should().Be(1);
    }
}
