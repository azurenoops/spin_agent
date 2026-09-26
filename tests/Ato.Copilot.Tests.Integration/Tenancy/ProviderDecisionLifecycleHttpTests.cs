using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class ProviderDecisionLifecycleHttpTests : IClassFixture<PackageImportFactory>
{
    private readonly PackageImportFactory _factory;
    private readonly HttpClient _client;
    public ProviderDecisionLifecycleHttpTests(PackageImportFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.GetActiveContext().IsCspAdmin = true;
        factory.GetActiveContext().ImpersonatedTenantId = null;
    }

    [Fact]
    public async Task HumanRecordingAndLifecycle_AdvanceConcurrencyFence_WithoutRewritingSnapshot()
    {
        // Arrange
        var (offering, draft, citations) = await Draft();
        var record = draft.GetProperty("recordId").GetGuid();
        var revisionId = draft.GetProperty("revisionId").GetGuid();
        var hash = draft.GetProperty("snapshotHash").GetString();
        // Act
        var recorded = await Post($"/api/csp/offerings/{offering}/authorization-records/{record}/record", new
        {
            expectedRevision = draft.GetProperty("revision").GetInt64(), revisionId, snapshotHash = hash,
            rationale = "Human checked retained synthetic source; not a real ATO."
        });
        var decision = await Data(recorded);
        var fence = decision.GetProperty("revision").GetInt64();
        var lifecycle = new { expectedRevision = fence, kind = "Withdrawn", effectiveOn = "2020-01-01", rationale = "Source withdrawal", citations };
        var first = await Post($"/api/csp/offerings/{offering}/authorization-records/{record}/lifecycle-events", lifecycle);
        var stale = await Post($"/api/csp/offerings/{offering}/authorization-records/{record}/lifecycle-events", lifecycle);
        var current = await Data(await _client.GetAsync($"/api/csp/offerings/{offering}/authorization-records/{record}"));
        // Assert
        recorded.StatusCode.Should().Be(HttpStatusCode.OK);
        decision.GetProperty("impactReviewRequired").GetBoolean().Should().BeTrue(
            "recording source metadata is not approval of offering publication or applicability");
        fence.Should().BeGreaterThan(draft.GetProperty("revision").GetInt64());
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        current.GetProperty("revision").GetInt64().Should().BeGreaterThan(fence);
        current.GetProperty("revisionId").GetGuid().Should().Be(revisionId);
        current.GetProperty("snapshotHash").GetString().Should().Be(hash);
        current.GetProperty("currentStanding").GetString().Should().Be("Withdrawn");
    }

    [Fact]
    public async Task ConcurrentOfferingReceipts_CommitOneExactIdempotentOutcome()
    {
        // Arrange
        var key = Guid.NewGuid().ToString("N");
        var input = new { name = "Concurrent synthetic offering", description = "", environments = new[] { "AzureCloud" } };
        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Post("/api/csp/offerings", input, key)));
        // Assert
        responses.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.Created);
        var records = await Task.WhenAll(responses.Select(Data));
        records.Select(x => x.GetProperty("offeringId").GetGuid()).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task DecisionRecording_RequiresRetainedSource_NotJustProviderAssertion()
    {
        // Arrange
        var (offering, draft, _) = await Draft(false);
        // Act
        var response = await Post($"/api/csp/offerings/{offering}/authorization-records/{draft.GetProperty("recordId").GetGuid()}/record", new
        {
            expectedRevision = draft.GetProperty("revision").GetInt64(),
            revisionId = draft.GetProperty("revisionId").GetGuid(), snapshotHash = draft.GetProperty("snapshotHash").GetString(),
            rationale = "Unsupported provider assertion"
        });
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OfferingUpload_PersistsExactAssociationAndSourceManifest_BeforeReceipt()
    {
        // Arrange
        var (offering, draft, _) = await Draft(false);
        var boundary = draft.GetProperty("boundaryRevisionId").GetGuid();
        var state = await Data(await _client.GetAsync($"/api/csp/offerings/{offering}"));
        var revision = state.GetProperty("revision").GetInt64();
        var key = Guid.NewGuid().ToString("N");
        // Act
        var received = await Upload(offering, boundary, revision, key);
        var replay = await Upload(offering, boundary, revision, key);
        received.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var receipt = await Data(received);
        var originalPackage = receipt.GetProperty("package").GetProperty("packageId").GetGuid();
        var current = await Data(await _client.GetAsync($"/api/csp/offerings/{offering}"));
        var successor = await Data(await Upload(offering, boundary, current.GetProperty("revision").GetInt64(), Guid.NewGuid().ToString("N")));
        // Assert
        replay.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Data(replay)).GetProperty("package").GetProperty("packageId").GetGuid().Should().Be(originalPackage);
        receipt.GetProperty("packageVersion").GetProperty("manifestHash").GetString().Should()
            .Be(successor.GetProperty("packageVersion").GetProperty("manifestHash").GetString(),
                "a source manifest hash must not change with receipt idempotency or aggregate concurrency metadata");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var package = await db.CspPackages.SingleAsync(x => x.Id == originalPackage);
        package.OfferingId.Should().Be(offering);
        package.BoundaryRevisionId.Should().Be(boundary);
        package.PackageVersionId.Should().Be(receipt.GetProperty("packageVersion").GetProperty("packageVersionId").GetGuid());
        (await db.CspPackageAudits.CountAsync(x => x.PackageId == originalPackage && x.Action == "ReceiptReplayed")).Should().Be(1);
        var original = await db.CspPackageEntries.SingleAsync(x => x.PackageId == originalPackage && x.IsOriginal);
        _factory.Files[original.StorageKey!].Should().Equal(System.Text.Encoding.UTF8.GetBytes("Synthetic retained package bytes."));
    }

    [Fact]
    public async Task OfferingUpload_RejectsStaleAndForeignBoundary_WithoutRetainingFilesOrRows()
    {
        // Arrange
        var (offering, decision, _) = await Draft(false);
        var (other, foreignDecision, _) = await Draft(false);
        var state = await Data(await _client.GetAsync($"/api/csp/offerings/{offering}"));
        var revision = state.GetProperty("revision").GetInt64();
        var filesBefore = _factory.Files.Count;
        // Act
        var stale = await Upload(offering, decision.GetProperty("boundaryRevisionId").GetGuid(), revision - 1, Guid.NewGuid().ToString("N"));
        var foreign = await Upload(offering, foreignDecision.GetProperty("boundaryRevisionId").GetGuid(), revision, Guid.NewGuid().ToString("N"));
        // Assert
        other.Should().NotBe(offering);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Files.Count.Should().Be(filesBefore);
        var packages = await Data(await _client.GetAsync($"/api/csp/offerings/{offering}/package-versions"));
        packages.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("ProviderDecision", "DATO")]
    [InlineData("InheritedMicrosoftReference", "Authorized")]
    public async Task Supersession_RejectsNegativeDecisionOrInheritedReferenceAsProviderReplacement(string kind, string stated)
    {
        // Arrange
        var (offering, draft, citations) = await Draft();
        var original = await Record(offering, draft);
        var current = await Data(await _client.GetAsync($"/api/csp/offerings/{offering}"));
        var replacement = await Data(await Post($"/api/csp/offerings/{offering}/authorization-records", new
        {
            expectedOfferingRevision = current.GetProperty("revision").GetInt64(),
            boundaryRevisionId = draft.GetProperty("boundaryRevisionId").GetGuid(), sourceCandidateRefs = Array.Empty<object>(),
            recordKind = kind, reference = "SYNTHETIC-REPLACEMENT", issuingAuthority = "Synthetic authority",
            decisionAsStated = stated, expiryBasis = "NoExpiryStated", scopeStatement = "Synthetic recorded scope",
            conditions = Array.Empty<string>(), citations
        }));
        var recordedReplacement = await Record(offering, replacement);
        // Act
        var response = await Post($"/api/csp/offerings/{offering}/authorization-records/{draft.GetProperty("recordId").GetGuid()}/lifecycle-events", new
        {
            expectedRevision = original.GetProperty("revision").GetInt64(), kind = "Superseded", effectiveOn = "2020-01-01",
            replacementRevisionId = recordedReplacement.GetProperty("revisionId").GetGuid(), rationale = "Invalid substitution", citations
        });
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var retained = await Data(await _client.GetAsync($"/api/csp/offerings/{offering}/authorization-records/{draft.GetProperty("recordId").GetGuid()}"));
        retained.GetProperty("currentStanding").GetString().Should().Be("CurrentAsRecorded");
    }

    private async Task<JsonElement> Record(Guid offering, JsonElement draft) =>
        await Data(await Post($"/api/csp/offerings/{offering}/authorization-records/{draft.GetProperty("recordId").GetGuid()}/record", new
        {
            expectedRevision = draft.GetProperty("revision").GetInt64(), revisionId = draft.GetProperty("revisionId").GetGuid(),
            snapshotHash = draft.GetProperty("snapshotHash").GetString(), rationale = "Source metadata explicitly reviewed"
        }));

    private async Task<HttpResponseMessage> Upload(Guid offering, Guid boundary, long revision, string key)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Synthetic source package"), "name");
        content.Add(new StringContent(boundary.ToString("D")), "boundaryRevisionId");
        content.Add(new StringContent(revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedOfferingRevision");
        content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("Synthetic retained package bytes.")), "files", "source.txt");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/csp/offerings/{offering}/package-versions") { Content = content };
        request.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(request);
    }

    private async Task<(Guid Offering, JsonElement Decision, object[] Citations)> Draft(bool source = true)
    {
        var offering = await Data(await Post("/api/csp/offerings", new
        {
            name = $"Synthetic {Guid.NewGuid():N}", description = "", environments = new[] { "AzureCloud" }
        }));
        var id = offering.GetProperty("offeringId").GetGuid();
        var boundary = await Data(await Post($"/api/csp/offerings/{id}/boundary-revisions", new
        {
            expectedOfferingRevision = offering.GetProperty("revision").GetInt64(), name = "Synthetic boundary",
            scopeStatement = "Recorded source scope only", services = Array.Empty<string>(),
            componentSnapshotIds = Array.Empty<Guid>(), includedScopes = Array.Empty<object>(), exclusions = Array.Empty<object>(),
            providerResponsibilities = Array.Empty<string>(), customerResponsibilities = Array.Empty<string>(), citations = Array.Empty<object>()
        }));
        object[] citations = [];
        if (source)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var provider = await db.CspProfiles.Select(x => x.Id).SingleAsync();
            var package = new CspPackage { ProviderId = provider, Name = "Synthetic decision source", CreatedBy = "test", IdempotencyKey = Guid.NewGuid().ToString("N"), ContentHash = new string('A', 64) };
            var entry = new CspPackageEntry
            {
                PackageId = package.Id, IsOriginal = true, StableKey = "root", FileName = "decision.txt",
                ArchivePath = "decision.txt", Status = "Processed", MediaType = "text/plain",
                SegmentsJson = JsonSerializer.Serialize(new[] { new { Locator = "line:1", Text = "Synthetic authority decision and withdrawal evidence." } })
            };
            db.CspPackages.Add(package);
            db.CspPackageEntries.Add(entry);
            await db.SaveChangesAsync();
            citations = [new { packageId = package.Id, artifactId = entry.Id, archivePath = entry.ArchivePath, locator = "line:1", quote = "Synthetic authority decision and withdrawal evidence." }];
        }
        var draft = await Post($"/api/csp/offerings/{id}/authorization-records", new
        {
            expectedOfferingRevision = boundary.GetProperty("offeringRevision").GetInt64(),
            boundaryRevisionId = boundary.GetProperty("boundaryRevisionId").GetGuid(), sourceCandidateRefs = Array.Empty<object>(),
            recordKind = "ProviderDecision", reference = "SYNTHETIC-ONLY", issuingAuthority = "Synthetic source authority",
            decisionAsStated = "ATO", issuedOn = "2019-01-01", effectiveOn = "2019-01-01",
            expiryBasis = "NoExpiryStated", scopeStatement = "Synthetic source scope", conditions = Array.Empty<string>(), citations
        });
        draft.StatusCode.Should().Be(HttpStatusCode.Created);
        return (id, await Data(draft), citations);
    }

    private async Task<HttpResponseMessage> Post(string path, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
}
