using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Printman.Desktop.Services;

namespace Printman.Desktop.Views;

public partial class AboutWindow : Window
{
    private readonly WebUiPresenter _presenter;
    private readonly UpdateChecker _updateChecker;
    private string? _pendingUpdateUrl;
    private bool _isCheckingUpdates;

    public AboutWindow() : this(new WebUiPresenter(), new UpdateChecker())
    {
    }

    public AboutWindow(WebUiPresenter presenter, UpdateChecker? updateChecker = null, bool checkImmediately = false)
    {
        InitializeComponent();
        _presenter = presenter;
        _updateChecker = updateChecker ?? new UpdateChecker();

        VersionText.Text = $"v{AppMetadata.Version}";
        ApplyUpdateResult(_updateChecker.LastResult);

        if (checkImmediately)
        {
            Opened += (_, _) => _ = RunUpdateCheckAsync();
        }
    }

    private void ApplyUpdateResult(UpdateCheckResult? result)
    {
        if (result == null)
        {
            UpdateStatusText.Text = $"Current version: v{AppMetadata.Version}";
            OpenUpdateButton.IsVisible = false;
            _pendingUpdateUrl = null;
            return;
        }

        UpdateStatusText.Text = result.Message ?? $"Current version: v{AppMetadata.Version}";
        if (result.Status == UpdateCheckStatus.UpdateAvailable)
        {
            _pendingUpdateUrl = result.DownloadUrl ?? result.ReleaseUrl ?? AppMetadata.ReleasesUrl;
            OpenUpdateButton.Content = $"Download v{result.LatestVersion}";
            OpenUpdateButton.IsVisible = true;
        }
        else
        {
            _pendingUpdateUrl = null;
            OpenUpdateButton.IsVisible = false;
        }
    }

    private async Task RunUpdateCheckAsync()
    {
        if (_isCheckingUpdates)
        {
            return;
        }

        _isCheckingUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking GitHub Releases for updates...";

        try
        {
            var result = await _updateChecker.CheckForUpdatesAsync();
            ApplyUpdateResult(result);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            _isCheckingUpdates = false;
        }
    }

    private async void OnCheckUpdatesClick(object? sender, RoutedEventArgs e) =>
        await RunUpdateCheckAsync();

    private void OnOpenUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_pendingUpdateUrl))
        {
            _presenter.Open(_pendingUpdateUrl);
        }
    }

    private void OnAuthorLinkClick(object? sender, RoutedEventArgs e) =>
        _presenter.Open(AppMetadata.AuthorUrl);

    private void OnWebsiteLinkClick(object? sender, RoutedEventArgs e) =>
        _presenter.Open(AppMetadata.WebsiteUrl);

    private void OnGuidesLinkClick(object? sender, RoutedEventArgs e) =>
        _presenter.Open(AppMetadata.GuidesUrl);

    private void OnGitHubLinkClick(object? sender, RoutedEventArgs e) =>
        _presenter.Open(AppMetadata.GitHubUrl);

    private void OnReleasesLinkClick(object? sender, RoutedEventArgs e) =>
        _presenter.Open(AppMetadata.ReleasesUrl);

    private void OnSponsorLinkClick(object? sender, RoutedEventArgs e) =>
        _presenter.Open(AppMetadata.SponsorUrl);

    private void OnSupportEmailClick(object? sender, RoutedEventArgs e) =>
        _presenter.Open(AppMetadata.SupportMailto);

    private async void OnCopySupportEmailClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard != null)
        {
            await Clipboard.SetTextAsync(AppMetadata.SupportEmail);
            CopySupportEmailButton.Content = "Copied";
            await Task.Delay(1200);
            if (ReferenceEquals(CopySupportEmailButton.Content, "Copied"))
            {
                CopySupportEmailButton.Content = "Copy";
            }
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
