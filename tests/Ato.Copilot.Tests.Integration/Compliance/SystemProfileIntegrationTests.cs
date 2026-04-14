using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

/// <summary>
/// Integration/performance assertions for Feature 046 profile workflows.
/// T054: verify p95 for profile operations stays under 500ms (SC-011).
/// </summary>
public class SystemProfileIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AtoCopilotContext _db;

    private readonly ComplianceGetSystemProfileTool _getProfileTool;
    private readonly ComplianceSaveProfileSectionTool _saveSectionTool;
    private readonly ComplianceSubmitProfileSectionTool _submitTool;
    private readonly ComplianceReviewProfileSectionTool _reviewTool;
    private readonly ComplianceGetProfileCompletenessTool _completenessTool;

    private const string MoUserId = "mo-user";
    private const string IssmUserId = "issm-user";

    public SystemProfileIntegrationTests()
    {
        var dbName = $"ProfilePerfInt_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();

        _db = _serviceProvider.GetRequiredService<AtoCopilotContext>();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var profileService = new SystemProfileService(scopeFactory, Mock.Of<ILogger<SystemProfileService>>());

        _getProfileTool = new ComplianceGetSystemProfileTool(profileService, Mock.Of<ILogger<ComplianceGetSystemProfileTool>>());
        _saveSectionTool = new ComplianceSaveProfileSectionTool(profileService, Mock.Of<ILogger<ComplianceSaveProfileSectionTool>>());
        _submitTool = new ComplianceSubmitProfileSectionTool(profileService, Mock.Of<ILogger<ComplianceSubmitProfileSectionTool>>());
        _reviewTool = new ComplianceReviewProfileSectionTool(profileService, Mock.Of<ILogger<ComplianceReviewProfileSectionTool>>());
        _completenessTool = new ComplianceGetProfileCompletenessTool(profileService, Mock.Of<ILogger<ComplianceGetProfileCompletenessTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    [Fact]
    public async Task ProfileOperations_P95_Under500ms_SC011()
    {
        const int samples = 15;

        var getProfileTimes = new List<long>(samples);
        var saveTimes = new List<long>(samples);
        var submitTimes = new List<long>(samples);
        var withdrawTimes = new List<long>(samples);
        var reviewTimes = new List<long>(samples);
        var completenessTimes = new List<long>(samples);

        for (var i = 0; i < samples; i++)
        {
            // get profile
            {
                var system = await SeedSystemWithRolesAsync();
                var elapsed = await MeasureAsync(async () =>
                {
                    var result = await _getProfileTool.ExecuteAsync(new Dictionary<string, object?>
                    {
                        ["system_id"] = system.Id
                    });
                    EnsureSuccess(result);
                });
                getProfileTimes.Add(elapsed);
            }

            // save section
            {
                var system = await SeedSystemWithRolesAsync();
                var elapsed = await MeasureAsync(async () =>
                {
                    var result = await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
                    {
                        ["system_id"] = system.Id,
                        ["section_type"] = "MissionAndPurpose",
                        ["content"] = $"{{\"mission\":\"test-{i}\"}}",
                        ["user_id"] = MoUserId
                    });
                    EnsureSuccess(result);
                });
                saveTimes.Add(elapsed);
            }

            // submit section
            {
                var system = await SeedSystemWithRolesAsync();
                var saveResult = await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = system.Id,
                    ["section_type"] = "MissionAndPurpose",
                    ["content"] = $"{{\"mission\":\"submit-{i}\"}}",
                    ["user_id"] = MoUserId
                });
                EnsureSuccess(saveResult);

                var elapsed = await MeasureAsync(async () =>
                {
                    var result = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
                    {
                        ["system_id"] = system.Id,
                        ["section_types"] = "MissionAndPurpose",
                        ["user_id"] = MoUserId
                    });
                    EnsureSuccess(result);
                });
                submitTimes.Add(elapsed);
            }

            // withdraw section
            {
                var system = await SeedSystemWithRolesAsync();
                var saveResult = await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = system.Id,
                    ["section_type"] = "MissionAndPurpose",
                    ["content"] = $"{{\"mission\":\"withdraw-{i}\"}}",
                    ["user_id"] = MoUserId
                });
                EnsureSuccess(saveResult);

                var submitResult = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = system.Id,
                    ["section_types"] = "MissionAndPurpose",
                    ["user_id"] = MoUserId
                });
                EnsureSuccess(submitResult);

                var elapsed = await MeasureAsync(async () =>
                {
                    var result = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
                    {
                        ["system_id"] = system.Id,
                        ["action"] = "withdraw",
                        ["section_types"] = "MissionAndPurpose",
                        ["user_id"] = MoUserId
                    });
                    EnsureSuccess(result);
                });
                withdrawTimes.Add(elapsed);
            }

            // review section
            {
                var system = await SeedSystemWithRolesAsync();
                var saveResult = await _saveSectionTool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = system.Id,
                    ["section_type"] = "MissionAndPurpose",
                    ["content"] = $"{{\"mission\":\"review-{i}\"}}",
                    ["user_id"] = MoUserId
                });
                EnsureSuccess(saveResult);

                var submitResult = await _submitTool.ExecuteAsync(new Dictionary<string, object?>
                {
                    ["system_id"] = system.Id,
                    ["section_types"] = "MissionAndPurpose",
                    ["user_id"] = MoUserId
                });
                EnsureSuccess(submitResult);

                var elapsed = await MeasureAsync(async () =>
                {
                    var result = await _reviewTool.ExecuteAsync(new Dictionary<string, object?>
                    {
                        ["system_id"] = system.Id,
                        ["section_type"] = "MissionAndPurpose",
                        ["decision"] = "approve",
                        ["user_id"] = IssmUserId
                    });
                    EnsureSuccess(result);
                });
                reviewTimes.Add(elapsed);
            }

            // completeness
            {
                var system = await SeedSystemWithRolesAsync();
                var elapsed = await MeasureAsync(async () =>
                {
                    var result = await _completenessTool.ExecuteAsync(new Dictionary<string, object?>
                    {
                        ["system_id"] = system.Id
                    });
                    EnsureSuccess(result);
                });
                completenessTimes.Add(elapsed);
            }
        }

        AssertP95Under(getProfileTimes, 500, "get profile");
        AssertP95Under(saveTimes, 500, "save section");
        AssertP95Under(submitTimes, 500, "submit section");
        AssertP95Under(withdrawTimes, 500, "withdraw section");
        AssertP95Under(reviewTimes, 500, "review section");
        AssertP95Under(completenessTimes, 500, "get completeness");
    }

    private async Task<RegisteredSystem> SeedSystemWithRolesAsync()
    {
        var system = new RegisteredSystem
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Profile Performance Test System",
            Acronym = "PPTS",
            Description = "Performance integration test system",
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

    private static async Task<long> MeasureAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static void EnsureSuccess(string toolResult)
    {
        using var doc = JsonDocument.Parse(toolResult);
        var status = doc.RootElement.GetProperty("status").GetString();
        status.Should().Be("success", "tool execution should succeed during perf assertion runs");
    }

    private static void AssertP95Under(IReadOnlyCollection<long> samples, long thresholdMs, string operationName)
    {
        samples.Should().NotBeEmpty();

        var ordered = samples.OrderBy(x => x).ToList();
        var rank = (int)Math.Ceiling(0.95 * ordered.Count) - 1;
        rank = Math.Clamp(rank, 0, ordered.Count - 1);
        var p95 = ordered[rank];

        p95.Should().BeLessThan(thresholdMs,
            $"{operationName} should stay under {thresholdMs}ms at p95 (SC-011). Samples: [{string.Join(", ", ordered)}]");
    }
}
