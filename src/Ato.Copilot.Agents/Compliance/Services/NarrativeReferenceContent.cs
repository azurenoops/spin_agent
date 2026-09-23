using System.Security.Cryptography;
using System.Text.Json;

namespace Ato.Copilot.Agents.Compliance.Services;

internal sealed record NarrativeImportedContent(string SourceName, string SourceSha256, string PassagesJson);

internal static class NarrativeReferenceContent
{
    public static async Task<NarrativeImportedContent> ReadAsync(string fileName, Stream content, CancellationToken ct)
    {
        fileName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (fileName.Length is 0 or > 200) throw new ArgumentException("Filename must contain 1-200 characters.");
        using var bytes = new MemoryStream();
        var block = new byte[81920];
        int count;
        while ((count = await content.ReadAsync(block, ct)) > 0)
        {
            if (bytes.Length + count > NarrativeLibraryParser.MaxBytes)
                throw new InvalidDataException("Reference uploads must not exceed 5 MB.");
            await bytes.WriteAsync(block.AsMemory(0, count), ct);
        }
        bytes.Position = 0;
        var passages = await NarrativeLibraryParser.ExtractAsync(bytes, fileName, ct);
        return new(fileName, Convert.ToHexString(SHA256.HashData(bytes.ToArray())), JsonSerializer.Serialize(passages));
    }
}
