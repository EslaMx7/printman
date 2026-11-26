using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Printman.Core.Models;
using Printman.Desktop.Services;

namespace Printman.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly DesktopServerManager _serverManager;
    private readonly WebUiPresenter _webUiPresenter;
    private readonly DesktopSettings _settings;
    private readonly Func<Task> _quitCallback;

    private bool _isSuppressingUiEvents;
    private bool _hasPendingSettingsChanges;
    private bool _isQuitting;
    private string? _alertCopyCommand;

    public MainWindow()
    {
        InitializeComponent();
        _serverManager = null!;
        _webUiPresenter = null!;
        _settings = new DesktopSettings();
        _quitCallback = () => Task.CompletedTask;
    }

    public MainWindow(
        DesktopServerManager serverManager,
        WebUiPresenter webUiPresenter,
        DesktopSettings settings,
        Func<Task> quitCallback)
    {
        InitializeComponent();
        _serverManager = serverManager;
        _webUiPresenter = webUiPresenter;
        _settings = settings;
        _quitCallback = quitCallback;

        LoadSettingsIntoControls();

        _serverManager.StateChanged += () => Dispatcher.UIThread.Post(RefreshView);
        _serverManager.EventReceived += evt => Dispatcher.UIThread.Post(() => AppendActivityEvent(evt));

        RefreshView();
    }

    public void MarkQuitting() => _isQuitting = true;

    public void ReloadSettingsFromExternal()
    {
        Dispatcher.UIThread.Post(() =>
        {
            LoadSettingsIntoControls();
            _hasPendingSettingsChanges = false;
            RefreshView();
        });
    }

    public void ShowNotice(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AlertBanner.IsVisible = true;
            AlertBanner.Background = new SolidColorBrush(Color.Parse("#1E3A8A"));
            AlertBanner.BorderBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
            AlertTitleText.Text = "Notice";
            AlertTitleText.Foreground = Brushes.White;
            AlertBodyText.Text = message;
            AlertBodyText.Foreground = Brushes.White;
            AlertCopyButton.IsVisible = false;
            AlertRetryButton.IsVisible = false;
        });
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isQuitting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void LoadSettingsIntoControls()
    {
        _isSuppressingUiEvents = true;
        try
        {
            ShareAllCheckBox.IsChecked = _settings.ShareAllPrinters;
            EnableSharingCheckBox.IsChecked = _settings.EnableSharing;
            EnableWebUiCheckBox.IsChecked = _settings.EnableWebUi;
            WebPortTextBox.Text = (_settings.WebPort > 0 ? _settings.WebPort : 5000).ToString();
            RequirePinCheckBox.IsChecked = _settings.RequirePin;
            CustomPinTextBox.Text = _settings.CustomPin ?? "";
            PreferBuiltInWebViewCheckBox.IsChecked = _settings.PreferBuiltInWebView;
            AutoStartCheckBox.IsChecked = _settings.AutoStartServer;
            UpdateOptionControlEnabledStates();
        }
        finally
        {
            _isSuppressingUiEvents = false;
        }
    }

    private void UpdateOptionControlEnabledStates()
    {
        bool webEnabled = EnableWebUiCheckBox.IsChecked == true;
        WebPortTextBox.IsEnabled = webEnabled;
        RequirePinCheckBox.IsEnabled = webEnabled;
        CustomPinTextBox.IsEnabled = webEnabled && RequirePinCheckBox.IsChecked == true;
    }

    private void SaveControlsToSettings()
    {
        _settings.ShareAllPrinters = ShareAllCheckBox.IsChecked == true;
        _settings.EnableSharing = EnableSharingCheckBox.IsChecked == true;
        _settings.EnableWebUi = EnableWebUiCheckBox.IsChecked == true;

        if (int.TryParse(WebPortTextBox.Text?.Trim(), out int port) && port is >= 1 and <= 65535)
        {
            _settings.WebPort = port;
        }
        else
        {
            _isSuppressingUiEvents = true;
            try
            {
                WebPortTextBox.Text = (_settings.WebPort > 0 ? _settings.WebPort : 5000).ToString();
            }
            finally
            {
                _isSuppressingUiEvents = false;
            }
        }

        _settings.RequirePin = RequirePinCheckBox.IsChecked == true;
        _settings.CustomPin = string.IsNullOrWhiteSpace(CustomPinTextBox.Text)
            ? null
            : CustomPinTextBox.Text.Trim();
        _settings.PreferBuiltInWebView = PreferBuiltInWebViewCheckBox.IsChecked == true;
        _settings.AutoStartServer = AutoStartCheckBox.IsChecked == true;

        DesktopSettingsStore.Save(_settings);
    }

    private bool TryValidatePin([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? notice)
    {
        notice = null;
        if (RequirePinCheckBox.IsChecked != true)
        {
            return true;
        }

        var raw = CustomPinTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true; // auto-generated 6-digit PIN
        }

        if (raw.Length is >= 4 and <= 8)
        {
            return true;
        }

        notice = "Custom PIN must be 4-8 characters. Use 4-8 characters, or clear the field to auto-generate a 6-digit PIN.";
        return false;
    }

    private void RefreshView()
    {
        var state = _serverManager.State;
        var info = _serverManager.RuntimeInfo;
        var printers = _serverManager.InstalledPrinters;

        RebuildPrintersList(printers);
        UpdateStatusHeader(state, info);
        UpdateAlertBanner(state, info);
        UpdateConnectionPanels(state, info);
        ApplyRestartButton.IsVisible = _hasPendingSettingsChanges && state == DesktopServerState.Running;
    }

    private void UpdateStatusHeader(DesktopServerState state, ServerRuntimeInfo? info)
    {
        switch (state)
        {
            case DesktopServerState.Running:
                StatusBadge.Background = new SolidColorBrush(Color.Parse("#15803D"));
                StatusBadgeText.Text = "Running";
                PrimaryActionButton.Content = "Stop Sharing";
                PrimaryActionButton.IsEnabled = true;
                SubtitleText.Text = info != null
                    ? BuildRunningSubtitle(info)
                    : "Server is active on your local network.";
                break;

            case DesktopServerState.Starting:
                StatusBadge.Background = new SolidColorBrush(Color.Parse("#B45309"));
                StatusBadgeText.Text = "Starting...";
                PrimaryActionButton.Content = "Starting...";
                PrimaryActionButton.IsEnabled = false;
                SubtitleText.Text = "Starting HTTP, IPP, and mDNS services...";
                break;

            case DesktopServerState.CupsUnavailable:
                StatusBadge.Background = new SolidColorBrush(Color.Parse("#B91C1C"));
                StatusBadgeText.Text = "CUPS Unavailable";
                PrimaryActionButton.Content = "Retry Start";
                PrimaryActionButton.IsEnabled = true;
                SubtitleText.Text = "Local CUPS service is required on Linux and macOS.";
                break;

            case DesktopServerState.Error:
                StatusBadge.Background = new SolidColorBrush(Color.Parse("#B91C1C"));
                StatusBadgeText.Text = "Error";
                PrimaryActionButton.Content = "Start Sharing";
                PrimaryActionButton.IsEnabled = true;
                SubtitleText.Text = _serverManager.ErrorMessage ?? "Failed to start Printman.";
                break;

            default:
                StatusBadge.Background = new SolidColorBrush(Color.Parse("#475569"));
                StatusBadgeText.Text = "Stopped";
                PrimaryActionButton.Content = "Start Sharing";
                PrimaryActionButton.IsEnabled = true;
                SubtitleText.Text = "Share any USB printer over AirPrint / Mopria and local Web UI.";
                break;
        }

        bool webRunning = state == DesktopServerState.Running && info?.WebUiEnabled == true && !string.IsNullOrEmpty(info.LocalUrl);
        OpenWebUiButton.IsEnabled = webRunning;
        OpenBrowserButton.IsEnabled = webRunning;
    }

    private static string BuildRunningSubtitle(ServerRuntimeInfo info)
    {
        var parts = new List<string>();
        if (info.SharingEnabled)
        {
            parts.Add($"Sharing {info.SharedPrinters.Count} printer(s) on IPP :{info.IppPort}");
        }
        if (info.WebUiEnabled)
        {
            parts.Add($"Web UI on :{info.WebPort}");
        }
        return string.Join("  •  ", parts);
    }

    private void UpdateAlertBanner(DesktopServerState state, ServerRuntimeInfo? info)
    {
        _alertCopyCommand = null;

        if (state == DesktopServerState.CupsUnavailable && _serverManager.CupsDiagnosticLines.Count > 0)
        {
            AlertBanner.IsVisible = true;
            AlertBanner.Background = new SolidColorBrush(Color.Parse("#7F1D1D"));
            AlertBanner.BorderBrush = new SolidColorBrush(Color.Parse("#EF4444"));
            AlertTitleText.Text = "CUPS Print Service Required";
            AlertTitleText.Foreground = Brushes.White;
            AlertBodyText.Text = string.Join(Environment.NewLine, _serverManager.CupsDiagnosticLines.Where(l => !string.IsNullOrWhiteSpace(l)));
            AlertBodyText.Foreground = Brushes.White;

            var cmdLine = _serverManager.CupsDiagnosticLines.FirstOrDefault(l => l.TrimStart().StartsWith("sudo ", StringComparison.Ordinal));
            if (cmdLine != null)
            {
                _alertCopyCommand = cmdLine.Trim();
                AlertCopyButton.IsVisible = true;
            }
            else
            {
                AlertCopyButton.IsVisible = false;
            }
            AlertRetryButton.IsVisible = true;
            return;
        }

        if (state == DesktopServerState.Error && !string.IsNullOrWhiteSpace(_serverManager.ErrorMessage))
        {
            AlertBanner.IsVisible = true;
            AlertBanner.Background = new SolidColorBrush(Color.Parse("#7F1D1D"));
            AlertBanner.BorderBrush = new SolidColorBrush(Color.Parse("#EF4444"));
            AlertTitleText.Text = "Startup Error";
            AlertTitleText.Foreground = Brushes.White;
            AlertBodyText.Text = _serverManager.ErrorMessage;
            AlertBodyText.Foreground = Brushes.White;
            AlertCopyButton.IsVisible = false;
            AlertRetryButton.IsVisible = true;
            return;
        }

        if (state == DesktopServerState.Running && info != null && info.Warnings.Count > 0)
        {
            AlertBanner.IsVisible = true;
            AlertBanner.Background = new SolidColorBrush(Color.Parse("#78350F"));
            AlertBanner.BorderBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
            AlertTitleText.Text = "Network & Firewall Notes";
            AlertTitleText.Foreground = Brushes.White;
            AlertBodyText.Text = string.Join(Environment.NewLine, info.Warnings);
            AlertBodyText.Foreground = Brushes.White;
            AlertCopyButton.IsVisible = false;
            AlertRetryButton.IsVisible = false;
            return;
        }

        AlertBanner.IsVisible = false;
    }

    private void RebuildPrintersList(IReadOnlyList<PrinterInfo> printers)
    {
        PrintersListPanel.Children.Clear();
        if (printers.Count == 0)
        {
            PrintersListPanel.Children.Add(new TextBlock
            {
                Text = "No installed printers detected. Connect your printer and click Refresh.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray
            });
            return;
        }

        var selectedSet = new HashSet<string>(_settings.SelectedPrinters, StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Count == 0 && printers.FirstOrDefault(p => p.IsDefault) is { } defaultPrinter)
        {
            selectedSet.Add(defaultPrinter.Name);
        }

        bool shareAll = _settings.ShareAllPrinters;
        foreach (var printer in printers)
        {
            var name = printer.Name;
            var label = printer.IsDefault ? $"{name} (Default)" : name;
            var cb = new CheckBox
            {
                Content = label,
                Tag = name,
                IsChecked = shareAll || selectedSet.Contains(name),
                IsEnabled = !shareAll
            };

            cb.IsCheckedChanged += (_, _) =>
            {
                if (_isSuppressingUiEvents) return;

                _settings.SelectedPrinters = PrintersListPanel.Children
                    .OfType<CheckBox>()
                    .Where(box => box.IsChecked == true && box.Tag is string)
                    .Select(box => (string)box.Tag!)
                    .ToList();

                DesktopSettingsStore.Save(_settings);
                if (_serverManager.State == DesktopServerState.Running)
                {
                    _hasPendingSettingsChanges = true;
                    ApplyRestartButton.IsVisible = true;
                }
            };

            PrintersListPanel.Children.Add(cb);
        }
    }

    private void UpdateConnectionPanels(DesktopServerState state, ServerRuntimeInfo? info)
    {
        if (state != DesktopServerState.Running || info == null)
        {
            IdleInfoPanel.IsVisible = true;
            WebConnectionPanel.IsVisible = false;
            SharedPrintersPanel.IsVisible = false;
            return;
        }

        IdleInfoPanel.IsVisible = false;

        // Web UI + QR code
        if (info.WebUiEnabled)
        {
            WebConnectionPanel.IsVisible = true;
            var urls = info.LanUrls.Count > 0
                ? info.LanUrls
                : (info.LocalUrl != null ? [info.LocalUrl] : Array.Empty<string>());

            _isSuppressingUiEvents = true;
            try
            {
                LanUrlSelector.ItemsSource = urls;
                if (urls.Count > 0)
                {
                    LanUrlSelector.SelectedIndex = 0;
                    LanQrCode.Value = urls[0];
                }
                else
                {
                    LanQrCode.Value = null;
                }
            }
            finally
            {
                _isSuppressingUiEvents = false;
            }

            if (info.RequireAuth && !string.IsNullOrEmpty(info.Pin))
            {
                PinDisplayBadge.IsVisible = true;
                PinDisplayText.Text = $"PIN: {info.Pin}";
                CopyPinButton.IsVisible = true;
            }
            else
            {
                PinDisplayBadge.IsVisible = true;
                PinDisplayText.Text = "Open Access";
                CopyPinButton.IsVisible = false;
            }
        }
        else
        {
            WebConnectionPanel.IsVisible = false;
        }

        // Shared IPP endpoints
        if (info.SharingEnabled && info.SharedPrinters.Count > 0)
        {
            SharedPrintersPanel.IsVisible = true;
            SharedPrintersHeaderText.Text = info.MdnsActive
                ? $"Network Printers (AirPrint / Mopria active on :{info.IppPort})"
                : $"Network Printers (IPP active on :{info.IppPort}, mDNS off)";

            SharedPrintersLinksPanel.Children.Clear();
            foreach (var sp in info.SharedPrinters)
            {
                var card = new StackPanel { Spacing = 2 };
                card.Children.Add(new TextBlock
                {
                    Text = sp.DisplayName,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12
                });

                foreach (var ippUrl in sp.IppUrls)
                {
                    var row = new Grid
                    {
                        ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
                        ColumnSpacing = 8
                    };
                    row.Children.Add(new SelectableTextBlock
                    {
                        Text = ippUrl,
                        FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    var copyBtn = new Button
                    {
                        Content = "Copy",
                        Padding = new Avalonia.Thickness(8, 2),
                        FontSize = 11
                    };
                    var urlCopy = ippUrl;
                    copyBtn.Click += async (_, _) => await CopyTextAsync(urlCopy);
                    Grid.SetColumn(copyBtn, 1);
                    row.Children.Add(copyBtn);
                    card.Children.Add(row);
                }

                SharedPrintersLinksPanel.Children.Add(card);
            }
        }
        else
        {
            SharedPrintersPanel.IsVisible = false;
        }
    }

    private void AppendActivityEvent(PrintEvent evt)
    {
        var line = new TextBlock
        {
            Text = $"[{evt.Timestamp.ToLocalTime():HH:mm:ss}] {evt.Message}",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        ActivityLogPanel.Children.Add(line);
        while (ActivityLogPanel.Children.Count > 40)
        {
            ActivityLogPanel.Children.RemoveAt(0);
        }
        ActivityScrollViewer.ScrollToEnd();
    }

    private void OnSettingChanged(object? sender, RoutedEventArgs e)
    {
        if (_isSuppressingUiEvents) return;
        UpdateOptionControlEnabledStates();
        SaveControlsToSettings();
        if (ReferenceEquals(sender, ShareAllCheckBox))
        {
            RebuildPrintersList(_serverManager.InstalledPrinters);
        }

        if (_serverManager.State == DesktopServerState.Running &&
            !ReferenceEquals(sender, AutoStartCheckBox) &&
            !ReferenceEquals(sender, PreferBuiltInWebViewCheckBox))
        {
            _hasPendingSettingsChanges = true;
            ApplyRestartButton.IsVisible = true;
        }
    }

    private void OnTextSettingChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isSuppressingUiEvents) return;
        if (ReferenceEquals(sender, WebPortTextBox) &&
            !(int.TryParse(WebPortTextBox.Text?.Trim(), out int port) && port is >= 1 and <= 65535))
        {
            return;
        }
        SaveControlsToSettings();
        if (_serverManager.State == DesktopServerState.Running)
        {
            _hasPendingSettingsChanges = true;
            ApplyRestartButton.IsVisible = true;
        }
    }

    private async void OnPrimaryActionClick(object? sender, RoutedEventArgs e)
    {
        PrimaryActionButton.IsEnabled = false;
        SaveControlsToSettings();

        if (_serverManager.State != DesktopServerState.Running && !TryValidatePin(out var pinNotice))
        {
            ShowNotice(pinNotice);
            PrimaryActionButton.IsEnabled = true;
            return;
        }

        _hasPendingSettingsChanges = false;
        if (_serverManager.State == DesktopServerState.Running)
        {
            await _serverManager.StopAsync();
        }
        else
        {
            await _serverManager.StartAsync(_settings);
        }
    }

    private async void OnApplyRestartClick(object? sender, RoutedEventArgs e)
    {
        ApplyRestartButton.IsEnabled = false;
        SaveControlsToSettings();

        if (!TryValidatePin(out var pinNotice))
        {
            ShowNotice(pinNotice);
            ApplyRestartButton.IsEnabled = true;
            return;
        }

        _hasPendingSettingsChanges = false;
        ApplyRestartButton.IsVisible = false;
        try
        {
            await _serverManager.RestartAsync(_settings);
        }
        finally
        {
            ApplyRestartButton.IsEnabled = true;
        }
    }

    private async void OnRefreshPrintersClick(object? sender, RoutedEventArgs e)
    {
        await _serverManager.RefreshEnvironmentAsync();
    }

    private void OnOpenWebUiClick(object? sender, RoutedEventArgs e)
    {
        var url = _serverManager.RuntimeInfo?.LocalUrl;
        if (string.IsNullOrEmpty(url)) return;

        var result = _webUiPresenter.Open(url, _settings.PreferBuiltInWebView, this);
        if (!string.IsNullOrEmpty(result.Notice))
        {
            ShowNotice(result.Notice);
        }
    }

    private void OnOpenBrowserClick(object? sender, RoutedEventArgs e)
    {
        var url = _serverManager.RuntimeInfo?.LocalUrl;
        if (string.IsNullOrEmpty(url)) return;
        WebUiPresenter.OpenInSystemBrowser(url);
    }

    private void OnLanUrlSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSuppressingUiEvents) return;
        if (LanUrlSelector.SelectedItem is string url)
        {
            LanQrCode.Value = url;
        }
    }

    private async void OnCopyLanUrlClick(object? sender, RoutedEventArgs e)
    {
        if (LanUrlSelector.SelectedItem is string url)
        {
            await CopyTextAsync(url);
        }
    }

    private async void OnCopyPinClick(object? sender, RoutedEventArgs e)
    {
        var pin = _serverManager.RuntimeInfo?.Pin;
        if (!string.IsNullOrEmpty(pin))
        {
            await CopyTextAsync(pin);
        }
    }

    private async void OnAlertCopyClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_alertCopyCommand))
        {
            await CopyTextAsync(_alertCopyCommand);
        }
    }

    private async void OnAlertRetryClick(object? sender, RoutedEventArgs e)
    {
        SaveControlsToSettings();
        if (_serverManager.State == DesktopServerState.CupsUnavailable)
        {
            await _serverManager.RefreshEnvironmentAsync();
        }
        else
        {
            if (!TryValidatePin(out var pinNotice))
            {
                ShowNotice(pinNotice);
                return;
            }
            await _serverManager.StartAsync(_settings);
        }
    }

    private async void OnQuitClick(object? sender, RoutedEventArgs e)
    {
        await _quitCallback();
    }

    private async Task CopyTextAsync(string text)
    {
        if (Clipboard != null)
        {
            await Clipboard.SetTextAsync(text);
        }
    }
}
