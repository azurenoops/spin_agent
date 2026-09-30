using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Agents.Compliance.Services;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class DocumentPreviewHttpTests
{
    [Fact]
    public async Task ContextOptions_OnlyReturnsSelectedSystemRetainedSources_AndPreservesHashes()
    {
        // Arrange
        var own = await SeedAsync();
        var foreign = await SeedAsync(WorkspaceMembershipFactory.TenantBId);
        var unassigned = await SeedAsync(assignRole: false);
        var directory = Path.Combine("TestResults", $"context-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "baseline.zip");
        var bytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(path, bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var baseline = new AuthorizationPackage
        {
            TenantId = Tenant, RegisteredSystemId = own.System, Status = PackageStatus.Completed,
            Purpose = PackagePurpose.InitialSubmission, ContentHash = hash, FilePath = path,
            ValidationPassed = true, CompletedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            GeneratedBy = "synthetic"
        };
        var decision = new AuthorizationDecision
        {
            TenantId = Tenant, RegisteredSystemId = own.System, DecisionType = AuthorizationDecisionType.Ato,
            DecisionDate = new DateTime(2026, 9, 1), IssuedBy = "synthetic-ao", IssuedByName = "Recorded AO"
        };
        var settings = factory.Services.GetRequiredService<IOptions<ExportSettings>>().Value;
        var previewDirectory = Path.Combine(settings.ExportsPath, $"options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(previewDirectory);
        var previewPath = Path.Combine(previewDirectory, "preview.json");
        await File.WriteAllTextAsync(previewPath, "{}");
        var previewHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(previewPath)));
        var preview = new SspExport
        {
            SystemId = own.System, SourceTenantId = Tenant, Status = "Preview", Format = "json",
            FilePath = Path.GetRelativePath(settings.ExportsPath, previewPath), ContentHash = previewHash,
            SourceManifestJson = "{}", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.AddRange(baseline, decision, new AuthorizationPackage
            {
                TenantId = WorkspaceMembershipFactory.TenantBId, RegisteredSystemId = foreign.System,
                Status = PackageStatus.Completed, ContentHash = hash, FilePath = path, ValidationPassed = true,
                CompletedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), GeneratedBy = "foreign"
            });
            db.AddRange(preview, new SspExport
            {
                SystemId = own.System, SourceTenantId = WorkspaceMembershipFactory.TenantBId, Status = "Preview", Format = "json",
                FilePath = preview.FilePath, ContentHash = previewHash, SourceManifestJson = "{}", ExpiresAt = preview.ExpiresAt
            }, new AuthorizationDecision
            {
                TenantId = WorkspaceMembershipFactory.TenantBId, RegisteredSystemId = foreign.System,
                DecisionType = AuthorizationDecisionType.Ato, DecisionDate = decision.DecisionDate, IssuedBy = "foreign"
            });
            await db.SaveChangesAsync();
        }
        using var client = Client(own.Actor);
        using var unrelated = Client(unassigned.Actor);
        try
        {
            // Act
            using var response = await client.GetAsync($"/api/v1/systems/{own.System}/packages/context-options");
            using var denied = await client.GetAsync($"/api/v1/systems/{foreign.System}/packages/context-options");
            using var deniedPerson = await unrelated.GetAsync($"/api/v1/systems/{own.System}/packages/context-options");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            result.GetProperty("baselines").GetArrayLength().Should().Be(1);
            result.GetProperty("baselines")[0].GetProperty("id").GetString().Should().Be(baseline.Id);
            result.GetProperty("baselines")[0].GetProperty("contentHash").GetString().Should().Be(hash);
            result.GetProperty("decisions")[0].GetProperty("id").GetString().Should().Be(decision.Id);
            result.GetProperty("decisions")[0].GetProperty("snapshotHash").GetString().Should().HaveLength(64);
            result.GetProperty("decisions").GetArrayLength().Should().Be(1);
            result.GetProperty("decisions")[0].GetProperty("snapshotHash").GetString().Should().Be(
                Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                    AuthorizationPackageContextOptions.DecisionSnapshot(decision).GetRawText()))));
            result.GetProperty("previews").GetArrayLength().Should().Be(1);
            result.GetProperty("previews")[0].GetProperty("id").GetGuid().Should().Be(preview.Id);
            result.GetProperty("previews")[0].GetProperty("contentHash").GetString().Should().Be(previewHash);
            result.TryGetProperty("selectedBaselineId", out _).Should().BeFalse();
            denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
            deniedPerson.StatusCode.Should().Be(HttpStatusCode.NotFound);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, true);
            Directory.Delete(previewDirectory, true);
        }
    }

    [Fact]
    public async Task ContextOptions_DeniesUnauthenticatedRequests()
    {
        // Arrange
        using var client = factory.CreateClient();
        // Act
        using var response = await client.GetAsync("/api/v1/systems/unknown/packages/context-options");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
