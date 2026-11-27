using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Printman.Core.Models;
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

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}

[TestClass]
public sealed class MainWindowUiTests
{
    private static string? _tempConfigDir;
    private static readonly char[] DisallowedUiChars = ['\u2014', '\u2013', '\u2022', '\u00B7', '\u25CF', '\u2713'];

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

    private static void AssertNoDisallowedPunctuation(string? text, string source)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        int idx = text.IndexOfAny(DisallowedUiChars);
        Assert.AreEqual(-1, idx, $"Disallowed punctuation character U+{(idx >= 0 ? ((int)text[idx]).ToString("X4") : "")} found in {source}: \"{text}\"");
    }

    [TestMethod]
    public async Task StartRestartStop_InsideMainWindow_DoesNotFreezeAndUpdatesUi()
    {
        int port1 = GetFreeTcpPort();
        int port2 = GetFreeTcpPort();

        await Session.Dispatch(() =>
        {
            Assert.IsNotNull(SynchronizationContext.Current, "Expected Avalonia UI SynchronizationContext.");

            var openedUrls = new List<string>();
            var serverManager = new DesktopServerManager();
            var presenter = new WebUiPresenter(url =>
            {
                openedUrls.Add(url);
                return true;
            });
            var settings = new DesktopSettings
            {
                EnableSharing = true,
                ShareAllPrinters = true,
                EnableWebUi = true,
                WebPort = port1,
                RequirePin = true,
                CustomPin = "246810",
                AutoStartServer = false
            };

            var window = new MainWindow(serverManager, presenter, settings);
            window.Show();

            try
            {
                Assert.AreEqual(AppMetadata.WindowTitle, window.Title);
                Assert.AreEqual(1060, window.Width);
                Assert.AreEqual(750, window.Height);
                AssertNoDisallowedPunctuation(window.Title, "MainWindow.Title");

                bool hasQuitButton = window.GetLogicalDescendants()
                    .OfType<Button>()
                    .Any(b => string.Equals(b.Content?.ToString(), "Quit Printman", StringComparison.Ordinal));
                Assert.IsFalse(hasQuitButton, "Main window should not have a Quit Printman button.");

                var refreshTask = serverManager.RefreshEnvironmentAsync();
                WaitUntil(() => refreshTask.IsCompleted, TimeSpan.FromSeconds(10));
                refreshTask.GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();

                var primaryBtn = window.FindControl<Button>("PrimaryActionButton")!;
                var statusText = window.FindControl<TextBlock>("StatusBadgeText")!;
                var footerStatusText = window.FindControl<TextBlock>("FooterStatusText")!;
                var footerDomainBtn = window.FindControl<Button>("FooterDomainLinkButton")!;
                var webPanel = window.FindControl<Border>("WebConnectionPanel")!;
                var idlePanel = window.FindControl<Border>("IdleInfoPanel")!;
                var pinText = window.FindControl<TextBlock>("PinDisplayText")!;
                var openWebBtn = window.FindControl<Button>("OpenWebUiButton")!;
                var qrControl = window.FindControl<QrCodeControl>("LanQrCode")!;
                var portBox = window.FindControl<TextBox>("WebPortTextBox")!;
                var shareAllBox = window.FindControl<CheckBox>("ShareAllCheckBox")!;
                var applyRestartBtn = window.FindControl<Button>("ApplyRestartButton")!;

                Assert.AreEqual("Stopped", statusText.Text);
                StringAssert.Contains(footerStatusText.Text, "Stopped");
                AssertNoDisallowedPunctuation(footerStatusText.Text, "FooterStatusText (Stopped)");
                Assert.IsTrue(idlePanel.IsVisible);
                Assert.IsFalse(webPanel.IsVisible);

                // Footer domain link opens official website
                footerDomainBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                CollectionAssert.Contains(openedUrls, "https://printman.eslamx.com/");

                // Simulate clicking "Start Sharing" on the UI thread
                primaryBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

                WaitUntil(() => serverManager.State == DesktopServerState.Running && statusText.Text == "Running", TimeSpan.FromSeconds(10));

                Assert.AreEqual("Running", statusText.Text);
                StringAssert.StartsWith(footerStatusText.Text, "Running:");
                AssertNoDisallowedPunctuation(footerStatusText.Text, "FooterStatusText (Running)");
                Assert.AreEqual("Stop Sharing", primaryBtn.Content?.ToString());
                Assert.IsTrue(primaryBtn.IsEnabled);
                Assert.IsFalse(idlePanel.IsVisible);
                Assert.IsTrue(webPanel.IsVisible);
                Assert.AreEqual("PIN: 246810", pinText.Text);
                Assert.IsTrue(openWebBtn.IsEnabled);
                Assert.IsFalse(string.IsNullOrEmpty(qrControl.Value));

                // Ensure no disallowed punctuation exists in any visible TextBlock/Button in MainWindow
                foreach (var tb in window.GetLogicalDescendants().OfType<TextBlock>())
                {
                    AssertNoDisallowedPunctuation(tb.Text, $"MainWindow.TextBlock({tb.Name})");
                }

                // Open Web UI launches directly in system browser
                openWebBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                CollectionAssert.Contains(openedUrls, serverManager.RuntimeInfo?.LocalUrl);

                // Verify QR control measures and arranges cleanly with the generated matrix
                qrControl.Measure(new Size(104, 104));
                qrControl.Arrange(new Rect(0, 0, 104, 104));
                Assert.AreEqual(104, qrControl.Bounds.Width);
                Assert.AreEqual(104, qrControl.Bounds.Height);

                // Confirm live Web UI endpoint responds
                using var http = new HttpClient();
                var authStatusTask = http.GetStringAsync($"http://127.0.0.1:{port1}/api/auth/status?pin=246810");
                WaitUntil(() => authStatusTask.IsCompleted, TimeSpan.FromSeconds(5));
                StringAssert.Contains(authStatusTask.GetAwaiter().GetResult(), "\"authenticated\":true");

                // Modify settings in UI -> Apply & Restart button appears in header
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
                StringAssert.Contains(footerStatusText.Text, "Stopped");
                Assert.AreEqual("Start Sharing", primaryBtn.Content?.ToString());
                Assert.IsTrue(primaryBtn.IsEnabled);
                Assert.IsTrue(idlePanel.IsVisible);
                Assert.IsFalse(webPanel.IsVisible);
                Assert.IsFalse(openWebBtn.IsEnabled);

                // Closing window hides to tray instead of closing permanently
                window.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(window.IsVisible);
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
            var presenter = new WebUiPresenter();
            var settings = new DesktopSettings
            {
                EnableSharing = false,
                EnableWebUi = true,
                WebPort = busyPort,
                RequirePin = false,
                AutoStartServer = false
            };

            var window = new MainWindow(serverManager, presenter, settings);
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
            var presenter = new WebUiPresenter();
            var settings = new DesktopSettings
            {
                EnableSharing = false,
                EnableWebUi = true,
                WebPort = port,
                RequirePin = true,
                CustomPin = "12", // too short: contract is 4-8 characters
                AutoStartServer = false
            };

            var window = new MainWindow(serverManager, presenter, settings);
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
    public async Task SharedPrintersPanel_RendersMultiplePrintersCompactlyAndSwitchesHost()
    {
        if (OperatingSystem.IsWindows())
        {
            var exePath = Path.ChangeExtension(typeof(Printman.Desktop.Program).Assembly.Location, ".exe");
            Assert.IsTrue(File.Exists(exePath), $"Expected desktop executable at {exePath}");
            var exeText = Encoding.ASCII.GetString(File.ReadAllBytes(exePath));
            StringAssert.Contains(
                exeText,
                "8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a",
                "printman-desktop.exe must embed app.manifest with Windows 10/11 supportedOS GUID.");
        }

        await Session.Dispatch(() =>
        {
            var serverManager = new DesktopServerManager();
            var presenter = new WebUiPresenter();
            var settings = new DesktopSettings
            {
                EnableSharing = true,
                EnableWebUi = true,
                WebPort = 5000,
                RequirePin = false,
                AutoStartServer = false
            };

            var window = new MainWindow(serverManager, presenter, settings);
            window.Show();

            try
            {
                var enableSharingBox = window.FindControl<CheckBox>("EnableSharingCheckBox")!;
                var shareAllBox = window.FindControl<CheckBox>("ShareAllCheckBox")!;
                var printersListPanel = window.FindControl<StackPanel>("PrintersListPanel")!;

                enableSharingBox.IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(shareAllBox.IsEnabled);
                Assert.IsFalse(printersListPanel.IsEnabled);

                enableSharingBox.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(shareAllBox.IsEnabled);
                Assert.IsTrue(printersListPanel.IsEnabled);

                var runtimeInfo = new ServerRuntimeInfo
                {
                    WebUiEnabled = true,
                    WebPort = 5000,
                    RequireAuth = false,
                    LocalUrl = "http://localhost:5000/",
                    LanUrls = ["http://192.168.1.25:5000/"],
                    LanIps = ["192.168.1.25"],
                    SharingEnabled = true,
                    IppPort = 631,
                    MdnsActive = true,
                    MdnsHostName = "desk-pc.local",
                    SharedPrinters =
                    [
                        new SharedPrinterEndpoint
                        {
                            SystemName = "Brother HL-L2320D",
                            DisplayName = "Printman - Brother HL-L2320D",
                            ResourcePath = "ipp/print/brother-hl-l2320d",
                            IppUrls =
                            [
                                "ipp://desk-pc.local:631/ipp/print/brother-hl-l2320d",
                                "ipp://192.168.1.25:631/ipp/print/brother-hl-l2320d"
                            ]
                        },
                        new SharedPrinterEndpoint
                        {
                            SystemName = "HP LaserJet Pro",
                            DisplayName = "Printman - HP LaserJet Pro",
                            ResourcePath = "ipp/print/hp-laserjet-pro",
                            IppUrls =
                            [
                                "ipp://desk-pc.local:631/ipp/print/hp-laserjet-pro",
                                "ipp://192.168.1.25:631/ipp/print/hp-laserjet-pro"
                            ]
                        },
                        new SharedPrinterEndpoint
                        {
                            SystemName = "Canon PIXMA",
                            DisplayName = "Printman - Canon PIXMA",
                            ResourcePath = "ipp/print/canon-pixma",
                            IppUrls =
                            [
                                "ipp://desk-pc.local:631/ipp/print/canon-pixma",
                                "ipp://192.168.1.25:631/ipp/print/canon-pixma"
                            ]
                        },
                        new SharedPrinterEndpoint
                        {
                            SystemName = "Microsoft Print to PDF",
                            DisplayName = "Printman - Microsoft Print to PDF",
                            ResourcePath = "ipp/print/microsoft-print-to-pdf",
                            IppUrls =
                            [
                                "ipp://desk-pc.local:631/ipp/print/microsoft-print-to-pdf",
                                "ipp://192.168.1.25:631/ipp/print/microsoft-print-to-pdf"
                            ]
                        }
                    ]
                };

                window.PreviewConnectionInfo(runtimeInfo);
                Dispatcher.UIThread.RunJobs();

                var sharedPanel = window.FindControl<Border>("SharedPrintersPanel")!;
                var countText = window.FindControl<TextBlock>("SharedPrintersCountText")!;
                var headerText = window.FindControl<TextBlock>("SharedPrintersHeaderText")!;
                var linksPanel = window.FindControl<StackPanel>("SharedPrintersLinksPanel")!;
                var hostPanel = window.FindControl<StackPanel>("IppHostSelectorPanel")!;
                var hostSelector = window.FindControl<ComboBox>("IppHostSelector")!;
                var singleLanUrlBorder = window.FindControl<Border>("SingleLanUrlBorder")!;
                var lanUrlSelector = window.FindControl<ComboBox>("LanUrlSelector")!;

                Assert.IsTrue(sharedPanel.IsVisible);
                Assert.AreEqual("4 printers", countText.Text);
                AssertNoDisallowedPunctuation(headerText.Text, "SharedPrintersHeaderText");
                Assert.AreEqual(4, linksPanel.Children.Count);
                Assert.IsTrue(hostPanel.IsVisible);
                Assert.IsTrue(singleLanUrlBorder.IsVisible);
                Assert.IsFalse(lanUrlSelector.IsVisible);

                var initialUrls = linksPanel.GetLogicalDescendants().OfType<SelectableTextBlock>().Select(b => b.Text).ToList();
                Assert.AreEqual(4, initialUrls.Count);
                foreach (var u in initialUrls)
                {
                    StringAssert.Contains(u, "desk-pc.local:631");
                }

                hostSelector.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                var switchedUrls = linksPanel.GetLogicalDescendants().OfType<SelectableTextBlock>().Select(b => b.Text).ToList();
                Assert.AreEqual(4, switchedUrls.Count);
                foreach (var u in switchedUrls)
                {
                    StringAssert.Contains(u, "192.168.1.25:631");
                    Assert.IsFalse(u!.Contains("desk-pc.local:631", StringComparison.Ordinal));
                }
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
    public void DynamicVersion_IsResolvedFromRepoAndNotStaticDefault()
    {
        Assert.AreNotEqual("1.0.0", AppMetadata.Version, "App version must be resolved dynamically rather than staying at 1.0.0.");
        Assert.IsTrue(UpdateChecker.TryParseSemVer(AppMetadata.Version, out var parsed), $"Expected valid SemVer, got '{AppMetadata.Version}'.");
        Assert.IsTrue(parsed.Major >= 2, $"Expected resolved version >= 2.0.0, got '{AppMetadata.Version}'.");
        AssertNoDisallowedPunctuation(AppMetadata.WindowTitle, "AppMetadata.WindowTitle");
        AssertNoDisallowedPunctuation(AppMetadata.Tagline, "AppMetadata.Tagline");
    }

    [TestMethod]
    public async Task UpdateChecker_EvaluatesLatestReleaseAndErrorsProperly()
    {
        const string releaseJson = """
            {
              "tag_name": "v2.10.0",
              "html_url": "https://github.com/EslaMx7/printman/releases/tag/v2.10.0",
              "assets": [
                { "name": "printman-desktop-v2.10.0-win-x64.exe", "browser_download_url": "https://github.com/EslaMx7/printman/releases/download/v2.10.0/printman-desktop-v2.10.0-win-x64.exe" },
                { "name": "printman-desktop-v2.10.0-win-arm64.exe", "browser_download_url": "https://github.com/EslaMx7/printman/releases/download/v2.10.0/printman-desktop-v2.10.0-win-arm64.exe" },
                { "name": "printman-v2.10.0-osx-arm64.dmg", "browser_download_url": "https://github.com/EslaMx7/printman/releases/download/v2.10.0/printman-v2.10.0-osx-arm64.dmg" },
                { "name": "printman-v2.10.0-osx-x64.dmg", "browser_download_url": "https://github.com/EslaMx7/printman/releases/download/v2.10.0/printman-v2.10.0-osx-x64.dmg" },
                { "name": "printman-desktop-v2.10.0-linux-x64", "browser_download_url": "https://github.com/EslaMx7/printman/releases/download/v2.10.0/printman-desktop-v2.10.0-linux-x64" },
                { "name": "printman-desktop-v2.10.0-linux-arm64", "browser_download_url": "https://github.com/EslaMx7/printman/releases/download/v2.10.0/printman-desktop-v2.10.0-linux-arm64" }
              ]
            }
            """;

        using var updateChecker = new UpdateChecker(
            new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
            }),
            () => "2.2.0");

        var updateResult = await updateChecker.CheckForUpdatesAsync();
        Assert.AreEqual(UpdateCheckStatus.UpdateAvailable, updateResult.Status);
        Assert.AreEqual("2.10.0", updateResult.LatestVersion);
        Assert.IsNotNull(updateResult.DownloadUrl);
        StringAssert.Contains(updateResult.Message, "v2.10.0");
        AssertNoDisallowedPunctuation(updateResult.Message, "UpdateChecker.Message");

        using var upToDateChecker = new UpdateChecker(
            new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v2.10.0","html_url":"https://github.com/EslaMx7/printman/releases/tag/v2.10.0"}""", Encoding.UTF8, "application/json")
            }),
            () => "2.10.0");

        var upToDateResult = await upToDateChecker.CheckForUpdatesAsync();
        Assert.AreEqual(UpdateCheckStatus.UpToDate, upToDateResult.Status);
        StringAssert.Contains(upToDateResult.Message, "up to date");

        using var errorChecker = new UpdateChecker(
            new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)),
            () => "2.1.1");

        var errorResult = await errorChecker.CheckForUpdatesAsync();
        Assert.AreEqual(UpdateCheckStatus.Error, errorResult.Status);
        StringAssert.Contains(errorResult.Message, "rate limit");
    }

    [TestMethod]
    public async Task AboutWindow_DisplaysCreditsLinksSupportEmailAndChecksUpdates()
    {
        await Session.Dispatch(() =>
        {
            var opened = new List<string>();
            var presenter = new WebUiPresenter(url =>
            {
                opened.Add(url);
                return true;
            });

            using var updateChecker = new UpdateChecker(
                new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"tag_name":"v9.9.0","html_url":"https://github.com/EslaMx7/printman/releases/tag/v9.9.0"}""",
                        Encoding.UTF8,
                        "application/json")
                }),
                () => "2.1.1");

            var serverManager = new DesktopServerManager();
            var mainWindow = new MainWindow(serverManager, presenter, new DesktopSettings(), updateChecker);
            var about = new AboutWindow(presenter, updateChecker);
            mainWindow.Show();
            about.Show(mainWindow);

            try
            {
                var allText = about.GetLogicalDescendants()
                    .OfType<TextBlock>()
                    .Select(t => t.Text ?? "")
                    .Concat(about.GetLogicalDescendants().OfType<Button>().Select(b => b.Content?.ToString() ?? ""))
                    .ToList();

                foreach (var text in allText)
                {
                    AssertNoDisallowedPunctuation(text, "AboutWindow");
                }

                Assert.IsTrue(allText.Any(t => t.Contains("Eslam Hamouda", StringComparison.Ordinal)));
                Assert.IsTrue(allText.Any(t => t.Contains("printman.eslamx.com", StringComparison.Ordinal)));
                Assert.IsTrue(allText.Any(t => t.Contains("github.com/EslaMx7/printman", StringComparison.Ordinal)));
                Assert.IsTrue(allText.Any(t => t.Contains("support@eslamx.com", StringComparison.Ordinal)));
                Assert.AreEqual($"v{AppMetadata.Version}", about.FindControl<TextBlock>("VersionText")!.Text);

                var checkUpdatesBtn = about.FindControl<Button>("CheckUpdatesButton")!;
                var openUpdateBtn = about.FindControl<Button>("OpenUpdateButton")!;
                var updateStatusText = about.FindControl<TextBlock>("UpdateStatusText")!;
                var footerUpdateBtn = mainWindow.FindControl<Button>("FooterUpdateButton")!;

                checkUpdatesBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => openUpdateBtn.IsVisible && footerUpdateBtn.IsVisible, TimeSpan.FromSeconds(5));

                StringAssert.Contains(updateStatusText.Text, "v9.9.0");
                StringAssert.Contains(footerUpdateBtn.Content?.ToString(), "v9.9.0");

                openUpdateBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                about.FindControl<Button>("WebsiteLinkButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                about.FindControl<Button>("GitHubLinkButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                about.FindControl<Button>("AuthorLinkButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                about.FindControl<Button>("SponsorLinkButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                about.FindControl<Button>("SupportEmailButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                CollectionAssert.AreEqual(
                    new[]
                    {
                        "https://github.com/EslaMx7/printman/releases/tag/v9.9.0",
                        "https://printman.eslamx.com/",
                        "https://github.com/EslaMx7/printman",
                        "https://eslamx.com/",
                        "https://buymeacoffee.com/eslamx7",
                        "mailto:support@eslamx.com"
                    },
                    opened);
            }
            finally
            {
                about.Close();
                mainWindow.MarkQuitting();
                mainWindow.Close();
                serverManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task TrayMenu_UsesOpenPrintman_HidesPinValue_AndIncludesAbout()
    {
        await Session.Dispatch(() =>
        {
            var app = new App();
            var menu = new NativeMenu();
            var info = new ServerRuntimeInfo
            {
                WebUiEnabled = true,
                WebPort = 5000,
                RequireAuth = true,
                Pin = "987654",
                LocalUrl = "http://localhost:5000/?pin=987654",
                LanUrls = ["http://192.168.1.25:5000/?pin=987654"],
                SharingEnabled = true,
                IppPort = 631
            };

            app.PopulateTrayMenu(menu, DesktopServerState.Running, info, []);

            var headers = menu.Items
                .OfType<NativeMenuItem>()
                .Select(i => i.Header ?? "")
                .ToList();

            foreach (var h in headers)
            {
                AssertNoDisallowedPunctuation(h, "TrayMenu");
            }

            CollectionAssert.Contains(headers, "Open Printman");
            CollectionAssert.DoesNotContain(headers, "Open Setup & Status");
            CollectionAssert.Contains(headers, "Copy PIN");
            CollectionAssert.Contains(headers, "Check for Updates");
            CollectionAssert.Contains(headers, "About Printman");
            CollectionAssert.Contains(headers, "Quit Printman");
            Assert.IsFalse(headers.Any(h => h.Contains("987654", StringComparison.Ordinal)), "Tray menu must never display the PIN value.");
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
                KeepCache = true
            };
            DesktopSettingsStore.Save(saved);

            var reloaded = DesktopSettingsStore.Load();
            CollectionAssert.AreEqual(new[] { "Printer A", "Printer B" }, reloaded.SelectedPrinters);
            Assert.AreEqual(5050, reloaded.WebPort);
            Assert.AreEqual("654321", reloaded.CustomPin);
            Assert.IsTrue(reloaded.AutoStartServer);
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
