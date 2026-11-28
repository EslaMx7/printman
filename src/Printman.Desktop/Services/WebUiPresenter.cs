using System.Diagnostics;

namespace Printman.Desktop.Services;

public sealed class WebUiPresenter(Func<string, bool>? browserLauncher = null)
{
    private readonly Func<string, bool> _browserLauncher = browserLauncher ?? OpenInSystemBrowser;

    public bool Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeMailto))
        {
            return false;
        }

        return _browserLauncher(uri.AbsoluteUri);
    }

    public static bool OpenInSystemBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
