using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class ProviderSetupReceiptContractTests : IAsyncLifetime
{
    private readonly PackageImportFactory _factory;
    private readonly HttpClient _client;
    private readonly ProviderHandlingOptions _policy;

    public ProviderSetupReceiptContractTests()
    {
        var factory = new PackageImportFactory();
        _factory = factory;
        _client = factory.CreateClient();
        factory.GetActiveContext().IsCspAdmin = true;
        factory.GetActiveContext().ImpersonatedTenantId = null;
        _policy = factory.Services.GetRequiredService<IOptions<ProviderHandlingOptions>>().Value;
        _policy.PolicyId = null;
        _policy.Version = null;
        _policy.ApprovalReference = null;
        _policy.UploadsEnabled = false;
        _policy.AnalysisEnabled = false;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task UnknownHandling_RejectsIntentWithoutStoringSourceBytes()
    {
        // Arrange
        var state = await SaveDraft();
        var before = _factory.Files.Count;
        var intent = Intent(Guid.NewGuid(), "synthetic source");
        // Act
        var response = await Send(HttpMethod.Post, "/api/csp/package-imports/upload-intents",
            new { expectedSetupRevision = Revision(state), intent }, intent.intentId.ToString());
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _factory.Files.Count.Should().Be(before);
    }

    [Theory]
    [InlineData("Onboarding")]
    [InlineData("ActivePortal")]
    public async Task ActivePortalTwentySixthReceipt_PreservesHistoryWithoutChangingCompletedSetupDraft(string originalEntryPoint)
    {
        // Arrange
        AllowSynthetic();
        var state = await SaveDraft();
        var savedRevision = Revision(state);
        var savedFields = state.GetProperty("draft").GetProperty("fields").GetRawText();
        var ids = new List<Guid>();
        // Act
        for (var index = 0; index < 26; index++)
        {
            var entryPoint = index < 25 ? originalEntryPoint : "ActivePortal";
            var intent = Intent(Guid.NewGuid(), "synthetic source") with { entryPoint = entryPoint };
            ids.Add(intent.intentId);
            var draftState = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
            var preparation = await Send(HttpMethod.Post, "/api/csp/package-imports/upload-intents",
                new { expectedSetupRevision = entryPoint == "Onboarding" ? Revision(draftState) : 0, intent }, intent.intentId.ToString());
            preparation.StatusCode.Should().Be(HttpStatusCode.Created, $"active portal intake {index + 1} must not consume setup capacity");
            (await Upload(intent.intentId, "synthetic source")).StatusCode.Should().Be(HttpStatusCode.Accepted);
            if (index == 24)
            {
                var checkpoint = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
                savedRevision = Revision(checkpoint);
                savedFields = checkpoint.GetProperty("draft").GetProperty("fields").GetRawText();
            }
        }
        // Assert
        var current = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
        Revision(current).Should().Be(savedRevision);
        current.GetProperty("draft").GetProperty("fields").GetRawText().Should().Be(savedFields);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.Set<CspPackageUploadIntent>().CountAsync(x => ids.Contains(x.Id))).Should().Be(26);
        (await db.CspPackages.CountAsync(x => x.UploadIntentId.HasValue && ids.Contains(x.UploadIntentId.Value))).Should().Be(26);
        (await db.CspPackageEntries.CountAsync(x => db.CspPackages.Any(p => p.Id == x.PackageId
            && p.UploadIntentId.HasValue && ids.Contains(p.UploadIntentId.Value)) && x.IsOriginal)).Should().Be(26);
    }

    [Fact]
    public async Task ActivePortal_PreparesWithoutAnySetupDraft_AndListsRecoverableExactIntent()
    {
        // Arrange
        await using var independent = new PackageImportFactory();
        using var client = independent.CreateClient();
        independent.GetActiveContext().IsCspAdmin = true;
        independent.GetActiveContext().ImpersonatedTenantId = null;
        await independent.EnsureActiveCspProfileAsync();
        var policy = independent.Services.GetRequiredService<IOptions<ProviderHandlingOptions>>().Value;
        policy.PolicyId = "synthetic";
        policy.Version = "test-1";
        policy.ApprovalReference = "Synthetic test only";
        policy.ValidUntil = DateTimeOffset.UtcNow.AddHours(1);
        policy.AllowedClassifications = ["Unclassified"];
        policy.UploadsEnabled = true;
        policy.AnalysisEnabled = true;
        policy.SyntheticOnly = true;
        var intent = Intent(Guid.NewGuid(), "synthetic source");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/csp/package-imports/upload-intents")
        {
            Content = JsonContent.Create(new { expectedSetupRevision = 0, intent })
        };
        request.Headers.Add("Idempotency-Key", intent.intentId.ToString());
        // Act
        var response = await client.SendAsync(request);
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var list = await client.GetAsync("/api/csp/package-imports/upload-intents?entryPoint=ActivePortal&page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data(list)).GetProperty("items").EnumerateArray()
            .Should().Contain(item => item.GetProperty("intentId").GetGuid() == intent.intentId);
        using var scope = independent.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.Set<CspPackageUploadIntent>().SingleAsync(x => x.Id == intent.intentId)).DraftId.Should().BeNull();
        (await db.Set<ProviderSetupDraft>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UnassociatedReceipt_ReconcilesAcrossClients_AndSameKeyWrongBytesCannotReplaceIt()
    {
        // Arrange
        AllowSynthetic();
        var state = await SaveDraft();
        var intent = Intent(Guid.NewGuid(), "synthetic source");
        var prepared = await Send(HttpMethod.Post, "/api/csp/package-imports/upload-intents",
            new { expectedSetupRevision = Revision(state), intent }, intent.intentId.ToString());
        prepared.StatusCode.Should().Be(HttpStatusCode.Created);
        var savedIntent = await Data(prepared);
        // Act
        var receipt = await Upload(intent.intentId, "synthetic source");
        var wrong = await Upload(intent.intentId, "different bytes");
        using var restarted = _factory.CreateClient();
        var recovered = await restarted.PostAsJsonAsync("/api/csp/package-imports/receipt-reconciliation",
            new { requestKey = intent.intentId.ToString(), intentHash = savedIntent.GetProperty("intentHash").GetString() });
        // Assert
        receipt.StatusCode.Should().Be(HttpStatusCode.Accepted);
        wrong.StatusCode.Should().Be(HttpStatusCode.Conflict);
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        var package = await Data(receipt);
        package.GetProperty("association").ValueKind.Should().Be(JsonValueKind.Null);
        var recovery = await Data(recovered);
        recovery.GetProperty("outcome").GetString().Should().Be("Confirmed");
        recovery.GetProperty("receipt").GetProperty("packageId").GetGuid().Should().Be(package.GetProperty("packageId").GetGuid());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var id = package.GetProperty("packageId").GetGuid();
        var row = await db.CspPackages.SingleAsync(x => x.Id == id);
        row.RequiresOfferingAssociation.Should().BeTrue();
        row.UploadIntentId.Should().Be(intent.intentId);
        row.PublicationState.Should().Be("Unpublished");
    }

    [Fact]
    public async Task UnknownReceipt_CannotBecomeDeferral_AndCanBeAcknowledgedWithoutFalseReceipt()
    {
        // Arrange
        AllowSynthetic();
        var state = await SaveDraft();
        var intent = Intent(Guid.NewGuid(), "synthetic source") with { entryPoint = "Onboarding" };
        (await Send(HttpMethod.Post, "/api/csp/package-imports/upload-intents",
            new { expectedSetupRevision = Revision(state), intent }, intent.intentId.ToString())).StatusCode.Should().Be(HttpStatusCode.Created);
        var current = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
        // Act
        var discarded = await Send(HttpMethod.Put, "/api/csp/onboarding/setup/draft",
            new { expectedRevision = Revision(current), draft = BlankDraft() }, Guid.NewGuid().ToString());
        var completion = await Send(HttpMethod.Post, "/api/csp/onboarding/setup/completion",
            new { expectedRevision = Revision(current), expectedProfileRevision = current.GetProperty("profileRevision").GetInt64(),
                confirmed = true, acknowledgedUnresolvedIntentIds = new[] { intent.intentId } }, Guid.NewGuid().ToString());
        // Assert
        discarded.StatusCode.Should().Be(HttpStatusCode.Conflict);
        completion.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await Data(completion);
        result.GetProperty("current").GetProperty("state").GetProperty("uploadIntents").EnumerateArray()
            .Single(x => x.GetProperty("intentId").GetGuid() == intent.intentId).GetProperty("receipt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task RevokedPolicy_BlocksPreviouslyPreparedBytesBeforeStorage_WhileRecoveryRemainsReadable()
    {
        // Arrange
        AllowSynthetic();
        var state = await SaveDraft();
        var intent = Intent(Guid.NewGuid(), "synthetic source");
        (await Send(HttpMethod.Post, "/api/csp/package-imports/upload-intents",
            new { expectedSetupRevision = Revision(state), intent }, intent.intentId.ToString())).StatusCode.Should().Be(HttpStatusCode.Created);
        var before = _factory.Files.Count;
        _policy.UploadsEnabled = false;
        // Act
        var upload = await Upload(intent.intentId, "synthetic source");
        var recovered = await _client.GetAsync($"/api/csp/package-imports/upload-intents/{intent.intentId}");
        // Assert
        upload.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Files.Count.Should().Be(before);
    }

    private void AllowSynthetic()
    {
        _policy.PolicyId = "synthetic-test-policy";
        _policy.Version = "test-1";
        _policy.ApprovalReference = "Synthetic test approval only";
        _policy.ValidUntil = DateTimeOffset.UtcNow.AddHours(1);
        _policy.AllowedClassifications = ["Unclassified"];
        _policy.AllowedMarkings = [];
        _policy.SyntheticOnly = true;
        _policy.UploadsEnabled = true;
        _policy.AnalysisEnabled = true;
    }

    private static object BlankDraft() => new
    {
        currentScreen = "p-sources",
        details = new { displayName = "Synthetic provider", legalEntityName = "Synthetic operator", serviceContactEmail = "synthetic@example.invalid" },
        securityContact = new { choice = "Deferred", deferral = new { reason = "Reviewer later", ownerRole = "CSP.Admin" } },
        firstOffering = new { choice = "Deferred", deferral = new { reason = "Offering later", ownerRole = "CSP.Admin" } },
        sources = new { choice = "Deferred", intentIds = Array.Empty<Guid>(),
            deferral = new { reason = "Sources later", ownerRole = "CSP.Admin" } }
    };

    private async Task<JsonElement> SaveDraft()
    {
        await _factory.EnsureActiveCspProfileAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        // Each case isolates its private setup facts without deleting canonical profile or receipts.
        db.RemoveRange(await db.Set<ProviderSetupCommand>().ToListAsync());
        db.RemoveRange(await db.Set<CspPackageUploadIntent>().Where(x => !db.CspPackages.Any(p => p.UploadIntentId == x.Id)).ToListAsync());
        await db.SaveChangesAsync();
        var state = await Data(await _client.GetAsync("/api/csp/onboarding/setup"));
        var draft = JsonSerializer.SerializeToElement(BlankDraft());
        var retainedIds = state.GetProperty("uploadIntents").EnumerateArray().Select(x => x.GetProperty("intentId").GetGuid()).ToArray();
        if (retainedIds.Length > 0)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(draft.GetRawText())!;
            node["sources"]!["choice"] = "Intents";
            node["sources"]!["intentIds"] = JsonSerializer.SerializeToNode(retainedIds);
            draft = JsonSerializer.SerializeToElement(node);
        }
        var saved = await Send(HttpMethod.Put, "/api/csp/onboarding/setup/draft",
            new { expectedRevision = Revision(state), draft }, Guid.NewGuid().ToString());
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await Data(saved)).GetProperty("current").GetProperty("state").Clone();
    }

    private static long Revision(JsonElement state) => state.GetProperty("draft").ValueKind == JsonValueKind.Null ? 0
        : state.GetProperty("draft").GetProperty("revision").GetInt64();

    private static ProviderIntentFixture Intent(Guid id, string bytes) => new(id, 1, "Synthetic source", "ActivePortal", "Unassociated", null, null,
        [new(0, "synthetic.txt", "text/plain", Encoding.UTF8.GetByteCount(bytes), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bytes))))],
        "test-1", new("Unclassified", [], true));
    private sealed record FileFixture(int ordinal, string fileName, string mediaType, long byteLength, string sha256);
    private sealed record DeclarationFixture(string classification, string[] markings, bool containsOnlySyntheticData);
    private sealed record ProviderIntentFixture(Guid intentId, int schemaVersion, string packageName, string entryPoint,
        string associationMode, Guid? offeringHintId, object? context, FileFixture[] files, string handlingPolicyVersion, DeclarationFixture declaredContent);

    private async Task<HttpResponseMessage> Upload(Guid id, string bytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Synthetic source"), "name");
        var source = new ByteArrayContent(Encoding.UTF8.GetBytes(bytes));
        source.Headers.ContentType = new("text/plain");
        form.Add(source, "files", "synthetic.txt");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/csp/inherited-components/import") { Content = form };
        request.Headers.Add("Idempotency-Key", id.ToString());
        request.Headers.Add("X-Provider-Upload-Intent", id.ToString());
        request.Headers.Add("Prefer", "respond-async");
        return await _client.SendAsync(request);
    }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object body, string key)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(request);
    }
    private static async Task<JsonElement> Data(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Clone();
}
