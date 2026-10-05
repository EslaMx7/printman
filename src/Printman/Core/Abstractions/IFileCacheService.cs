using Printman.Core.Models;

namespace Printman.Core.Abstractions;

public interface IFileCacheService
{
    /// <summary>
    /// Stores an uploaded file stream into the local cache directory using SHA-256 hash deduplication.
    /// If the file already exists in cache, the existing path is returned without duplicate write.
    /// </summary>
    Task<FileCacheResult> StoreFileAsync(string originalFileName, Stream contentStream, CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="StoreFileAsync(string, Stream, CancellationToken)"/> with an explicit size limit
    /// (used for network print jobs, which have their own quota).
    /// </summary>
    Task<FileCacheResult> StoreFileAsync(string originalFileName, Stream contentStream, long maxFileSizeBytes, CancellationToken ct = default);

    /// <summary>
    /// Gets a cached file result by fileId/hash, or null if not found.
    /// </summary>
    FileCacheResult? GetFile(string fileId);

    /// <summary>
    /// Path to the local cache directory.
    /// </summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Maximum allowed single file size in bytes (default 50 MB).
    /// </summary>
    long MaxFileSizeBytes { get; set; }

    /// <summary>
    /// Maximum allowed total cache size in bytes (default 500 MB).
    /// </summary>
    long MaxCacheSizeBytes { get; set; }

    /// <summary>
    /// Cleans up old cached files if the total cache exceeds the maximum allowed size.
    /// </summary>
    Task CleanupOldFilesAsync(CancellationToken ct = default);
}
