using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class DocumentPreviewHttpTests
{
    [Fact]
    public async Task ArchivePurpose_ProductionRoutes_CopyRetainedPackageAndRecordedDecisionOnly()
    {
        // Arrange: historical completed package/decision are prerequisites, not synthesized by archive generation.
        var fixture = await SeedAsync();
        using var client = Client(fixture.Actor);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var settings = factory.Services.GetRequiredService<IOptions<ExportSettings>>().Value;
        Directory.CreateDirectory(settings.PackagesPath);
        var sourcePath = Path.Combine(settings.PackagesPath, $"baseline-prerequisite-{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(sourcePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("oscal-ssp.json").Open());
            await writer.WriteAsync("{\"retained\":\"DEMO historical original artifact\"}");
        }
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        var baseline = new AuthorizationPackage
        {
            TenantId = Tenant, RegisteredSystemId = fixture.System, Status = PackageStatus.Completed,
            ContentHash = hash, FilePath = sourcePath, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        var decision = new AuthorizationDecision
        {
            TenantId = Tenant, RegisteredSystemId = fixture.System, DecisionType = AuthorizationDecisionType.Ato,
            IssuedBy = "DEMO recorded AO", IssuedByName = "DEMO recorded authority", DecisionDate = new DateTime(2025, 4, 17),
            TermsAndConditions = "DEMO recorded historical conditions",
            BaselinePackageId = baseline.Id, BaselinePackageHash = hash
        };
        var otherBaseline = new AuthorizationPackage
        {
            TenantId = Tenant, RegisteredSystemId = fixture.System, Status = PackageStatus.Completed,
            ContentHash = hash, FilePath = sourcePath, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AddRange(baseline, decision, otherBaseline);
            await db.SaveChangesAsync();
        }
        var selection = new RetainedPackageSelection(baseline.Id, hash, decision.Id);
        var root = $"/api/v1/systems/{fixture.System}/packages";
        string? generatedPath = null;
        try
        {
            // Act
            using var wrongPair = await client.PostAsJsonAsync(root + "/validate?purpose=AuthorizedBaselineArchive",
                selection with { BaselinePackageId = otherBaseline.Id });
            wrongPair.StatusCode.Should().Be(HttpStatusCode.OK);
            (await wrongPair.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isValid").GetBoolean().Should().BeFalse();
            using var validation = await client.PostAsJsonAsync(root + "/validate?purpose=AuthorizedBaselineArchive", selection);
            validation.StatusCode.Should().Be(HttpStatusCode.OK, await validation.Content.ReadAsStringAsync());
            var validated = await validation.Content.ReadFromJsonAsync<JsonElement>();
            validated.GetProperty("isValid").GetBoolean().Should().BeTrue(validated.ToString());
            validated.GetProperty("retainedContext").GetProperty("decisionBaselineLinkage").GetString().Should().Be("RecordedExactMatch");
            selection = selection with
            {
                ExpectedDecisionSnapshotHash = validated.GetProperty("retainedContext").GetProperty("decisionSnapshotHash").GetString(),
                ExpectedSourceContextHash = validated.GetProperty("sourceContextHash").GetString()
            };
            using var queued = await client.PostAsJsonAsync(root, new GeneratePackageRequest
            {
                Purpose = PackagePurpose.AuthorizedBaselineArchive, RetainedContext = selection
            });
            queued.StatusCode.Should().Be(HttpStatusCode.Accepted, await queued.Content.ReadAsStringAsync());
            var packageId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString()!;
            using var replay = await client.PostAsJsonAsync(root, new GeneratePackageRequest
            {
                Purpose = PackagePurpose.AuthorizedBaselineArchive, RetainedContext = selection
            });
            replay.StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packageId").GetString().Should().Be(packageId);
            JsonElement detail = default;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                detail = await client.GetFromJsonAsync<JsonElement>($"{root}/{packageId}");
                if (detail.GetProperty("status").GetString() is "Completed" or "Failed") break;
                await Task.Delay(30);
            }
            using var download = await client.GetAsync($"{root}/{packageId}/download");

            // Assert
            detail.GetProperty("status").GetString().Should().Be("Completed", detail.ToString());
            detail.GetProperty("sourceContextHash").GetString().Should().Be(selection.ExpectedSourceContextHash);
            detail.GetProperty("retainedContext").GetProperty("decisionSnapshotHash").GetString()
                .Should().Be(selection.ExpectedDecisionSnapshotHash);
            download.StatusCode.Should().Be(HttpStatusCode.OK, await download.Content.ReadAsStringAsync());
            using var archive = new ZipArchive(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));
            await using var retained = archive.GetEntry("retained-baseline.zip")!.Open();
            using var bytes = new MemoryStream();
            await retained.CopyToAsync(bytes);
            bytes.ToArray().Should().Equal(sourceBytes);
            using var manifestReader = new StreamReader(archive.GetEntry("package-context.json")!.Open());
            var manifest = await manifestReader.ReadToEndAsync();
            manifest.Should().Contain("DEMO recorded historical conditions").And.Contain(hash);
            await using var check = factory.Services.CreateAsyncScope();
            var db = check.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            generatedPath = (await db.AuthorizationPackages.SingleAsync(p => p.Id == packageId)).FilePath;
            (await db.AuthorizationDecisions.CountAsync(d => d.RegisteredSystemId == fixture.System)).Should().Be(1);
            (await File.ReadAllBytesAsync(sourcePath)).Should().Equal(sourceBytes);
        }
        finally
        {
            File.Delete(sourcePath);
            if (generatedPath != null) File.Delete(generatedPath);
        }
    }
}
