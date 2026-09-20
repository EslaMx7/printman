using System.Collections.Concurrent;
using System.Security.Cryptography;
using OhMyPrinter.Core.Abstractions;
using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Services;

public class FileCacheService : IFileCacheService
{
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, FileCacheResult> _cacheIndex = new(StringComparer.OrdinalIgnoreCase);

    public string CacheDirectory => _cacheDirectory;

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
                    await tempFileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    sha.AppendData(buffer, 0, bytesRead);
                    totalBytesWritten += bytesRead;
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
}
