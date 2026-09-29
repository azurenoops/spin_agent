using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class DocumentPreviewHttpTests
{
    [NonRootUnixFact]
    public async Task RetainPreview_RealStorageDenialIsNotRoleDenial_AndSameKeySucceedsAfterStorageRepair()
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();

        // Arrange
        var fixture = await SeedAsync();
        var outsider = await SeedAsync(assignRole: false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.SystemRoleAssignments.SingleAsync(r => r.RegisteredSystemId == fixture.System &&
                r.PersonId == fixture.Person)).Role = OrganizationRole.Issm;
            await db.SaveChangesAsync();
        }
        var settings = factory.Services.GetRequiredService<IOptions<ExportSettings>>().Value;
        var originalPath = settings.DataPath;
        var storage = Path.Combine(Directory.GetCurrentDirectory(), "preview-storage-denial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storage);
        settings.DataPath = storage;
        File.SetUnixFileMode(storage, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        using var client = Client(fixture.Actor);
        async Task<HttpResponseMessage> Retain()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, PreviewPath(fixture.System));
            request.Headers.Add("Idempotency-Key", "storage-permission-retry");
            return await client.SendAsync(request);
        }
        try
        {
            var actualDirectoryWrite = () => Directory.CreateDirectory(Path.Combine(storage, "probe"));
            actualDirectoryWrite.Should().Throw<UnauthorizedAccessException>("the test must reproduce a real filesystem permission denial");

            // Act
            using var get = await client.GetAsync(PreviewPath(fixture.System));
            using var failed = await Retain();

            // Assert
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await failed.Content.ReadAsStringAsync());
            var error = await failed.Content.ReadFromJsonAsync<JsonElement>();
            error.GetProperty("errorCode").GetString().Should().Be("DOCUMENT_STORAGE_UNAVAILABLE");
            using (var inspect = factory.Services.CreateScope())
                (await inspect.ServiceProvider.GetRequiredService<AtoCopilotContext>().SspExports
                    .CountAsync(s => s.SystemId == fixture.System)).Should().Be(0);
            using var outsiderClient = Client(outsider.Actor);
            using var forbidden = await outsiderClient.PostAsync(PreviewPath(fixture.System), null);
            forbidden.StatusCode.Should().Be(HttpStatusCode.NotFound);

            File.SetUnixFileMode(storage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var retained = await Retain();
            using var replay = await Retain();
            retained.StatusCode.Should().Be(HttpStatusCode.OK, await retained.Content.ReadAsStringAsync());
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            var first = await retained.Content.ReadFromJsonAsync<JsonElement>();
            var second = await replay.Content.ReadFromJsonAsync<JsonElement>();
            first.GetProperty("previewId").GetGuid().Should().Be(second.GetProperty("previewId").GetGuid());
            first.GetProperty("contentHash").GetString().Should().Be(second.GetProperty("contentHash").GetString());
            using var verify = factory.Services.CreateScope();
            var retainedRows = await verify.ServiceProvider.GetRequiredService<AtoCopilotContext>().SspExports
                .Where(s => s.SystemId == fixture.System).ToListAsync();
            retainedRows.Should().ContainSingle();
            var retainedFile = Path.Combine(storage, "exports", retainedRows.Single().FilePath!);
            File.SetUnixFileMode(retainedFile, UnixFileMode.UserWrite);
            using var unreadableReplay = await Retain();
            unreadableReplay.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            File.SetUnixFileMode(retainedFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using var restoredReplay = await Retain();
            restoredReplay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await restoredReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("previewId").GetGuid()
                .Should().Be(first.GetProperty("previewId").GetGuid());
        }
        finally
        {
            settings.DataPath = originalPath;
            File.SetUnixFileMode(storage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(storage, recursive: true);
        }
    }

    public sealed class NonRootUnixFactAttribute : FactAttribute
    {
        public NonRootUnixFactAttribute()
        {
            if (OperatingSystem.IsWindows() || Environment.UserName == "root")
                Skip = "Real Unix mode-bit denial requires a non-root Unix test process.";
        }
    }
}
