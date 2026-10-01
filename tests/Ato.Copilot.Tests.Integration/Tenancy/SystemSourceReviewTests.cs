using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Storage;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using QuestPDF.Fluent;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class SystemSourceReviewTests : IClassFixture<WorkspaceMembershipFactory>, IDisposable
{
    private readonly WebApplicationFactory<McpProgram> _host;
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private static readonly Guid Directory = Guid.Parse("acab0000-0000-0000-0000-000000000001");
    private string Root => $"/api/workspaces/organizations/{Tenant}/systems";

    public SystemSourceReviewTests(WorkspaceMembershipFactory factory)
    {
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFileStorageProvider>();
            services.AddSingleton<IFileStorageProvider>(_ => new MemorySources());
        }));
    }
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ReviewedReceipt_RetainsOriginalAndTargetIdentity_InActualDocxOutput()
    {
        // Arrange
        using var client = await ClientAsync();
        var source = Workbook();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var creation = await client.PostAsJsonAsync($"{Root}/setup-drafts", new { name = "Retained mission identity",
            missionPurpose = "Retained mission purpose", objective = "initialAto", lastScreen = "s-details" });
        creation.StatusCode.Should().Be(HttpStatusCode.Created, await creation.Content.ReadAsStringAsync());
        var system = (await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("systemId").GetString()!;
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // Act: receipt and review are separate from apply.
        var receiptResponse = await Upload(client, system, source);
        receiptResponse.StatusCode.Should().Be(HttpStatusCode.Created, await receiptResponse.Content.ReadAsStringAsync());
        var receipt = (await receiptResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var session = receipt.GetProperty("sessionId").GetGuid();
        var route = $"{Root}/{system}/source-imports/emass/{session}";
        var original = await client.GetByteArrayAsync($"{route}/original");
        var previewResponse = await client.PostAsJsonAsync($"{route}/review-previews", new { expectedSourceRevision = receipt.GetProperty("sourceRevision").GetInt64() });
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var before = await scope.ServiceProvider.GetRequiredService<AtoCopilotContext>().RegisteredSystems.SingleAsync(x => x.Id == system);
            before.Name.Should().Be("Retained mission identity");
            before.Acronym.Should().BeNull("preview cannot apply a proposed value");
        }
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var applied = await client.PostAsJsonAsync($"{route}/apply", new
        {
            expectedSourceRevision = preview.GetProperty("sourceRevision").GetInt64(),
            expectedSystemRevision = preview.GetProperty("identityRevision").GetString(),
            previewHash = preview.GetProperty("previewHash").GetString(),
            decisions = new[] { new { field = "name", decision = "keepCurrent" }, new { field = "acronym", decision = "applyProposed" } },
        });
        // Assert
        applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        original.Should().Equal(source);
        receipt.GetProperty("sha256").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant());
        await using var documentScope = _host.Services.CreateAsyncScope();
        var db = documentScope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var retained = await db.RegisteredSystems.SingleAsync(x => x.Id == system);
        retained.Name.Should().Be("Retained mission identity");
        retained.Acronym.Should().Be("SOURCE-ACR");
        (await db.RegisteredSystems.CountAsync(x => x.TenantId == Tenant && x.Name == "Source display name")).Should().Be(0);
        (await db.AuthorizationDecisions.AnyAsync(x => x.RegisteredSystemId == system)).Should().BeFalse();
        using var tenantScope = documentScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .Push(new TenantContext(Tenant));
        var docx = await documentScope.ServiceProvider.GetRequiredService<IDocumentTemplateService>()
            .RenderDocxAsync(system, "ssp", null);
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var text = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var content = await text.ReadToEndAsync();
        content.Should().Contain("Retained mission identity").And.Contain("SOURCE-ACR")
            .And.Contain("Retained mission purpose").And.Contain("Not recorded (setup draft)")
            .And.Contain(receipt.GetProperty("sha256").GetString()!);
    }

    [Fact]
    public async Task ReceiptReplayAndWrongSystemReview_DoNotCreateOrOverwriteSystems()
    {
        // Arrange
        using var client = await ClientAsync();
        var a = $"a-{Guid.NewGuid():N}";
        var b = $"b-{Guid.NewGuid():N}";
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.RegisteredSystems.AddRange(new RegisteredSystem { Id = a, TenantId = Tenant, Name = "A", CreatedBy = "fixture" },
            new RegisteredSystem { Id = b, TenantId = Tenant, Name = "B", CreatedBy = "fixture" });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var source = Workbook();
        // Act
        var first = await Upload(client, a, source);
        var replay = await Upload(client, a, source);
        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var receipt = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var session = receipt.GetProperty("sessionId").GetGuid();
        var bytes = await client.GetByteArrayAsync($"{Root}/{a}/source-imports/emass/{session}/original");
        var exactReplay = await Upload(client, a, bytes);
        exactReplay.StatusCode.Should().Be(HttpStatusCode.OK, await exactReplay.Content.ReadAsStringAsync());
        (await exactReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("sessionId").GetGuid().Should().Be(session);
        var wrong = await client.PostAsJsonAsync($"{Root}/{b}/source-imports/emass/{session}/review-previews", new { expectedSourceRevision = receipt.GetProperty("sourceRevision").GetInt64() });
        wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await db.RegisteredSystems.SingleAsync(x => x.Id == b)).Name.Should().Be("B");
    }

    [Fact]
    public async Task DigitalSspPdf_IsRetainedAsProposalsWithoutCreatingAnotherSystem()
    {
        // Arrange
        using var client = await ClientAsync();
        var id = $"pdf-{Guid.NewGuid():N}";
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.RegisteredSystems.Add(new RegisteredSystem { Id = id, TenantId = Tenant, Name = "PDF target", CreatedBy = "fixture" });
        await db.SaveChangesAsync();
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var bytes = QuestPDF.Fluent.Document.Create(container => container.Page(page => page.Content().Column(column =>
        {
            column.Item().Text("System ID: PDF-ACR");
            column.Item().Text("System Name: Source PDF system");
            column.Item().Text("NIST SP 800-53 Rev 5 System Security Plan. This synthetic source records identity fields for review.");
            column.Item().Text("Impact Level: Moderate. All extracted fields remain proposals until the operator reviews the exact selected target.");
        }))).GeneratePdf();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // Act
        var response = await Upload(client, id, bytes, "ssp-pdf", "synthetic.pdf");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var receipt = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        receipt.GetProperty("analysisState").GetString().Should().Be("parsed");
        receipt.GetProperty("reviewState").GetString().Should().Be("pending");
        receipt.GetProperty("fields").EnumerateArray().Should().Contain(field =>
            field.GetProperty("field").GetString() == "acronym" && field.GetProperty("proposedValue").GetString() == "PDF-ACR");
        (await db.RegisteredSystems.SingleAsync(x => x.Id == id)).Acronym.Should().BeNull();
    }

    [Fact]
    public async Task StalePreviewAndApprovedBaseline_PreventIdentityOverwrite()
    {
        // Arrange
        using var client = await ClientAsync();
        var id = $"sys-{Guid.NewGuid():N}";
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = new RegisteredSystem { Id = id, TenantId = Tenant, Name = "Original target", CreatedBy = "fixture" };
        db.RegisteredSystems.Add(system);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var uploaded = await Upload(client, id, Workbook());
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);
        var source = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var path = $"{Root}/{id}/source-imports/emass/{source.GetProperty("sessionId").GetGuid()}";
        var response = await client.PostAsJsonAsync($"{path}/review-previews", new { expectedSourceRevision = 1 });
        var preview = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        system.Name = "Concurrent human edit";
        await db.SaveChangesAsync();
        object Decisions(JsonElement p) => new { expectedSourceRevision = 1,
            expectedSystemRevision = p.GetProperty("identityRevision").GetString(), previewHash = p.GetProperty("previewHash").GetString(),
            decisions = new[] { new { field = "name", decision = "keepCurrent" }, new { field = "acronym", decision = "applyProposed" } } };
        // Act
        var stale = await client.PostAsJsonAsync($"{path}/apply", Decisions(preview));
        var refreshed = await client.PostAsJsonAsync($"{path}/review-previews", new { expectedSourceRevision = 1 });
        var fresh = (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        db.SspSections.Add(new SspSection { TenantId = Tenant, RegisteredSystemId = id, SectionNumber = 1,
            SectionTitle = "Approved identification", Content = "Reviewed baseline", Status = SspSectionStatus.Approved, AuthoredBy = "fixture" });
        await db.SaveChangesAsync();
        var protectedBaseline = await client.PostAsJsonAsync($"{path}/apply", Decisions(fresh));
        // Assert
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        protectedBaseline.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await protectedBaseline.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code")
            .GetString().Should().Be("SOURCE_REQUIRES_DOCUMENT_REVIEW");
        await db.Entry(system).ReloadAsync();
        system.Name.Should().Be("Concurrent human edit");
        system.Acronym.Should().BeNull();
    }

    [Fact]
    public async Task BoundReceipt_CannotBeCommittedThroughLegacyBulkService()
    {
        // Arrange
        using var client = await ClientAsync();
        var id = $"sys-{Guid.NewGuid():N}";
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.RegisteredSystems.Add(new RegisteredSystem { Id = id, TenantId = Tenant, Name = "Legacy bypass target", CreatedBy = "fixture" });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var uploaded = await Upload(client, id, Workbook());
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("sessionId").GetGuid();
        var service = scope.ServiceProvider.GetRequiredService<Ato.Copilot.Core.Interfaces.Onboarding.IEmassImportService>();
        // Act
        var commit = () => service.CommitAsync(Tenant, session,
            [new("SOURCE-ACR", Ato.Copilot.Core.Interfaces.Onboarding.EmassCommitDecision.Overwrite)],
            Guid.NewGuid(), Guid.NewGuid());
        // Assert
        await commit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*selected-system*");
    }

    [Fact]
    public async Task DefaultDocx_PreservesLongPurposeAndEveryReviewedSourceHash()
    {
        // Arrange
        using var client = await ClientAsync();
        var purpose = new string('P', 700) + " END-OF-REVIEWED-PURPOSE";
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var creation = await client.PostAsJsonAsync($"{Root}/setup-drafts", new
        { name = "Long-form system", missionPurpose = purpose, objective = "initialAto", lastScreen = "s-details" });
        creation.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("systemId").GetString()!;
        var hashes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var upload = await Upload(client, id, Workbook($"Distinct source {i}"));
            upload.StatusCode.Should().Be(HttpStatusCode.Created);
            var source = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            hashes.Add(source.GetProperty("sha256").GetString()!);
            var path = $"{Root}/{id}/source-imports/emass/{source.GetProperty("sessionId").GetGuid()}";
            var reviewed = await client.PostAsJsonAsync($"{path}/review-previews", new { expectedSourceRevision = 1 });
            reviewed.StatusCode.Should().Be(HttpStatusCode.OK);
            var preview = (await reviewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            var applied = await client.PostAsJsonAsync($"{path}/apply", new
            {
                expectedSourceRevision = 1, expectedSystemRevision = preview.GetProperty("identityRevision").GetString(),
                previewHash = preview.GetProperty("previewHash").GetString(),
                decisions = new[] { new { field = "name", decision = "keepCurrent" }, new { field = "acronym", decision = "keepCurrent" } }
            });
            applied.StatusCode.Should().Be(HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        }
        // Act
        await using var scope = _host.Services.CreateAsyncScope();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Push(new TenantContext(Tenant));
        var docx = await scope.ServiceProvider.GetRequiredService<IDocumentTemplateService>().RenderDocxAsync(id, "ssp", null);
        using var archive = new ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var xml = await reader.ReadToEndAsync();
        // Assert
        xml.Should().Contain(purpose, "the full recorded mission purpose must survive default DOCX rendering");
        hashes.Distinct().Should().HaveCount(3);
        foreach (var hash in hashes)
            xml.Should().Contain(hash, "every reviewed source SHA must survive, not just the first 500 characters");
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = _host.CreateClient();
        var actor = Guid.NewGuid();
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var person = new Person { TenantId = Tenant, DisplayName = "Source reviewer", Email = $"{actor:N}@example.invalid" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, PersonId = person.Id, DirectoryTenantId = Directory, ObjectId = actor, GrantedBy = "fixture" });
        db.OrganizationRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, Role = OrganizationRole.Issm });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return client;
    }
    private static byte[] Workbook(string sourceName = "Source display name")
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Systems");
        sheet.Cell(1, 1).Value = "system_identifier"; sheet.Cell(1, 2).Value = "system_name";
        sheet.Cell(2, 1).Value = "SOURCE-ACR"; sheet.Cell(2, 2).Value = sourceName;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
    private Task<HttpResponseMessage> Upload(HttpClient client, string system, byte[] bytes, string kind = "emass", string fileName = "mission.xlsx")
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return UploadCore(client, $"{Root}/{system}/source-imports/{kind}", form);
    }
    private static async Task<HttpResponseMessage> UploadCore(HttpClient client, string url, MultipartFormDataContent form)
    {
        using (form) return await client.PostAsync(url, form);
    }
    private sealed class MemorySources : IFileStorageProvider
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new();
        public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _files[path] = buffer.ToArray();
        }
        public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(_files.TryGetValue(path, out var value) ? new MemoryStream(value) : null);
        public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(_files.TryRemove(path, out _));
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(_files.ContainsKey(path));
    }
}
