using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Printman.Core.Models;
using Printman.Desktop.Services;
using Printman.Desktop.Views;

namespace Printman.Desktop;

public partial class App : Avalonia.Application
{
    private readonly DesktopServerManager _serverManager = new();
    private readonly WebUiPresenter _webUiPresenter = new();
    private readonly UpdateChecker _updateChecker = new();
    private DesktopSettings _settings = new();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private readonly NativeMenu _trayMenu = new();
    private bool _isQuitting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _settings = DesktopSettingsStore.Load();

            _mainWindow = new MainWindow(_serverManager, _webUiPresenter, _settings, _updateChecker);
            desktop.MainWindow = _mainWindow;

            SetupTrayIcon();
            _serverManager.StateChanged += () => Dispatcher.UIThread.Post(UpdateTrayMenu);

            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, _) => Dispatcher.UIThread.Post(ShowMainWindow);
            }

            _mainWindow.Show();

            _ = InitializeServerAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeServerAsync()
    {
        if (_settings.AutoStartServer)
        {
            await _serverManager.StartAsync(_settings);
        }
        else
        {
            await _serverManager.RefreshEnvironmentAsync();
        }

        _ = CheckForUpdatesQuietlyAsync();
    }

    private async Task CheckForUpdatesQuietlyAsync()
    {
        try
        {
            var result = await _updateChecker.CheckForUpdatesAsync();
            if (result.Status == UpdateCheckStatus.UpdateAvailable)
            {
                Dispatcher.UIThread.Post(UpdateTrayMenu);
            }
        }
        catch
        {
            // Startup update checks must never disrupt the tray app.
        }
    }

    private void SetupTrayIcon()
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://printman-desktop/Assets/tray-icon.png"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Printman - Ready",
            Menu = _trayMenu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        UpdateTrayMenu();
        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    private void UpdateTrayMenu()
    {
        if (_trayIcon == null)
        {
            return;
        }

        var state = _serverManager.State;
        var info = _serverManager.RuntimeInfo;
        var printers = _serverManager.InstalledPrinters;

        string statusText = FormatStatusText(state, info);
        _trayIcon.ToolTipText = $"Printman - {statusText.Replace("Status: ", "")}";

        PopulateTrayMenu(_trayMenu, state, info, printers);
    }

    public void PopulateTrayMenu(
        NativeMenu menu,
        DesktopServerState state,
        ServerRuntimeInfo? info,
        IReadOnlyList<PrinterInfo> printers)
    {
        string statusText = FormatStatusText(state, info);

        menu.Items.Clear();

        var statusItem = new NativeMenuItem(statusText) { IsEnabled = false };
        menu.Items.Add(statusItem);

        var openWindowItem = new NativeMenuItem("Open Printman");
        openWindowItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openWindowItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        if (state == DesktopServerState.Running)
        {
            var stopItem = new NativeMenuItem("Stop Sharing");
            stopItem.Click += async (_, _) => await _serverManager.StopAsync();
            menu.Items.Add(stopItem);
        }
        else if (state != DesktopServerState.Starting)
        {
            var startItem = new NativeMenuItem("Start Sharing");
            startItem.Click += async (_, _) => await _serverManager.StartAsync(_settings);
            menu.Items.Add(startItem);
        }

        // Printers submenu
        var printersMenuItem = new NativeMenuItem("Printers");
        var printersSubMenu = new NativeMenu();
        if (printers.Count == 0)
        {
            printersSubMenu.Items.Add(new NativeMenuItem("No printers detected") { IsEnabled = false });
        }
        else
        {
            var shareAllItem = new NativeMenuItem("Share All Printers")
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _settings.ShareAllPrinters
            };
            shareAllItem.Click += async (_, _) =>
            {
                _settings.ShareAllPrinters = !_settings.ShareAllPrinters;
                DesktopSettingsStore.Save(_settings);
                _mainWindow?.ReloadSettingsFromExternal();
                if (_serverManager.State == DesktopServerState.Running)
                {
                    await _serverManager.RestartAsync(_settings);
                }
                else
                {
                    UpdateTrayMenu();
                }
            };
            printersSubMenu.Items.Add(shareAllItem);
            printersSubMenu.Items.Add(new NativeMenuItemSeparator());

            var selectedSet = GetEffectiveSelectedPrinters(printers);
            foreach (var printer in printers)
            {
                var printerName = printer.Name;
                var label = printer.IsDefault ? $"{printerName} (Default)" : printerName;
                var item = new NativeMenuItem(label)
                {
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = _settings.ShareAllPrinters || selectedSet.Contains(printerName),
                    IsEnabled = !_settings.ShareAllPrinters
                };
                item.Click += async (_, _) => await TogglePrinterFromTrayAsync(printerName, printers);
                printersSubMenu.Items.Add(item);
            }
        }
        printersSubMenu.Items.Add(new NativeMenuItemSeparator());
        var refreshItem = new NativeMenuItem("Refresh Printers");
        refreshItem.Click += async (_, _) => await _serverManager.RefreshEnvironmentAsync();
        printersSubMenu.Items.Add(refreshItem);
        printersMenuItem.Menu = printersSubMenu;
        menu.Items.Add(printersMenuItem);

        // Web UI actions when running
        if (state == DesktopServerState.Running && info?.WebUiEnabled == true && info.LocalUrl != null)
        {
            menu.Items.Add(new NativeMenuItemSeparator());

            var openWebUiItem = new NativeMenuItem("Open Web UI");
            openWebUiItem.Click += (_, _) => _webUiPresenter.Open(info.LocalUrl);
            menu.Items.Add(openWebUiItem);

            var primaryLanUrl = info.LanUrls.FirstOrDefault() ?? info.LocalUrl;
            var copyUrlItem = new NativeMenuItem("Copy Network URL");
            copyUrlItem.Click += async (_, _) => await CopyToClipboardAsync(primaryLanUrl);
            menu.Items.Add(copyUrlItem);

            if (info.RequireAuth && !string.IsNullOrEmpty(info.Pin))
            {
                var copyPinItem = new NativeMenuItem("Copy PIN");
                copyPinItem.Click += async (_, _) => await CopyToClipboardAsync(info.Pin);
                menu.Items.Add(copyPinItem);
            }
        }

        menu.Items.Add(new NativeMenuItemSeparator());

        var updateLabel = _updateChecker.LastResult?.Status == UpdateCheckStatus.UpdateAvailable &&
                          !string.IsNullOrEmpty(_updateChecker.LastResult.LatestVersion)
            ? $"Update Available (v{_updateChecker.LastResult.LatestVersion})"
            : "Check for Updates";
        var checkUpdatesItem = new NativeMenuItem(updateLabel);
        checkUpdatesItem.Click += (_, _) => OpenAboutDialog(checkForUpdates: true);
        menu.Items.Add(checkUpdatesItem);

        var aboutItem = new NativeMenuItem("About Printman");
        aboutItem.Click += (_, _) => OpenAboutDialog();
        menu.Items.Add(aboutItem);

        var quitItem = new NativeMenuItem("Quit Printman");
        quitItem.Click += async (_, _) => await RequestQuitAsync();
        menu.Items.Add(quitItem);
    }

    private static string FormatStatusText(DesktopServerState state, ServerRuntimeInfo? info) =>
        state switch
        {
            DesktopServerState.Starting => "Status: Starting...",
            DesktopServerState.Running when info != null => BuildRunningSummary(info),
            DesktopServerState.CupsUnavailable => "Status: CUPS Not Running",
            DesktopServerState.Error => "Status: Error",
            _ => "Status: Stopped"
        };

    private void OpenAboutDialog(bool checkForUpdates = false)
    {
        if (_mainWindow != null)
        {
            _mainWindow.ShowAboutWindow(checkForUpdates);
            return;
        }

        var about = new AboutWindow(_webUiPresenter, _updateChecker, checkForUpdates);
        about.Show();
    }

    private async Task TogglePrinterFromTrayAsync(string printerName, IReadOnlyList<PrinterInfo> printers)
    {
        var current = GetEffectiveSelectedPrinters(printers);
        if (!current.Remove(printerName))
        {
            current.Add(printerName);
        }

        _settings.SelectedPrinters = current.ToList();
        DesktopSettingsStore.Save(_settings);
        _mainWindow?.ReloadSettingsFromExternal();

        if (_serverManager.State == DesktopServerState.Running)
        {
            await _serverManager.RestartAsync(_settings);
        }
        else
        {
            UpdateTrayMenu();
        }
    }

    private HashSet<string> GetEffectiveSelectedPrinters(IReadOnlyList<PrinterInfo> printers)
    {
        var set = new HashSet<string>(_settings.SelectedPrinters, StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0 && printers.FirstOrDefault(p => p.IsDefault) is { } def)
        {
            set.Add(def.Name);
        }
        return set;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (_mainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static string BuildRunningSummary(ServerRuntimeInfo info)
    {
        var parts = new List<string>();
        if (info.SharingEnabled)
        {
            int count = info.SharedPrinters.Count;
            parts.Add(count == 1 ? "1 printer shared" : $"{count} printers shared");
        }
        if (info.WebUiEnabled)
        {
            parts.Add($"Web :{info.WebPort}");
        }
        return parts.Count > 0 ? $"Status: {string.Join(", ", parts)}" : "Status: Running";
    }

    private async Task RequestQuitAsync()
    {
        if (_isQuitting)
        {
            return;
        }
        _isQuitting = true;

        _mainWindow?.MarkQuitting();
        _updateChecker.Dispose();

        try
        {
            await _serverManager.DisposeAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Shutdown error: {ex}");
        }

        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _desktop?.Shutdown(0);
    }
}
