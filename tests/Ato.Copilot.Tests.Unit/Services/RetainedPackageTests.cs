using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Services.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class RetainedPackageTests
{
    [Theory]
    [InlineData("DifferentSameSystemBaseline")]
    [InlineData("DifferentHash")]
    [InlineData("MissingLinkedHash")]
    [InlineData("MissingLinkedId")]
    public async Task RecordedDecisionBaselineLink_CannotBeRepairedBySelectingAnotherPackage(string mismatch)
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var original = await db.AuthorizationPackages.SingleAsync();
            var other = new AuthorizationPackage
            {
                RegisteredSystemId = original.RegisteredSystemId, TenantId = original.TenantId,
                Status = PackageStatus.Completed, FilePath = original.FilePath, ContentHash = original.ContentHash,
                ExpiresAt = original.ExpiresAt
            };
            db.Add(other);
            var decision = await db.AuthorizationDecisions.SingleAsync();
            decision.BaselinePackageId = mismatch == "DifferentSameSystemBaseline" ? other.Id
                : mismatch == "MissingLinkedId" ? null : selection.BaselinePackageId;
            decision.BaselinePackageHash = mismatch == "DifferentHash" ? new string('0', 64)
                : mismatch == "MissingLinkedHash" ? null : selection.BaselineContentHash;
            await db.SaveChangesAsync();
        }

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(f => f.Description.Contains("baseline linkage"));
        fixture.Channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecisionBaselineLink_ExactMatchOrExplicitLegacyUncertainty(bool recordedLink)
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        if (recordedLink)
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var decision = await db.AuthorizationDecisions.SingleAsync();
            decision.BaselinePackageId = selection.BaselinePackageId;
            decision.BaselinePackageHash = selection.BaselineContentHash.ToUpperInvariant();
            await db.SaveChangesAsync();
        }

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);

        // Assert
        validation.IsValid.Should().BeTrue();
        if (recordedLink)
            validation.Findings.Should().NotContain(f => f.Category == "decision-baseline-linkage");
        else
        {
            validation.RetainedContext!.BundleScope.Should().Contain("no recorded baseline linkage");
            validation.Findings.Should().Contain(f => f.Category == "decision-baseline-linkage"
                && f.Severity == ValidationSeverity.Warning && f.Description.Contains("does not establish authorization coverage"));
        }
    }

    [Fact]
    public async Task ContextOptionsHashes_BindValidationToFinalSourceReadWithoutCaptureClockDrift()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var options = await AuthorizationPackageContextOptions.ReadAsync(db,
            new ExportSettings { DataPath = fixture.DirectoryPath }, "mission", CancellationToken.None);
        selection = selection with { ExpectedDecisionSnapshotHash = options.Decisions.Single().SnapshotHash };
        var validated = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);
        selection = selection with { ExpectedSourceContextHash = validated.SourceContextHash };

        // Act
        var package = await fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);

        // Assert
        validated.IsValid.Should().BeTrue();
        AuthorizationPackageContextOptions.SourceContextHash(package.RetainedContextJson!).Should().Be(validated.SourceContextHash);
        options.Decisions.Single().SnapshotHash.Should().Be(validated.RetainedContext!.DecisionSnapshotHash);
        package.RetainedContextHash.Should().Be(ProviderAuthorizationStore.Hash(package.RetainedContextJson!));
    }

    [Fact]
    public async Task ValidatedSourceContext_RejectsDecisionDriftWithoutSeparateDecisionHash()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        var validated = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);
        selection = selection with { ExpectedSourceContextHash = validated.SourceContextHash };
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AuthorizationDecisions.SingleAsync()).TermsAndConditions = "Changed after validation";
            await db.SaveChangesAsync();
        }

        // Act
        var revalidated = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);
        var final = () => fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);

        // Assert
        revalidated.IsValid.Should().BeFalse();
        revalidated.Findings.Should().Contain(f => f.Description.Contains("source context changed"));
        await final.Should().ThrowAsync<DbUpdateConcurrencyException>().WithMessage("READINESS_CONTEXT_MISMATCH:*");
        fixture.Channel.Reader.TryRead(out _).Should().BeFalse();
        using var verification = fixture.Services.CreateScope();
        var dbAfter = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await dbAfter.PackageReadinessRuns.SingleAsync()).Outcome.Should().Be("Failed");
        (await dbAfter.AuthorizationPackages.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("MissingBaseline")]
    [InlineData("MissingDecision")]
    [InlineData("ForeignSystem")]
    [InlineData("ForeignTenant")]
    [InlineData("ForeignDecisionSystem")]
    [InlineData("ForeignDecisionTenant")]
    [InlineData("HashMismatch")]
    [InlineData("FileChanged")]
    [InlineData("FailedBaselineValidation")]
    [InlineData("DeniedDecision")]
    public async Task InvalidSelection_BlocksBeforeQueueing(string invalid)
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        if (invalid == "MissingBaseline") selection = selection with { BaselinePackageId = "missing" };
        if (invalid == "MissingDecision") selection = selection with { AuthorizationDecisionId = "missing" };
        if (invalid == "HashMismatch") selection = selection with { BaselineContentHash = new string('0', 64) };
        if (invalid == "FileChanged") await File.WriteAllTextAsync(fixture.BaselinePath, "tampered");
        if (invalid is "ForeignSystem" or "ForeignTenant")
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var baseline = await db.AuthorizationPackages.SingleAsync();
            if (invalid == "ForeignSystem") baseline.RegisteredSystemId = "other";
            else baseline.TenantId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        if (invalid is "ForeignDecisionSystem" or "ForeignDecisionTenant")
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var decision = await db.AuthorizationDecisions.SingleAsync();
            if (invalid == "ForeignDecisionSystem") decision.RegisteredSystemId = "other";
            else decision.TenantId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        if (invalid is "FailedBaselineValidation" or "DeniedDecision")
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            if (invalid == "DeniedDecision") (await db.AuthorizationDecisions.SingleAsync()).DecisionType = AuthorizationDecisionType.Dato;
            else (await db.AuthorizationPackages.SingleAsync()).ValidationPassed = false;
            await db.SaveChangesAsync();
        }

        // Act
        var validate = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);
        var enqueue = () => fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);

        // Assert
        validate.IsValid.Should().BeFalse();
        await enqueue.Should().ThrowAsync<DbUpdateConcurrencyException>().WithMessage("READINESS_CONTEXT_MISMATCH:*");
        fixture.Channel.Reader.TryRead(out _).Should().BeFalse();
        using var verification = fixture.Services.CreateScope();
        var dbAfter = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await dbAfter.PackageReadinessRuns.SingleAsync()).Outcome.Should().Be("Failed");
        (await dbAfter.AuthorizationPackages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Archive_CopiesExactRetainedPackage_AndPinsDecisionWithoutRegenerating()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        var original = await File.ReadAllBytesAsync(fixture.BaselinePath);
        var archived = await fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);
        fixture.Channel.Reader.TryRead(out var job).Should().BeTrue();

        // Act
        await fixture.Worker.ProcessJobAsync(job!, CancellationToken.None);
        var result = await fixture.Service.GetPackageAsync(archived.Id);
        var originalArchiveHash = result!.ContentHash;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AuthorizationDecisions.SingleAsync()).TermsAndConditions = "Later draft must not replace pinned decision.";
            await db.SaveChangesAsync();
        }
        await fixture.Worker.ProcessJobAsync(job!, CancellationToken.None);
        (await fixture.Service.GetPackageAsync(archived.Id))!.ContentHash.Should().Be(originalArchiveHash);

        // Assert
        result!.Status.Should().Be(PackageStatus.Completed);
        using var zip = ZipFile.OpenRead(result.FilePath!);
        await using var baselineStream = zip.GetEntry("retained-baseline.zip")!.Open();
        using var copied = new MemoryStream();
        await baselineStream.CopyToAsync(copied);
        copied.ToArray().Should().Equal(original);
        using var reader = new StreamReader(zip.GetEntry("package-context.json")!.Open());
        var manifest = await reader.ReadToEndAsync();
        manifest.Should().Contain(selection.BaselinePackageId).And.Contain(selection.BaselineContentHash)
            .And.Contain("DEMO recorded conditions").And.NotContain("Later draft");
        (await File.ReadAllBytesAsync(fixture.BaselinePath)).Should().Equal(original);
        using var verification = fixture.Services.CreateScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verifyDb.AuthorizationDecisions.CountAsync()).Should().Be(1);
        (await verifyDb.AuthorizationPackages.SingleAsync(p => p.Id == selection.BaselinePackageId)).Status.Should().Be(PackageStatus.Completed);
    }

    [Fact]
    public async Task ChangeSubmission_UnreviewedRequirementMappings_BlockNewPreparation()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync(), reviewRequirementMappings: false);

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(x => x.Description.Contains("requirement coverage gaps"));
    }

    [Fact]
    public async Task ChangeSubmission_UsesRetainedCatalogSourceIds_NotDisplayIdsOrCurrentBinding()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync(), distinctSourceId: true);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.ControlBaselines.SingleAsync()).RequirementCatalogBindingId = null;
        await db.SaveChangesAsync();

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeTrue(string.Join("; ", validation.Findings.Select(f => f.Description)));
        var bytes = await File.ReadAllTextAsync(Path.Combine(fixture.DirectoryPath, "exports", "reviewed-change.json"));
        using var document = JsonDocument.Parse(bytes);
        document.RootElement.GetProperty("system-security-plan").GetProperty("control-implementation")
            .GetProperty("implemented-requirements")[0].GetProperty("control-id").GetString().Should().Be("audit-source-control");
    }

    [Theory]
    [InlineData("catalog-hash")]
    [InlineData("statement-id")]
    [InlineData("param-id")]
    [InlineData("catalog-tenant")]
    [InlineData("catalog-system")]
    [InlineData("control-case")]
    [InlineData("missing-catalog-pin")]
    [InlineData("null-source-pin")]
    [InlineData("duplicate-source-manifest")]
    [InlineData("missing-coverage-evaluation")]
    public async Task ChangeSubmission_InvalidRetainedSourceReference_IsBlocked(string mutation)
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync());
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        if (mutation == "catalog-hash")
            (await db.BaselineCatalogBindings.SingleAsync()).CatalogJson += " ";
        else if (mutation == "catalog-tenant")
            (await db.BaselineCatalogBindings.SingleAsync()).TenantId = Guid.NewGuid();
        else if (mutation == "catalog-system")
            (await db.ControlBaselines.SingleAsync()).RegisteredSystemId = "another-system";
        else
        {
            var path = Path.Combine(fixture.DirectoryPath, "exports", "reviewed-change.json");
            var document = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            var control = document["system-security-plan"]!["control-implementation"]!["implemented-requirements"]![0]!;
            if (mutation == "statement-id")
                control["statements"] = System.Text.Json.Nodes.JsonNode.Parse(
                    """[{"uuid":"d3ad9c84-f7d8-4771-a7bf-e65d03154967","statement-id":"another-controls-statement","remarks":"DEMO response"}]""");
            else if (mutation == "param-id")
                control["set-parameters"] = System.Text.Json.Nodes.JsonNode.Parse(
                    """[{"param-id":"another-controls-parameter","values":["DEMO"]}]""");
            else if (mutation == "control-case")
                control["control-id"] = "au-2";
            else if (mutation == "missing-coverage-evaluation")
            {
                var props = document["system-security-plan"]!["metadata"]!["props"]!.AsArray();
                props.Remove(props.Single(x => x!["name"]!.GetValue<string>() == "requirement-coverage-gaps"));
            }
            else if (mutation == "duplicate-source-manifest")
            {
                var props = document["system-security-plan"]!["metadata"]!["props"]!.AsArray();
                props.Add(props.Single(x => x!["name"]!.GetValue<string>() == "requirement-source-manifest")!.DeepClone());
            }
            else
                document["system-security-plan"]!["metadata"]!["props"]!.AsArray()
                    .Single(x => x!["name"]!.GetValue<string>() == "requirement-source-manifest")!["value"] =
                        mutation == "null-source-pin" ? "[null]" : "[]";
            var json = document.ToJsonString();
            await File.WriteAllTextAsync(path, json);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            (await db.SspExports.SingleAsync()).ContentHash = hash;
            selection = selection with { ChangeContentHash = hash };
        }
        await db.SaveChangesAsync();

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(x => x.Description.Contains("catalog", StringComparison.OrdinalIgnoreCase)
            || x.Description.Contains("source", StringComparison.OrdinalIgnoreCase)
            || x.Description.Contains("requirement coverage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangeSubmission_HistoricalPreviewWithoutCatalogManifest_PreservesLegacySemantics()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync());
        var path = Path.Combine(fixture.DirectoryPath, "exports", "reviewed-change.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var props = document["system-security-plan"]!["metadata"]!["props"]!.AsArray();
        props.Remove(props.Single(x => x!["name"]!.GetValue<string>() == "requirement-source-manifest"));
        props.Remove(props.Single(x => x!["name"]!.GetValue<string>() == "requirement-coverage-gaps"));
        document["system-security-plan"]!["control-implementation"]!["implemented-requirements"]![0]!["control-id"] = "au-2";
        var json = document.ToJsonString();
        await File.WriteAllTextAsync(path, json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.SspExports.SingleAsync()).ContentHash = hash;
        await db.SaveChangesAsync();
        selection = selection with { ChangeContentHash = hash };

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeTrue(string.Join("; ", validation.Findings.Select(f => f.Description)));
        (await File.ReadAllTextAsync(path)).Should().Be(json);
    }

    [Fact]
    public async Task ChangeBundle_RetainsPredecessorAndExactReviewedSsp_WithExplicitLimitedScope()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        selection = await fixture.SeedReviewedChangeAsync(selection);
        var package = await fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);
        fixture.Channel.Reader.TryRead(out var job).Should().BeTrue();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.ControlImplementations.SingleAsync()).TechnicalNarrative = "Later mutable draft must not leak.";
            await db.SaveChangesAsync();
        }

        // Act
        await fixture.Worker.ProcessJobAsync(job!, CancellationToken.None);
        var completed = await fixture.Service.GetPackageAsync(package.Id);

        // Assert
        completed!.Status.Should().Be(PackageStatus.Completed);
        using var zip = ZipFile.OpenRead(completed.FilePath!);
        using var reader = new StreamReader(zip.GetEntry("changes/oscal-ssp.json")!.Open());
        var changedSsp = await reader.ReadToEndAsync();
        changedSsp.Should().Contain("DEMO reviewed technical").And.NotContain("Later mutable draft");
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(changedSsp))).ToLowerInvariant().Should().Be(selection.ChangeContentHash);
        using var contextReader = new StreamReader(zip.GetEntry("package-context.json")!.Open());
        var context = await contextReader.ReadToEndAsync();
        context.Should().Contain("other current artifacts are not regenerated")
            .And.Contain(selection.BaselinePackageId).And.Contain(selection.ChangePreviewId!.Value.ToString());
        completed.Artifacts.Should().HaveCount(3);
    }

    [Fact]
    public async Task ChangeSubmission_WithoutReviewedSnapshot_IsBlocked()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(f => f.Description.Contains("reviewed SSP preview"));
    }

    [Fact]
    public async Task ChangeSubmission_UnapprovedNarrativePin_IsBlocked()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync());
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.Set<NarrativeVersion>().SingleAsync()).Status = SspSectionStatus.Draft;
            await db.SaveChangesAsync();
        }

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(f => f.Description.Contains("retained approval"));
    }

    [Fact]
    public async Task ChangeSubmission_SectionOnlyUserApprovalCannotSatisfyReviewedSourceGate()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync(), approveCategories: false);

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(f => f.Description.Contains("independently approved access context"));
    }

    [Fact]
    public async Task ChangeSubmission_RemovingUnapprovedSiblingDoesNotRequireReapprovalOfUnchangedBaseline()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync(), removeDraftSiblingBeforeExport: true);

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeTrue(string.Join("; ", validation.Findings.Select(f => f.Description)));
    }

    [Fact]
    public async Task ChangeSubmission_PartialUserCategoryPreviewCannotBypassSourceGapGate()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync(), includeUnreviewedSibling: true);

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(f => f.Description.Contains("unresolved source/approval gaps"));
    }

    [Fact]
    public async Task ChangeSubmission_WorkingProfilePreviewCannotMasqueradeAsApprovedSource()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedReviewedChangeAsync(await fixture.SeedAsync(), workingProfilePreview: true);

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.ChangeSubmission, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(f => f.Description.Contains("Working profile previews are review-only"));
    }

    [Theory]
    [InlineData("BaselineBytes")]
    [InlineData("Decision")]
    public async Task Archive_SourceChangedAfterQueue_FailsWithoutRegeneration(string source)
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        var package = await fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);
        fixture.Channel.Reader.TryRead(out var job).Should().BeTrue();
        if (source == "BaselineBytes")
            await File.WriteAllTextAsync(fixture.BaselinePath, "source changed after selection");
        else
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AuthorizationDecisions.SingleAsync()).TermsAndConditions = "Decision changed after queueing";
            await db.SaveChangesAsync();
        }

        // Act
        await fixture.Worker.ProcessJobAsync(job!, CancellationToken.None);

        // Assert
        var failed = await fixture.Service.GetPackageAsync(package.Id);
        failed!.Status.Should().Be(PackageStatus.Failed);
        failed.FilePath.Should().BeNull();
        failed.RetainedContextJson.Should().Contain(selection.BaselineContentHash);
    }

    [Fact]
    public async Task DecisionChangedAfterSelection_IsRejectedWhenExpectedSnapshotHashProvided()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();
        var reviewed = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);
        selection = selection with { ExpectedDecisionSnapshotHash = reviewed.RetainedContext!.DecisionSnapshotHash };
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AuthorizationDecisions.SingleAsync()).TermsAndConditions = "Revised recorded terms";
            await db.SaveChangesAsync();
        }

        // Act
        var validation = await fixture.Service.ValidateRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive, selection);

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Findings.Should().Contain(f => f.Description.Contains("changed since selection"));
    }

    [Fact]
    public async Task RetainedRequestKey_ReplaysSamePackageAndRejectsChangedSelection()
    {
        // Arrange
        using var fixture = new Fixture();
        var selection = await fixture.SeedAsync();

        // Act
        var first = await fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive,
            selection, idempotencyKey: "retained-request");
        var replay = await fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive,
            selection, idempotencyKey: "retained-request");
        var changed = () => fixture.Service.EnqueueRetainedPackageAsync("mission", PackagePurpose.AuthorizedBaselineArchive,
            selection with { AuthorizationDecisionId = "different" }, idempotencyKey: "retained-request");

        // Assert
        replay.Id.Should().Be(first.Id);
        await changed.Should().ThrowAsync<DbUpdateConcurrencyException>();
        fixture.Channel.Reader.TryRead(out _).Should().BeTrue();
        fixture.Channel.Reader.TryRead(out _).Should().BeFalse();
    }

    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Directory.GetCurrentDirectory(), $"retained-package-tests-{Guid.NewGuid():N}");
        public string BaselinePath => Path.Combine(DirectoryPath, "baseline.zip");
        public ServiceProvider Services { get; }
        public Channel<PackageExportJob> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<PackageExportJob>();
        public AuthorizationPackageService Service { get; }
        public PackageBackgroundService Worker { get; }
        private readonly Guid tenant = Guid.NewGuid();

        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            var database = Guid.NewGuid().ToString();
            Services = new ServiceCollection().AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(database))
                .AddLogging()
                .AddSingleton(Options.Create(new ExportSettings { DataPath = DirectoryPath }))
                .AddSingleton(Channel)
                .AddSingleton(Mock.Of<IEvidenceArtifactService>())
                .AddSingleton(Mock.Of<IFileStorageProvider>())
                .AddSingleton(Mock.Of<IPackageValidationService>())
                .AddSingleton<IAuthorizationPackageService, AuthorizationPackageService>()
                .AddSingleton<PackageReadinessService>()
                .AddSingleton<ITenantContextAccessor, TenantContextAccessor>()
                .AddSingleton<IOscalSchemaValidationService>(new OscalSchemaValidationService(
                    Mock.Of<IEmassExportService>(), Mock.Of<IOscalSapExportService>(), NullLogger<OscalSchemaValidationService>.Instance))
                .BuildServiceProvider();
            var scopes = Services.GetRequiredService<IServiceScopeFactory>();
            Service = (AuthorizationPackageService)Services.GetRequiredService<IAuthorizationPackageService>();
            Worker = new(Channel, scopes, Mock.Of<IPackageExportNotifier>(), NullLogger<PackageBackgroundService>.Instance);
        }

        public async Task<RetainedPackageSelection> SeedAsync()
        {
            using (var zip = ZipFile.Open(BaselinePath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(zip.CreateEntry("oscal-ssp.json").Open());
                await writer.WriteAsync("{\"retained\":\"DEMO original artifact\"}");
            }
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(BaselinePath))).ToLowerInvariant();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var package = new AuthorizationPackage
            {
                TenantId = tenant, RegisteredSystemId = "mission", Status = PackageStatus.Completed,
                FilePath = BaselinePath, ContentHash = hash, ExpiresAt = DateTimeOffset.UtcNow.AddDays(10)
            };
            var decision = new AuthorizationDecision
            {
                TenantId = tenant, RegisteredSystemId = "mission", DecisionType = AuthorizationDecisionType.Ato,
                DecisionDate = new DateTime(2025, 4, 17), IssuedBy = "DEMO AO", IssuedByName = "DEMO recorded authority",
                TermsAndConditions = "DEMO recorded conditions"
            };
            db.AddRange(new RegisteredSystem { Id = "mission", TenantId = tenant, Name = "DEMO mission" }, package, decision);
            await db.SaveChangesAsync();
            return new(package.Id, hash, decision.Id);
        }

        public async Task<RetainedPackageSelection> SeedReviewedChangeAsync(
            RetainedPackageSelection selection, bool approveCategories = true, bool removeDraftSiblingBeforeExport = false,
            bool includeUnreviewedSibling = false, bool workingProfilePreview = false, bool distinctSourceId = false,
            bool reviewRequirementMappings = true)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.RegisteredSystems.SingleAsync()).OperationalStatus = OperationalStatus.UnderDevelopment;
            var categorization = new SecurityCategorization { TenantId = tenant, RegisteredSystemId = "mission" };
            categorization.InformationTypes.Add(new InformationType
            {
                Name = "DEMO changed data", Category = "Synthetic", Sp80060Id = "D.1.1",
                ConfidentialityImpact = ImpactValue.Moderate, IntegrityImpact = ImpactValue.Moderate, AvailabilityImpact = ImpactValue.Moderate
            });
            var implementation = new ControlImplementation
            {
                TenantId = tenant, RegisteredSystemId = "mission", ControlId = "AU-2",
                PolicyNarrative = "DEMO reviewed policy", TechnicalNarrative = "DEMO reviewed technical",
                ApprovalStatus = SspSectionStatus.Approved
            };
            var version = new NarrativeVersion
            {
                TenantId = tenant, ControlImplementationId = implementation.Id, Status = SspSectionStatus.Approved,
                SnapshotJson = NarrativeContentSnapshot.Capture(implementation)
            };
            implementation.ApprovedVersionId = version.Id;
            var rawCatalog = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Requirements", "export-audit-catalog.json"));
            if (distinctSourceId)
            {
                var catalog = System.Text.Json.Nodes.JsonNode.Parse(rawCatalog)!;
                catalog["controls"]![0]!["id"] = "audit-source-control";
                catalog["controls"]![0]!["props"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"name":"label","value":"AU-2"}]""");
                rawCatalog = catalog.ToJsonString();
            }
            var baseline = new ControlBaseline { TenantId = tenant, RegisteredSystemId = "mission",
                BaselineLevel = "Moderate", ControlIds = ["AU-2"], TotalControls = 1 };
            var binding = new BaselineCatalogBinding { TenantId = tenant, ControlBaselineId = baseline.Id,
                CatalogJson = rawCatalog, ContentHash = RequirementCoverageService.Hash(rawCatalog),
                CatalogVersion = "test-1", SourceUri = "https://example.invalid/audit-catalog.json" };
            baseline.RequirementCatalogBindingId = binding.Id;
            var evidence = new EvidenceArtifact { TenantId = tenant, RegisteredSystemId = "mission",
                FileName = "reviewed-audit.txt", ContentHash = RequirementCoverageService.Hash("DEMO reviewed evidence") };
            if (reviewRequirementMappings)
            {
                var snapshot = new RequirementCoverageSnapshot(binding.Id, binding.ContentHash,
                    [new("audit-statement", "Technical", "DEMO reviewed audit response", [new(evidence.Id, evidence.ContentHash)])],
                    new Dictionary<string, string>(), RequirementCoverageService.NarrativeHash(implementation),
                    Guid.NewGuid(), "DEMO author", DateTime.UtcNow, Guid.NewGuid(), "DEMO reviewer", DateTime.UtcNow);
                implementation.ApprovedRequirementCoverageJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                implementation.RequirementCoverageJson = implementation.ApprovedRequirementCoverageJson;
                version.SnapshotJson = NarrativeContentSnapshot.Capture(implementation);
            }
            db.AddRange(categorization, implementation, version,
                baseline, binding, evidence,
                new RmfRoleAssignment { TenantId = tenant, RegisteredSystemId = "mission", UserId = "reviewer", UserDisplayName = "DEMO reviewer", RmfRole = RmfRole.Issm, IsActive = true },
                new AuthorizationBoundaryDefinition { TenantId = tenant, RegisteredSystemId = "mission", Name = "DEMO documented boundary" },
                new SystemComponent { TenantId = tenant, RegisteredSystemId = "mission", Name = "DEMO component", Description = "DEMO existing component", ComponentType = ComponentType.Thing });
            foreach (var type in Enum.GetValues<ProfileSectionType>().Where(t => t != ProfileSectionType.LeveragedAuthorizations))
                db.Add(new SystemProfileSection { TenantId = tenant, RegisteredSystemId = "mission", SectionType = type,
                    GovernanceStatus = SspSectionStatus.UnderReview, DraftContent = $"DEMO reviewed {type}" });
            await db.SaveChangesAsync();
            var category = new UserCategory { TenantId = tenant, CategoryName = "DEMO reviewed operators",
                SystemProfileSectionId = await db.SystemProfileSections
                    .Where(s => s.SectionType == ProfileSectionType.UsersAndAccess).Select(s => s.Id).SingleAsync() };
            db.UserCategories.Add(category);
            if (removeDraftSiblingBeforeExport || includeUnreviewedSibling)
                db.UserCategories.Add(new UserCategory { TenantId = tenant, CategoryName = "Disposable draft sibling",
                    SystemProfileSectionId = category.SystemProfileSectionId, SortOrder = 1 });
            await db.SaveChangesAsync();
            var profile = new SystemProfileService(Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SystemProfileService>.Instance);
            await profile.BatchApproveSectionsAsync("mission", "reviewer", RmfRole.Issm);
            if (approveCategories)
            {
                await profile.ReviewUserCategoryAsync("mission", category.Id, "submit", 1, "owner", simulatedRole: RmfRole.MissionOwner);
                await profile.ReviewUserCategoryAsync("mission", category.Id, "approve", 2, "reviewer", simulatedRole: RmfRole.Issm);
            }
            if (removeDraftSiblingBeforeExport)
                await profile.SaveDraftWithChildrenAsync("mission", ProfileSectionType.UsersAndAccess, "DEMO reviewed UsersAndAccess",
                    [JsonSerializer.SerializeToElement(new { id = category.Id, revision = 3, categoryName = category.CategoryName })],
                    "owner", RmfRole.MissionOwner);
            // The fixture has no stamping interceptor; retain the real owning tenant on newly created approval entries.
            foreach (var entry in await db.ProfileAuditEntries.ToListAsync()) entry.TenantId = tenant;
            await db.SaveChangesAsync();
            var generator = new OscalSspExportService(Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<OscalSspExportService>.Instance);
            var generated = workingProfilePreview ? await generator.PreviewAsync("mission") : await generator.ExportAsync("mission");
            var exportDirectory = Path.Combine(DirectoryPath, "exports");
            Directory.CreateDirectory(exportDirectory);
            await File.WriteAllTextAsync(Path.Combine(exportDirectory, "reviewed-change.json"), generated.OscalJson);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(generated.OscalJson))).ToLowerInvariant();
            var preview = new SspExport
            {
                SystemId = "mission", Format = "json", Status = "Preview", FilePath = "reviewed-change.json",
                ContentHash = hash, SourceManifestJson = JsonSerializer.Serialize(generated.SourceManifest),
                SourceGapsJson = includeUnreviewedSibling
                    ? JsonSerializer.Serialize(generated.ProfileSourceGaps.Select(g => new DocumentSourceGapDto("PROFILE_APPROVAL_UNVERIFIED", g)))
                    : "[]",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(10)
            };
            db.Add(preview);
            await db.SaveChangesAsync();
            return selection with { ChangePreviewId = preview.Id, ChangeContentHash = hash };
        }

        public void Dispose()
        {
            Services.Dispose();
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }
}
