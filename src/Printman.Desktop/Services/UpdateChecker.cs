using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Printman.Desktop.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Error
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? DownloadUrl,
    string? Message);

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto>? Assets { get; set; }
}

internal sealed class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

[JsonSerializable(typeof(GitHubReleaseDto))]
internal sealed partial class GitHubReleaseJsonContext : JsonSerializerContext;

public sealed class UpdateChecker : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly Func<string> _currentVersionProvider;

    public UpdateChecker(HttpMessageHandler? httpHandler = null, Func<string>? currentVersionProvider = null)
    {
        if (httpHandler != null)
        {
            _http = new HttpClient(httpHandler, disposeHandler: false);
            _ownsClient = true;
        }
        else
        {
            _http = new HttpClient();
            _ownsClient = true;
        }

        _currentVersionProvider = currentVersionProvider ?? (() => AppMetadata.Version);
    }

    public UpdateCheckResult? LastResult { get; private set; }

    public event Action<UpdateCheckResult>? Completed;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var currentVersion = _currentVersionProvider();
        UpdateCheckResult result;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            using var request = new HttpRequestMessage(HttpMethod.Get, AppMetadata.LatestReleaseApiUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Printman", SanitizeProductVersion(currentVersion)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string message = response.StatusCode == HttpStatusCode.Forbidden
                    ? "Could not check updates right now (GitHub API rate limit)."
                    : $"Could not check updates (HTTP {(int)response.StatusCode}).";
                result = new UpdateCheckResult(UpdateCheckStatus.Error, currentVersion, null, null, null, message);
                return Publish(result);
            }

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.GitHubReleaseDto);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                result = new UpdateCheckResult(
                    UpdateCheckStatus.Error,
                    currentVersion,
                    null,
                    null,
                    null,
                    "Could not read release information from GitHub.");
                return Publish(result);
            }

            string latestVersion = release.TagName.Trim().TrimStart('v', 'V');
            string releaseUrl = !string.IsNullOrWhiteSpace(release.HtmlUrl)
                ? release.HtmlUrl
                : AppMetadata.ReleasesUrl;

            if (!TryParseSemVer(currentVersion, out var currentSemVer) ||
                !TryParseSemVer(latestVersion, out var latestSemVer))
            {
                result = new UpdateCheckResult(
                    UpdateCheckStatus.Error,
                    currentVersion,
                    latestVersion,
                    releaseUrl,
                    null,
                    $"Could not compare version v{currentVersion} with v{latestVersion}.");
                return Publish(result);
            }

            if (CompareSemVer(latestSemVer, currentSemVer) > 0)
            {
                var downloadUrl = SelectPlatformAssetUrl(release.Assets, RuntimeInformation.ProcessArchitecture) ?? releaseUrl;
                result = new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable,
                    currentVersion,
                    latestVersion,
                    releaseUrl,
                    downloadUrl,
                    $"Update available: v{latestVersion} (installed: v{currentVersion}).");
            }
            else
            {
                result = new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentVersion,
                    latestVersion,
                    releaseUrl,
                    null,
                    $"You are up to date (v{currentVersion}).");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result = new UpdateCheckResult(
                UpdateCheckStatus.Error,
                currentVersion,
                null,
                null,
                null,
                "Update check timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            result = new UpdateCheckResult(
                UpdateCheckStatus.Error,
                currentVersion,
                null,
                null,
                null,
                $"Could not check updates: {ex.Message}");
        }

        return Publish(result);
    }

    private UpdateCheckResult Publish(UpdateCheckResult result)
    {
        LastResult = result;
        Completed?.Invoke(result);
        return result;
    }

    private static string SanitizeProductVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }
        var clean = new string(version.Where(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-').ToArray());
        return string.IsNullOrEmpty(clean) ? "0.0.0" : clean;
    }

    internal readonly record struct SemVer(int Major, int Minor, int Patch, string? Prerelease);

    internal static bool TryParseSemVer(string? raw, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string trimmed = raw.Trim().TrimStart('v', 'V');
        int plusIndex = trimmed.IndexOf('+');
        if (plusIndex >= 0)
        {
            trimmed = trimmed[..plusIndex];
        }

        string? prerelease = null;
        int dashIndex = trimmed.IndexOf('-');
        if (dashIndex >= 0)
        {
            prerelease = trimmed[(dashIndex + 1)..];
            if (string.IsNullOrWhiteSpace(prerelease))
            {
                return false;
            }
            trimmed = trimmed[..dashIndex];
        }

        var parts = trimmed.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int major) || major < 0 ||
            !int.TryParse(parts[1], out int minor) || minor < 0)
        {
            return false;
        }

        int patch = 0;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out patch) || patch < 0))
        {
            return false;
        }

        version = new SemVer(major, minor, patch, prerelease);
        return true;
    }

    internal static int CompareSemVer(SemVer a, SemVer b)
    {
        int cmp = a.Major.CompareTo(b.Major);
        if (cmp != 0) return cmp;
        cmp = a.Minor.CompareTo(b.Minor);
        if (cmp != 0) return cmp;
        cmp = a.Patch.CompareTo(b.Patch);
        if (cmp != 0) return cmp;

        if (a.Prerelease == null && b.Prerelease == null) return 0;
        if (a.Prerelease == null) return 1;
        if (b.Prerelease == null) return -1;

        var aIds = a.Prerelease.Split('.');
        var bIds = b.Prerelease.Split('.');
        int len = Math.Min(aIds.Length, bIds.Length);
        for (int i = 0; i < len; i++)
        {
            bool aNum = int.TryParse(aIds[i], out int aVal);
            bool bNum = int.TryParse(bIds[i], out int bVal);
            if (aNum && bNum)
            {
                cmp = aVal.CompareTo(bVal);
                if (cmp != 0) return cmp;
            }
            else if (aNum != bNum)
            {
                return aNum ? -1 : 1;
            }
            else
            {
                cmp = string.CompareOrdinal(aIds[i], bIds[i]);
                if (cmp != 0) return cmp;
            }
        }

        return aIds.Length.CompareTo(bIds.Length);
    }

    internal static string? SelectPlatformAssetUrl(
        IReadOnlyList<GitHubReleaseAssetDto>? assets,
        Architecture arch)
    {
        if (assets == null || assets.Count == 0)
        {
            return null;
        }

        string archToken = arch == Architecture.Arm64 ? "arm64" : "x64";
        string[] patterns = OperatingSystem.IsWindows()
            ? [$"printman-desktop-v", $"win-{archToken}.exe", $"win-{archToken}.zip"]
            : OperatingSystem.IsMacOS()
                ? [$"osx-{archToken}.dmg", $"osx-{archToken}.tar.gz"]
                : [$"printman-desktop-v", $"linux-{archToken}", $"linux-{archToken}.tar.gz"];

        if (OperatingSystem.IsWindows())
        {
            return FindAsset(assets, $"printman-desktop-", $"win-{archToken}.exe")
                ?? FindAsset(assets, "printman-", $"win-{archToken}.zip");
        }

        if (OperatingSystem.IsMacOS())
        {
            return FindAsset(assets, "printman-", $"osx-{archToken}.dmg")
                ?? FindAsset(assets, "printman-", $"osx-{archToken}.tar.gz");
        }

        return FindExactSuffixAsset(assets, "printman-desktop-", $"linux-{archToken}")
            ?? FindAsset(assets, "printman-", $"linux-{archToken}.tar.gz");
    }

    private static string? FindAsset(
        IReadOnlyList<GitHubReleaseAssetDto> assets,
        string prefix,
        string suffix)
    {
        foreach (var asset in assets)
        {
            if (!string.IsNullOrWhiteSpace(asset.Name) &&
                !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
                asset.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return asset.BrowserDownloadUrl;
            }
        }
        return null;
    }

    private static string? FindExactSuffixAsset(
        IReadOnlyList<GitHubReleaseAssetDto> assets,
        string prefix,
        string suffix)
    {
        foreach (var asset in assets)
        {
            if (!string.IsNullOrWhiteSpace(asset.Name) &&
                !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
                asset.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return asset.BrowserDownloadUrl;
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
