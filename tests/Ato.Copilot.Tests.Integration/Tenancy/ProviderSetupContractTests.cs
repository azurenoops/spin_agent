using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

/// <summary>HTTP-only RED tests: no reference to not-yet-implemented setup types.</summary>
public sealed class ProviderSetupContractTests : IClassFixture<PackageImportFactory>
{
    private readonly PackageImportFactory _factory;
    private readonly HttpClient _client;

    public ProviderSetupContractTests(PackageImportFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.GetActiveContext().IsCspAdmin = true;
        factory.GetActiveContext().ImpersonatedTenantId = null;
    }

    [Fact]
    public async Task SetupRead_PreservesActiveProfile_AndDoesNotInventSavedDraftOrHandlingAuthority()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        // Act
        var response = await _client.GetAsync("/api/csp/onboarding/setup");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await Data(response);
        data.GetProperty("profile").GetProperty("onboardingState").GetString().Should().Be("Active");
        data.GetProperty("handling").GetProperty("state").GetString().Should().Be("Unknown");
        data.GetProperty("handling").GetProperty("uploadsPermitted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PartialDraft_RehydratesInNewClient_WithExplicitDeferrals_WithoutProfileMutation()
    {
        // Arrange
        var before = await ReadState();
        var name = $"Synthetic partial {Guid.NewGuid():N}";
        var body = new { expectedRevision = Revision(before), draft = Draft(name) };
        // Act
        var saved = await Write(HttpMethod.Put, "/api/csp/onboarding/setup/draft", body, Guid.NewGuid().ToString());
        using var fresh = _factory.CreateClient();
        var reread = await fresh.GetAsync("/api/csp/onboarding/setup");
        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await Data(reread);
        data.GetProperty("draft").GetProperty("fields").GetProperty("details").GetProperty("displayName").GetString().Should().Be(name);
        data.GetProperty("draft").GetProperty("fields").GetProperty("sources").GetProperty("choice").GetString().Should().Be("Deferred");
        data.GetProperty("profile").GetRawText().Should().Be(before.GetProperty("profile").GetRawText());
    }

    [Fact]
    public async Task DraftReplay_IsSameCommittedOutcome_ChangedIntentAndStaleRevisionConflict()
    {
        // Arrange
        var before = await ReadState();
        var key = Guid.NewGuid().ToString();
        var body = new { expectedRevision = Revision(before), draft = Draft("Synthetic original") };
        // Act
        var first = await Write(HttpMethod.Put, "/api/csp/onboarding/setup/draft", body, key);
        var replay = await Write(HttpMethod.Put, "/api/csp/onboarding/setup/draft", body, key);
        var changed = await Write(HttpMethod.Put, "/api/csp/onboarding/setup/draft",
            new { expectedRevision = Revision(before), draft = Draft("Different synthetic intent") }, key);
        var stale = await Write(HttpMethod.Put, "/api/csp/onboarding/setup/draft", body, Guid.NewGuid().ToString());
        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data(replay)).GetProperty("committedOutcome").GetRawText()
            .Should().Be((await Data(first)).GetProperty("committedOutcome").GetRawText());
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task PrivateSetupAndRecovery_RejectSubscriberOrSupport(bool admin, bool support)
    {
        // Arrange
        _factory.GetActiveContext().IsCspAdmin = admin;
        _factory.GetActiveContext().ImpersonatedTenantId = support ? Guid.NewGuid() : null;
        // Act
        var state = await _client.GetAsync("/api/csp/onboarding/setup");
        var reconcile = await _client.PostAsJsonAsync("/api/csp/package-imports/receipt-reconciliation",
            new { requestKey = Guid.NewGuid().ToString(), intentHash = new string('A', 64) });
        // Assert
        state.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reconcile.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SavedDeferrals_ProjectStableNamedActions_WithoutCreatingReceipts()
    {
        // Arrange
        var state = await ReadState();
        var save = await Write(HttpMethod.Put, "/api/csp/onboarding/setup/draft",
            new { expectedRevision = Revision(state), draft = Draft("Synthetic deferred provider") }, Guid.NewGuid().ToString());
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        // Act
        var first = await Data(await _client.GetAsync("/api/csp/onboarding/setup/actions"));
        var second = await Data(await _client.GetAsync("/api/csp/onboarding/setup/actions"));
        // Assert
        var items = first.GetProperty("items").EnumerateArray().ToArray();
        items.Should().Contain(x => x.GetProperty("state").GetString() == "Deferred"
            && x.GetProperty("ownerRole").GetString() == "CSP.Admin");
        items.Select(x => x.GetProperty("actionId").GetString()).Should()
            .Equal(second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("actionId").GetString()));
    }

    [Fact]
    public async Task CommandReconciliation_ReturnsHistoricalOutcomeAndFreshStateSeparately()
    {
        // Arrange
        var state = await ReadState();
        var key = Guid.NewGuid().ToString();
        var first = await Write(HttpMethod.Put, "/api/csp/onboarding/setup/draft",
            new { expectedRevision = Revision(state), draft = Draft("Synthetic replay") }, key);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        // Act
        var replay = await _client.PostAsJsonAsync("/api/csp/onboarding/setup/commands/reconcile",
            new { operation = "SaveDraft", requestKey = key });
        // Assert
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await Data(replay);
        data.GetProperty("outcome").GetString().Should().Be("Committed");
        data.GetProperty("historicalCommitSnapshot").ValueKind.Should().Be(JsonValueKind.Object);
        data.GetProperty("current").GetProperty("projectionState").GetString().Should().Be("Available");
        data.GetProperty("committedOutcome").TryGetProperty("access", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CrossAdministratorReplay_PreservesCommitIdentity_ButProjectsCurrentActorAndRevocation()
    {
        // Arrange
        var state = await ReadState();
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProviderSetupService>();
        var firstActor = new ProviderSetupActor("synthetic-directory", "first-administrator", "First synthetic administrator");
        var nextActor = new ProviderSetupActor("synthetic-directory", "next-administrator", "Next synthetic administrator");
        var key = Guid.NewGuid().ToString();
        var saved = await service.SaveAsync(new SaveProviderSetupDraft(Revision(state),
            JsonSerializer.SerializeToElement(Draft("Synthetic cross-admin replay"))), key, firstActor, default);
        // Act
        var replay = await service.ReconcileAsync(new("SaveDraft", key), nextActor, default);
        var current = JsonSerializer.SerializeToElement(replay.Current);
        _factory.GetActiveContext().IsCspAdmin = false;
        var denied = () => service.ReconcileAsync(new("SaveDraft", key), firstActor, default);
        // Assert
        replay.CommittedOutcome.Should().Be(saved.CommittedOutcome);
        current.GetProperty("actor").GetProperty("ObjectId").GetString().Should().Be(nextActor.ObjectId);
        replay.HistoricalCommitSnapshot!.Value.GetProperty("committedBy").GetProperty("objectId")
            .GetString().Should().Be(firstActor.ObjectId);
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private async Task<JsonElement> ReadState()
    {
        var response = await _client.GetAsync("/api/csp/onboarding/setup");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await Data(response);
    }

    private static long Revision(JsonElement state) => state.GetProperty("draft").ValueKind == JsonValueKind.Null
        ? 0 : state.GetProperty("draft").GetProperty("revision").GetInt64();

    private static object Draft(string name) => new
    {
        currentScreen = "p-details",
        details = new { displayName = name, legalEntityName = "", serviceContactEmail = "" },
        securityContact = new { choice = "Deferred", deferral = new { reason = "Select reviewer later", ownerRole = "CSP.Admin" } },
        firstOffering = new { choice = "Deferred", deferral = new { reason = "Record service later", ownerRole = "CSP.Admin" } },
        sources = new { choice = "Deferred", intentIds = Array.Empty<Guid>(),
            deferral = new { reason = "Prepare sources later", ownerRole = "CSP.Admin" } }
    };

    private async Task<HttpResponseMessage> Write(HttpMethod method, string path, object body, string key)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
}
