using System.IO.Compression;
using System.Security.Cryptography;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class PackageBackgroundService
{
    private async Task ProcessRetainedJobAsync(PackageExportJob job, IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AtoCopilotContext>();
        var settings = services.GetRequiredService<IOptions<ExportSettings>>().Value;
        Directory.CreateDirectory(settings.PackagesPath);
        var output = Path.Combine(settings.PackagesPath, $"authorization-package-{job.PackageId}.zip");
        try
        {
            var package = await db.AuthorizationPackages.SingleAsync(p => p.Id == job.PackageId && p.RegisteredSystemId == job.SystemId, ct);
            var run = await services.GetRequiredService<PackageReadinessService>().RequirePackageSourceAsync(package, ct);
            if (package.Purpose != job.Purpose || package.RetainedContextJson == null ||
                ApprovedProfileDocumentData.Hash(package.RetainedContextJson) != package.RetainedContextHash)
                throw new InvalidOperationException("Retained package context integrity check failed.");
            var manifest = RetainedPackageContext.Read(package.RetainedContextJson);
            if (manifest.TenantId != package.TenantId || manifest.SystemId != job.SystemId || manifest.Purpose != job.Purpose)
                throw new InvalidOperationException("Retained package context does not match the requested system and purpose.");
            await RetainedPackageContext.VerifyEvidenceAsync(services, manifest, manifest.RequestedPersonId, ct);
            var baseline = await RetainedPackageContext.BaselineAsync(db, job.SystemId, manifest.TenantId,
                manifest.BaselinePackageId, manifest.BaselineContentHash, ct);
            using (var archive = ZipFile.Open(output, ZipArchiveMode.Create))
            {
                await WriteZipEntryAsync(archive, "package-metadata.json", System.Text.Json.JsonSerializer.Serialize(new
                {
                    packageId = package.Id, systemId = job.SystemId, purpose = job.Purpose.ToString(),
                    receivingWorkflowOutcome = "NotRecorded", readiness = ReadinessMetadata(run)
                }));
                var size = await CopyPinnedEntryAsync(archive, "retained-baseline.zip", baseline.FilePath!, manifest.BaselineContentHash, ct);
                AddRetainedArtifact(db, package, PackageArtifactType.RetainedBaseline, "retained-baseline.zip", "zip", size, manifest.BaselineContentHash);
                if (job.Purpose == PackagePurpose.ChangeSubmission)
                {
                    var preview = await db.SspExports.AsNoTracking().SingleOrDefaultAsync(p =>
                        p.Id == manifest.ChangePreviewId && p.SystemId == job.SystemId && p.Status == "Preview", ct)
                        ?? throw new InvalidOperationException("The pinned SSP change preview is unavailable.");
                    if (preview.ExpiresAt <= DateTimeOffset.UtcNow || preview.FilePath == null)
                        throw new InvalidOperationException("The pinned SSP change bytes are expired or unavailable.");
                    var path = Path.Combine(settings.ExportsPath, preview.FilePath);
                    await RetainedPackageContext.RequireFileHashAsync(path, manifest.ChangeContentHash!, ct);
                    var validation = await services.GetRequiredService<IOscalSchemaValidationService>()
                        .ValidateAsync(await File.ReadAllTextAsync(path, ct), "ssp", ct);
                    if (!validation.IsValid) throw new InvalidOperationException("The pinned SSP change fails OSCAL schema validation.");
                    size = await CopyPinnedEntryAsync(archive, "changes/oscal-ssp.json", path, manifest.ChangeContentHash!, ct);
                    AddRetainedArtifact(db, package, PackageArtifactType.ReviewedSspChange, "changes/oscal-ssp.json", "json", size, manifest.ChangeContentHash!);
                }
                await WriteZipEntryAsync(archive, "package-context.json", package.RetainedContextJson);
                AddRetainedArtifact(db, package, PackageArtifactType.PackageContext, "package-context.json", "json",
                    System.Text.Encoding.UTF8.GetByteCount(package.RetainedContextJson), package.RetainedContextHash!);
            }
            await db.SaveChangesAsync(ct);
            await using var content = File.OpenRead(output);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
            await CompletePackageAsync(job.PackageId, output, content.Length, hash, ct);
            await _notifier.SendStatusChangedAsync(job.PackageId, "Completed", ct);
            await _notifier.SendPackageCompleteAsync(job.PackageId,
                $"/api/v1/systems/{job.SystemId}/packages/{job.PackageId}/download", ct);
        }
        catch (Exception ex)
        {
            if (File.Exists(output)) File.Delete(output);
            await MarkFailedAsync(job.PackageId, "retained-context", ex.Message, "Restore the exact retained source or select a new reviewed context.");
            await _notifier.SendPackageFailedAsync(job.PackageId, "retained-context", ex.Message,
                "Restore the exact retained source or select a new reviewed context.", ct);
        }
    }

    private static async Task<long> CopyPinnedEntryAsync(ZipArchive archive, string name, string sourcePath, string expectedHash, CancellationToken ct)
    {
        await using var input = File.OpenRead(sourcePath);
        await using var output = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;
        }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Retained source changed; copied bytes do not match the pinned content hash.");
        return copied;
    }

    private static void AddRetainedArtifact(AtoCopilotContext db, AuthorizationPackage package, PackageArtifactType type,
        string name, string format, long size, string hash) =>
        db.PackageArtifacts.Add(new PackageArtifact
        {
            TenantId = package.TenantId, AuthorizationPackageId = package.Id, ArtifactType = type,
            FileName = name, Format = format, FileSize = size, ContentHash = hash,
            OscalVersion = type == PackageArtifactType.ReviewedSspChange ? "1.1.2" : null,
            SchemaValid = type == PackageArtifactType.ReviewedSspChange ? true : null
        });
}
