using System.Text;
using Printman.Core.Models;
using Printman.Services;
using Printman.Tests.Support;

namespace Printman.Tests.Services;

[TestClass]
public sealed class FileCacheServiceTests
{
    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<FileCacheResult> StoreAsync(FileCacheService service, string fileName, string text)
    {
        using var stream = Content(text);
        return await service.StoreFileAsync(fileName, stream);
    }

    [TestMethod]
    public void Constructor_CreatesMissingDirectory_AndExposesDefaults()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("nested", "cache");

        var service = new FileCacheService(path);

        Assert.AreEqual(path, service.CacheDirectory);
        Assert.IsTrue(Directory.Exists(path));
        Assert.AreEqual(50L * 1024 * 1024, service.MaxFileSizeBytes);
        Assert.AreEqual(500L * 1024 * 1024, service.MaxCacheSizeBytes);
    }

    [TestMethod]
    public void Constructor_UsesExistingDirectory_AndAllowsOverridingQuotas()
    {
        using var temp = new TempDirectory();

        var service = new FileCacheService(temp.Path);
        service.MaxFileSizeBytes = 123;
        service.MaxCacheSizeBytes = 456;

        Assert.AreEqual(temp.Path, service.CacheDirectory);
        Assert.AreEqual(123L, service.MaxFileSizeBytes);
        Assert.AreEqual(456L, service.MaxCacheSizeBytes);
    }

    [TestMethod]
    public void Constructor_RebuildsIndex_AndRemovesAbandonedTempFiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("abcdef0123456789.pdf"), "hello");
        File.WriteAllText(temp.Combine("tmp_abandoned.tmp"), "junk");

        var service = new FileCacheService(temp.Path);

        Assert.IsFalse(File.Exists(temp.Combine("tmp_abandoned.tmp")));

        var result = service.GetFile("abcdef0123456789");
        Assert.IsNotNull(result);
        Assert.AreEqual("abcdef0123456789.pdf", result!.OriginalFileName);
        Assert.AreEqual(".pdf", result.Extension);
        Assert.IsFalse(result.IsDuplicate);
        Assert.AreEqual(5L, result.FileSizeBytes);
    }

    [TestMethod]
    [DataRow("doc.pdf", ".pdf")]
    [DataRow("image.png", ".png")]
    [DataRow("image.jpg", ".jpg")]
    [DataRow("image.jpeg", ".jpeg")]
    [DataRow("image.bmp", ".bmp")]
    [DataRow("image.tiff", ".tiff")]
    [DataRow("image.tif", ".tif")]
    [DataRow("notes.txt", ".txt")]
    [DataRow("server.log", ".log")]
    [DataRow("data.csv", ".csv")]
    [DataRow("data.json", ".json")]
    [DataRow("readme.md", ".md")]
    [DataRow("job.pwg", ".pwg")]
    [DataRow("job.urf", ".urf")]
    [DataRow("PHOTO.PDF", ".pdf")]
    public async Task StoreFileAsync_SupportedExtension_StoresFileAndLowercasesExtension(string fileName, string expectedExtension)
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);

        var result = await StoreAsync(service, fileName, "data");

        Assert.AreEqual(fileName, result.OriginalFileName);
        Assert.AreEqual(expectedExtension, result.Extension);
        Assert.AreEqual(4L, result.FileSizeBytes);
        Assert.IsFalse(result.IsDuplicate);
        Assert.AreEqual(16, result.FileId.Length);
        Assert.IsTrue(File.Exists(result.CachedFilePath));
        Assert.IsTrue(result.CachedFilePath.EndsWith(expectedExtension, StringComparison.Ordinal));
        Assert.AreEqual(1, Directory.GetFiles(temp.Path).Length);
    }

    [TestMethod]
    [DataRow("malware.exe")]
    [DataRow("archive.zip")]
    [DataRow("noextension")]
    [DataRow("trailing.")]
    [DataRow("")]
    public async Task StoreFileAsync_UnsupportedExtension_ThrowsArgumentException(string fileName)
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await service.StoreFileAsync(fileName, Content("data")));

        Assert.AreEqual(0, Directory.GetFiles(temp.Path).Length);
    }

    [TestMethod]
    public async Task StoreFileAsync_SameContentSameExtension_IsDeduplicated()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);

        var first = await StoreAsync(service, "one.txt", "identical content");
        var second = await StoreAsync(service, "two.txt", "identical content");

        Assert.IsFalse(first.IsDuplicate);
        Assert.IsTrue(second.IsDuplicate);
        Assert.AreEqual(first.FileId, second.FileId);
        Assert.AreEqual(first.CachedFilePath, second.CachedFilePath);
        Assert.AreEqual("two.txt", second.OriginalFileName);
        Assert.AreEqual(first.FileSizeBytes, second.FileSizeBytes);
        Assert.AreEqual(1, Directory.GetFiles(temp.Path).Length);
    }

    [TestMethod]
    public async Task StoreFileAsync_SameContentDifferentExtension_IsNotDeduplicated()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);

        var first = await StoreAsync(service, "one.txt", "identical content");
        var second = await StoreAsync(service, "one.pdf", "identical content");

        Assert.IsFalse(first.IsDuplicate);
        Assert.IsFalse(second.IsDuplicate);
        Assert.AreEqual(first.FileId, second.FileId);
        Assert.AreNotEqual(first.CachedFilePath, second.CachedFilePath);
        Assert.AreEqual(2, Directory.GetFiles(temp.Path).Length);
    }

    [TestMethod]
    public async Task StoreFileAsync_ExceedsPerCallLimit_ThrowsAndCleansUpTempFile()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.StoreFileAsync("big.txt", Content("12345"), 4));

        Assert.AreEqual(0, Directory.GetFiles(temp.Path).Length);
    }

    [TestMethod]
    public async Task StoreFileAsync_ExceedsConfiguredDefaultLimit_Throws()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path) { MaxFileSizeBytes = 4 };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.StoreFileAsync("big.txt", Content("12345")));

        Assert.AreEqual(0, Directory.GetFiles(temp.Path).Length);
    }

    [TestMethod]
    public async Task StoreFileAsync_ExactLimit_IsAccepted()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);

        var result = await service.StoreFileAsync("exact.txt", Content("1234"), 4);

        Assert.AreEqual(4L, result.FileSizeBytes);
        Assert.IsTrue(File.Exists(result.CachedFilePath));
    }

    [TestMethod]
    public async Task StoreFileAsync_CancelledToken_ThrowsTaskCanceledException()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await service.StoreFileAsync("a.txt", Content("data"), cts.Token));
    }

    [TestMethod]
    public async Task GetFile_IndexedFile_ReturnsCachedResult()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);
        var stored = await StoreAsync(service, "doc.txt", "hello");

        var found = service.GetFile(stored.FileId);

        Assert.IsNotNull(found);
        Assert.AreEqual(stored.FileId, found!.FileId);
        Assert.AreEqual(stored.CachedFilePath, found.CachedFilePath);
        Assert.AreEqual(stored.OriginalFileName, found.OriginalFileName);
    }

    [TestMethod]
    public void GetFile_UnknownId_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);

        Assert.IsNull(service.GetFile("deadbeefdeadbeef"));
    }

    [TestMethod]
    public async Task GetFile_IndexedButDeleted_FallsBackAndReturnsNull()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);
        var stored = await StoreAsync(service, "gone.txt", "hello");
        File.Delete(stored.CachedFilePath);

        Assert.IsNull(service.GetFile(stored.FileId));
    }

    [TestMethod]
    public void GetFile_NotIndexedButOnDisk_FallsBackToFilesystem()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);
        var filePath = temp.Combine("0123456789abcdef.txt");
        File.WriteAllText(filePath, "fallback");

        var found = service.GetFile("0123456789abcdef");

        Assert.IsNotNull(found);
        Assert.AreEqual(filePath, found!.CachedFilePath);
        Assert.AreEqual(".txt", found.Extension);
        Assert.AreEqual(8L, found.FileSizeBytes);
        Assert.IsFalse(found.IsDuplicate);

        // The fallback result is cached for subsequent lookups.
        Assert.IsNotNull(service.GetFile("0123456789abcdef"));
    }

    [TestMethod]
    public async Task CleanupOldFilesAsync_UnderQuota_KeepsAllFiles()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path) { MaxCacheSizeBytes = 10_000 };
        var first = await StoreAsync(service, "a.txt", new string('a', 100));
        var second = await StoreAsync(service, "b.txt", new string('b', 100));

        await service.CleanupOldFilesAsync();

        Assert.IsTrue(File.Exists(first.CachedFilePath));
        Assert.IsTrue(File.Exists(second.CachedFilePath));
    }

    [TestMethod]
    public async Task CleanupOldFilesAsync_OverQuota_EvictsLeastRecentlyUsedFiles()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);
        var first = await StoreAsync(service, "a.txt", new string('a', 100));
        var second = await StoreAsync(service, "b.txt", new string('b', 100));
        var third = await StoreAsync(service, "c.txt", new string('c', 100));
        var fourth = await StoreAsync(service, "d.txt", new string('d', 100));

        File.SetLastAccessTimeUtc(first.CachedFilePath, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastAccessTimeUtc(second.CachedFilePath, new DateTime(2021, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastAccessTimeUtc(third.CachedFilePath, new DateTime(2021, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastAccessTimeUtc(fourth.CachedFilePath, new DateTime(2021, 1, 4, 0, 0, 0, DateTimeKind.Utc));

        // 400 bytes total, quota 250 -> evict down to the 200-byte target (80%).
        service.MaxCacheSizeBytes = 250;
        await service.CleanupOldFilesAsync();

        Assert.IsFalse(File.Exists(first.CachedFilePath));
        Assert.IsFalse(File.Exists(second.CachedFilePath));
        Assert.IsTrue(File.Exists(third.CachedFilePath));
        Assert.IsTrue(File.Exists(fourth.CachedFilePath));
        Assert.IsNull(service.GetFile(first.FileId));
        Assert.IsNull(service.GetFile(second.FileId));
        Assert.IsNotNull(service.GetFile(third.FileId));
        Assert.IsNotNull(service.GetFile(fourth.FileId));
    }

    [TestMethod]
    public async Task CleanupOldFilesAsync_StoredViaIndexedFiles_EvictsThemToo()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("aaaabbbbccccdddd.txt"), new string('x', 100));
        File.WriteAllText(temp.Combine("eeeeffff00001111.txt"), new string('y', 100));
        var old = new DateTime(2021, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastAccessTimeUtc(temp.Combine("aaaabbbbccccdddd.txt"), old);

        var service = new FileCacheService(temp.Path) { MaxCacheSizeBytes = 150 };
        await service.CleanupOldFilesAsync();

        Assert.IsFalse(File.Exists(temp.Combine("aaaabbbbccccdddd.txt")));
        Assert.IsTrue(File.Exists(temp.Combine("eeeeffff00001111.txt")));
        Assert.IsNull(service.GetFile("aaaabbbbccccdddd"));
    }

    [TestMethod]
    public async Task CleanupOldFilesAsync_TempFilesAreIgnored()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path) { MaxCacheSizeBytes = 10_000 };
        var stored = await StoreAsync(service, "a.txt", "data");
        File.WriteAllText(temp.Combine("tmp_ignored.tmp"), new string('z', 500));

        await service.CleanupOldFilesAsync();

        Assert.IsTrue(File.Exists(stored.CachedFilePath));
        Assert.IsTrue(File.Exists(temp.Combine("tmp_ignored.tmp")));
    }

    [TestMethod]
    public async Task CleanupOldFilesAsync_LockedFile_IsLeftInPlace()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);
        var stored = await StoreAsync(service, "locked.txt", new string('l', 200));

        using (new FileStream(stored.CachedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            service.MaxCacheSizeBytes = 100;
            await service.CleanupOldFilesAsync();
        }

        Assert.IsTrue(File.Exists(stored.CachedFilePath));
        Assert.IsNotNull(service.GetFile(stored.FileId));
    }

    [TestMethod]
    public async Task CleanupOldFilesAsync_DeletedDirectory_DoesNothing()
    {
        using var temp = new TempDirectory();
        var service = new FileCacheService(temp.Path);
        await StoreAsync(service, "a.txt", "data");
        Directory.Delete(temp.Path, recursive: true);

        await service.CleanupOldFilesAsync();

        Assert.IsFalse(Directory.Exists(temp.Path));
    }

    [TestMethod]
    public void DefaultCacheDirectory_Windows_IsNextToExecutable()
    {
        Assert.AreEqual(
            Path.Combine(@"C:\Tools\printman", "cache"),
            FileCacheService.DefaultCacheDirectory(windows: true, @"C:\Tools\printman", "/xdg", "/home/me"));
    }

    [TestMethod]
    public void DefaultCacheDirectory_Unix_UsesXdgCacheHome()
    {
        Assert.AreEqual(
            Path.Combine("/var/cache/me", "printman"),
            FileCacheService.DefaultCacheDirectory(windows: false, "/usr/local/bin", "/var/cache/me", "/home/me"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("relative/cache")]
    public void DefaultCacheDirectory_Unix_FallsBackToHomeCache(string? xdgCacheHome)
    {
        Assert.AreEqual(
            Path.Combine("/home/me", ".cache", "printman"),
            FileCacheService.DefaultCacheDirectory(windows: false, "/usr/local/bin", xdgCacheHome, "/home/me"));
    }
}
