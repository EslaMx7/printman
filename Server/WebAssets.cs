using System.Reflection;
using System.Text;

namespace Printman.Server;

public static class WebAssets
{
    private static readonly Lazy<string> CachedEmbeddedHtml = new(LoadEmbeddedHtml);
    private static readonly Lazy<string> CachedEmbeddedFavicon = new(LoadEmbeddedFavicon);

    /// <summary>
    /// Gets the HTML content for the mobile web SPA.
    /// In DEBUG mode, reads directly from disk if available to enable instant live-reload.
    /// In Release or standalone binary mode, loads from the in-assembly embedded resource (cached).
    /// </summary>
    public static string IndexHtml
    {
        get
        {
#if DEBUG
            // 1. Development live-reload: check workspace source paths
            var devPaths = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Server", "Web", "index.html")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Server", "Web", "index.html"))
            };

            foreach (var path in devPaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        return File.ReadAllText(path, Encoding.UTF8);
                    }
                    catch
                    {
                        // Fall through to embedded resource if file is temporarily locked
                    }
                }
            }
#endif
            return CachedEmbeddedHtml.Value;
        }
    }

    /// <summary>
    /// Gets the SVG content for the favicon and app icon.
    /// In DEBUG mode, reads directly from disk if available to enable instant live-reload.
    /// In Release mode, loads from the in-assembly embedded resource (cached).
    /// </summary>
    public static string FaviconSvg
    {
        get
        {
#if DEBUG
            var devPaths = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Server", "Web", "favicon.svg")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Server", "Web", "favicon.svg"))
            };

            foreach (var path in devPaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        return File.ReadAllText(path, Encoding.UTF8);
                    }
                    catch
                    {
                    }
                }
            }
#endif
            return CachedEmbeddedFavicon.Value;
        }
    }

    private static string LoadEmbeddedHtml()
    {
        var assembly = typeof(WebAssets).Assembly;
        const string primaryResourceName = "Printman.Server.Web.index.html";

        var stream = assembly.GetManifestResourceStream(primaryResourceName);
        if (stream == null)
        {
            // Fallback: search manifest resource names ending with index.html
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                stream = assembly.GetManifestResourceStream(resourceName);
            }
        }

        if (stream == null)
        {
            throw new InvalidOperationException(
                $"Embedded web asset resource '{primaryResourceName}' was not found in assembly '{assembly.FullName}'. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
        }

        using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }

    private static string LoadEmbeddedFavicon()
    {
        var assembly = typeof(WebAssets).Assembly;
        const string primaryResourceName = "Printman.Server.Web.favicon.svg";

        var stream = assembly.GetManifestResourceStream(primaryResourceName);
        if (stream == null)
        {
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("favicon.svg", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                stream = assembly.GetManifestResourceStream(resourceName);
            }
        }

        if (stream == null)
        {
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\"><rect width=\"32\" height=\"32\" rx=\"7\" fill=\"#2563eb\"/></svg>";
        }

        using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
