using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Integration.Tools;

/// <summary>
/// Integration tests for Feature 015 Phase 4 — Security Categorization Tools.
/// Uses real CategorizationService + RmfLifecycleService with in-memory EF Core database.
/// Validates register → categorize → high-water mark → IL derivation → FIPS 199 notation.
/// </summary>
public class CategorizationIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly RegisterSystemTool _registerTool;
    private readonly CategorizeSystemTool _categorizeTool;
    private readonly GetCategorizationTool _getCategorizationTool;
    private readonly SuggestInfoTypesTool _suggestInfoTypesTool;

    public CategorizationIntegrationTests()
    {
        var dbName = $"CatIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddDbContextFactory<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var lifecycleSvc = new RmfLifecycleService(scopeFactory, _serviceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(), Mock.Of<ILogger<RmfLifecycleService>>());
        var categorizationSvc = new CategorizationService(scopeFactory, Mock.Of<ILogger<CategorizationService>>(), Mock.Of<IPrivacyService>());

        _registerTool = new RegisterSystemTool(lifecycleSvc, Mock.Of<ILogger<RegisterSystemTool>>());
        _categorizeTool = new CategorizeSystemTool(categorizationSvc, Mock.Of<ILogger<CategorizeSystemTool>>());
        _getCategorizationTool = new GetCategorizationTool(categorizationSvc, Mock.Of<ILogger<GetCategorizationTool>>());
        _suggestInfoTypesTool = new SuggestInfoTypesTool(categorizationSvc, Mock.Of<ILogger<SuggestInfoTypesTool>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    /// <summary>
    /// End-to-end: Register system → categorize with info types → get categorization →
    /// verify high-water mark, IL derivation, and FIPS 199 notation.
    /// </summary>
    [Fact]
    public async Task FullCategorization_RegisterCategorizeFetchVerify()
    {
        // Step 1: Register a system
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Categorization Test System",
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment",
            ["description"] = "Financial management system for DoD"
        });

        var regJson = JsonDocument.Parse(regResult);
        regJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        var systemId = regJson.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        // Step 2: Categorize the system with information types
        var catResult = await _categorizeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["information_types"] = new List<InformationTypeInput>
            {
                new()
                {
                    Sp80060Id = "C.3.1.4", Name = "Financial Management",
                    Category = "Management and Support",
                    ConfidentialityImpact = "Moderate", IntegrityImpact = "Moderate",
                    AvailabilityImpact = "Low"
                },
                new()
                {
                    Sp80060Id = "C.3.5.8", Name = "Information Security",
                    Category = "Management and Support",
                    ConfidentialityImpact = "Moderate", IntegrityImpact = "High",
                    AvailabilityImpact = "Moderate"
                }
            },
            ["justification"] = "Categorized per mission requirements"
        });

        var catJson = JsonDocument.Parse(catResult);
        catJson.RootElement.GetProperty("status").GetString().Should().Be("success");

        // Verify high-water mark: C=Moderate, I=High, A=Moderate → Overall=High
        var data = catJson.RootElement.GetProperty("data");
        data.GetProperty("overall_categorization").GetString().Should().Be("High");
        data.GetProperty("confidentiality_impact").GetString().Should().Be("Moderate");
        data.GetProperty("integrity_impact").GetString().Should().Be("High");
        data.GetProperty("availability_impact").GetString().Should().Be("Moderate");

        // Verify IL derivation: High → IL5
        data.GetProperty("dod_impact_level").GetString().Should().Be("IL5");
        data.GetProperty("nist_baseline").GetString().Should().Be("High");

        // Verify FIPS 199 notation
        data.GetProperty("fips_199_notation").GetString().Should().Contain("MODERATE");
        data.GetProperty("fips_199_notation").GetString().Should().Contain("HIGH");

        // Verify info type count
        data.GetProperty("information_type_count").GetInt32().Should().Be(2);

        // Step 3: Get categorization (idempotent read)
        var getResult = await _getCategorizationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var getJson = JsonDocument.Parse(getResult);
        getJson.RootElement.GetProperty("status").GetString().Should().Be("success");
        getJson.RootElement.GetProperty("data").GetProperty("overall_categorization").GetString().Should().Be("High");
        getJson.RootElement.GetProperty("data").GetProperty("information_type_count").GetInt32().Should().Be(2);
    }

    /// <summary>
    /// Re-categorization replaces previous categorization entirely.
    /// </summary>
    [Fact]
    public async Task Recategorize_ReplacesPreviousCategorization()
    {
        // Register
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Recat System",
            ["system_type"] = "Enclave",
            ["mission_criticality"] = "MissionEssential",
            ["hosting_environment"] = "AzureGovernment"
        });
        var systemId = JsonDocument.Parse(regResult).RootElement.GetProperty("data").GetProperty("id").GetString()!;

        // First categorization: Low
        await _categorizeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["information_types"] = new List<InformationTypeInput>
            {
                new() { Sp80060Id = "D.1.1", Name = "Strategic Planning",
                    ConfidentialityImpact = "Low", IntegrityImpact = "Low", AvailabilityImpact = "Low" }
            }
        });

        // Second categorization: High (replaces first)
        var recat = await _categorizeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["information_types"] = new List<InformationTypeInput>
            {
                new() { Sp80060Id = "C.3.5.8", Name = "Information Security",
                    ConfidentialityImpact = "High", IntegrityImpact = "High", AvailabilityImpact = "Moderate" },
                new() { Sp80060Id = "C.3.1.4", Name = "Financial Management",
                    ConfidentialityImpact = "Moderate", IntegrityImpact = "Moderate", AvailabilityImpact = "Low" }
            }
        });

        var json = JsonDocument.Parse(recat);
        json.RootElement.GetProperty("data").GetProperty("overall_categorization").GetString().Should().Be("High");
        json.RootElement.GetProperty("data").GetProperty("information_type_count").GetInt32().Should().Be(2);

        // Verify get returns the new categorization
        var getResult = await _getCategorizationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });
        var getJson = JsonDocument.Parse(getResult);
        getJson.RootElement.GetProperty("data").GetProperty("dod_impact_level").GetString().Should().Be("IL5");
    }

    /// <summary>
    /// Get categorization for uncategorized system returns null data.
    /// </summary>
    [Fact]
    public async Task GetCategorization_UncategorizedSystem_ReturnsNullData()
    {
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Uncat System",
            ["system_type"] = "PlatformIt",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment"
        });
        var systemId = JsonDocument.Parse(regResult).RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var getResult = await _getCategorizationTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId
        });

        var json = JsonDocument.Parse(getResult);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        json.RootElement.GetProperty("message").GetString().Should().Contain("No categorization");
    }

    /// <summary>
    /// Suggest info types for a registered system returns ranked suggestions.
    /// </summary>
    [Fact]
    public async Task SuggestInfoTypes_ReturnsRankedSuggestions()
    {
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "Security Portal",
            ["system_type"] = "MajorApplication",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment",
            ["description"] = "Security monitoring and audit logging platform"
        });
        var systemId = JsonDocument.Parse(regResult).RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var suggestResult = await _suggestInfoTypesTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["description"] = "security audit compliance monitoring"
        });

        var json = JsonDocument.Parse(suggestResult);
        json.RootElement.GetProperty("status").GetString().Should().Be("success");
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("suggestion_count").GetInt32().Should().BeGreaterThan(0);

        var suggestions = data.GetProperty("suggestions");
        suggestions.EnumerateArray().First().GetProperty("confidence").GetDouble().Should().BeGreaterThan(0);
        suggestions.EnumerateArray().First().GetProperty("sp800_60_id").GetString().Should().NotBeEmpty();
    }

    /// <summary>
    /// NSS categorization with Low info types still yields IL2 (no classified designation).
    /// </summary>
    [Fact]
    public async Task NssCategorization_LowImpact_YieldsIL2WithoutClassified()
    {
        var regResult = await _registerTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["name"] = "NSS Test System",
            ["system_type"] = "Enclave",
            ["mission_criticality"] = "MissionCritical",
            ["hosting_environment"] = "AzureGovernment"
        });
        var systemId = JsonDocument.Parse(regResult).RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var catResult = await _categorizeTool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["system_id"] = systemId,
            ["information_types"] = new List<InformationTypeInput>
            {
                new() { Sp80060Id = "D.1.1", Name = "Strategic Planning",
                    ConfidentialityImpact = "Low", IntegrityImpact = "Low", AvailabilityImpact = "Low" }
            },
            ["is_national_security_system"] = true
        });

        var json = JsonDocument.Parse(catResult);
        json.RootElement.GetProperty("data").GetProperty("overall_categorization").GetString().Should().Be("Low");
        // NSS without classified designation → IL2 (not IL6)
        json.RootElement.GetProperty("data").GetProperty("dod_impact_level").GetString().Should().Be("IL2");
        json.RootElement.GetProperty("data").GetProperty("is_national_security_system").GetBoolean().Should().BeTrue();
    }
}
