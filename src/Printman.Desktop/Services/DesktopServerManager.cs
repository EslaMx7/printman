using Microsoft.Extensions.DependencyInjection;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Server;
#if !WINDOWS
using Printman.Services.Cups;
#endif

namespace Printman.Desktop.Services;

public enum DesktopServerState
{
    Stopped,
    Starting,
    Running,
    CupsUnavailable,
    Error
}

public sealed class DesktopServerManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly List<PrintEvent> _recentEvents = [];
    private readonly object _eventLock = new();

    private ServiceProvider? _activeProvider;
    private RunningWebServer? _runningServer;
    private CancellationTokenSource? _eventCts;
    private Task? _eventTask;
    private volatile DesktopServerState _state = DesktopServerState.Stopped;

    public DesktopServerState State => _state;
    public ServerRuntimeInfo? RuntimeInfo { get; private set; }
    public IReadOnlyList<PrinterInfo> InstalledPrinters { get; private set; } = [];
    public IReadOnlyList<string> CupsDiagnosticLines { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public IReadOnlyList<PrintEvent> RecentEvents
    {
        get
        {
            lock (_eventLock)
            {
                return [.. _recentEvents];
            }
        }
    }

    public event Action? StateChanged;
    public event Action<PrintEvent>? EventReceived;

    public async Task RefreshEnvironmentAsync(CancellationToken ct = default)
    {
        await Task.Run(async () =>
        {
            try
            {
                var services = new ServiceCollection();
                Printman.Program.ConfigureServices(services);
                using var provider = services.BuildServiceProvider();

#if !WINDOWS
                var cups = provider.GetRequiredService<CupsClient>();
                var tools = provider.GetRequiredService<IExternalTools>();
                var problem = await cups.ProbeAsync(ct);
                if (problem != null)
                {
                    CupsDiagnosticLines = CupsReadiness.Explain(
                        problem,
                        cups.Endpoint,
                        OperatingSystem.IsMacOS(),
                        tools.Find("cupsd") != null,
                        CupsReadiness.DetectInstallCommand(tools));
                    InstalledPrinters = [];
                    if (_state != DesktopServerState.Running)
                    {
                        ErrorMessage = problem;
                        _state = DesktopServerState.CupsUnavailable;
                    }
                    StateChanged?.Invoke();
                    return;
                }
                CupsDiagnosticLines = [];
                if (_state == DesktopServerState.CupsUnavailable)
                {
                    ErrorMessage = null;
                    _state = DesktopServerState.Stopped;
                }
#endif
                var discovery = provider.GetRequiredService<IPrinterDiscoveryService>();
                InstalledPrinters = discovery.GetPrinters();
                StateChanged?.Invoke();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation during shutdown is expected.
            }
            catch (Exception ex)
            {
                // Discovery / CUPS probe failures must not crash the UI via async void handlers
                // (Refresh is also fired-and-forgotten from App.InitializeServerAsync).
                ErrorMessage = ex.Message;
                if (_state != DesktopServerState.Running)
                {
                    _state = DesktopServerState.Error;
                }
                StateChanged?.Invoke();
            }
        }, ct);
    }

    public Task StartAsync(DesktopSettings settings, CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await StopInternalAsync().ConfigureAwait(false);

                ErrorMessage = null;
                RuntimeInfo = null;
                _state = DesktopServerState.Starting;
                StateChanged?.Invoke();

                var services = new ServiceCollection();
                Printman.Program.ConfigureServices(services);
                var provider = services.BuildServiceProvider();

                try
                {
#if !WINDOWS
                    var cups = provider.GetRequiredService<CupsClient>();
                    var tools = provider.GetRequiredService<IExternalTools>();
                    var problem = await cups.ProbeAsync(ct).ConfigureAwait(false);
                    if (problem != null)
                    {
                        CupsDiagnosticLines = CupsReadiness.Explain(
                            problem,
                            cups.Endpoint,
                            OperatingSystem.IsMacOS(),
                            tools.Find("cupsd") != null,
                            CupsReadiness.DetectInstallCommand(tools));
                        InstalledPrinters = [];
                        ErrorMessage = problem;
                        _state = DesktopServerState.CupsUnavailable;
                        await provider.DisposeAsync().ConfigureAwait(false);
                        StateChanged?.Invoke();
                        return;
                    }
                    CupsDiagnosticLines = [];
#endif

                    var discovery = provider.GetRequiredService<IPrinterDiscoveryService>();
                    var printers = discovery.GetPrinters();
                    InstalledPrinters = printers;

                    var selectedNames = BuildPrinterSelection(settings, printers);
                    if (settings.EnableSharing && selectedNames.Count == 0 && printers.Count == 0 && !settings.EnableWebUi)
                    {
                        ErrorMessage = "No printers are installed and the Web UI is disabled.";
                        _state = DesktopServerState.Error;
                        await provider.DisposeAsync().ConfigureAwait(false);
                        StateChanged?.Invoke();
                        return;
                    }

                    int webPort = settings.WebPort > 0 ? settings.WebPort : 5000;
                    var serverOptions = new ServerOptions
                    {
                        Port = webPort,
                        BindAddress = "0.0.0.0",
                        Pin = settings.RequirePin && !string.IsNullOrWhiteSpace(settings.CustomPin)
                            ? settings.CustomPin.Trim()
                            : null,
                        RequireAuth = settings.RequirePin,
                        KeepCache = settings.KeepCache,
                        EnableWebUi = settings.EnableWebUi,
                        Share = new ShareOptions
                        {
                            Enabled = settings.EnableSharing && (printers.Count > 0 || selectedNames.Count > 0),
                            Printers = selectedNames
                        }
                    };

                    var host = provider.GetRequiredService<PrintingWebServerHost>();
                    var result = await host.StartAsync(serverOptions, emitConsoleOutput: false, ct).ConfigureAwait(false);
                    if (!result.Success || result.Server == null)
                    {
                        ErrorMessage = FormatStartupError(result.ErrorMessage, webPort);
                        _state = DesktopServerState.Error;
                        await provider.DisposeAsync().ConfigureAwait(false);
                        StateChanged?.Invoke();
                        return;
                    }

                    _activeProvider = provider;
                    _runningServer = result.Server;
                    RuntimeInfo = result.Server.Info;

                    var eventHub = provider.GetRequiredService<IPrintEventHub>();
                    StartEventPump(eventHub);

                    _state = DesktopServerState.Running;
                    StateChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    ErrorMessage = FormatStartupError(ex.Message, settings.WebPort > 0 ? settings.WebPort : 5000);
                    _state = DesktopServerState.Error;
                    await provider.DisposeAsync().ConfigureAwait(false);
                    StateChanged?.Invoke();
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }, ct);
    }

    private static string FormatStartupError(string? message, int webPort)
    {
        var baseMessage = string.IsNullOrWhiteSpace(message) ? "Failed to start server." : message;
        if (OperatingSystem.IsMacOS() &&
            webPort == 5000 &&
            baseMessage.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseMessage} (On macOS, Control Center's AirPlay Receiver uses port 5000 by default - try setting Port to 5050 and retrying.)";
        }
        return baseMessage;
    }

    public Task StopAsync()
    {
        return Task.Run(async () =>
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopInternalAsync().ConfigureAwait(false);
                ErrorMessage = null;
                if (_state != DesktopServerState.CupsUnavailable)
                {
                    _state = DesktopServerState.Stopped;
                }
                StateChanged?.Invoke();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        });
    }

    public Task RestartAsync(DesktopSettings settings, CancellationToken ct = default) =>
        StartAsync(settings, ct);

    private async Task StopInternalAsync()
    {
        if (_eventCts != null)
        {
            _eventCts.Cancel();
            if (_eventTask != null)
            {
                try { await _eventTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { }
            }
            _eventCts.Dispose();
            _eventCts = null;
            _eventTask = null;
        }

        if (_runningServer != null)
        {
            await _runningServer.StopAsync().ConfigureAwait(false);
            _runningServer = null;
        }

        RuntimeInfo = null;

        if (_activeProvider != null)
        {
            await _activeProvider.DisposeAsync().ConfigureAwait(false);
            _activeProvider = null;
        }
    }

    private void StartEventPump(IPrintEventHub eventHub)
    {
        _eventCts = new CancellationTokenSource();
        var token = _eventCts.Token;
        var reader = eventHub.Subscribe();

        _eventTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in reader.ReadAllAsync(token))
                {
                    lock (_eventLock)
                    {
                        _recentEvents.Add(evt);
                        if (_recentEvents.Count > 50)
                        {
                            _recentEvents.RemoveAt(0);
                        }
                    }
                    EventReceived?.Invoke(evt);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                eventHub.Unsubscribe(reader);
            }
        }, token);
    }

    private static IReadOnlyList<string> BuildPrinterSelection(
        DesktopSettings settings,
        IReadOnlyList<PrinterInfo> installed)
    {
        if (!settings.EnableSharing)
        {
            return [];
        }

        if (settings.ShareAllPrinters)
        {
            return installed.Select(p => p.Name).ToList();
        }

        var installedNames = installed.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validSelected = settings.SelectedPrinters.Where(installedNames.Contains).ToList();
        if (validSelected.Count > 0)
        {
            return validSelected;
        }

        // Empty list instructs SharedPrinterRegistry to share the system default printer
        return [];
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(Task.Run(async () =>
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopInternalAsync().ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
                _lifecycleLock.Dispose();
            }
        }));
    }
}
