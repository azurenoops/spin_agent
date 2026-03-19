using Ato.Copilot.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Services.Storage;

/// <summary>
/// Local filesystem implementation of <see cref="IFileStorageProvider"/>.
/// Stores files under a Docker-mountable volume path using structured
/// <c>evidence/{systemId}/{artifactId}/{filename}</c> convention.
/// </summary>
public class LocalFileStorageProvider : IFileStorageProvider
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="LocalFileStorageProvider"/>.
    /// </summary>
    /// <param name="basePath">Root directory for evidence file storage.</param>
    /// <param name="logger">Logger instance.</param>
    public LocalFileStorageProvider(string basePath, ILogger<LocalFileStorageProvider> logger)
    {
        _basePath = basePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogDebug("Saved evidence file to {Path} ({ContentType})", path, contentType);
    }

    /// <inheritdoc />
    public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Evidence file not found at {Path}", path);
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);
        _logger.LogDebug("Deleted evidence file at {Path}", path);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    private string GetFullPath(string path)
    {
        // Prevent path traversal attacks by ensuring the resolved path stays under _basePath
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));
        if (!fullPath.StartsWith(Path.GetFullPath(_basePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage path: attempted path traversal.");
        }
        return fullPath;
    }
}
