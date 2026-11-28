using System.Reflection;
using Printman.Interactive;

namespace Printman.Desktop.Services;

public static class AppMetadata
{
    public const string AppName = "Printman";
    public const string WindowTitle = "Printman - Wireless Printer Sharing & Web Print Server";
    public const string Tagline = "Turn any USB printer into a native AirPrint, Mopria, and Web printer. 100% local, zero cloud.";
    public const string AuthorName = "Eslam Hamouda";
    public const string AuthorHandle = "@EslaMx7";
    public const string AuthorDisplay = "eslamx.com";
    public const string AuthorUrl = "https://eslamx.com";
    public const string WebsiteDisplay = "printman.eslamx.com";
    public const string WebsiteUrl = "https://printman.eslamx.com";
    public const string GuidesUrl = "https://printman.eslamx.com/guides";
    public const string GitHubDisplay = "github.com/EslaMx7/printman";
    public const string GitHubUrl = "https://github.com/EslaMx7/printman";
    public const string ReleasesUrl = "https://github.com/EslaMx7/printman/releases";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/EslaMx7/printman/releases/latest";
    public const string SponsorDisplay = "buymeacoffee.com/eslamx7";
    public const string SponsorUrl = "https://buymeacoffee.com/eslamx7";
    public const string SupportEmail = "support@eslamx.com";
    public const string SupportMailto = "mailto:support@eslamx.com";
    public const string Copyright = "© 2026 Eslam Hamouda";
    public const string License = "MIT License";

    public static string Version
    {
        get
        {
            var assembly = typeof(AppMetadata).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                int plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? ConsoleUi.AppVersion;
        }
    }
}
