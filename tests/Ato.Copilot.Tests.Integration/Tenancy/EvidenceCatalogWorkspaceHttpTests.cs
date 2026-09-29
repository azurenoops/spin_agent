using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class EvidenceCatalogWorkspaceHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid Directory = Guid.NewGuid();
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private readonly WebApplicationFactory<McpProgram> _factory;

    public EvidenceCatalogWorkspaceHttpTests(WorkspaceMembershipFactory factory)
    {
        Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING").Should().BeNullOrEmpty();
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType != typeof(TenancySeedHostedService)).ToArray())
                services.Remove(descriptor);
            var storage = new Mock<IFileStorageProvider>();
            storage.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            storage.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("protected bytes")));
            storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            services.AddSingleton(storage.Object);
        }));
    }

    [Fact]
    public async Task ExactScope_SameTenantSharedControlCannotReadDownloadReplaceDeleteForeignEvidence()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        var root = $"/api/dashboard/systems/{f.System}";

        // Act
        var list = await client.GetFromJsonAsync<JsonElement>(root + "/evidence-catalog");
        var detail = await client.GetFromJsonAsync<JsonElement>(root + $"/evidence-catalog/artifact:{f.Artifact}");

        // Assert
        list.GetProperty("counts").GetProperty("system").GetInt32().Should().Be(2);
        list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("recordId").GetString())
            .Should().BeEquivalentTo(f.Artifact, f.Automated);
        detail.GetProperty("owner").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("review").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("version").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetRawText().Should().NotContain("private-storage-key");
        detail.GetProperty("permissions").GetProperty("canDownload").GetBoolean().Should().BeTrue();
        detail.GetProperty("item").GetProperty("controls")[0].GetProperty("kind").GetString().Should().Be("direct");
        foreach (var suffix in new[] { "", "/download", "/versions", $"/versions/{f.ForeignVersion}/download" })
            (await client.GetAsync(root + $"/evidence/{f.ForeignArtifact}{suffix}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(root + $"/evidence-catalog/artifact:{f.ForeignArtifact}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(root + $"/evidence-catalog/automated:{f.ForeignAutomated}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(root + $"/evidence/{f.ForeignAutomated}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(root + $"/evidence/{f.ForeignArtifact}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var replacement = FileForm();
        (await client.PutAsync(root + $"/evidence/{f.ForeignArtifact}", replacement)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/dashboard/systems/{f.OtherSystem}/evidence/{f.ForeignArtifact}"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the foreign record must remain unchanged");
    }

    [Fact]
    public async Task ReadOnlyWorkspace_PermissionsDenyAllMutations()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.MissionOwner);
        using var client = Client(f.Actor);
        var root = $"/api/dashboard/systems/{f.System}";

        // Act
        var list = await client.GetFromJsonAsync<JsonElement>(root + "/evidence-catalog");
        var detail = await client.GetFromJsonAsync<JsonElement>(root + $"/evidence-catalog/artifact:{f.Artifact}");

        // Assert
        var permissions = list.GetProperty("permissions");
        permissions.GetProperty("canUpload").GetBoolean().Should().BeFalse();
        permissions.GetProperty("canManageEvidence").GetBoolean().Should().BeFalse();
        permissions.GetProperty("canManageLinks").GetBoolean().Should().BeFalse();
        permissions.GetProperty("uploadReason").GetString().Should().NotBeNullOrEmpty();
        detail.GetProperty("permissions").GetProperty("canLink").GetBoolean().Should().BeFalse();
        using var upload = FileForm();
        using var replacement = FileForm();
        (await client.PostAsync(root + "/evidence", upload)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsync(root + $"/evidence/{f.Artifact}", replacement)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync(root + $"/evidence/{f.Artifact}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(root + $"/controls/{f.Control}/collect-evidence", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync(root + $"/evidence-catalog/artifact:{f.Artifact}/links",
            new { controlId = f.Control, expectedHash = f.Hash })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Assessor_CanLinkButCannotManageEvidence_TargetAndHashAreValidated()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Assessor);
        using var client = Client(f.Actor);
        var root = $"/api/dashboard/systems/{f.System}/evidence-catalog";
        var linkedControl = "AU-" + Guid.NewGuid().ToString("N")[..8];
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.ControlImplementations.Add(new() { TenantId = Tenant, RegisteredSystemId = f.System, ControlId = linkedControl });
            await db.SaveChangesAsync();
        }

        // Act
        var list = await client.GetFromJsonAsync<JsonElement>(root);
        var stale = await client.PostAsJsonAsync(root + $"/artifact:{f.Artifact}/links",
            new { controlId = f.Control, expectedHash = "stale" });
        var foreign = await client.PostAsJsonAsync(root + $"/artifact:{f.ForeignArtifact}/links",
            new { controlId = f.Control, expectedHash = f.Hash });
        var linked = await client.PostAsJsonAsync(root + $"/artifact:{f.Artifact}/links",
            new { controlId = linkedControl, expectedHash = f.Hash });

        // Assert
        list.GetProperty("permissions").GetProperty("canManageEvidence").GetBoolean().Should().BeFalse();
        list.GetProperty("permissions").GetProperty("canManageLinks").GetBoolean().Should().BeTrue();
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        linked.StatusCode.Should().Be(HttpStatusCode.OK, await linked.Content.ReadAsStringAsync());
        var detail = await client.GetFromJsonAsync<JsonElement>(root + $"/artifact:{f.Artifact}");
        detail.GetProperty("item").GetProperty("controls").EnumerateArray()
            .Select(c => c.GetProperty("kind").GetString()).Should().BeEquivalentTo("direct", "validation");
        (await client.PostAsJsonAsync(root + $"/artifact:{f.Artifact}/links",
            new { controlId = linkedControl, expectedHash = f.Hash })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync(root + $"/provider:{Guid.NewGuid()}/links",
            new { controlId = f.Control, expectedHash = f.Hash })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadAndMutations_ValidateSystemTargetsAndStaleHash()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        var root = $"/api/dashboard/systems/{f.System}";
        using var foreignTarget = FileForm();
        foreignTarget.Add(new StringContent(f.ForeignImplementation), "controlImplementationId");
        using var goodTarget = FileForm();
        goodTarget.Add(new StringContent(f.Control), "controlId");
        using var replacement = FileForm();
        replacement.Add(new StringContent("stale"), "expectedHash");

        // Act / Assert
        (await client.PostAsync(root + "/evidence", foreignTarget)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync(root + "/evidence", goodTarget)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsync(root + $"/evidence/{f.Artifact}", replacement)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync(root + $"/evidence/{f.Artifact}?expectedHash=stale")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetAsync(root + $"/evidence/{f.Artifact}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync(root + "/controls/AC-missing/collect-evidence", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProtectedReads_RecheckRevokedSystemAssignment()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var assignment = await db.SystemRoleAssignments.SingleAsync(a => a.RegisteredSystemId == f.System);
            assignment.RemovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var root = $"/api/dashboard/systems/{f.System}";

        // Act / Assert
        foreach (var path in new[] { "/evidence-catalog", $"/evidence-catalog/artifact:{f.Artifact}",
            $"/evidence/{f.Artifact}", $"/evidence/{f.Artifact}/download", $"/evidence/{f.Artifact}/versions" })
            (await client.GetAsync(root + path)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AutomatedDetail_CollectionUnavailableWithoutConfiguredSubscription()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);

        // Act
        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/dashboard/systems/{f.System}/evidence-catalog/automated:{f.Automated}");

        // Assert
        detail.GetProperty("permissions").GetProperty("canCollect").GetBoolean().Should().BeFalse();
        detail.GetProperty("permissions").GetProperty("manageReason").GetString().Should().Contain("subscription");
    }

    private static MultipartFormDataContent FileForm()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("file bytes"));
        file.Headers.ContentType = new("text/plain");
        form.Add(file, "file", "evidence.txt");
        form.Add(new StringContent("Other"), "category");
        return form;
    }

    private HttpClient Client(Guid actor)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return client;
    }

    private async Task<Fixture> SeedAsync(OrganizationRole role)
    {
        using var initialized = _factory.CreateClient();
        var actor = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var person = new Person { TenantId = Tenant, DisplayName = "Evidence actor", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = Tenant, Name = $"Evidence {actor:N}", IsActive = true };
        var other = new RegisteredSystem { TenantId = Tenant, Name = $"Other evidence {actor:N}", IsActive = true };
        var control = "AC-" + Random.Shared.Next(1000000, 9999999);
        var implementation = new ControlImplementation { TenantId = Tenant, RegisteredSystemId = system.Id, ControlId = control };
        var foreignImplementation = new ControlImplementation { TenantId = Tenant, RegisteredSystemId = other.Id, ControlId = control };
        var assessment = new ComplianceAssessment { TenantId = Tenant, RegisteredSystemId = system.Id };
        var foreignAssessment = new ComplianceAssessment { TenantId = Tenant, RegisteredSystemId = other.Id };
        var artifact = new EvidenceArtifact { TenantId = Tenant, RegisteredSystemId = system.Id,
            ControlImplementationId = implementation.Id, FileName = "evidence.txt", ContentType = "text/plain",
            ContentHash = new string('a', 64), StoragePath = "private-storage-key", UploadedBy = "uploader" };
        var foreign = new EvidenceArtifact { TenantId = Tenant, RegisteredSystemId = other.Id,
            ControlImplementationId = foreignImplementation.Id, FileName = "foreign.txt", ContentHash = artifact.ContentHash };
        var version = new EvidenceVersion { TenantId = Tenant, EvidenceArtifactId = foreign.Id, FileName = "old.txt" };
        var automated = new ComplianceEvidence { TenantId = Tenant, AssessmentId = assessment.Id, ControlId = control, Description = "Scoped collection" };
        var foreignAutomated = new ComplianceEvidence { TenantId = Tenant, AssessmentId = foreignAssessment.Id, ControlId = control, Description = "Foreign collection" };
        db.AddRange(person, system, other, implementation, foreignImplementation, assessment, foreignAssessment,
            artifact, foreign, version, automated, foreignAutomated);
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = Directory,
            ObjectId = actor, PersonId = person.Id, GrantedBy = "fixture" });
        db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = system.Id, Role = role });
        db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = other.Id, Role = role });
        await db.SaveChangesAsync();
        return new(actor, system.Id, other.Id, control, artifact.Id, foreign.Id, automated.Id,
            foreignAutomated.Id, version.Id, foreignImplementation.Id, artifact.ContentHash);
    }

    private sealed record Fixture(Guid Actor, string System, string OtherSystem, string Control,
        string Artifact, string ForeignArtifact, string Automated, string ForeignAutomated,
        string ForeignVersion, string ForeignImplementation, string Hash);
}
