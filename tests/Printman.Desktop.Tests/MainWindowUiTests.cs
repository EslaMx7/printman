using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Printman.Desktop.Controls;
using Printman.Desktop.Services;
using Printman.Desktop.Tests;
using Printman.Desktop.Views;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Printman.Desktop.Tests;

public sealed class TestApp : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true
            });
}

[TestClass]
public sealed class MainWindowUiTests
{
    private static string? _tempConfigDir;

    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(MainWindowUiTests).Assembly);

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _tempConfigDir = Path.Combine(Path.GetTempPath(), $"printman-ui-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempConfigDir);
        DesktopSettingsStore.OverrideConfigDirectory = _tempConfigDir;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DesktopSettingsStore.OverrideConfigDirectory = null;
        if (_tempConfigDir != null && Directory.Exists(_tempConfigDir))
        {
            try { Directory.Delete(_tempConfigDir, recursive: true); } catch { }
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Condition was not met within {timeout.TotalSeconds:0.#}s.");
            }
            Thread.Sleep(15);
        }
    }

    [TestMethod]
    public async Task StartRestartStop_InsideMainWindow_DoesNotFreezeAndUpdatesUi()
    {
        int port1 = GetFreeTcpPort();
        int port2 = GetFreeTcpPort();

        await Session.Dispatch(() =>
        {
            Assert.IsNotNull(SynchronizationContext.Current, "Expected Avalonia UI SynchronizationContext.");

            var serverManager = new DesktopServerManager();
            using var presenter = new WebUiPresenter();
            var settings = new DesktopSettings
            {
                EnableSharing = true,
                ShareAllPrinters = true,
                EnableWebUi = true,
                WebPort = port1,
                RequirePin = true,
                CustomPin = "246810",
                AutoStartServer = false,
                PreferBuiltInWebView = false
            };

            bool quitRequested = false;
            var window = new MainWindow(serverManager, presenter, settings, () =>
            {
                quitRequested = true;
                return Task.CompletedTask;
            });
            window.Show();

            try
            {
                var refreshTask = serverManager.RefreshEnvironmentAsync();
                WaitUntil(() => refreshTask.IsCompleted, TimeSpan.FromSeconds(10));
                refreshTask.GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();

                var primaryBtn = window.FindControl<Button>("PrimaryActionButton")!;
                var statusText = window.FindControl<TextBlock>("StatusBadgeText")!;
                var webPanel = window.FindControl<Border>("WebConnectionPanel")!;
                var idlePanel = window.FindControl<Border>("IdleInfoPanel")!;
                var pinText = window.FindControl<TextBlock>("PinDisplayText")!;
                var openWebBtn = window.FindControl<Button>("OpenWebUiButton")!;
                var openBrowserBtn = window.FindControl<Button>("OpenBrowserButton")!;
                var qrControl = window.FindControl<QrCodeControl>("LanQrCode")!;
                var portBox = window.FindControl<TextBox>("WebPortTextBox")!;
                var shareAllBox = window.FindControl<CheckBox>("ShareAllCheckBox")!;
                var applyRestartBtn = window.FindControl<Button>("ApplyRestartButton")!;

                Assert.AreEqual("Stopped", statusText.Text);
                Assert.IsTrue(idlePanel.IsVisible);
                Assert.IsFalse(webPanel.IsVisible);

                // Simulate clicking "Start Sharing" on the UI thread
                primaryBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

                WaitUntil(() => serverManager.State == DesktopServerState.Running && statusText.Text == "Running", TimeSpan.FromSeconds(10));

                Assert.AreEqual("Running", statusText.Text);
                Assert.AreEqual("Stop Sharing", primaryBtn.Content?.ToString());
                Assert.IsTrue(primaryBtn.IsEnabled);
                Assert.IsFalse(idlePanel.IsVisible);
                Assert.IsTrue(webPanel.IsVisible);
                Assert.AreEqual("PIN: 246810", pinText.Text);
                Assert.IsTrue(openWebBtn.IsEnabled);
                Assert.IsTrue(openBrowserBtn.IsEnabled);
                Assert.IsFalse(string.IsNullOrEmpty(qrControl.Value));

                // Verify QR control measures and arranges cleanly with the generated matrix
                qrControl.Measure(new Size(132, 132));
                qrControl.Arrange(new Rect(0, 0, 132, 132));
                Assert.AreEqual(132, qrControl.Bounds.Width);
                Assert.AreEqual(132, qrControl.Bounds.Height);

                // Confirm live Web UI endpoint responds
                using var http = new HttpClient();
                var authStatusTask = http.GetStringAsync($"http://127.0.0.1:{port1}/api/auth/status?pin=246810");
                WaitUntil(() => authStatusTask.IsCompleted, TimeSpan.FromSeconds(5));
                StringAssert.Contains(authStatusTask.GetAwaiter().GetResult(), "\"authenticated\":true");

                // Modify settings in UI -> Apply & Restart button appears
                shareAllBox.IsChecked = false;
                portBox.Text = port2.ToString();
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(applyRestartBtn.IsVisible);

                applyRestartBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                WaitUntil(
                    () => serverManager.State == DesktopServerState.Running &&
                          serverManager.RuntimeInfo?.WebPort == port2 &&
                          statusText.Text == "Running",
                    TimeSpan.FromSeconds(10));

                var authStatus2Task = http.GetStringAsync($"http://127.0.0.1:{port2}/api/auth/status?pin=246810");
                WaitUntil(() => authStatus2Task.IsCompleted, TimeSpan.FromSeconds(5));
                StringAssert.Contains(authStatus2Task.GetAwaiter().GetResult(), "\"authenticated\":true");

                // Simulate clicking "Stop Sharing"
                primaryBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => serverManager.State == DesktopServerState.Stopped && statusText.Text == "Stopped", TimeSpan.FromSeconds(10));

                Assert.AreEqual("Stopped", statusText.Text);
                Assert.AreEqual("Start Sharing", primaryBtn.Content?.ToString());
                Assert.IsTrue(primaryBtn.IsEnabled);
                Assert.IsTrue(idlePanel.IsVisible);
                Assert.IsFalse(webPanel.IsVisible);
                Assert.IsFalse(openWebBtn.IsEnabled);

                // Closing window hides to tray instead of quitting
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(window.IsVisible);
                Assert.IsFalse(quitRequested);
            }
            finally
            {
                window.MarkQuitting();
                window.Close();
                serverManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task StartSharing_WhenPortIsInUse_ShowsErrorAndRecoversOnRetry()
    {
        using var occupied = new TcpListener(IPAddress.Any, 0);
        occupied.Start();
        int busyPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
        int freePort = GetFreeTcpPort();

        await Session.Dispatch(() =>
        {
            var serverManager = new DesktopServerManager();
            using var presenter = new WebUiPresenter();
            var settings = new DesktopSettings
            {
                EnableSharing = false,
                EnableWebUi = true,
                WebPort = busyPort,
                RequirePin = false,
                AutoStartServer = false
            };

            var window = new MainWindow(serverManager, presenter, settings, () => Task.CompletedTask);
            window.Show();

            try
            {
                var primaryBtn = window.FindControl<Button>("PrimaryActionButton")!;
                var statusText = window.FindControl<TextBlock>("StatusBadgeText")!;
                var alertBanner = window.FindControl<Border>("AlertBanner")!;
                var alertRetryBtn = window.FindControl<Button>("AlertRetryButton")!;
                var portBox = window.FindControl<TextBox>("WebPortTextBox")!;

                primaryBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

                WaitUntil(() => serverManager.State == DesktopServerState.Error && statusText.Text == "Error", TimeSpan.FromSeconds(4));

                Assert.AreEqual("Error", statusText.Text);
                Assert.IsTrue(primaryBtn.IsEnabled);
                Assert.IsTrue(alertBanner.IsVisible);
                Assert.IsTrue(alertRetryBtn.IsVisible);

                // Change to a free port and click Retry in the alert banner
                portBox.Text = freePort.ToString();
                Dispatcher.UIThread.RunJobs();
                alertRetryBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

                WaitUntil(() => serverManager.State == DesktopServerState.Running && statusText.Text == "Running", TimeSpan.FromSeconds(5));
                Assert.AreEqual("Running", statusText.Text, $"State={serverManager.State}, Err={serverManager.ErrorMessage}");
                Assert.IsFalse(alertBanner.IsVisible);
            }
            finally
            {
                window.MarkQuitting();
                window.Close();
                var disposeTask = serverManager.DisposeAsync().AsTask();
                WaitUntil(() => disposeTask.IsCompleted, TimeSpan.FromSeconds(5));
                disposeTask.GetAwaiter().GetResult();
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task StartSharing_WithTooShortPin_IsBlockedAndShowsNotice()
    {
        int port = GetFreeTcpPort();

        await Session.Dispatch(() =>
        {
            var serverManager = new DesktopServerManager();
            using var presenter = new WebUiPresenter();
            var settings = new DesktopSettings
            {
                EnableSharing = false,
                EnableWebUi = true,
                WebPort = port,
                RequirePin = true,
                CustomPin = "12", // too short: contract is 4-8 characters
                AutoStartServer = false
            };

            var window = new MainWindow(serverManager, presenter, settings, () => Task.CompletedTask);
            window.Show();

            try
            {
                var primaryBtn = window.FindControl<Button>("PrimaryActionButton")!;
                var statusText = window.FindControl<TextBlock>("StatusBadgeText")!;
                var alertBanner = window.FindControl<Border>("AlertBanner")!;
                var alertTitle = window.FindControl<TextBlock>("AlertTitleText")!;

                primaryBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.AreEqual("Stopped", statusText.Text, "Server must not start with an invalid PIN.");
                Assert.IsTrue(alertBanner.IsVisible);
                Assert.AreEqual("Notice", alertTitle.Text);
            }
            finally
            {
                window.MarkQuitting();
                window.Close();
                serverManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public void QrCodeGenerator_EncodesUrlsCleanly()
    {
        var matrix = QrCodeGenerator.Encode("http://192.168.1.42:5050/?pin=246810");
        Assert.IsNotNull(matrix);
        Assert.AreEqual(matrix.GetLength(0), matrix.GetLength(1));
        Assert.IsTrue(matrix.GetLength(0) >= 21);

        Assert.IsNull(QrCodeGenerator.Encode(""));
        Assert.IsNull(QrCodeGenerator.Encode(new string('a', 400)));
    }

    [TestMethod]
    public void DesktopSettingsStore_RoundTripsAndHandlesCorruptFile()
    {
        var originalDir = DesktopSettingsStore.OverrideConfigDirectory;
        var tempDir = Path.Combine(Path.GetTempPath(), $"printman-settings-test-{Guid.NewGuid():N}");
        try
        {
            DesktopSettingsStore.OverrideConfigDirectory = tempDir;

            var loadedMissing = DesktopSettingsStore.Load();
            Assert.IsTrue(loadedMissing.EnableSharing);
            Assert.AreEqual(5000, loadedMissing.WebPort);

            var saved = new DesktopSettings
            {
                EnableSharing = true,
                ShareAllPrinters = false,
                SelectedPrinters = ["Printer A", "Printer B"],
                EnableWebUi = true,
                WebPort = 5050,
                RequirePin = true,
                CustomPin = "654321",
                AutoStartServer = true,
                PreferBuiltInWebView = false,
                KeepCache = true
            };
            DesktopSettingsStore.Save(saved);

            var reloaded = DesktopSettingsStore.Load();
            CollectionAssert.AreEqual(new[] { "Printer A", "Printer B" }, reloaded.SelectedPrinters);
            Assert.AreEqual(5050, reloaded.WebPort);
            Assert.AreEqual("654321", reloaded.CustomPin);
            Assert.IsTrue(reloaded.AutoStartServer);
            Assert.IsFalse(reloaded.PreferBuiltInWebView);
            Assert.IsTrue(reloaded.KeepCache);

            File.WriteAllText(DesktopSettingsStore.GetConfigPath(), "{ invalid json");
            var fallback = DesktopSettingsStore.Load();
            Assert.AreEqual(5000, fallback.WebPort);
        }
        finally
        {
            DesktopSettingsStore.OverrideConfigDirectory = originalDir;
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
