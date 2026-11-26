using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Printman.Desktop.Services;

public sealed record WebUiOpenResult(bool UsedBuiltInDialog, string? Notice);

public sealed class WebUiPresenter : IDisposable
{
    private NativeWebDialog? _dialog;
    private Uri? _currentUri;

    public WebUiOpenResult Open(string url, bool preferBuiltInWebView, Window? owner = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new WebUiOpenResult(false, "Invalid Web UI URL.");
        }

        if (!preferBuiltInWebView)
        {
            OpenInSystemBrowser(url);
            return new WebUiOpenResult(false, null);
        }

        try
        {
            if (_dialog != null)
            {
                if (_currentUri != uri)
                {
                    _currentUri = uri;
                    _dialog.Navigate(uri);
                }
                _dialog.Focus();
                return new WebUiOpenResult(true, null);
            }

            var dialog = new NativeWebDialog
            {
                Title = "Printman • Web Print Server",
                CanUserResize = true,
                Source = uri
            };

            dialog.EnvironmentRequested += (_, args) =>
            {
                if (args is WindowsWebView2EnvironmentRequestedEventArgs wv2)
                {
                    wv2.UserDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Printman",
                        "WebView2");
                }
            };

            dialog.Closing += (_, _) =>
            {
                if (ReferenceEquals(_dialog, dialog))
                {
                    _dialog = null;
                    _currentUri = null;
                }
                try { dialog.Dispose(); } catch { }
            };

            _dialog = dialog;
            _currentUri = uri;
            dialog.Resize(1040, 760);

            if (owner != null)
            {
                dialog.Show(owner);
            }
            else
            {
                dialog.Show();
            }

            return new WebUiOpenResult(true, null);
        }
        catch (Exception ex)
        {
            Close();
            OpenInSystemBrowser(url);
            return new WebUiOpenResult(
                false,
                $"Built-in WebView unavailable ({ex.Message}); opened in default browser instead.");
        }
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

    public void Close()
    {
        var dialog = _dialog;
        _dialog = null;
        _currentUri = null;
        if (dialog != null)
        {
            try { dialog.Close(); } catch { }
            try { dialog.Dispose(); } catch { }
        }
    }

    public void Dispose() => Close();
}
