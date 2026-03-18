using Ato.Copilot.Core.Interfaces.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Services.Storage;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IFileStorageProvider"/>.
/// Uses the same <c>evidence/{systemId}/{artifactId}/{filename}</c> path convention as local storage.
/// </summary>
public class AzureBlobStorageProvider : IFileStorageProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<AzureBlobStorageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AzureBlobStorageProvider"/>.
    /// </summary>
    /// <param name="connectionString">Azure Blob Storage connection string.</param>
    /// <param name="containerName">Blob container name for evidence files.</param>
    /// <param name="logger">Logger instance.</param>
    public AzureBlobStorageProvider(string connectionString, string containerName, ILogger<AzureBlobStorageProvider> logger)
        : this(new BlobContainerClient(connectionString, containerName), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance with an externally-provided <see cref="BlobContainerClient"/> (for testing).
    /// </summary>
    public AzureBlobStorageProvider(BlobContainerClient containerClient, ILogger<AzureBlobStorageProvider> logger)
    {
        _containerClient = containerClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var blobClient = _containerClient.GetBlobClient(path);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blobClient.UploadAsync(content, options, cancellationToken);
        _logger.LogDebug("Saved evidence blob at {Path} ({ContentType})", path, contentType);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(path);
        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            _logger.LogWarning("Evidence blob not found at {Path}", path);
            return null;
        }

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(path);
        var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
        if (response.Value)
        {
            _logger.LogDebug("Deleted evidence blob at {Path}", path);
        }
        return response.Value;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(path);
        var response = await blobClient.ExistsAsync(cancellationToken);
        return response.Value;
    }
}
