using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class RequirementCoverageServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<ISystemWorkspaceAccessService> _access = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _author = Guid.NewGuid();
    private AtoCopilotContext _db = null!;
    private RequirementCoverageService _service = null!;
    private string _systemId = string.Empty;
    private string _baselineId = string.Empty;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        var system = new RegisteredSystem { Name = "Synthetic coverage system", TenantId = _tenantId };
        var framework = new ComplianceFramework
        {
            Identifier = "SYNTHETIC", Name = "Synthetic framework", Version = "test-1",
            CatalogUrl = "https://example.invalid/synthetic-catalog",
            RequirementCatalogJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Requirements", "catalog.json"))
                .Replace("unrelated-enhancement-key", "enhancement-source", StringComparison.Ordinal)
        };
        var baseline = new ControlBaseline
        {
            RegisteredSystemId = system.Id, TenantId = _tenantId, BaselineLevel = "Test",
            ControlIds = ["AC-11"], TotalControls = 1, CreatedBy = "fixture"
        };
        _db.AddRange(system, framework, baseline);
        await _db.SaveChangesAsync();
        _systemId = system.Id;
        _baselineId = baseline.Id;
        _tenant.SetupGet(x => x.EffectiveTenantId).Returns(_tenantId);
        _tenant.SetupGet(x => x.PersonId).Returns(_author);
        SetPermissions(true, true, true, true);
        _service = new(_db, _tenant.Object, _access.Object, NullLogger<RequirementCoverageService>.Instance);
        await _service.BindCatalogAsync(_systemId, framework.Id, 0, "Verified synthetic source", "author", default);
    }

    private void SetPermissions(bool read, bool author, bool manage, bool review) =>
        _access.Setup(x => x.GetAccessAsync(_tenantId, It.IsAny<Guid?>(), _systemId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemWorkspaceAccessResponse(_systemId, [],
                new(read, false, manage, author, review, author, false, false, false)));

    [Fact]
    public async Task Proposal_DoesNotChangeBaseline_AndDuplicateReturnsSameProposal()
    {
        // Arrange
        var input = new EnhancementAdditionInput("AC-11", "AC-11(1)", 1, "Scope requires concealment", null, "Synthetic draft");

        // Act
        var first = await _service.ProposeAsync(_systemId, input, "author", default);
        var retry = await _service.ProposeAsync(_systemId, input, "author", default);

        // Assert
        retry.Id.Should().Be(first.Id);
        (await _db.ControlBaselines.SingleAsync(x => x.Id == _baselineId)).ControlIds.Should().Equal("AC-11");
        (await _db.ControlImplementations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Accept_RequiresDifferentReviewer_AndKeepsNarrativeDraft()
    {
        // Arrange
        var proposal = await _service.ProposeAsync(_systemId,
            new("AC-11", "AC-11(1)", 1, "Required for scope", null, "Synthetic draft"), "author", default);

        // Act
        Func<Task> selfReview = () => _service.AcceptAsync(_systemId, proposal.Id, 1, "author", default);

        // Assert
        await selfReview.Should().ThrowAsync<UnauthorizedAccessException>();

        // Arrange
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());

        // Act
        await _service.AcceptAsync(_systemId, proposal.Id, 1, "reviewer", default);

        // Assert
        var implementation = await _db.ControlImplementations.SingleAsync();
        implementation.ControlId.Should().Be("AC-11(1)");
        implementation.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        implementation.ApprovedVersionId.Should().BeNull();
        implementation.TechnicalNarrative.Should().Be("Synthetic draft");
        (await _db.ControlInheritances.CountAsync()).Should().Be(0);
        (await _db.ControlBaselines.AsNoTracking().SingleAsync()).ControlIds.Should().Equal("AC-11", "AC-11(1)");
    }

    [Fact]
    public async Task ViewerCannotPropose_AndCrossTenantCannotRead()
    {
        // Arrange
        SetPermissions(true, false, false, false);

        // Act
        Func<Task> propose = () => _service.ProposeAsync(_systemId,
            new("AC-11", "AC-11(1)", 1, "Required for scope", null, "Draft"), "viewer", default);

        // Assert
        await propose.Should().ThrowAsync<UnauthorizedAccessException>();

        // Arrange
        _tenant.SetupGet(x => x.EffectiveTenantId).Returns(Guid.NewGuid());

        // Act
        Func<Task> read = () => _service.ReadAsync(_systemId, "AC-11", default);

        // Assert
        await read.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Read_UsesCatalogStructure_AndDoesNotInventCoverage()
    {
        // Arrange
        _db.ControlImplementations.Add(new()
        {
            RegisteredSystemId = _systemId, TenantId = _tenantId, ControlId = "AC-11",
            TechnicalNarrative = "A legacy response without mappings", AuthoredBy = "fixture"
        });
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ReadAsync(_systemId, "AC-11", default);

        // Assert
        result.Requirements.Should().HaveCount(2);
        result.Requirements.Should().OnlyContain(x => x.ResponseState == "Missing" && !x.Reviewed);
        result.Enhancements.Single().ControlId.Should().Be("AC-11(1)");
        result.Enhancements.Single().Selected.Should().BeFalse();
        result.Enhancements.Single().HasNarrative.Should().BeFalse();
        result.Gaps.Should().Contain("Existing narratives have unreviewed requirement mappings.");
    }

    [Fact]
    public async Task Proposal_RejectsStaleBaselineAndBlankRationale()
    {
        // Arrange
        var stale = new EnhancementAdditionInput("AC-11", "AC-11(1)", 0, "Scope", null, "Draft");

        // Act
        Func<Task> propose = () => _service.ProposeAsync(_systemId, stale, "author", default);

        // Assert
        await propose.Should().ThrowAsync<InvalidOperationException>();

        // Act
        Func<Task> empty = () => _service.ProposeAsync(_systemId, stale with { ExpectedBaselineRevision = 1, Rationale = " " }, "author", default);

        // Assert
        await empty.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SchemaAdditions_AreSafeToRerunWithoutChangingBindings()
    {
        // Arrange
        var bindingId = (await _db.ControlBaselines.SingleAsync()).RequirementCatalogBindingId;

        // Act
        await RequirementCoverageSchemaAdditions.ApplyAsync(_db);
        await RequirementCoverageSchemaAdditions.ApplyAsync(_db);

        // Assert
        (await _db.BaselineCatalogBindings.SingleAsync()).Id.Should().Be(bindingId);
    }

    [Fact]
    public async Task Accept_WhenNarrativeInsertFails_RollsBackSelectionAndProposal()
    {
        // Arrange
        var proposal = await _service.ProposeAsync(_systemId,
            new("AC-11", "AC-11(1)", 1, "Required", null, "Draft"), "author", default);
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());
        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_narrative BEFORE INSERT ON ControlImplementations
            BEGIN SELECT RAISE(ABORT, 'synthetic fault'); END;
            """);

        // Act
        Func<Task> accept = () => _service.AcceptAsync(_systemId, proposal.Id, 1, "reviewer", default);

        // Assert
        await accept.Should().ThrowAsync<DbUpdateException>();
        _db.ChangeTracker.Clear();
        (await _db.ControlBaselines.SingleAsync()).ControlIds.Should().Equal("AC-11");
        (await _db.RequirementEnhancementProposals.SingleAsync()).Status.Should().Be("Pending");
        (await _db.ControlTailorings.CountAsync()).Should().Be(0);
        (await _db.NarrativeVersions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_RetainsUnflattenedCatalogAndActualMetadata()
    {
        // Arrange
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Requirements", "catalog.json"));
        using var client = new HttpClient(new CatalogHandler(raw));
        using var services = new ServiceCollection()
            .AddScoped(_ => new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options))
            .BuildServiceProvider();
        var importer = new FrameworkImportService(services.GetRequiredService<IServiceScopeFactory>(),
            client, NullLogger<FrameworkImportService>.Instance);

        // Act
        await importer.ImportFrameworkAsync("NIST-800-53-R5");

        // Assert
        var framework = await _db.ComplianceFrameworks.AsNoTracking().SingleAsync(x => x.Identifier == "NIST-800-53-R5");
        framework.RequirementCatalogJson.Should().NotBeNullOrWhiteSpace();
        RequirementCatalog.Parse(framework.RequirementCatalogJson!).Controls.Single(x => x.DisplayId == "AC-11")
            .Requirements.Select(x => x.Id).Should().Equal("requirement-alpha", "requirement-beta");
        framework.Version.Should().Be("test-1");
    }

    private sealed class CatalogHandler(string catalog) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("catalog", StringComparison.Ordinal)
                    ? "{\"catalog\":" + catalog + "}"
                    : "{\"profile\":{\"imports\":[{\"include-controls\":[{\"with-ids\":[\"parent-original\"]}]}]}}")
            });
    }

    [Fact]
    public async Task SourceOnlyCapture_BackfillsWithoutReplacingDefinitionsOrSystemData()
    {
        // Arrange
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Requirements", "catalog.json"));
        var framework = new ComplianceFramework { Identifier = "NIST-800-53-R5", Name = "Legacy catalog",
            Version = "legacy-definition-version", CatalogUrl = "https://example.invalid/legacy", ControlCount = 1 };
        _db.Add(framework);
        _db.Add(new FrameworkControl { FrameworkId = framework.Id, ControlId = "AC-11",
            Title = "Preserve existing definition", Family = "AC" });
        await _db.SaveChangesAsync();
        var bindingJson = (await _db.BaselineCatalogBindings.SingleAsync()).CatalogJson;
        using var client = new HttpClient(new CatalogHandler(raw));
        using var services = new ServiceCollection()
            .AddScoped(_ => new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options))
            .BuildServiceProvider();
        var importer = new FrameworkImportService(services.GetRequiredService<IServiceScopeFactory>(),
            client, NullLogger<FrameworkImportService>.Instance);

        // Act
        var captured = await importer.CaptureSourceAsync("NIST-800-53-R5", true);
        var repeated = await importer.CaptureSourceAsync("NIST-800-53-R5", true);

        // Assert
        captured.Changed.Should().BeTrue();
        captured.Version.Should().Be("test-1");
        repeated.Changed.Should().BeFalse();
        _db.ChangeTracker.Clear();
        var persisted = await _db.ComplianceFrameworks.SingleAsync(x => x.Id == framework.Id);
        persisted.Version.Should().Be("legacy-definition-version");
        persisted.RequirementCatalogVersion.Should().Be("test-1");
        persisted.RequirementCatalogSourceUri.Should().StartWith("https://raw.githubusercontent.com/usnistgov/");
        (await _db.FrameworkControls.SingleAsync(x => x.FrameworkId == framework.Id)).Title.Should().Be("Preserve existing definition");
        (await _db.ControlBaselines.SingleAsync()).ControlIds.Should().Equal("AC-11");
        (await _db.BaselineCatalogBindings.SingleAsync()).CatalogJson.Should().Be(bindingJson);
    }

    [Fact]
    public async Task MappingReview_PreservesApprovedResponsesAndRejectsStaleEvidence()
    {
        // Arrange
        var implementation = new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "AC-11", PolicyNarrative = "Policy", TechnicalNarrative = "Technical", AuthoredBy = "original" };
        var artifact = new EvidenceArtifact { TenantId = _tenantId, RegisteredSystemId = _systemId,
            FileName = "synthetic.txt", ContentHash = new string('a', 64), UploadedBy = "fixture" };
        _db.AddRange(implementation, artifact);
        await _db.SaveChangesAsync();
        RequirementResponse Response(string id) => new(id, "Policy", $"Synthetic response {id}", [new(artifact.Id, artifact.ContentHash)]);
        var input = new RequirementMappingInput(1, [Response("requirement-alpha"), Response("requirement-beta")],
            new Dictionary<string, string> { ["lock-duration"] = "Synthetic recorded period" });

        // Act
        await _service.SaveMappingsAsync(_systemId, "AC-11", input, "author", default);
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());
        await _service.ReviewMappingsAsync(_systemId, "AC-11", 2, "reviewer", default);
        var approved = await _service.ReadAsync(_systemId, "AC-11", default);

        // Assert
        approved.Requirements.Should().OnlyContain(x => x.Reviewed);
        implementation.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        var approvedJson = implementation.ApprovedRequirementCoverageJson;

        // Arrange
        artifact.ContentHash = new string('b', 64);
        await _db.SaveChangesAsync();

        // Act
        var stale = await _service.ReadAsync(_systemId, "AC-11", default);

        // Assert
        stale.Requirements.Should().OnlyContain(x => !x.Reviewed && x.EvidenceGap);
        implementation.ApprovedRequirementCoverageJson.Should().Be(approvedJson);
        (await _db.NarrativeVersions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task MappingWrite_RejectsUnderReviewAndUnknownEvidence()
    {
        // Arrange
        var implementation = new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "AC-11", AuthoredBy = "fixture", ApprovalStatus = SspSectionStatus.UnderReview };
        _db.Add(implementation);
        await _db.SaveChangesAsync();
        var input = new RequirementMappingInput(1, [new("requirement-alpha", "Policy", "Draft", [])], new Dictionary<string, string>());

        // Act
        Func<Task> locked = () => _service.SaveMappingsAsync(_systemId, "AC-11", input, "author", default);

        // Assert
        await locked.Should().ThrowAsync<InvalidOperationException>().WithMessage("*review*");
        Func<Task> reviewLocked = () => _service.ReviewMappingsAsync(_systemId, "AC-11", 1, "reviewer", default);
        await reviewLocked.Should().ThrowAsync<InvalidOperationException>().WithMessage("*review*");
    }

    [Fact]
    public async Task ReturningProposal_RetainsAuditAndAllowsRevisedProposalWithoutSelectingControl()
    {
        // Arrange
        var proposal = await _service.ProposeAsync(_systemId,
            new("AC-11", "AC-11(1)", 1, "First rationale", null, "First draft"), "author", default);
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());

        // Act
        await _service.ReturnProposalAsync(_systemId, proposal.Id, 1, "Explain the scope", "reviewer", default);
        var returned = await _service.ReadAsync(_systemId, "AC-11", default);
        _tenant.SetupGet(x => x.PersonId).Returns(_author);
        var replacement = await _service.ProposeAsync(_systemId,
            new("AC-11", "AC-11(1)", 1, "Revised rationale", null, "Revised draft"), "author", default);

        // Assert
        replacement.Id.Should().NotBe(proposal.Id);
        (await _db.RequirementEnhancementProposals.CountAsync()).Should().Be(2);
        proposal.Status.Should().Be("NeedsRevision");
        proposal.ReviewNote.Should().Be("Explain the scope");
        returned.Proposals.Single().ReviewNote.Should().Be("Explain the scope");
        (await _db.ControlBaselines.AsNoTracking().SingleAsync()).ControlIds.Should().Equal("AC-11");
    }

    [Fact]
    public async Task Accept_WithExistingNarrative_PreservesApprovedContentAndUntouchedHalf()
    {
        // Arrange
        var existing = new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "AC-11(1)", PolicyNarrative = "Retained policy", TechnicalNarrative = "Retained technical",
            AuthoredBy = "original", ApprovalStatus = SspSectionStatus.Approved, AiSuggested = true, IsAutoPopulated = true };
        _db.Add(existing);
        await _db.SaveChangesAsync();
        var approved = new NarrativeVersion { TenantId = _tenantId, ControlImplementationId = existing.Id,
            Content = "Retained technical", SnapshotJson = NarrativeContentSnapshot.Capture(existing),
            AuthoredBy = "original", Status = SspSectionStatus.Approved };
        _db.Add(approved);
        await _db.SaveChangesAsync();
        existing.ApprovedVersionId = approved.Id;
        await _db.SaveChangesAsync();
        var proposal = await _service.ProposeAsync(_systemId,
            new("AC-11", "AC-11(1)", 1, "Include the existing record", null, "Proposed technical"), "author", default);
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());

        // Act
        await _service.AcceptAsync(_systemId, proposal.Id, 1, "reviewer", default);

        // Assert
        _db.ChangeTracker.Clear();
        var result = await _db.ControlImplementations.SingleAsync();
        result.PolicyNarrative.Should().Be("Retained policy");
        result.TechnicalNarrative.Should().Be("Proposed technical");
        result.ApprovedVersionId.Should().Be(approved.Id);
        result.ApprovalStatus.Should().Be(SspSectionStatus.Draft);
        result.CurrentVersion.Should().Be(2);
        result.AiSuggested.Should().BeFalse();
        result.IsAutoPopulated.Should().BeFalse();
        result.IsManuallyCustomized.Should().BeTrue();
        (await _db.NarrativeVersions.SingleAsync(x => x.Id == approved.Id)).Content.Should().Be("Retained technical");
    }

    [Fact]
    public async Task LegacyTailoring_CannotBypassReviewedEnhancementAddition()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddScoped(_ => new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options))
            .BuildServiceProvider();
        var tailoring = new BaselineService(services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IReferenceDataService>(), NullLogger<BaselineService>.Instance, Mock.Of<IOrgInheritanceService>());

        // Act
        var result = await tailoring.TailorBaselineAsync(_systemId,
            [new TailoringInput { ControlId = "AC-11(1)", Action = "Added", Rationale = "Attempt to bypass review" }], "author");

        // Assert
        result.Accepted.Should().BeEmpty();
        result.Rejected.Single().Reason.Should().Contain("proposal");
        (await _db.ControlBaselines.AsNoTracking().SingleAsync()).ControlIds.Should().Equal("AC-11");
    }

    [Theory]
    [InlineData("bind")]
    [InlineData("save")]
    [InlineData("review")]
    [InlineData("accept")]
    [InlineData("return")]
    public async Task MutationPermissions_AreCheckedBeforeChangingRecords(string operation)
    {
        // Arrange
        SetPermissions(true, false, false, false);
        Func<Task> action = operation switch
        {
            "bind" => () => _service.BindCatalogAsync(_systemId, "framework", 1, "Source", "viewer", default),
            "save" => () => _service.SaveMappingsAsync(_systemId, "AC-11", new(1, [], new Dictionary<string, string>()), "viewer", default),
            "review" => () => _service.ReviewMappingsAsync(_systemId, "AC-11", 1, "viewer", default),
            "accept" => () => _service.AcceptAsync(_systemId, "proposal", 1, "viewer", default),
            _ => () => _service.ReturnProposalAsync(_systemId, "proposal", 1, "Note", "viewer", default)
        };

        // Act / Assert
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        (await _db.ControlBaselines.AsNoTracking().SingleAsync()).ControlIds.Should().Equal("AC-11");
    }

    [Theory]
    [InlineData("missing-binding")]
    [InlineData("corrupt-binding")]
    [InlineData("missing-source")]
    [InlineData("unknown-baseline-id")]
    public async Task CatalogSourceFailures_AreExplicit(string failure)
    {
        // Arrange
        var baseline = await _db.ControlBaselines.SingleAsync();
        var framework = await _db.ComplianceFrameworks.SingleAsync();
        var binding = await _db.BaselineCatalogBindings.SingleAsync();
        if (failure == "missing-binding") baseline.RequirementCatalogBindingId = null;
        if (failure == "corrupt-binding") binding.CatalogJson += " ";
        if (failure == "missing-source") framework.RequirementCatalogJson = null;
        if (failure == "unknown-baseline-id") baseline.ControlIds = ["unknown"];
        await _db.SaveChangesAsync();

        // Act / Assert
        if (failure == "missing-binding")
        {
            (await _service.ReadAsync(_systemId, "AC-11", default)).Gaps.Should().Contain("Reference catalog association is being prepared automatically.");
            Func<Task> propose = () => _service.ProposeAsync(_systemId, new("AC-11", "AC-11(1)", 1, "Rationale", null, "Draft"), "author", default);
            await propose.Should().ThrowAsync<InvalidOperationException>();
        }
        else if (failure == "corrupt-binding")
        {
            Func<Task> read = () => _service.ReadAsync(_systemId, "AC-11", default);
            await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integrity*");
        }
        else
        {
            Func<Task> bind = () => _service.BindCatalogAsync(_systemId, framework.Id, 1, "Rationale", "author", default);
            if (failure == "unknown-baseline-id") await bind.Should().ThrowAsync<ArgumentException>();
            else await bind.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown-statement")]
    [InlineData("wrong-kind")]
    [InlineData("evidence-null")]
    [InlineData("evidence-too-many")]
    [InlineData("evidence-foreign")]
    [InlineData("parameter-unknown")]
    [InlineData("responses-null")]
    [InlineData("responses-null-entry")]
    [InlineData("parameters-null")]
    public async Task MappingValidation_RejectsInvalidOrForeignReferences(string failure)
    {
        // Arrange
        var implementation = new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "AC-11", AuthoredBy = "fixture" };
        _db.Add(implementation);
        await _db.SaveChangesAsync();
        var response = new RequirementResponse("requirement-alpha", "Policy", "Draft", []);
        var input = new RequirementMappingInput(1, [response], new Dictionary<string, string>());
        input = failure switch
        {
            "duplicate" => input with { Responses = [response, response] },
            "unknown-statement" => input with { Responses = [response with { StatementId = "other" }] },
            "wrong-kind" => input with { Responses = [response with { Kind = "Combined" }] },
            "evidence-null" => input with { Responses = [response with { Evidence = null! }] },
            "evidence-too-many" => input with { Responses = [response with { Evidence = Enumerable.Repeat(new RequirementEvidencePin("a", "hash"), 101).ToArray() }] },
            "evidence-foreign" => input with { Responses = [response with { Evidence = [new("foreign", "hash")] }] },
            "parameter-unknown" => input with { Parameters = new Dictionary<string, string> { ["foreign"] = "Value" } },
            "responses-null" => input with { Responses = null! },
            "responses-null-entry" => input with { Responses = [null!] },
            _ => input with { Parameters = null! }
        };

        // Act
        Func<Task> save = () => _service.SaveMappingsAsync(_systemId, "AC-11", input, "author", default);

        // Assert
        await save.Should().ThrowAsync<ArgumentException>();
        implementation.RequirementCoverageJson.Should().BeNull();
    }

    [Theory]
    [InlineData("empty-draft")]
    [InlineData("large-draft")]
    [InlineData("unrelated")]
    [InlineData("selected")]
    [InlineData("duplicate")]
    [InlineData("under-review")]
    public async Task ProposalValidation_RejectsIneligibleAdditions(string failure)
    {
        // Arrange
        var input = new EnhancementAdditionInput("AC-11", "AC-11(1)", 1, "Rationale", null, "Draft");
        if (failure == "empty-draft") input = input with { TechnicalDraft = null };
        if (failure == "large-draft") input = input with { TechnicalDraft = new string('x', 8001) };
        if (failure == "unrelated") input = input with { ParentControlId = "AC-11(1)" };
        if (failure == "selected")
        {
            var baseline = await _db.ControlBaselines.SingleAsync();
            baseline.ControlIds = ["AC-11", "AC-11(1)"];
        }
        if (failure == "under-review") _db.Add(new ControlImplementation { TenantId = _tenantId,
            RegisteredSystemId = _systemId, ControlId = "AC-11(1)", AuthoredBy = "fixture", ApprovalStatus = SspSectionStatus.UnderReview });
        await _db.SaveChangesAsync();
        if (failure == "duplicate")
        {
            await _service.ProposeAsync(_systemId, input, "author", default);
            input = input with { TechnicalDraft = "Different draft" };
        }

        // Act
        Func<Task> propose = () => _service.ProposeAsync(_systemId, input, "author", default);

        // Assert
        if (failure is "empty-draft" or "large-draft" or "unrelated")
            await propose.Should().ThrowAsync<ArgumentException>();
        else
            await propose.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DraftCoverage_ReportsAllSourceParameterAndReviewGaps()
    {
        // Arrange
        var implementation = new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "AC-11", PolicyNarrative = "Policy", AuthoredBy = "fixture" };
        _db.Add(implementation);
        await _db.SaveChangesAsync();
        await _service.SaveMappingsAsync(_systemId, "AC-11",
            new(1, [new("requirement-alpha", "Policy", "Draft", [])], new Dictionary<string, string>()), "author", default);
        var snapshot = RequirementCoverageService.Deserialize(implementation.RequirementCoverageJson)!;
        implementation.RequirementCoverageJson = JsonSerializer.Serialize(snapshot with { BindingId = "previous", NarrativeHash = "previous" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await _db.SaveChangesAsync();
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());

        // Act
        var detail = await _service.ReadAsync(_systemId, "AC-11", default);
        Func<Task> review = () => _service.ReviewMappingsAsync(_systemId, "AC-11", 2, "reviewer", default);

        // Assert
        detail.Gaps.Should().HaveCount(5);
        await review.Should().ThrowAsync<InvalidOperationException>();
        implementation.ApprovedRequirementCoverageJson.Should().BeNull();
    }

    [Fact]
    public async Task ReadProjectsPendingProposalAndParent_AndAcceptanceRejectsChangedBaseline()
    {
        // Arrange
        var proposal = await _service.ProposeAsync(_systemId, new("AC-11", "AC-11(1)", 1, "Scope", null, "Draft"), "author", default);
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());

        // Act
        await _service.RequireReadAsync(_systemId, default);
        var detail = await _service.ReadAsync(_systemId, "AC-11(1)", default);

        // Assert
        detail.Parent!.ControlId.Should().Be("AC-11");
        detail.Proposals.Single().CanAccept.Should().BeTrue();

        // Arrange
        var baseline = await _db.ControlBaselines.SingleAsync();
        baseline.CoverageRevision++;
        await _db.SaveChangesAsync();

        // Act
        Func<Task> accept = () => _service.AcceptAsync(_systemId, proposal.Id, 1, "reviewer", default);

        // Assert
        await accept.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CONCURRENCY_CONFLICT*");
    }

    [Fact]
    public async Task AcceptExistingLegacyDraft_CapturesOriginalVersionBeforeReplacingOneHalf()
    {
        // Arrange
        var legacy = new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "AC-11(1)", TechnicalNarrative = "Keep technical", AuthoredBy = "original" };
        _db.Add(legacy);
        await _db.SaveChangesAsync();
        var proposal = await _service.ProposeAsync(_systemId, new("AC-11", "AC-11(1)", 1, "Scope", "New policy", null), "author", default);
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());

        // Act
        await _service.AcceptAsync(_systemId, proposal.Id, 1, "reviewer", default);

        // Assert
        legacy.TechnicalNarrative.Should().Be("Keep technical");
        (await _db.NarrativeVersions.SingleAsync(x => x.VersionNumber == 1)).Content.Should().Be("Keep technical");
    }

    [Fact]
    public async Task MappingWrite_RejectsAnUnselectedEnhancement()
    {
        // Arrange
        _db.Add(new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "AC-11(1)", AuthoredBy = "fixture" });
        await _db.SaveChangesAsync();

        // Act
        Func<Task> save = () => _service.SaveMappingsAsync(_systemId, "AC-11(1)",
            new(1, [new("concealment-statement", "Technical", "Draft", [])], new Dictionary<string, string>()), "author", default);

        // Assert
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not selected*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acceptance_RechecksExistingNarrativeAndCatalogEligibility(bool withdrawn)
    {
        // Arrange
        var implementation = new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "AC-11(1)", TechnicalNarrative = "Original", AuthoredBy = "fixture" };
        _db.Add(implementation);
        await _db.SaveChangesAsync();
        var proposal = await _service.ProposeAsync(_systemId, new("AC-11", "AC-11(1)", 1, "Scope", null, "Draft"), "author", default);
        if (withdrawn)
        {
            var binding = await _db.BaselineCatalogBindings.SingleAsync();
            var source = JsonNode.Parse(binding.CatalogJson)!;
            source["groups"]![0]!["controls"]![0]!["controls"]![0]!["props"]!.AsArray()
                .Add(new JsonObject { ["name"] = "status", ["value"] = "withdrawn" });
            binding.CatalogJson = source.ToJsonString();
            binding.ContentHash = RequirementCoverageService.Hash(binding.CatalogJson);
        }
        else implementation.TechnicalNarrative = "Changed by a legacy writer";
        await _db.SaveChangesAsync();
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());

        // Act
        Func<Task> accept = () => _service.AcceptAsync(_systemId, proposal.Id, 1, "reviewer", default);

        // Assert
        await accept.Should().ThrowAsync<InvalidOperationException>();
        (await _db.ControlBaselines.AsNoTracking().SingleAsync()).ControlIds.Should().Equal("AC-11");
        proposal.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task SourceIdentifiers_ResolveExistingRecordsWithoutCreatingDisplayAliasDuplicates()
    {
        // Arrange
        var baseline = await _db.ControlBaselines.SingleAsync();
        baseline.ControlIds = ["parent-original"];
        _db.Add(new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
            ControlId = "enhancement-source", TechnicalNarrative = "Original response", AuthoredBy = "fixture" });
        await _db.SaveChangesAsync();

        // Act
        var proposal = await _service.ProposeAsync(_systemId,
            new("parent-original", "enhancement-source", 1, "Source identifier scope", null, "Proposed response"), "author", default);
        _tenant.SetupGet(x => x.PersonId).Returns(Guid.NewGuid());
        await _service.AcceptAsync(_systemId, proposal.Id, 1, "reviewer", default);
        var parent = await _service.ReadAsync(_systemId, "parent-original", default);
        var child = await _service.ReadAsync(_systemId, "enhancement-source", default);

        // Assert
        (await _db.ControlImplementations.CountAsync()).Should().Be(1);
        proposal.ControlId.Should().Be("enhancement-source");
        parent.Enhancements.Single().ControlId.Should().Be("enhancement-source");
        child.Parent!.ControlId.Should().Be("parent-original");
        child.Proposals.Single().Status.Should().Be("Accepted");
    }

    [Fact]
    public async Task AmbiguousLegacyAliases_RequireExplicitReconciliation()
    {
        // Arrange
        foreach (var id in new[] { "AC-11", "parent-original" })
            _db.Add(new ControlImplementation { TenantId = _tenantId, RegisteredSystemId = _systemId,
                ControlId = id, TechnicalNarrative = "Retained legacy text", AuthoredBy = "fixture" });
        await _db.SaveChangesAsync();

        // Act
        Func<Task> read = () => _service.ReadAsync(_systemId, "AC-11", default);

        // Assert
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Reconcile source/display identifiers*");
        (await _db.ControlImplementations.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task BaselineReselection_PreservesExistingCatalogSnapshotHistory()
    {
        // Arrange
        _db.SecurityCategorizations.Add(new() { TenantId = _tenantId, RegisteredSystemId = _systemId });
        await _db.SaveChangesAsync();
        var binding = await _db.BaselineCatalogBindings.AsNoTracking().SingleAsync();
        var reference = new Mock<IReferenceDataService>();
        reference.Setup(x => x.GetBaselineControlIds(It.IsAny<string>())).Returns(["AC-11"]);
        using var services = new ServiceCollection()
            .AddScoped(_ => new AtoCopilotContext(new DbContextOptionsBuilder<AtoCopilotContext>().UseSqlite(_connection).Options))
            .BuildServiceProvider();
        var service = new BaselineService(services.GetRequiredService<IServiceScopeFactory>(),
            reference.Object, NullLogger<BaselineService>.Instance, Mock.Of<IOrgInheritanceService>());

        // Act
        var selected = await service.SelectBaselineAsync(_systemId, applyOverlay: false, selectedBy: "author");

        // Assert
        selected.Id.Should().Be(_baselineId);
        (await _db.BaselineCatalogBindings.AsNoTracking().SingleAsync()).ContentHash.Should().Be(binding.ContentHash);
        selected.SourceFrameworkIdentifier.Should().Be(AutomaticCatalogBindingService.NistRev5);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
