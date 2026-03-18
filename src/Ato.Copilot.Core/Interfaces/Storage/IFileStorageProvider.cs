namespace Ato.Copilot.Core.Interfaces.Storage;

/// <summary>
/// Abstracted file storage provider for evidence artifacts.
/// Implementations: <c>LocalFileStorageProvider</c> (default), <c>AzureBlobStorageProvider</c> (optional).
/// </summary>
public interface IFileStorageProvider
{
    /// <summary>
    /// Save a file to storage.
    /// </summary>
    /// <param name="path">Storage path/key (e.g., <c>evidence/{systemId}/{artifactId}/{filename}</c>).</param>
    /// <param name="content">File content stream.</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a file from storage.
    /// </summary>
    /// <param name="path">Storage path/key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File content stream, or <c>null</c> if the file does not exist.</returns>
    Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a file from storage.
    /// </summary>
    /// <param name="path">Storage path/key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the file was deleted; <c>false</c> if it did not exist.</returns>
    Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether a file exists in storage.
    /// </summary>
    /// <param name="path">Storage path/key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the file exists.</returns>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
}
