using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class CspPackageLifecycleHttpTests : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly MultiTenantWebApplicationFactory<McpProgram> _factory;
    private readonly HttpClient _client;

    public CspPackageLifecycleHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        factory.GetActiveContext().IsCspAdmin = true;
    }

    [Fact]
    public async Task BoundaryScopeQuery_ReturnsTypedCitedClaim_WithoutPublishing()
    {
        // Arrange
        var (path, _) = await ReceiveReferenceAsync("""
            {"authorizationReferences":[{"reference":"Synthetic reference"}],
             "boundaryClaims":[{"kind":"BoundaryClaim","id":"synthetic-scope","name":"Synthetic included scope",
               "claim":{"boundary":{"subject":"Synthetic offering","relationship":"Included",
                 "scope":"Only the explicitly listed synthetic collaboration services."}}}]}
            """, expectedProcessingState: "ReadyForReview");
        var before = await DataAsync<PackageStatus>(await _client.GetAsync(path));
        // Act
        using var response = await _client.GetAsync(path + "/candidates?page=1&pageSize=25&type=BoundaryClaim");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await DataAsync<JsonElement>(response);
        data.GetProperty("total").GetInt32().Should().Be(1);
        var candidate = data.GetProperty("items").EnumerateArray().Single();
        candidate.GetProperty("type").GetString().Should().Be("BoundaryClaim");
        candidate.GetProperty("claim").GetProperty("boundary").GetProperty("scope").GetString()
            .Should().Be("Only the explicitly listed synthetic collaboration services.");
        candidate.GetProperty("citations").GetArrayLength().Should().BeGreaterThan(0);
        candidate.GetProperty("reviewState").GetString().Should().Be("NeedsReview");
        (await DataAsync<PackageStatus>(await _client.GetAsync(path))).Should().BeEquivalentTo(before);
        before.PublicationState.Should().Be("Unpublished");
        _factory.GetActiveContext().IsCspAdmin = false;
        (await _client.GetAsync(path + "/candidates?type=BoundaryClaim")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PersistedRecoveryAndReferenceReads_ArePrivateAndNotCacheable()
    {
        // Arrange
        var (path, _) = await ReceiveReferenceAsync();
        using var refreshedClient = _factory.CreateClient();

        // Act
        using var recovery = await refreshedClient.GetAsync(path + "/review-state");
        using var references = await refreshedClient.GetAsync(path + "/candidates?type=AuthorizationReference");

        // Assert
        foreach (var response in new[] { recovery, references })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl.Should().NotBeNull();
            response.Headers.CacheControl!.Private.Should().BeTrue();
            response.Headers.CacheControl.NoStore.Should().BeTrue();
        }
        var saved = await DataAsync<PackageReviewStateResponse>(recovery);
        saved.Preview.Should().BeNull();
        saved.Publication.Should().BeNull();
        (await DataAsync<Page<PackageCandidateResponse>>(references)).Items
            .Should().ContainSingle().Which.AuthorizationReference.Should().NotBeNull();
    }

    [Fact]
    public async Task CitedReference_EditReviewReject_PersistsRevisionsAndAudit_WithoutAuthorizationEffects()
    {
        // Arrange
        var (path, proposed) = await ReceiveReferenceAsync();
        proposed.ReviewState.Should().Be("NeedsReview");
        proposed.Citations.Should().NotBeEmpty();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var packageId = Guid.Parse(path.Split('/')[^1]);
        var decisionsBefore = await db.AuthorizationDecisions.CountAsync();
        var systemsBefore = await db.RegisteredSystems.CountAsync();
        var releasesBefore = await db.ProviderCapabilityReleases.CountAsync();
        var componentsBefore = await db.CspInheritedComponents.CountAsync();
        var edit = new EditPackageCandidateRequest(proposed.Revision, proposed.Name, proposed.Description,
            proposed.ComponentType, proposed.Classification, proposed.ServiceCategory,
            proposed.ControlDuties, proposed.ContributorIds, "NeedsReview", null, null,
            proposed.AuthorizationReference! with { Issuer = "Human-corrected synthetic issuer" });
        var endpoint = path + $"/candidates/{proposed.CandidateId:D}";

        // Act
        var edited = await DataAsync<PackageCandidateResponse>(await _client.PatchAsJsonAsync(endpoint, edit));
        var stale = await _client.PatchAsJsonAsync(endpoint, edit);
        var reviewed = await DataAsync<PackageCandidateResponse>(await _client.PatchAsJsonAsync(endpoint,
            edit with { ExpectedRevision = edited.Revision, ReviewAction = "Reviewed" }));
        const string rationale = "Synthetic reference withdrawn after human review.";
        var rejected = await DataAsync<PackageCandidateResponse>(await _client.PatchAsJsonAsync(endpoint,
            edit with { ExpectedRevision = reviewed.Revision, ReviewAction = "Rejected", Rationale = rationale }));
        using var refreshedClient = _factory.CreateClient();
        var retained = (await DataAsync<Page<PackageCandidateResponse>>(await refreshedClient.GetAsync(
            path + "/candidates?type=AuthorizationReference&reviewState=Rejected"))).Items.Single();

        // Assert
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        edited.Revision.Should().Be(proposed.Revision + 1);
        reviewed.Revision.Should().Be(edited.Revision + 1);
        reviewed.ReviewState.Should().Be("Reviewed");
        rejected.Revision.Should().Be(reviewed.Revision + 1);
        retained.Should().BeEquivalentTo(rejected);
        retained.ReviewState.Should().Be("Rejected");
        retained.Rationale.Should().Be(rationale);
        retained.AuthorizationReference!.Issuer.Should().Be("Human-corrected synthetic issuer");
        retained.Citations.Should().BeEquivalentTo(proposed.Citations);
        retained.PublishedRecordId.Should().BeNull();
        (await DataAsync<Page<PackageCandidateResponse>>(await refreshedClient.GetAsync(
            path + "/candidates?type=AuthorizationReference&reviewState=Reviewed"))).Items.Should().BeEmpty();
        var auditRows = await db.CspPackageAudits.AsNoTracking()
            .Where(x => x.PackageId == packageId && x.Action == "CandidateDecision").ToListAsync();
        auditRows.Should().HaveCount(3).And.OnlyContain(x => x.Actor.Length > 0);
        var audit = auditRows.Select(x => JsonSerializer.Deserialize<CandidateDecisionAudit>(
            x.Detail, new JsonSerializerOptions(JsonSerializerDefaults.Web))!).OrderBy(x => x.Revision).ToArray();
        audit.Select(x => x.ReviewAction).Should().Equal("NeedsReview", "Reviewed", "Rejected");
        audit.Select(x => x.Revision).Should().Equal(edited.Revision, reviewed.Revision, rejected.Revision);
        audit[^1].Rationale.Should().Be(rationale);
        audit.Should().OnlyContain(x => x.SnapshotHash.Length == 64);
        (await db.AuthorizationDecisions.CountAsync()).Should().Be(decisionsBefore);
        (await db.RegisteredSystems.CountAsync()).Should().Be(systemsBefore);
        (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(releasesBefore);
        (await db.CspInheritedComponents.CountAsync()).Should().Be(componentsBefore);
        var status = await DataAsync<PackageStatus>(await refreshedClient.GetAsync(path));
        status.PublicationState.Should().Be("Unpublished");
        status.ProcessingState.Should().Be("NeedsAttention");
    }

    [Theory]
    [InlineData(256)]
    [InlineData(2000)]
    public async Task ReferenceOnlySource_ReviewsFullTitleWithoutComponentType(int referenceLength)
    {
        // Arrange
        var referenceText = new string('R', referenceLength);
        var source = JsonSerializer.Serialize(new
        {
            notice = "Synthetic surrounding text is not verified semantic analysis.",
            authorizationReferences = new[] { new { reference = referenceText } }
        });
        var (path, candidate) = await ReceiveReferenceAsync(source);
        candidate.Name.Should().Be(referenceText);
        candidate.AuthorizationReference!.Reference.Should().Be(referenceText);
        string? componentType = null;
        var edit = new
        {
            expectedRevision = candidate.Revision,
            candidate.Name,
            candidate.Description,
            componentType,
            candidate.Classification,
            candidate.ServiceCategory,
            candidate.ControlDuties,
            candidate.ContributorIds,
            reviewAction = "Reviewed",
            candidate.Rationale,
            candidate.DuplicateResolution,
            candidate.AuthorizationReference
        };

        // Act
        using var response = await _client.PatchAsJsonAsync(path + $"/candidates/{candidate.CandidateId:D}", edit);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reviewed = await DataAsync<PackageCandidateResponse>(response);
        reviewed.Name.Should().Be(referenceText);
        reviewed.AuthorizationReference.Should().BeEquivalentTo(candidate.AuthorizationReference);
        reviewed.ReviewState.Should().Be("Reviewed");
        reviewed.Revision.Should().Be(candidate.Revision + 1);
        reviewed.Citations.Should().BeEquivalentTo(candidate.Citations);
        reviewed.ComponentType.Should().Be(candidate.ComponentType);
        reviewed.PublishedRecordId.Should().BeNull();
    }

    [Fact]
    public async Task RealAuthorizationReference_RoundTripsHumanReviewWithoutVerifyingAuthorizationOrCompletingCoverage()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        const string source = """
            {"notice":"Synthetic source only; surrounding prose has not received semantic analysis.",
             "authorizationReferences":[{"reference":"Synthetic stated reference","issuer":"Synthetic issuer",
                "issuedAt":"2026-01-01","expiresAt":"2027-01-01"}]}
            """;
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(source));
        content.Headers.ContentType = new("application/json");
        form.Add(content, "files", "synthetic-reference.json");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/csp/inherited-components/import") { Content = form };
        upload.Headers.Add("Prefer", "respond-async");
        upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var id = (await DataAsync<PackageStatus>(await _client.SendAsync(upload))).PackageId;
        var path = $"/api/csp/package-imports/{id:D}";
        (await WaitForAnalysisAsync(path)).ProcessingState.Should().Be("NeedsAttention");
        var candidate = (await DataAsync<Page<PackageCandidateResponse>>(
            await _client.GetAsync(path + "/candidates?type=AuthorizationReference"))).Items.Single();
        candidate.AuthorizationReference!.Reference.Should().Be("Synthetic stated reference");
        candidate.Citations.Single().Quote.Should().Contain("Synthetic issuer");
        // Act
        await DataAsync<PackageCandidateResponse>(await _client.PatchAsJsonAsync(path + $"/candidates/{candidate.CandidateId:D}",
            new EditPackageCandidateRequest(candidate.Revision, candidate.Name, candidate.Description, candidate.ComponentType,
                candidate.Classification, candidate.ServiceCategory, candidate.ControlDuties, candidate.ContributorIds,
                "Reviewed", null, null, candidate.AuthorizationReference with { Issuer = "Human-reviewed synthetic issuer" })));
        // Assert
        var reviewed = (await DataAsync<Page<PackageCandidateResponse>>(
            await _client.GetAsync(path + "/candidates?type=AuthorizationReference&reviewState=Reviewed"))).Items.Single();
        reviewed.AuthorizationReference!.Issuer.Should().Be("Human-reviewed synthetic issuer");
        reviewed.PublishedRecordId.Should().BeNull();
        var status = await DataAsync<PackageStatus>(await _client.GetAsync(path));
        status.ProcessingState.Should().Be("NeedsAttention");
        status.PublicationState.Should().Be("Unpublished");
        status.Coverage.Unsupported.Should().Be(1);
    }

    [Fact]
    public async Task RealResume_KeepsCompletedReview_AndDoesNotClaimUnavailableSemanticAnalysisSucceeded()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var writer = new StreamWriter(archive.CreateEntry("complete.json").Open()))
                await writer.WriteAsync("""
                    {"components":[{"id":"retained-component","name":"Retained completed component",
                    "type":"service","description":"Synthetic completed source."}]}
                    """);
            await using (var writer = new StreamWriter(archive.CreateEntry("incomplete.txt").Open()))
                await writer.WriteAsync("Unstructured synthetic source requires unavailable semantic analysis.");
        }
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes.ToArray());
        content.Headers.ContentType = new("application/octet-stream");
        form.Add(content, "files", "resume.zip");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/csp/inherited-components/import") { Content = form };
        upload.Headers.Add("Prefer", "respond-async");
        upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var id = (await DataAsync<PackageStatus>(await _client.SendAsync(upload))).PackageId;
        var path = $"/api/csp/package-imports/{id:D}";
        var initial = await WaitForAnalysisAsync(path);
        initial.ProcessingState.Should().Be("NeedsAttention");
        var candidate = (await DataAsync<Page<PackageCandidateResponse>>(await _client.GetAsync(path + "/candidates"))).Items.Single();
        var reviewed = await DataAsync<PackageCandidateResponse>(await _client.PatchAsJsonAsync(path + $"/candidates/{candidate.CandidateId:D}",
            new EditPackageCandidateRequest(candidate.Revision, "Human-reviewed preserved component", candidate.Description,
                candidate.ComponentType, "Unclassified", "Audit", candidate.ControlDuties, candidate.ContributorIds,
                "Reviewed", null, null)));
        using var retry = new HttpRequestMessage(HttpMethod.Post, path + "/retry");
        retry.Headers.Add("Idempotency-Key", "retry-incomplete-entry");
        // Act
        (await _client.SendAsync(retry)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var resumed = await WaitForAnalysisAsync(path);
        // Assert
        resumed.ProcessingState.Should().Be("NeedsAttention");
        resumed.Coverage.Unsupported.Should().Be(1);
        var preserved = (await DataAsync<Page<PackageCandidateResponse>>(await _client.GetAsync(path + "/candidates"))).Items.Single();
        preserved.Should().BeEquivalentTo(reviewed);
        var entries = (await DataAsync<Page<PackageEntryResponse>>(await _client.GetAsync(path + "/entries"))).Items;
        var incomplete = entries.Single(x => x.ArchivePath.EndsWith("incomplete.txt"));
        (await _client.PatchAsJsonAsync(path + $"/entries/{incomplete.EntryId:D}",
            new ExcludePackageEntryRequest(incomplete.Revision, "Explicitly excluded unavailable unrelated source"))).StatusCode.Should().Be(HttpStatusCode.OK);
        using var excludedRetry = new HttpRequestMessage(HttpMethod.Post, path + "/retry");
        excludedRetry.Headers.Add("Idempotency-Key", "retry-after-exclusion");
        (await _client.SendAsync(excludedRetry)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var excluded = await WaitForAnalysisAsync(path);
        excluded.ProcessingState.Should().Be("ReadyForReview");
        excluded.Coverage.Excluded.Should().Be(1);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.CspPackageEntries.SingleAsync(x => x.PackageId == id && x.IsOriginal)).MediaType.Should().Be("application/octet-stream");
        var checkpoint = JsonSerializer.Deserialize<CspPackageAnalysisCheckpoint>(
            (await db.CspPackages.SingleAsync(x => x.Id == id)).AnalysisCheckpointJson!);
        checkpoint!.Entries.Should().OnlyContain(x => x.Content == null);
    }

    [Fact]
    public async Task RealWorkerAndAnalyzer_RetainPrivateSources_RequireHumanReview_AndPublishCanonicalRelease()
    {
        // Arrange
        await _factory.EnsureActiveCspProfileAsync();
        const string source = """
            {"components":[{
                "id":"synthetic-component","type":"service","name":"Synthetic audit service",
                "description":"Synthetic provider records administrative events."}],
              "capabilities":[{
                "id":"synthetic-capability","name":"Synthetic event logging",
                "description":"Synthetic provider records administrative events.",
                "componentIds":["synthetic-component"],"controlId":"AU-2","responsibility":"Provider"}]}
            """;
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(source));
        content.Headers.ContentType = new("application/json");
        form.Add(content, "files", "synthetic-oscal.json");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/csp/inherited-components/import") { Content = form };
        upload.Headers.Add("Prefer", "respond-async");
        upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var receipt = await _client.SendAsync(upload);
        receipt.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await DataAsync<PackageStatus>(receipt)).PackageId;
        var path = $"/api/csp/package-imports/{id:D}";
        var status = await WaitForAnalysisAsync(path);
        status.ProcessingState.Should().Be("ReadyForReview");
        var candidates = (await DataAsync<Page<PackageCandidateResponse>>(await _client.GetAsync(path + "/candidates"))).Items;
        candidates.Should().Contain(x => x.Type == "Component").And.Contain(x => x.Type == "Capability");
        candidates.Single(x => x.Type == "Component").ComponentType.Should().Be("Service");
        candidates.Should().OnlyContain(x => x.ReviewState == "NeedsReview" && x.Citations.Count > 0);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.CspInheritedComponents.CountAsync()).Should().Be(0);
        (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(0);
        var selected = candidates.Where(x => x.Type is "Component" or "Capability").ToArray();
        var premature = await DataAsync<PackagePreviewResponse>(await _client.PostAsJsonAsync(path + "/approval-previews",
            new PackagePreviewRequest(status.Revision, selected.Select(x => new PackageSelection(x.CandidateId, x.Revision)).ToArray())));
        premature.Blockers.Should().Contain(x => x.Contains("human review"));
        foreach (var candidate in selected)
        {
            var reviewed = await _client.PatchAsJsonAsync(path + $"/candidates/{candidate.CandidateId:D}",
                new EditPackageCandidateRequest(candidate.Revision, candidate.Name, candidate.Description,
                    candidate.ComponentType, "Unclassified", "Audit", candidate.ControlDuties,
                    candidate.ContributorIds, "Reviewed", null, null));
            reviewed.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        status = await DataAsync<PackageStatus>(await _client.GetAsync(path));
        candidates = (await DataAsync<Page<PackageCandidateResponse>>(await _client.GetAsync(path + "/candidates"))).Items;
        var preview = await DataAsync<PackagePreviewResponse>(await _client.PostAsJsonAsync(path + "/approval-previews",
            new PackagePreviewRequest(status.Revision, candidates.Where(x => x.Type is "Component" or "Capability")
                .Select(x => new PackageSelection(x.CandidateId, x.Revision)).ToArray())));
        preview.Blockers.Should().BeEmpty();
        var decision = new PackageDecisionRequest(preview.PreviewId, preview.PreviewHash, preview.Revision);
        (await _client.PostAsJsonAsync(path + "/approve", decision)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var refreshedClient = _factory.CreateClient();
        var recoveredApproval = await DataAsync<PackageReviewStateResponse>(await refreshedClient.GetAsync(path + "/review-state"));
        recoveredApproval.Preview!.PreviewId.Should().Be(preview.PreviewId);
        recoveredApproval.Preview.PreviewHash.Should().Be(preview.PreviewHash);
        recoveredApproval.Preview.Revision.Should().Be(preview.Revision);
        recoveredApproval.Preview.Candidates.Should().BeEquivalentTo(preview.Candidates);
        recoveredApproval.Preview.State.Should().Be("Approved");
        recoveredApproval.PreviewIsStale.Should().BeFalse();
        using var publish = new HttpRequestMessage(HttpMethod.Post, path + "/publish") { Content = JsonContent.Create(decision) };
        publish.Headers.Add("Idempotency-Key", "synthetic-canonical-publication");
        // Act
        var result = await _client.SendAsync(publish);
        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var publication = await DataAsync<PackagePublicationResponse>(result);
        publication.PublicationState.Should().Be("Published");
        var recoveredPublication = await DataAsync<PackageReviewStateResponse>(await refreshedClient.GetAsync(path + "/review-state"));
        recoveredPublication.Publication!.PublicationState.Should().Be("Published");
        recoveredPublication.Publication.Records.Should().BeEquivalentTo(publication.Records).And.HaveCount(selected.Length);
        (await db.ProviderCapabilityReleases.CountAsync()).Should().Be(1);
        var published = await DataAsync<Page<PackageCandidateResponse>>(await _client.GetAsync(path + "/candidates?reviewState=Published"));
        published.Items.Should().HaveCount(selected.Length).And.OnlyContain(x => x.PublishedRecordId.HasValue);
        _factory.GetActiveContext().IsCspAdmin = false;
        var privateSource = await _client.GetAsync(path + $"/artifacts/{candidates[0].Citations[0].ArtifactId:D}/content");
        privateSource.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(string Path, PackageCandidateResponse Candidate)> ReceiveReferenceAsync(
        string? sourceJson = null, string expectedProcessingState = "NeedsAttention")
    {
        await _factory.EnsureActiveCspProfileAsync();
        const string source = """
            {"notice":"Synthetic surrounding text is not verified semantic analysis.",
             "authorizationReferences":[{"reference":"Synthetic stated reference","issuer":"Synthetic issuer",
                "issuedAt":"2026-01-01","expiresAt":"2027-01-01"}]}
            """;
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(sourceJson ?? source));
        content.Headers.ContentType = new("application/json");
        form.Add(content, "files", "synthetic-reference.json");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/csp/inherited-components/import") { Content = form };
        upload.Headers.Add("Prefer", "respond-async");
        upload.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var receipt = await _client.SendAsync(upload);
        receipt.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await DataAsync<PackageStatus>(receipt)).PackageId;
        var path = $"/api/csp/package-imports/{id:D}";
        (await WaitForAnalysisAsync(path)).ProcessingState.Should().Be(expectedProcessingState);
        return (path, (await DataAsync<Page<PackageCandidateResponse>>(
            await _client.GetAsync(path + "/candidates?type=AuthorizationReference"))).Items.Single());
    }

    private static async Task<T> DataAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private async Task<PackageStatus> WaitForAnalysisAsync(string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            await Task.Delay(200, deadline.Token);
            var status = await DataAsync<PackageStatus>(await _client.GetAsync(path, deadline.Token));
            if (status.ProcessingState is not ("Received" or "Processing")) return status;
        }
    }

    private sealed record Page<T>(IReadOnlyList<T> Items);
    private sealed record CandidateDecisionAudit(long Revision, string ReviewAction, string? Rationale, string SnapshotHash);
}
