using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Core.Abstractions;

public interface IFileCacheService
{
    /// <summary>
    /// Stores an uploaded file stream into the local cache directory using SHA-256 hash deduplication.
    /// If the file already exists in cache, the existing path is returned without duplicate write.
    /// </summary>
    Task<FileCacheResult> StoreFileAsync(string originalFileName, Stream contentStream, CancellationToken ct = default);

    /// <summary>
    /// Gets a cached file result by fileId/hash, or null if not found.
    /// </summary>
    FileCacheResult? GetFile(string fileId);

    /// <summary>
    /// Path to the local cache directory.
    /// </summary>
    string CacheDirectory { get; }
}
