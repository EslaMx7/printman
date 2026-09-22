using System.Collections.Concurrent;
using System.Security.Cryptography;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services;

public class FileCacheService : IFileCacheService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif", ".txt", ".log", ".csv", ".json", ".md"
    };

    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, FileCacheResult> _cacheIndex = new(StringComparer.OrdinalIgnoreCase);

    public string CacheDirectory => _cacheDirectory;
    public long MaxFileSizeBytes { get; set; } = 50L * 1024 * 1024; // 50 MB default
    public long MaxCacheSizeBytes { get; set; } = 500L * 1024 * 1024; // 500 MB default

    public FileCacheService()
    {
        _cacheDirectory = Path.Combine(AppContext.BaseDirectory, "cache");
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }

        // Index existing files on startup
        IndexExistingFiles();
    }

    public async Task<FileCacheResult> StoreFileAsync(string originalFileName, Stream contentStream, CancellationToken ct = default)
    {
        var sanitizedExt = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sanitizedExt) || !AllowedExtensions.Contains(sanitizedExt))
        {
            throw new ArgumentException($"File extension '{sanitizedExt}' is not supported for printing.");
        }

        // Proactively evict old files if cache exceeds quota
        await CleanupOldFilesAsync(ct);

        var tempFilePath = Path.Combine(_cacheDirectory, $"tmp_{Guid.NewGuid():N}.tmp");

        string fileHash;
        long totalBytesWritten = 0;

        try
        {
            using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    totalBytesWritten += bytesRead;
                    if (totalBytesWritten > MaxFileSizeBytes)
                    {
                        throw new InvalidOperationException($"Uploaded file exceeds the maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)} MB.");
                    }

                    await tempFileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    sha.AppendData(buffer, 0, bytesRead);
                }

                fileHash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant()[..16]; // 16-char fast compact hash
            }

            var cachedFileName = $"{fileHash}{sanitizedExt}";
            var cachedFilePath = Path.Combine(_cacheDirectory, cachedFileName);

            bool isDuplicate = false;

            if (File.Exists(cachedFilePath))
            {
                // File already cached with this hash! Deduplicate by deleting the temp file
                isDuplicate = true;
                try { File.Delete(tempFilePath); } catch { }
            }
            else
            {
                // Move temp file to final cached file path
                File.Move(tempFilePath, cachedFilePath, overwrite: true);
            }

            var result = new FileCacheResult(
                FileId: fileHash,
                OriginalFileName: originalFileName,
                CachedFilePath: cachedFilePath,
                FileSizeBytes: totalBytesWritten,
                IsDuplicate: isDuplicate,
                Extension: sanitizedExt);

            _cacheIndex[fileHash] = result;
            return result;
        }
        catch
        {
            // Clean up temporary file on failure
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
            throw;
        }
    }

    public FileCacheResult? GetFile(string fileId)
    {
        if (_cacheIndex.TryGetValue(fileId, out var cached) && File.Exists(cached.CachedFilePath))
        {
            return cached;
        }

        // Check filesystem fallback
        var matches = Directory.GetFiles(_cacheDirectory, $"{fileId}.*");
        if (matches.Length > 0)
        {
            var path = matches[0];
            var fi = new FileInfo(path);
            var result = new FileCacheResult(
                FileId: fileId,
                OriginalFileName: Path.GetFileName(path),
                CachedFilePath: path,
                FileSizeBytes: fi.Length,
                IsDuplicate: false,
                Extension: Path.GetExtension(path).ToLowerInvariant());

            _cacheIndex[fileId] = result;
            return result;
        }

        return null;
    }

    private void IndexExistingFiles()
    {
        try
        {
            foreach (var filePath in Directory.GetFiles(_cacheDirectory))
            {
                var fileName = Path.GetFileName(filePath);
                if (fileName.StartsWith("tmp_") && fileName.EndsWith(".tmp"))
                {
                    // Clean up abandoned temp files from previous runs
                    try { File.Delete(filePath); } catch { }
                    continue;
                }

                var fileId = Path.GetFileNameWithoutExtension(filePath);
                var fi = new FileInfo(filePath);
                var result = new FileCacheResult(
                    FileId: fileId,
                    OriginalFileName: fileName,
                    CachedFilePath: filePath,
                    FileSizeBytes: fi.Length,
                    IsDuplicate: false,
                    Extension: Path.GetExtension(filePath).ToLowerInvariant());

                _cacheIndex[fileId] = result;
            }
        }
        catch
        {
            // Ignore directory scanning errors on startup
        }
    }

    public Task CleanupOldFilesAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                var dirInfo = new DirectoryInfo(_cacheDirectory);
                if (!dirInfo.Exists) return;

                var files = dirInfo.GetFiles()
                    .Where(f => !f.Name.StartsWith("tmp_"))
                    .OrderBy(f => f.LastAccessTimeUtc)
                    .ToList();

                long totalSize = files.Sum(f => f.Length);
                if (totalSize <= MaxCacheSizeBytes) return;

                long targetSize = (long)(MaxCacheSizeBytes * 0.8);
                foreach (var file in files)
                {
                    if (totalSize <= targetSize || ct.IsCancellationRequested) break;
                    try
                    {
                        var len = file.Length;
                        var id = Path.GetFileNameWithoutExtension(file.Name);
                        file.Delete();
                        totalSize -= len;
                        _cacheIndex.TryRemove(id, out _);
                    }
                    catch
                    {
                        // Ignore files currently locked by active printing
                    }
                }
            }
            catch
            {
                // Best effort cache quota cleanup
            }
        }, ct);
    }
}
