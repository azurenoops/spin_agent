using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.Dashboard;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Ato.Copilot.Core.Interfaces.Compliance;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class PackageReadinessHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Theory]
    [InlineData(OrganizationRole.MissionOwner)]
    [InlineData(OrganizationRole.Issm)]
    public async Task UnresolvedCheckActions_OpenActualSourceWorkflows_WithoutInventingAggregateEditAuthority(OrganizationRole role)
    {
        // Arrange
        var system = await SeedAsync(role);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.SystemProfileSections.Add(new()
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId, RegisteredSystemId = system,
            SectionType = ProfileSectionType.DataTypes, GovernanceStatus = SspSectionStatus.Draft
        });
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        var expected = new Dictionary<string, string>
        {
            ["profile-approval"] = "documents?tab=records#ssp-sections",
            ["ssp"] = "documents?tab=records#ssp-sections",
            ["schema-ssp"] = "documents?tab=records#ssp-sections",
            ["schema-assessment-plan"] = "assessments?tab=plan",
            ["schema-assessment-results"] = "assessments",
            ["schema-poam"] = "poam",
            ["responsibility"] = "inheritance/subscriptions",
            ["interconnection-agreements"] = "profile/PortsProtocolsAndServices"
        };

        // Act
        var response = await client.PostAsJsonAsync(Root(system) + "/runs", new { purpose = "InitialSubmission" });
        var workspace = await client.GetAsync(Root(system) + "?purpose=InitialSubmission");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var checks = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checks").GetProperty("items")
            .EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!);
        foreach (var (id, path) in expected)
        {
            var action = checks[id].GetProperty("action");
            action.GetProperty("path").GetString().Should().Be(path, id);
            action.GetProperty("canView").GetBoolean().Should().BeTrue();
            action.GetProperty("canEdit").GetBoolean().Should().BeFalse();
            action.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        }
        checks.Values.Where(x => x.GetProperty("outcome").GetString() is "Blocking" or "FollowUp" or "Unavailable")
            .Should().OnlyContain(x => x.GetProperty("action").GetProperty("path").GetString() != "documents");
        checks["inventory"].GetProperty("outcome").GetString().Should().Be("Blocking");
        checks["inventory"].GetProperty("action").GetProperty("path").GetString()
            .Should().Be("security-capabilities/inventory?tab=hardware-software");
        var inventory = (await workspace.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documents").EnumerateArray()
            .Single(x => x.GetProperty("kind").GetString() == "inventory");
        inventory.GetProperty("validationOutcome").GetString().Should().Be("Blocking");
    }

    [Fact]
    public async Task ReadOnlyWorkspace_DoesNotCreateRun_ValidationRetainsChecksAndPurpose()
    {
        // Arrange
        var system = await SeedAsync();
        using var client = factory.CreateClient();
        var root = Root(system);

        // Act
        var before = await client.GetAsync(root + "?purpose=InitialSubmission");
        var result = await client.PostAsJsonAsync(root + "/runs", new { purpose = "InitialSubmission" });
        var history = await client.GetAsync(root + "/runs?purpose=InitialSubmission");

        // Assert
        before.StatusCode.Should().Be(HttpStatusCode.OK, await before.Content.ReadAsStringAsync());
        (await before.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("latestRun").ValueKind.Should().Be(JsonValueKind.Null);
        result.StatusCode.Should().Be(HttpStatusCode.Created, await result.Content.ReadAsStringAsync());
        var data = await result.Content.ReadFromJsonAsync<JsonElement>();
        data.GetProperty("purpose").GetString().Should().Be("InitialSubmission");
        var checks = data.GetProperty("checks").GetProperty("items").EnumerateArray().ToArray();
        checks.Should().Contain(x => x.GetProperty("id").GetString() == "authorization-decision"
            && x.GetProperty("outcome").GetString() == "NotApplicable");
        checks.Should().Contain(x => x.GetProperty("id").GetString() == "boundary"
            && x.GetProperty("outcome").GetString() == "Blocking");
        checks.Should().Contain(x => x.GetProperty("id").GetString() == "privacy-pta");
        data.GetProperty("run").GetProperty("counts").GetProperty("total").GetInt32().Should()
            .Be(data.GetProperty("checks").GetProperty("totalCount").GetInt32());
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        (await history.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SavedSourceChange_MakesPriorRunStaleWithoutRewritingHistory_AndRejectsGeneration()
    {
        // Arrange
        var system = await SeedAsync();
        using var client = factory.CreateClient();
        var root = Root(system);
        var validation = await client.PostAsJsonAsync(root + "/runs", new { purpose = "InitialSubmission" });
        validation.StatusCode.Should().Be(HttpStatusCode.Created, await validation.Content.ReadAsStringAsync());
        var original = await validation.Content.ReadFromJsonAsync<JsonElement>();
        var run = original.GetProperty("run");
        var id = run.GetProperty("id").GetString();
        var sourceHash = run.GetProperty("sourceHash").GetString();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.RegisteredSystems.SingleAsync(x => x.Id == system)).Name = "Updated synthetic source";
            await db.SaveChangesAsync();
        }

        // Act
        var read = await client.GetAsync(root + $"/runs/{id}?purpose=InitialSubmission");
        var generation = await client.PostAsJsonAsync($"/api/v1/systems/{system}/packages", new
        {
            purpose = "InitialSubmission", readinessRunId = id, expectedSourceHash = sourceHash,
            evidenceMode = "ManifestOnly", includeEvidence = true
        });

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var after = (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run");
        after.GetProperty("sourceHash").GetString().Should().Be(sourceHash);
        after.GetProperty("evaluatedAt").GetString().Should().Be(run.GetProperty("evaluatedAt").GetString());
        after.GetProperty("freshness").GetProperty("state").GetString().Should().Be("Stale");
        generation.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var verify = factory.Services.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<AtoCopilotContext>().AuthorizationPackages.CountAsync(x => x.RegisteredSystemId == system))
            .Should().Be(0);
    }

    [Fact]
    public async Task HistoryAndCheckDetails_RejectDifferentPurposeSystemAndTenant()
    {
        // Arrange
        var system = await SeedAsync();
        using var client = factory.CreateClient();
        var validation = await client.PostAsJsonAsync(Root(system) + "/runs", new { purpose = "InitialSubmission" });
        validation.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = await validation.Content.ReadFromJsonAsync<JsonElement>();
        var id = data.GetProperty("run").GetProperty("id").GetString();
        var other = await SeedAsync();

        // Act
        var wrongPurpose = await client.GetAsync(Root(system) + $"/runs/{id}?purpose=Legacy");
        var wrongSystem = await client.GetAsync(Root(other) + $"/runs/{id}/checks/boundary?purpose=InitialSubmission");
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var wrongTenant = await client.GetAsync(Root(system) + $"/runs/{id}?purpose=InitialSubmission");

        // Assert
        wrongPurpose.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongSystem.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LegacyDecisionGate_AndUnknownOwnership_AreNotInventedFromPersona()
    {
        // Arrange
        var system = await SeedAsync(OrganizationRole.MissionOwner);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Simulated-Role", "Issm");

        // Act
        var validation = await client.PostAsJsonAsync(Root(system) + "/runs", new { purpose = "Legacy" });

        // Assert
        validation.StatusCode.Should().Be(HttpStatusCode.Created, await validation.Content.ReadAsStringAsync());
        var data = await validation.Content.ReadFromJsonAsync<JsonElement>();
        var decision = data.GetProperty("checks").GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("id").GetString() == "authorization-decision");
        decision.GetProperty("outcome").GetString().Should().Be("Blocking");
        decision.GetProperty("recordedOwner").ValueKind.Should().Be(JsonValueKind.Null);
        decision.GetProperty("action").GetProperty("canEdit").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("AuthorizedBaselineArchive")]
    [InlineData("ChangeSubmission")]
    public async Task InvalidOrUnselectedPurpose_DoesNotCreateFakePackages(string purpose)
    {
        // Arrange
        var system = await SeedAsync();
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(Root(system) + "/runs", new { purpose });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().AuthorizationPackages.CountAsync(x => x.RegisteredSystemId == system))
            .Should().Be(0);
    }

    [Theory]
    [InlineData(PtaDetermination.PiaRequired, "Blocking")]
    [InlineData(PtaDetermination.PiaNotRequired, "NotApplicable")]
    [InlineData(PtaDetermination.PendingConfirmation, "Unavailable")]
    public async Task PrivacyApplicability_UsesRecordedPta_NotUniversalPiaRequirement(PtaDetermination determination, string outcome)
    {
        // Arrange
        var system = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.PrivacyThresholdAnalyses.Add(new()
        {
            TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId, RegisteredSystemId = system,
            Determination = determination
        });
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(Root(system) + "/runs", new { purpose = "InitialSubmission" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var checks = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("checks").GetProperty("items").EnumerateArray();
        checks.Single(x => x.GetProperty("id").GetString() == "privacy-pia").GetProperty("outcome").GetString().Should().Be(outcome);
    }

    [Fact]
    public async Task ProfileEdit_ChangesActualWorkingPreviewAndReadinessFingerprint_ButNotApprovedOutput()
    {
        // Arrange
        var system = await SeedAsync(OrganizationRole.MissionOwner);
        using var client = factory.CreateClient();
        var profile = $"/api/dashboard/systems/{system}/profile/UsersAndAccess";
        var saved = await client.PutAsJsonAsync(profile, new
        {
            content = """{"access":"Before synthetic source edit"}""",
            childItems = new[] { new { categoryName = "Synthetic operators before", approximateCount = 1 } }
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var rowId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userCategories")[0].GetProperty("id").GetString();
        var validated = await client.PostAsJsonAsync(Root(system) + "/runs", new { purpose = "InitialSubmission" });
        validated.StatusCode.Should().Be(HttpStatusCode.Created);
        var runId = (await validated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run").GetProperty("id").GetString();
        await using var scope = factory.Services.CreateAsyncScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IOscalSspExportService>();
        var before = await exporter.PreviewAsync(system);
        var approvedBefore = await exporter.ExportAsync(system);

        // Act
        var edit = await client.PutAsJsonAsync(profile, new
        {
            content = """{"access":"After synthetic source edit"}""",
            childItems = new[] { new { id = rowId, revision = 1, categoryName = "Synthetic operators after", approximateCount = 2 } }
        });
        var after = await exporter.PreviewAsync(system);
        var approvedAfter = await exporter.ExportAsync(system);
        var historical = await client.GetAsync(Root(system) + $"/runs/{runId}?purpose=InitialSubmission");

        // Assert
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        before.OscalJson.Should().Contain("Synthetic operators before");
        after.OscalJson.Should().Contain("Synthetic operators after").And.NotContain("Synthetic operators before");
        approvedBefore.OscalJson.Should().NotContain("Synthetic operators before");
        approvedAfter.OscalJson.Should().NotContain("Synthetic operators after");
        (await historical.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run").GetProperty("freshness").GetProperty("state")
            .GetString().Should().Be("Stale");
    }

    [Fact]
    public async Task CurrentPermissionsAreRecomputed_WhileRetainedNamedOwnerIsImmutable()
    {
        // Arrange
        var system = await SeedAsync();
        using var client = factory.CreateClient();
        var validated = await client.PostAsJsonAsync(Root(system) + "/runs", new { purpose = "InitialSubmission" });
        validated.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await validated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run").GetProperty("id").GetString();
        var path = Root(system) + $"/runs/{id}/checks/boundary?purpose=InitialSubmission";
        var before = await client.GetFromJsonAsync<JsonElement>(path);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var role = await db.SystemRoleAssignments.SingleAsync(x => x.RegisteredSystemId == system);
        role.Role = OrganizationRole.MissionOwner;
        await db.SaveChangesAsync();

        // Act
        var after = await client.GetFromJsonAsync<JsonElement>(path);
        var retained = await db.PackageReadinessRuns.SingleAsync(x => x.Id == id);
        retained.ChecksJson = "[]";
        var overwrite = () => db.SaveChangesAsync();

        // Assert
        before.GetProperty("check").GetProperty("action").GetProperty("canEdit").GetBoolean().Should().BeTrue();
        after.GetProperty("check").GetProperty("action").GetProperty("canEdit").GetBoolean().Should().BeFalse();
        before.GetProperty("check").GetProperty("recordedOwner").GetRawText().Should().Be(after.GetProperty("check").GetProperty("recordedOwner").GetRawText());
        await overwrite.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
    }

    [Fact]
    public async Task RetainedArchive_IsScopedToExactSelection_AndIgnoresUnrelatedWorkingProfileChanges()
    {
        // Arrange
        var system = await SeedAsync();
        using var client = factory.CreateClient();
        var directory = Directory.CreateTempSubdirectory("readiness-retained-");
        var file = Path.Combine(directory.FullName, "baseline.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(file, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("oscal-ssp.json").Open());
                writer.Write("{\"synthetic\":true}");
            }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant();
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var package = new AuthorizationPackage
            {
                TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId, RegisteredSystemId = system,
                Status = PackageStatus.Completed, FilePath = file, ContentHash = hash, ValidationPassed = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), GeneratedBy = "test"
            };
            var decision = new AuthorizationDecision
            {
                TenantId = package.TenantId, RegisteredSystemId = system, DecisionType = AuthorizationDecisionType.Ato,
                IssuedBy = "test", BaselinePackageId = package.Id, BaselinePackageHash = hash
            };
            db.AddRange(package, decision);
            await db.SaveChangesAsync();
            var selection = new RetainedPackageSelection(package.Id, hash, decision.Id);
            var validation = await client.PostAsJsonAsync(Root(system) + "/runs", new { purpose = "AuthorizedBaselineArchive", retainedContext = selection });
            validation.StatusCode.Should().Be(HttpStatusCode.Created, await validation.Content.ReadAsStringAsync());
            var original = (await validation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run");
            original.GetProperty("outcome").GetString().Should().Be("Ready", original.GetRawText());
            var query = $"purpose=AuthorizedBaselineArchive&baselinePackageId={package.Id}&baselineContentHash={hash}&authorizationDecisionId={decision.Id}";
            (await db.RegisteredSystems.SingleAsync(x => x.Id == system)).Name = "Unrelated working change";
            await db.SaveChangesAsync();

            // Act
            var retained = await client.GetAsync(Root(system) + $"/runs/{original.GetProperty("id").GetString()}?{query}");
            var workspace = await client.GetAsync(Root(system) + $"?{query}");
            await File.AppendAllTextAsync(file, "tampered");
            var tampered = await client.GetAsync(Root(system) + $"/runs/{original.GetProperty("id").GetString()}?{query}");
            var failedEvaluation = await client.PostAsJsonAsync(Root(system) + "/runs",
                new { purpose = "AuthorizedBaselineArchive", retainedContext = selection });

            // Assert
            retained.StatusCode.Should().Be(HttpStatusCode.OK);
            (await retained.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run").GetProperty("freshness").GetProperty("state").GetString().Should().Be("Current");
            workspace.StatusCode.Should().Be(HttpStatusCode.OK, await workspace.Content.ReadAsStringAsync());
            (await tampered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run").GetProperty("freshness").GetProperty("state").GetString().Should().Be("Unavailable");
            failedEvaluation.StatusCode.Should().Be(HttpStatusCode.Created);
            (await failedEvaluation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("run").GetProperty("outcome").GetString().Should().Be("Failed");
        }
        finally
        {
            File.Delete(file);
            directory.Delete();
        }
    }

    [Fact]
    public async Task RecordedExportAndManualReceipt_DoNotInventValidationOrAuthorizationMilestones()
    {
        // Arrange
        var system = await SeedAsync();
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var package = new AuthorizationPackage
        {
            TenantId = tenant, RegisteredSystemId = system, Purpose = PackagePurpose.InitialSubmission,
            Status = PackageStatus.Completed, ContentHash = new string('a', 64), GeneratedBy = "test",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        db.AuthorizationPackages.Add(package);
        await db.SaveChangesAsync();
        var exportOnly = await client.GetFromJsonAsync<JsonElement>(Root(system) + "?purpose=InitialSubmission");
        db.Set<EmassExchangeRecord>().Add(new()
        {
            TenantId = tenant, RegisteredSystemId = system, PackageId = package.Id, PackageHash = package.ContentHash,
            ExportGeneratedAt = package.GeneratedAt, Outcome = "ImportAccepted", Version = 1,
            ReceivingWorkflow = "Synthetic manual receipt", ExternalReference = "SYNTHETIC-1",
            OccurredAt = DateTimeOffset.UtcNow, RecordedAt = DateTimeOffset.UtcNow, RecordedBy = "test",
            IdempotencyKey = Guid.NewGuid().ToString(), RequestHash = new string('b', 64)
        });
        await db.SaveChangesAsync();

        // Act
        var received = await client.GetFromJsonAsync<JsonElement>(Root(system) + "?purpose=InitialSubmission");

        // Assert
        var before = exportOnly.GetProperty("progress").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!);
        before["export"].GetProperty("records")[0].GetProperty("id").GetString().Should().Be(package.Id);
        before["export"].GetProperty("records")[0].GetProperty("sourceRelationship").GetString().Should().Be("Unknown");
        before["emass"].GetProperty("state").GetString().Should().Be("NotRecorded");
        var after = received.GetProperty("progress").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!);
        after["emass"].GetProperty("records")[0].GetProperty("status").GetString().Should().Be("ImportAccepted");
        after["validate"].GetProperty("state").GetString().Should().Be("NotChecked");
        after["decision"].GetProperty("state").GetString().Should().Be("NotRecorded");
        received.GetProperty("latestRun").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SourceMutationDuringValidation_ProducesImmutableSourceChangedRun()
    {
        // Arrange
        var system = await SeedAsync();
        var schema = new Mock<IOscalSchemaValidationService>();
        schema.Setup(x => x.ValidateForSystemAsync(system, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string model, CancellationToken ct) =>
            {
                if (model == "ssp")
                {
                    await using var scope = factory.Services.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
                    (await db.RegisteredSystems.SingleAsync(x => x.Id == system, ct)).Name = "Changed during validation";
                    await db.SaveChangesAsync(ct);
                }
                return new OscalSchemaValidationResult { IsValid = true };
            });
        using var isolated = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOscalSchemaValidationService>();
            services.AddSingleton(schema.Object);
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
        }));

        // Act
        var run = await isolated.Services.GetRequiredService<PackageReadinessService>()
            .ValidateAsync(system, new(PackagePurpose.InitialSubmission), "synthetic-test", default);

        // Assert
        run.Outcome.Should().Be("SourceChanged");
        run.SourceHash.Should().NotBeNull().And.NotBe(run.SourceHashAfter);
        await using var verify = isolated.Services.CreateAsyncScope();
        var saved = await verify.ServiceProvider.GetRequiredService<AtoCopilotContext>().PackageReadinessRuns.SingleAsync(x => x.Id == run.Id);
        saved.SourceHash.Should().Be(run.SourceHash);
        saved.SourceHashAfter.Should().Be(run.SourceHashAfter);
    }

    private static string Root(string system) => $"/api/dashboard/systems/{system}/package-readiness";

    private async Task<string> SeedAsync(OrganizationRole role = OrganizationRole.Issm)
    {
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var person = Guid.NewGuid();
        factory.ResetLegacyTenantContext(tenant);
        factory.GetActiveContext().PersonId = person;
        factory.GetActiveContext().IsWorkspaceRequest = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(new() { TenantId = tenant, Id = person, DisplayName = "Synthetic readiness owner", Email = $"{person}@example.invalid" });
        db.OrganizationMemberships.Add(new()
        {
            TenantId = tenant, PersonId = person, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test"
        });
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic readiness system", CreatedBy = "test" };
        db.RegisteredSystems.Add(system);
        db.SystemRoleAssignments.Add(new() { TenantId = tenant, RegisteredSystemId = system.Id, PersonId = person, Role = role });
        await db.SaveChangesAsync();
        return system.Id;
    }
}
