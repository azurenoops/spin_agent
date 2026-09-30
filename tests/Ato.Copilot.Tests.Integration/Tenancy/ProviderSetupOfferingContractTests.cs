using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class ProviderSetupOfferingContractTests : IClassFixture<PackageImportFactory>
{
    private readonly PackageImportFactory _factory;
    private readonly HttpClient _client;

    public ProviderSetupOfferingContractTests(PackageImportFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.GetActiveContext().IsCspAdmin = true;
        factory.GetActiveContext().ImpersonatedTenantId = null;
        var policy = factory.Services.GetRequiredService<IOptions<ProviderHandlingOptions>>().Value;
        policy.PolicyId = null;
        policy.Version = null;
        policy.ApprovalReference = null;
        policy.ValidUntil = null;
        policy.AllowedClassifications = ["Unclassified", "CUI", "Secret"];
        policy.UploadsEnabled = true;
        policy.AnalysisEnabled = true;
    }

    [Theory]
    [InlineData("AwsGovCloud", null, "ManualService")]
    [InlineData("Microsoft365DoD", null, "Microsoft365DoD")]
    [InlineData("Other", "Manually operated SaaS service", "ManualService")]
    public async Task NonAzureFirstOffering_PersistsCanonicalIdentityAndRetainsDeclaredDraftMetadata(
        string environment, string? environmentLabel, string canonicalEnvironment)
    {
        // Arrange
        var name = $"Synthetic {environment} {Guid.NewGuid():N}";
        var saved = await Save(NewOffering(name, environment, environmentLabel, []));
        // Act
        var response = await CommitOffering(saved);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var outcome = (await Data(response)).GetProperty("committedOutcome");
        var id = outcome.GetProperty("committedOfferingId").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var row = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == id);
        row.Name.Should().Be(name);
        JsonSerializer.Deserialize<string[]>(row.EnvironmentsJson).Should().Equal(canonicalEnvironment);
        row.ServiceModel.Should().Be("SoftwareAsAService");
        row.ManagementArrangement.Should().Be("ProviderManaged");
        row.ServiceOwner.Should().BeNull();
        row.SecurityContact.Should().BeNull();
        var draft = await db.Set<ProviderSetupDraft>().SingleAsync(x => x.ProviderId == row.ProviderId);
        var firstOffering = JsonSerializer.Deserialize<JsonElement>(draft.DraftJson).GetProperty("firstOffering");
        firstOffering.GetProperty("choice").GetString().Should().Be("Existing");
        firstOffering.GetProperty("offeringId").GetGuid().Should().Be(row.Id);
        var description = firstOffering.GetProperty("serviceDescription");
        description.GetRawText().Should().Be(saved.GetProperty("draft").GetProperty("fields")
            .GetProperty("firstOffering").GetProperty("serviceDescription").GetRawText());
        description.GetProperty("environmentKind").GetString().Should().Be(environment);
        description.GetProperty("serviceModel").GetString().Should().Be("Software");
        description.GetProperty("managedBy").GetString().Should().Be("Provider");
        description.GetProperty("intendedUse").GetString().Should().Be("Synthetic documentation preparation");
        description.GetProperty("declaredImpactLevel").GetString().Should().Be("IL6");
        if (environmentLabel is not null)
            description.GetProperty("environmentLabel").GetString().Should().Be(environmentLabel);
        row.CurrentBoundaryRevisionId.Should().BeNull();
        row.CurrentHostingScopeRevisionId.Should().BeNull();
        (await db.Set<ProviderBoundaryRevision>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
        (await db.Set<ProviderHostingScopeRevision>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
        (await db.Set<ProviderPackageVersion>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
        description.GetRawText().Should().NotContain("subscriptionId").And.NotContain("resourceId");
        var current = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
        current.GetProperty("draft").GetProperty("fields").GetProperty("firstOffering")
            .GetProperty("serviceDescription").GetRawText().Should().Be(description.GetRawText());
    }

    [Theory]
    [InlineData("InfrastructureShared", "InfrastructureSharedServices", "Provider", "ProviderManaged")]
    [InlineData("Platform", "PlatformService", "SharedOperations", "SharedOperations")]
    [InlineData("Software", "SoftwareAsAService", "MissionOwner", "MissionOwnerManaged")]
    [InlineData("BrokeredHosting", "BrokeredCloudSpace", "Provider", "ProviderManaged")]
    public async Task FirstOffering_MapsDeclaredIdentityToExactUpstreamValues(
        string declaredModel, string canonicalModel, string declaredManagement, string canonicalManagement)
    {
        // Arrange
        var saved = await Save(NewOffering($"Synthetic mapping {Guid.NewGuid():N}", "Other", "Manual service", [],
            declaredModel, declaredManagement));

        // Act
        var response = await CommitOffering(saved);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await Data(response)).GetProperty("committedOutcome").GetProperty("committedOfferingId").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var row = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == id);
        row.ServiceModel.Should().Be(canonicalModel);
        row.ManagementArrangement.Should().Be(canonicalManagement);
        JsonSerializer.Deserialize<string[]>(row.EnvironmentsJson).Should().Equal("ManualService");
        row.CurrentBoundaryRevisionId.Should().BeNull();
        row.CurrentHostingScopeRevisionId.Should().BeNull();
        (await db.Set<ProviderBoundaryRevision>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
        (await db.Set<ProviderHostingScopeRevision>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
        (await db.Set<ProviderPackageVersion>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
    }

    [Fact]
    public async Task FirstOffering_AcceptsCanonicalIdentityWithoutInventingDescriptorOrContext()
    {
        // Arrange
        var saved = await Save(new
        {
            choice = "New", name = $"Synthetic canonical identity {Guid.NewGuid():N}",
            description = "Explicit upstream identity", environments = new[] { "ManualService" },
            serviceModel = "PlatformService", managementArrangement = "SharedOperations",
            serviceOwner = "Synthetic service owner", securityContact = "security@example.invalid"
        });

        // Act
        var response = await CommitOffering(saved);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await Data(response)).GetProperty("committedOutcome").GetProperty("committedOfferingId").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var row = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == id);
        row.ServiceModel.Should().Be("PlatformService");
        row.ManagementArrangement.Should().Be("SharedOperations");
        row.ServiceOwner.Should().Be("Synthetic service owner");
        row.SecurityContact.Should().Be("security@example.invalid");
        JsonSerializer.Deserialize<string[]>(row.EnvironmentsJson).Should().Equal("ManualService");
        row.CurrentBoundaryRevisionId.Should().BeNull();
        row.CurrentHostingScopeRevisionId.Should().BeNull();
        (await db.Set<ProviderBoundaryRevision>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
        (await db.Set<ProviderHostingScopeRevision>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
        (await db.Set<ProviderPackageVersion>().CountAsync(x => x.OfferingId == id)).Should().Be(0);
        var draft = await db.Set<ProviderSetupDraft>().SingleAsync(x => x.ProviderId == row.ProviderId);
        JsonSerializer.Deserialize<JsonElement>(draft.DraftJson).GetProperty("firstOffering")
            .TryGetProperty("serviceDescription", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("PlatformService", null)]
    [InlineData(null, "MissionOwnerManaged")]
    public async Task FirstOffering_RejectsContradictoryCanonicalIdentityAndRetainsDraft(
        string? canonicalModel, string? canonicalManagement)
    {
        // Arrange
        var name = $"Synthetic mismatched identity {Guid.NewGuid():N}";
        var saved = await Save(NewOffering(name, "Other", "Manual service", [],
            canonicalModel: canonicalModel, canonicalManagement: canonicalManagement));

        // Act
        var response = await CommitOffering(saved);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.Set<ProviderOffering>().CountAsync(x => x.Name == name)).Should().Be(0);
        var retained = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
        Revision(retained).Should().Be(Revision(saved));
        retained.GetProperty("draft").GetProperty("fields").GetProperty("firstOffering").GetRawText()
            .Should().Be(saved.GetProperty("draft").GetProperty("fields").GetProperty("firstOffering").GetRawText());
    }

    [Fact]
    public async Task NonAzureFirstOffering_RejectsInventedAzureEnvironmentAndCreatesNoOffering()
    {
        // Arrange
        var name = $"Synthetic rejected service {Guid.NewGuid():N}";
        var saved = await Save(NewOffering(name, "Other", "Manually operated SaaS service", ["AzureCloud"]));
        // Act
        var response = await CommitOffering(saved);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.Set<ProviderOffering>().CountAsync(x => x.Name == name)).Should().Be(0);
        var retained = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
        Revision(retained).Should().Be(Revision(saved));
        retained.GetProperty("draft").GetProperty("fields").GetProperty("firstOffering")
            .GetProperty("name").GetString().Should().Be(name);
        retained.GetProperty("draft").GetProperty("fields").GetProperty("firstOffering").GetRawText()
            .Should().Be(saved.GetProperty("draft").GetProperty("fields").GetProperty("firstOffering").GetRawText());
    }

    [Theory]
    [InlineData(ClassificationLevel.Unclassified)]
    [InlineData(ClassificationLevel.CUI)]
    [InlineData(ClassificationLevel.Secret)]
    public async Task UnknownHandling_DoesNotDerivePermissionFromStoredFloorOrDeclaredOfferingImpact(ClassificationLevel floor)
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var profile = await db.CspProfiles.SingleAsync();
            profile.DefaultClassificationFloor = floor;
            profile.SetupRevision++;
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(CspProfileService.CacheKey);
        }
        var saved = await Save(NewOffering($"Synthetic IL6 descriptor {Guid.NewGuid():N}", "Other", "Manual SaaS", []));
        var committed = await CommitOffering(saved);
        committed.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
        var id = Guid.NewGuid();
        var bytesBefore = _factory.Files.Count;
        // Act
        var prepared = await Send(HttpMethod.Post, "/api/csp/package-imports/upload-intents",
            new { expectedSetupRevision = Revision(current), intent = new
            {
                intentId = id, schemaVersion = 1, packageName = "Synthetic source", entryPoint = "ActivePortal",
                associationMode = "Unassociated", offeringHintId = (Guid?)null, context = (object?)null,
                files = new[] { new { ordinal = 0, fileName = "synthetic.txt", mediaType = "text/plain", byteLength = 1, sha256 = new string('A', 64) } },
                handlingPolicyVersion = "Not an approval",
                declaredContent = new { classification = floor.ToString(), markings = Array.Empty<string>(), containsOnlySyntheticData = true }
            } }, id.ToString());
        // Assert
        current.GetProperty("handling").GetProperty("state").GetString().Should().Be("Unknown");
        current.GetProperty("handling").GetProperty("uploadsPermitted").GetBoolean().Should().BeFalse();
        current.GetProperty("handling").GetProperty("analysisPermitted").GetBoolean().Should().BeFalse();
        prepared.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _factory.Files.Count.Should().Be(bytesBefore);
        using var verifyScope = _factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verify.CspProfiles.SingleAsync()).DefaultClassificationFloor.Should().Be(floor);
        (await verify.Set<CspPackageUploadIntent>().CountAsync(x => x.Id == id)).Should().Be(0);
        (await verify.CspPackages.CountAsync(x => x.IdempotencyKey == id.ToString())).Should().Be(0);
    }

    [Fact]
    public async Task ExplicitOfferingDeferral_AllowsActivationWithoutCreatingOfferingBoundaryOrReceipt()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        int offeringsBefore;
        int packagesBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            offeringsBefore = await db.Set<ProviderOffering>().CountAsync();
            packagesBefore = await db.CspPackages.CountAsync();
            var profile = await db.CspProfiles.SingleAsync();
            profile.OnboardingState = OnboardingState.InWizard;
            profile.OnboardingCompletedAt = null;
            profile.SetupRevision++;
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(CspProfileService.CacheKey);
        }
        var saved = await Save(new { choice = "Deferred", deferral = new { reason = "First offering will be defined later", ownerRole = "CSP.Admin" } },
            ensureActive: false);
        // Act
        var completed = await Send(HttpMethod.Post, "/api/csp/onboarding/setup/completion", new
        {
            expectedRevision = Revision(saved), expectedProfileRevision = saved.GetProperty("profileRevision").GetInt64(),
            confirmed = true, acknowledgedUnresolvedIntentIds = Array.Empty<Guid>()
        }, Guid.NewGuid().ToString());
        // Assert
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verifyScope = _factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var active = await verify.CspProfiles.SingleAsync();
        active.OnboardingState.Should().Be(OnboardingState.Active);
        active.OnboardingCompletedAt.Should().NotBeNull();
        (await verify.Set<ProviderOffering>().CountAsync()).Should().Be(offeringsBefore);
        (await verify.CspPackages.CountAsync()).Should().Be(packagesBefore);
        var draft = await verify.Set<ProviderSetupDraft>().SingleAsync(x => x.ProviderId == active.Id);
        JsonSerializer.Deserialize<JsonElement>(draft.DraftJson).GetProperty("firstOffering").GetProperty("choice")
            .GetString().Should().Be("Deferred");
        draft.CompletionSnapshotJson.Should().NotBeNull();
    }

    private static object NewOffering(string name, string environment, string? environmentLabel, string[] environments,
        string declaredModel = "Software", string declaredManagement = "Provider",
        string? canonicalModel = null, string? canonicalManagement = null) => new
    {
        choice = "New", name, description = "Synthetic declared service, not authorization", environments,
        serviceModel = canonicalModel, managementArrangement = canonicalManagement,
        serviceDescription = new { environmentKind = environment, environmentLabel, serviceModel = declaredModel,
            managedBy = declaredManagement, intendedUse = "Synthetic documentation preparation", declaredImpactLevel = "IL6" }
    };
    private async Task<JsonElement> Save(object firstOffering, bool ensureActive = true)
    {
        if (ensureActive) await _factory.EnsureActiveCspProfileAsync();
        var state = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
        var response = await Send(HttpMethod.Put, "/api/csp/onboarding/setup/draft", new
        {
            expectedRevision = Revision(state),
            draft = new
            {
                currentScreen = "p-offering",
                details = new { displayName = "Synthetic provider", legalEntityName = "Synthetic operator", serviceContactEmail = "support@example.invalid" },
                securityContact = new { choice = "Deferred", deferral = new { reason = "Reviewer later", ownerRole = "CSP.Admin" } },
                firstOffering,
                sources = new { choice = "Deferred", intentIds = Array.Empty<Guid>(), deferral = new { reason = "Sources later", ownerRole = "CSP.Admin" } }
            }
        }, Guid.NewGuid().ToString());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await Data(response)).GetProperty("current").GetProperty("state").Clone();
    }
    private Task<HttpResponseMessage> CommitOffering(JsonElement state) => Send(HttpMethod.Post, "/api/csp/onboarding/setup/commits", new
    {
        expectedRevision = Revision(state), expectedProfileRevision = state.GetProperty("profileRevision").GetInt64(), section = "FirstOffering"
    }, Guid.NewGuid().ToString());
    private static long Revision(JsonElement state) => state.GetProperty("draft").ValueKind == JsonValueKind.Null ? 0
        : state.GetProperty("draft").GetProperty("revision").GetInt64();
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object body, string key)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(request);
    }
    private static async Task<JsonElement> Data(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
}
