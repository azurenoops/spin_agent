using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Agents.Document.Tools;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Core.Interfaces.Auth;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class CspCapabilityNarrativeRegenerationEndpointTests :
    IClassFixture<NarrativeGovernanceWebApplicationFactory>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspCapabilityNarrativeRegenerationEndpointTests(
        NarrativeGovernanceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DualSave_ExpectedVersion_CreatesHistoryAndRejectsStaleRetry()
    {
        // Arrange
        var (systemId, _) = await SeedSubscribedCapabilityAsync(["AC-2"]);
        await SeedNarrativeAsync(systemId, SspSectionStatus.Draft);
        var endpoint = $"/api/dashboard/systems/{systemId}/controls/AC-2/narrative";

        // Act
        using var response = await _client.PatchAsJsonAsync(endpoint, new { policyNarrative = "Updated policy", expectedVersion = 1 });
        using var stale = await _client.PatchAsJsonAsync(endpoint, new { technicalNarrative = "Stale technical", expectedVersion = 1 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("currentVersion").GetInt32().Should().Be(2);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementation = await db.ControlImplementations.SingleAsync(item => item.RegisteredSystemId == systemId);
        implementation.PolicyNarrative.Should().Be("Updated policy");
        implementation.TechnicalNarrative.Should().Be("Original technical");
        (await db.NarrativeVersions.CountAsync(item => item.ControlImplementationId == implementation.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DocumentRegeneration_RecordsRequestAuthorAndNewVersion()
    {
        // Arrange
        var (systemId, _) = await SeedSubscribedCapabilityAsync(["AC-2"]);
        await SeedNarrativeAsync(systemId, SspSectionStatus.Draft);

        // Act
        using var response = await _client.PostAsync(
            $"/api/dashboard/systems/{systemId}/controls/AC-2/regenerate-ai?sourceUrl=synthetic-reference&expectedVersion=1", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementation = await db.ControlImplementations.SingleAsync(item => item.RegisteredSystemId == systemId);
        implementation.AuthoredBy.Should().Be("synthetic-author");
        implementation.CurrentVersion.Should().Be(2);
        implementation.PolicyNarrative.Should().Be("Original policy");
        implementation.IsManuallyCustomized.Should().BeFalse();
        var version = await db.NarrativeVersions.SingleAsync(item => item.ControlImplementationId == implementation.Id && item.VersionNumber == 2);
        version.AuthoredBy.Should().Be("synthetic-author");
        version.Status.Should().Be(SspSectionStatus.Draft);
        version.Content.Should().Be(implementation.TechnicalNarrative);
    }

    [Theory]
    [InlineData("/api", "narrative", true)]
    [InlineData("/api/dashboard", "narrative", true)]
    [InlineData("/api/dashboard", "regenerate-ai", true)]
    [InlineData("/api/dashboard", "regenerate-ai?sourceUrl=synthetic-reference", true)]
    [InlineData("/api/dashboard", "regenerate-ai?expectedVersion=0", false)]
    [InlineData("/api/dashboard", "regenerate-ai?sourceUrl=synthetic-reference&expectedVersion=0", false)]
    public async Task NarrativeWriters_RejectGovernanceConflictsWithoutMutation(string prefix, string action, bool underReview)
    {
        // Arrange
        var (systemId, _) = await SeedSubscribedCapabilityAsync(["AC-2"]);
        await SeedNarrativeAsync(systemId, underReview ? SspSectionStatus.UnderReview : SspSectionStatus.Draft);
        var endpoint = $"{prefix}/systems/{systemId}/controls/AC-2/{action}";

        // Act
        using var response = action == "narrative"
            ? await _client.PatchAsJsonAsync(endpoint, new { technicalNarrative = "Rejected", expectedVersion = 1 })
            : await _client.PostAsync(endpoint, null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(underReview ? "UNDER_REVIEW" : "CONCURRENCY_CONFLICT");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementation = await db.ControlImplementations.SingleAsync(item => item.RegisteredSystemId == systemId);
        implementation.TechnicalNarrative.Should().Be("Original technical");
        implementation.PolicyNarrative.Should().Be("Original policy");
        implementation.CurrentVersion.Should().Be(1);
        (await db.NarrativeVersions.CountAsync(item => item.ControlImplementationId == implementation.Id)).Should().Be(0);
    }

    private async Task SeedNarrativeAsync(string systemId, SspSectionStatus reviewStatus)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        db.ControlImplementations.Add(new ControlImplementation
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            RegisteredSystemId = systemId, ControlId = "AC-2",
            PolicyNarrative = "Original policy", TechnicalNarrative = "Original technical", Narrative = "Original technical",
            ApprovalStatus = reviewStatus, AuthoredBy = "synthetic-author", IsManuallyCustomized = true,
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Generation_ConcurrentWriteOrReview_DoesNotPersistGeneratedContentOrHistory(bool documentSource, bool reviewStarted)
    {
        // Arrange
        var (systemId, _) = await SeedSubscribedCapabilityAsync(["AC-2"]);
        await SeedNarrativeAsync(systemId, SspSectionStatus.Draft);
        using var scope = _factory.Services.CreateScope();
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementation = await db.ControlImplementations.SingleAsync(item => item.RegisteredSystemId == systemId);
        var capability = new SecurityCapability { Name = $"Synthetic identity {systemId}", Provider = "Synthetic provider", CreatedBy = "test" };
        db.SecurityCapabilities.Add(capability);
        implementation.SecurityCapabilityId = capability.Id;
        await db.SaveChangesAsync();
        var chat = new Mock<IChatClient>();
        chat.Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>())).Returns(async () =>
        {
            using var otherScope = _factory.Services.CreateScope();
            var otherDb = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(otherScope.ServiceProvider);
            var concurrent = await otherDb.ControlImplementations.SingleAsync(item => item.Id == implementation.Id);
            if (reviewStarted)
                concurrent.ApprovalStatus = SspSectionStatus.UnderReview;
            else
            {
                concurrent.CurrentVersion++;
                concurrent.TechnicalNarrative = "Concurrent writer";
                otherDb.NarrativeVersions.Add(new NarrativeVersion
                {
                    ControlImplementationId = concurrent.Id, VersionNumber = concurrent.CurrentVersion,
                    Content = concurrent.TechnicalNarrative, SnapshotJson = NarrativeContentSnapshot.Capture(concurrent),
                    AuthoredBy = "concurrent-author",
                });
            }
            await otherDb.SaveChangesAsync();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Rejected generated text"));
        });

        // Act
        string? errorCode;
        if (documentSource)
        {
            var tool = new DocumentNarrativeGenerateAdapterTool(
                ServiceProviderServiceExtensions.GetRequiredService<ISspService>(scope.ServiceProvider),
                Mock.Of<IDocumentTemplateService>(), null, chat.Object, null,
                NullLogger<DocumentNarrativeGenerateAdapterTool>.Instance,
                ServiceProviderServiceExtensions.GetRequiredService<IServiceScopeFactory>(scope.ServiceProvider));
            var result = await tool.ExecuteAsync(new Dictionary<string, object?>
            {
                ["system_id"] = systemId, ["control_id"] = "AC-2", ["save_draft"] = "true", ["expected_version"] = 1,
            });
            using var json = JsonDocument.Parse(result);
            errorCode = json.RootElement.GetProperty("errorCode").GetString();
        }
        else
        {
            var generator = new NarrativeTemplateService(chat.Object,
                new AzureAiOptions { Enabled = true, Endpoint = "https://synthetic.test/" },
                NullLogger<NarrativeTemplateService>.Instance);
            var service = new CapabilityService(db, NullLogger<CapabilityService>.Instance,
                generator, Mock.Of<IDeviationService>(), Mock.Of<IOrgInheritanceService>());
            var result = await service.RegenerateNarrativeWithAiAsync(systemId, "AC-2", "synthetic-author", expectedVersion: 1);
            errorCode = result.ErrorCode;
            result.Narrative.Should().BeNull();
        }

        // Assert
        errorCode.Should().Be(documentSource && reviewStarted ? "UNDER_REVIEW" : "CONCURRENCY_CONFLICT");
        chat.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        db.ChangeTracker.Clear();
        var persisted = await db.ControlImplementations.SingleAsync(item => item.Id == implementation.Id);
        persisted.TechnicalNarrative.Should().Be(reviewStarted ? "Original technical" : "Concurrent writer");
        persisted.PolicyNarrative.Should().Be("Original policy");
        persisted.CurrentVersion.Should().Be(reviewStarted ? 1 : 2);
        persisted.ApprovalStatus.Should().Be(reviewStarted ? SspSectionStatus.UnderReview : SspSectionStatus.Draft);
        var history = await db.NarrativeVersions.Where(item => item.ControlImplementationId == implementation.Id).ToListAsync();
        history.Should().HaveCount(reviewStarted ? 0 : 1);
        if (!reviewStarted)
            history.Should().OnlyContain(item => item.AuthoredBy == "concurrent-author" && item.Content == "Concurrent writer");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task McpNarrativeWriters_RejectReviewAndStaleVersions(bool policy, bool reviewStarted)
    {
        // Arrange
        var (systemId, _) = await SeedSubscribedCapabilityAsync(["AC-2"]);
        await SeedNarrativeAsync(systemId, reviewStarted ? SspSectionStatus.UnderReview : SspSectionStatus.Draft);
        using var scope = _factory.Services.CreateScope();
        var service = ServiceProviderServiceExtensions.GetRequiredService<IDualNarrativeService>(scope.ServiceProvider);
        var scopeFactory = ServiceProviderServiceExtensions.GetRequiredService<IServiceScopeFactory>(scope.ServiceProvider);
        var arguments = new Dictionary<string, object?>
        {
            ["system_id"] = systemId, ["control_id"] = "AC-2", ["expected_version"] = 0,
            [policy ? "policy_narrative" : "technical_narrative"] = "Rejected text",
        };

        // Act
        var result = policy
            ? await new NarrativePolicyTool(service, scopeFactory, NullLogger<NarrativePolicyTool>.Instance).ExecuteAsync(arguments)
            : await new NarrativeTechnicalTool(service, scopeFactory, NullLogger<NarrativeTechnicalTool>.Instance).ExecuteAsync(arguments);

        // Assert
        result.Should().Contain(reviewStarted ? "UNDER_REVIEW" : "CONCURRENCY_CONFLICT");
        var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementation = await db.ControlImplementations.SingleAsync(item => item.RegisteredSystemId == systemId);
        implementation.PolicyNarrative.Should().Be("Original policy");
        implementation.TechnicalNarrative.Should().Be("Original technical");
        (await db.NarrativeVersions.CountAsync(item => item.ControlImplementationId == implementation.Id)).Should().Be(0);
    }

    [Fact]
    public async Task BulkRegenerate_WithSubscribedCspCapability_CreatesAndRegeneratesMappedControls()
    {
        // Arrange
        var (systemId, capabilityId) = await SeedSubscribedCapabilityAsync(["AC-2", "AC-6"]);

        // Act
        using var response = await _client.PostAsync(
            $"/api/dashboard/systems/{systemId}/capabilities/{capabilityId}/bulk-regenerate",
            content: null);
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("totalControls").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("regenerated").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("failed").GetInt32().Should().Be(0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementations = await db.ControlImplementations
            .Where(implementation => implementation.RegisteredSystemId == systemId)
            .ToListAsync();
        implementations.Should().HaveCount(2);
        implementations.Should().OnlyContain(implementation =>
            implementation.SecurityCapabilityId == null &&
            implementation.IsAutoPopulated &&
            !string.IsNullOrWhiteSpace(implementation.Narrative) &&
            implementation.TechnicalNarrative == implementation.Narrative &&
            implementation.PolicyNarrative == null);
    }

    [Fact]
    public async Task BulkRegenerate_WithDocumentSourceAndSubscribedCspCapability_UsesPreparedControl()
    {
        // Arrange
        var (systemId, capabilityId) = await SeedSubscribedCapabilityAsync(["AU-6"]);
        var sourceUrl = Uri.EscapeDataString("test-evidence-reference");
        await using (var preconditionScope = _factory.Services.CreateAsyncScope())
        {
            var preconditionDb = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<AtoCopilotContext>(preconditionScope.ServiceProvider);
            (await preconditionDb.ControlBaselines.AnyAsync(baseline => baseline.RegisteredSystemId == systemId))
                .Should().BeTrue();
        }

        // Act
        using var response = await _client.PostAsync(
            $"/api/dashboard/systems/{systemId}/capabilities/{capabilityId}/bulk-regenerate?sourceUrl={sourceUrl}",
            content: null);
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("totalControls").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("regenerated").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("failed").GetInt32().Should().Be(0);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        var implementation = await db.ControlImplementations
            .SingleAsync(candidate => candidate.RegisteredSystemId == systemId && candidate.ControlId == "AU-6");
        implementation.SecurityCapabilityId.Should().BeNull();
        implementation.Narrative.Should().Contain("Reference Sources Used:");
        implementation.TechnicalNarrative.Should().Be(implementation.Narrative);
        implementation.PolicyNarrative.Should().BeNull();
    }

    [Fact]
    public async Task BulkRegenerate_WithoutActiveCspSubscription_ReturnsNotFoundAndCreatesNothing()
    {
        // Arrange
        var (systemId, capabilityId) = await SeedSubscribedCapabilityAsync(["AC-2"], isSubscribed: false);

        // Act
        using var response = await _client.PostAsync(
            $"/api/dashboard/systems/{systemId}/capabilities/{capabilityId}/bulk-regenerate",
            content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("errorCode").GetString().Should().Be("NOT_FOUND");
        document.RootElement.GetProperty("suggestion").GetString()
            .Should().Contain("active subscription");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        (await db.ControlImplementations.CountAsync(implementation =>
            implementation.RegisteredSystemId == systemId)).Should().Be(0);
    }

    private async Task<(string SystemId, Guid CapabilityId)> SeedSubscribedCapabilityAsync(
        string[] controlIds,
        bool isSubscribed = true)
    {
        _factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        await _factory.EnsureActiveCspProfileAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CapabilitySubscriptions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CapabilitySubscriptions" PRIMARY KEY,
                "RegisteredSystemId" TEXT NOT NULL,
                "CspInheritedCapabilityId" TEXT NOT NULL,
                "SubscribedBy" TEXT NOT NULL DEFAULT 'dashboard-user',
                "SubscribedAt" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS "IX_CapabilitySubscription_System_Capability"
                ON "CapabilitySubscriptions" ("RegisteredSystemId", "CspInheritedCapabilityId");
            """);
        var profileId = await db.CspProfiles
            .IgnoreQueryFilters()
            .Select(profile => profile.Id)
            .FirstAsync();
        var system = new RegisteredSystem
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            Name = $"Regeneration Test {Guid.NewGuid():N}",
            Acronym = "REGEN",
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionSupport,
            HostingEnvironment = "Azure Government",
            CurrentRmfStep = RmfPhase.Implement,
            CreatedBy = "integration-test",
        };
        var component = new CspInheritedComponent
        {
            CspProfileId = profileId,
            Name = "Microsoft Entra ID P2",
            Description = "CSP identity governance component",
            ComponentType = CspComponentType.Identity,
            SourceFormat = SourceFormat.Manual,
            Status = CspInheritedComponentStatus.Published,
            ImportedBy = "integration-test",
        };
        var capability = new CspInheritedCapability
        {
            CspInheritedComponentId = component.Id,
            Name = "Access Reviews & Governance",
            Description = "CSP-managed identity governance",
            MappedNistControlIds = controlIds.ToList(),
            Status = CspInheritedCapabilityStatus.Mapped,
            CreatedBy = "integration-test",
        };
        db.AddRange(system, component, capability);
        db.ControlBaselines.Add(new ControlBaseline
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
            RegisteredSystemId = system.Id,
            BaselineLevel = "Moderate",
            TotalControls = controlIds.Length,
            ControlIds = controlIds.ToList(),
            CreatedBy = "integration-test",
        });
        if (isSubscribed)
        {
            db.CapabilitySubscriptions.Add(new CapabilitySubscription
            {
                RegisteredSystemId = system.Id,
                CspInheritedCapabilityId = capability.Id.ToString(),
                SubscribedBy = "integration-test",
                IsActive = true,
            });
        }
        await db.SaveChangesAsync();

        return (system.Id, capability.Id);
    }
}

public sealed class NarrativeGovernanceWebApplicationFactory : MultiTenantWebApplicationFactory<McpProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddScoped<IUserContext>(_ =>
            Mock.Of<IUserContext>(user => user.Role == "Compliance.Analyst" && user.UserId == "synthetic-author")));
    }
}