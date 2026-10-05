using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Printman.Core.Abstractions;
using Printman.Core.Models;
using Printman.Interactive;

namespace Printman.Server;

public class PrintingWebServerHost(
    IPrinterDiscoveryService printerDiscovery,
    IDocumentRendererResolver rendererResolver,
    IFileCacheService fileCache,
    IPrintEventHub eventHub,
    IPrintQueueService queueService,
    IPrintJobPipeline pipeline,
    ISharedPrinterRegistry sharedPrinters,
    IIppRequestHandler ippHandler,
    IppServerSettings ippSettings,
    IDnsSdServiceFactory dnsSdServices,
    IServiceAdvertiser advertiser,
    IFirewallInspector firewall)
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;
    private readonly IFileCacheService _fileCache = fileCache;
    private readonly IPrintEventHub _eventHub = eventHub;
    private readonly IPrintQueueService _queueService = queueService;
    private readonly IPrintJobPipeline _pipeline = pipeline;
    private readonly ISharedPrinterRegistry _sharedPrinters = sharedPrinters;
    private readonly IIppRequestHandler _ippHandler = ippHandler;
    private readonly IppServerSettings _ippSettings = ippSettings;
    private readonly IDnsSdServiceFactory _dnsSdServices = dnsSdServices;
    private readonly IServiceAdvertiser _advertiser = advertiser;
    private readonly IFirewallInspector _firewall = firewall;

    public Task<int> RunAsync(int port, string bindAddress, CancellationToken ct) =>
        RunAsync(new ServerOptions { Port = port, BindAddress = bindAddress }, ct);

    public Task<int> RunAsync(
        int port = 5000,
        string bindAddress = "0.0.0.0",
        string? pin = null,
        bool requireAuth = true,
        int maxUploadMb = 50,
        int cacheLimitMb = 500,
        CancellationToken ct = default) =>
        RunAsync(new ServerOptions
        {
            Port = port,
            BindAddress = bindAddress,
            Pin = pin,
            RequireAuth = requireAuth,
            MaxUploadMb = maxUploadMb,
            CacheLimitMb = cacheLimitMb
        }, ct);

    public async Task<int> RunAsync(ServerOptions options, CancellationToken ct = default)
    {
        int port = options.Port;
        string bindAddress = options.BindAddress;
        string? pin = options.Pin;
        // Without the web UI ("printman share") only the IPP endpoint is served, so there is nothing to protect with a PIN
        bool webUi = options.EnableWebUi;
        bool requireAuth = options.RequireAuth && webUi;
        int maxUploadMb = options.MaxUploadMb;

        // 1. Configure storage bounds and quotas (sec-02)
        _fileCache.MaxFileSizeBytes = maxUploadMb * 1024L * 1024L;
        _fileCache.MaxCacheSizeBytes = options.CacheLimitMb * 1024L * 1024L;
        _pipeline.OutputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? null
            : Path.GetFullPath(options.OutputDirectory);

        // 2. Resolve pairing PIN (sec-01)
        if (requireAuth && string.IsNullOrWhiteSpace(pin))
        {
            pin = RandomNumberGenerator.GetString("0123456789", 6);
        }

        var activeSessions = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        var lanIps = GetLocalLanIpv4Addresses();

        // 3. Network printer sharing (IPP Everywhere / AirPrint / Mopria) - opt-in
        var share = options.Share;
        bool sharing = false;
        int ippPort = share.IppPort;
        var shareWarnings = new List<string>();
        if (share.Enabled)
        {
            foreach (var missing in _sharedPrinters.Configure(share.Printers))
            {
                shareWarnings.Add($"Printer '{missing}' was not found and will not be shared.");
            }

            if (_sharedPrinters.Printers.Count == 0)
            {
                shareWarnings.Add("No printer could be shared (is a default printer set?). Network printing is disabled.");
            }
            else
            {
                sharing = true;
                if (ippPort != port && !IsTcpPortAvailable(bindAddress, ippPort))
                {
                    shareWarnings.Add($"IPP port {ippPort} is already in use; serving network printing on port {port} instead.");
                    ippPort = port;
                }

                _ippSettings.WebUiEnabled = webUi;
                _ippSettings.WebPort = port;
                _ippSettings.IppPort = ippPort;
                _ippSettings.MaxJobBytes = Math.Min(share.MaxJobMb, options.CacheLimitMb) * 1024L * 1024L;
                _ippSettings.StartedAt = DateTime.UtcNow;
            }
        }
        bool separateIppPort = sharing && ippPort != port;

        if (!webUi && !sharing)
        {
            foreach (var warning in shareWarnings)
            {
                ConsoleUi.PrintWarning(warning);
            }
            ConsoleUi.PrintError("Nothing to share. Run 'printman list' to see the installed printers.");
            return 1;
        }

        var builder = WebApplication.CreateBuilder();

        // Quiet logging
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);

        // Fast shutdown timeout (prevents hanging on open connections)
        builder.Services.Configure<HostOptions>(hostOptions =>
        {
            hostOptions.ShutdownTimeout = TimeSpan.FromSeconds(1);
        });

        // Kestrel request limits (sec-02); IPP requests raise their own limit per request
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (webUi)
            {
                kestrel.Listen(IPAddress.Parse(bindAddress), port);
            }
            if (separateIppPort || !webUi)
            {
                kestrel.Listen(IPAddress.Parse(bindAddress), ippPort);
            }
            kestrel.Limits.MaxRequestBodySize = maxUploadMb * 1024L * 1024L;
        });

        var app = builder.Build();

        // 3. Serialized Print Queue (sec-06)
        using var queueCts = CancellationTokenSource.CreateLinkedTokenSource(ct, app.Lifetime.ApplicationStopping);
        _ = Task.Run(() => _pipeline.RunAsync(queueCts.Token), ct);

        // 4. Background Spooler Queue Observer (feeds the web UI's live queue)
        _ = Task.Run(async () =>
        {
            int lastJobCount = -1;
            string lastStatusSummary = "";
            while (webUi && !ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1500, ct);

                    var jobs = _queueService.GetJobs();
                    int currentCount = jobs.Count;
                    string statusSummary = currentCount > 0
                        ? string.Join(";", jobs.Select(j => $"{j.JobId}:{j.StatusCode}:{j.PagesPrinted}"))
                        : "0";

                    if (currentCount != lastJobCount || statusSummary != lastStatusSummary)
                    {
                        lastJobCount = currentCount;
                        lastStatusSummary = statusSummary;
                        _eventHub.Publish(new PrintEvent
                        {
                            Type = "queue_updated",
                            Message = $"Queue updated ({currentCount} active job(s))."
                        });
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, ct);

        // Authentication and CSRF helper checks (sec-01, sec-04)
        bool IsRequestAuthenticated(HttpRequest req)
        {
            if (!requireAuth) return true;

            // Check header PIN
            if (req.Headers.TryGetValue("X-Printer-Pin", out var hPin) &&
                string.Equals(hPin.ToString().Trim(), pin, StringComparison.Ordinal))
            {
                return true;
            }

            // Check header auth token
            if (req.Headers.TryGetValue("X-Printer-Auth", out var hToken) &&
                activeSessions.ContainsKey(hToken.ToString().Trim()))
            {
                return true;
            }

            // Check cookie
            if (req.Cookies.TryGetValue("printman_auth", out var cToken) &&
                activeSessions.ContainsKey(cToken))
            {
                return true;
            }

            // Check query param PIN (convenience for EventSource / direct links)
            if (req.Query.TryGetValue("pin", out var qPin) &&
                string.Equals(qPin.ToString().Trim(), pin, StringComparison.Ordinal))
            {
                return true;
            }

            // Check query param token
            if (req.Query.TryGetValue("token", out var qToken) &&
                activeSessions.ContainsKey(qToken.ToString().Trim()))
            {
                return true;
            }

            return false;
        }

        bool IsSafeOrigin(HttpRequest req)
        {
            // Custom header check: Browsers prevent cross-origin forms from setting custom headers
            if (req.Headers.ContainsKey("X-Requested-With") ||
                req.Headers.ContainsKey("X-Printer-Pin") ||
                req.Headers.ContainsKey("X-Printer-Auth"))
            {
                return true;
            }

            // Origin verification against known local hosts
            if (req.Headers.TryGetValue("Origin", out var originVal) && !string.IsNullOrWhiteSpace(originVal))
            {
                if (Uri.TryCreate(originVal.ToString(), UriKind.Absolute, out var originUri))
                {
                    if (originUri.IsLoopback ||
                        lanIps.Contains(originUri.Host, StringComparer.OrdinalIgnoreCase) ||
                        originUri.Host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    return false;
                }
            }

            return true;
        }

        // Port separation: the IPP port only serves /ipp/*, the web port never does.
        // Without the web UI the only listener is the IPP port, so everything outside /ipp/* is a 404.
        if (separateIppPort || !webUi)
        {
            app.Use(async (ctx, next) =>
            {
                bool isIppPath = ctx.Request.Path.StartsWithSegments("/ipp", StringComparison.OrdinalIgnoreCase);
                bool onIppPort = !webUi || ctx.Connection.LocalPort == ippPort;
                if (isIppPath != onIppPort)
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await next();
            });
        }

        // Security Middleware (sec-01, sec-04); IPP lives outside /api/ and is LAN-filtered in IppEndpoints
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                // CSRF verification on state-changing requests (sec-04)
                if (HttpMethods.IsPost(ctx.Request.Method) ||
                    HttpMethods.IsPut(ctx.Request.Method) ||
                    HttpMethods.IsDelete(ctx.Request.Method))
                {
                    if (!IsSafeOrigin(ctx.Request))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await ctx.Response.WriteAsJsonAsync(new { error = "Cross-origin request rejected." });
                        return;
                    }
                }

                // Auth status and verification endpoints are accessible without pre-auth
                if (path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                // Enforce authentication on all other /api/* endpoints (sec-01)
                if (!IsRequestAuthenticated(ctx.Request))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new { error = "Authentication required. Please enter server PIN." });
                    return;
                }
            }

            await next();
        });

        // Network printer endpoint (no PIN: native print dialogs cannot send one)
        if (sharing)
        {
            IppEndpoints.Map(app, _ippHandler, _ippSettings, share.AllowAnySource);
        }

        // 1. Root SPA & Favicon
        app.MapGet("/", async ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(WebAssets.IndexHtml);
        });

        app.MapGet("/favicon.svg", async ctx =>
        {
            ctx.Response.ContentType = "image/svg+xml";
            await ctx.Response.WriteAsync(WebAssets.FaviconSvg);
        });

        app.MapGet("/favicon.ico", async ctx =>
        {
            ctx.Response.ContentType = "image/svg+xml";
            await ctx.Response.WriteAsync(WebAssets.FaviconSvg);
        });

        // 2. Auth Endpoints (sec-01)
        app.MapGet("/api/auth/status", (HttpRequest req) =>
        {
            bool isAuthed = IsRequestAuthenticated(req);
            return Results.Ok(new { required = requireAuth, authenticated = isAuthed });
        });

        app.MapPost("/api/auth/verify", (PinVerifyRequest req, HttpResponse res) =>
        {
            if (!requireAuth)
            {
                return Results.Ok(new { success = true, token = "none" });
            }

            if (!string.IsNullOrWhiteSpace(req.Pin) && string.Equals(req.Pin.Trim(), pin, StringComparison.Ordinal))
            {
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                activeSessions[token] = DateTime.UtcNow.AddDays(7);

                res.Cookies.Append("printman_auth", token, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddDays(7)
                });

                return Results.Ok(new { success = true, token });
            }

            return Results.BadRequest(new { success = false, error = "Invalid pairing PIN." });
        });

        // 3. Printers Listing
        app.MapGet("/api/printers", () =>
        {
            var printers = _printerDiscovery.GetPrinters();
            return Results.Ok(printers);
        });

        // 4. Printer Info
        app.MapGet("/api/printers/{name}", (string name) =>
        {
            var printer = _printerDiscovery.FindPrinter(name);
            return printer != null ? Results.Ok(printer) : Results.NotFound(new { error = $"Printer '{name}' not found." });
        });

        // 5. File Upload (Deduplicated with Fast Hashing, Size Limits & Sanitized Errors - sec-02, sec-05, sec-06)
        app.MapPost("/api/upload", async (HttpRequest request, CancellationToken uploadCt) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new { error = "No files uploaded." });
            }

            var file = request.Form.Files[0];
            if (file.Length == 0)
            {
                return Results.BadRequest(new { error = "Uploaded file is empty." });
            }

            try
            {
                using var stream = file.OpenReadStream();
                var cacheResult = await _fileCache.StoreFileAsync(file.FileName, stream, uploadCt);

                int pageCount = 1;
                try
                {
                    var renderer = _rendererResolver.Resolve(cacheResult.CachedFilePath);
                    pageCount = await renderer.GetPageCountAsync(cacheResult.CachedFilePath);
                }
                catch
                {
                    // Fallback to 1 if count could not be retrieved
                }

                var response = new UploadResponse
                {
                    FileId = cacheResult.FileId,
                    FileName = file.FileName,
                    FileSize = cacheResult.FileSizeBytes,
                    PageCount = pageCount,
                    IsDuplicate = cacheResult.IsDuplicate,
                    Extension = cacheResult.Extension
                };

                _eventHub.Publish(new PrintEvent
                {
                    Type = "queued",
                    FileName = file.FileName,
                    Message = cacheResult.IsDuplicate
                        ? $"File '{file.FileName}' recognized from cache (deduplicated)."
                        : $"File '{file.FileName}' uploaded ({pageCount} page(s))."
                });

                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] File upload handling error: {ex}");
                Console.ResetColor();
                return Results.Problem(detail: "An internal error occurred while processing the uploaded document.", statusCode: 500);
            }
        });

        // 6. Batch Print Execution (Enqueued in Serialized Queue - sec-03)
        app.MapPost("/api/print", (WebBatchPrintRequest req) =>
        {
            if (req.Items == null || req.Items.Count == 0)
            {
                return Results.BadRequest(new { error = "No items specified to print." });
            }

            _pipeline.Enqueue(ToPipelineBatch(req));
            int currentPos = Math.Max(1, _pipeline.PendingCount);

            _eventHub.Publish(new PrintEvent
            {
                Type = "queued",
                Message = currentPos > 1
                    ? $"Print job queued behind {currentPos - 1} pending batch(es) ({req.Items.Count} document(s))."
                    : $"Print job queued ({req.Items.Count} document(s)). Starting spooler..."
            });

            return Results.Accepted(value: new { status = "queued", position = currentPos, count = req.Items.Count });
        });

        // 7. SSE Real-Time Progress Stream
        app.MapGet("/api/events", async (HttpContext ctx, CancellationToken clientCt) =>
        {
            ctx.Response.Headers.Append("Content-Type", "text/event-stream");
            ctx.Response.Headers.Append("Cache-Control", "no-cache");
            ctx.Response.Headers.Append("Connection", "keep-alive");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                clientCt,
                app.Lifetime.ApplicationStopping);
            var streamCt = linkedCts.Token;

            var reader = _eventHub.Subscribe();

            try
            {
                // Send recent activity history first
                foreach (var evt in _eventHub.GetRecentEvents(10))
                {
                    var json = JsonSerializer.Serialize(evt);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", streamCt);
                }
                await ctx.Response.Body.FlushAsync(streamCt);

                // Stream live events
                await foreach (var evt in reader.ReadAllAsync(streamCt))
                {
                    var json = JsonSerializer.Serialize(evt);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", streamCt);
                    await ctx.Response.Body.FlushAsync(streamCt);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or server shutting down
            }
            finally
            {
                _eventHub.Unsubscribe(reader);
            }
        });

        // 8. Queue Inspection and Management
        app.MapGet("/api/queue", (string? printer) =>
        {
            var targetPrinter = !string.IsNullOrWhiteSpace(printer)
                ? _printerDiscovery.FindPrinter(printer)?.Name ?? printer
                : _printerDiscovery.GetDefaultPrinter()?.Name;

            var jobs = _queueService.GetJobs(targetPrinter);
            var status = !string.IsNullOrWhiteSpace(targetPrinter) ? _queueService.GetPrinterStatus(targetPrinter) : null;
            return Results.Ok(new QueueResponse
            {
                Printer = targetPrinter,
                Status = status,
                Jobs = jobs.ToList()
            });
        });

        app.MapGet("/api/printers/{name}/status", (string name) =>
        {
            var status = _queueService.GetPrinterStatus(name);
            return Results.Ok(status);
        });

        app.MapPost("/api/queue/cancel", (CancelJobRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.JobId))
            {
                return Results.BadRequest(new { error = "Job ID must be specified." });
            }

            bool cancelled = _queueService.CancelJob(req.Printer, req.JobId);
            _eventHub.Publish(new PrintEvent
            {
                Type = "queue_updated",
                Printer = req.Printer,
                Message = cancelled
                    ? $"Job '{req.JobId}' cancellation requested."
                    : $"Failed to cancel job '{req.JobId}'."
            });

            return cancelled
                ? Results.Ok(new { success = true, message = $"Job '{req.JobId}' cancellation requested." })
                : Results.BadRequest(new { success = false, error = $"Job '{req.JobId}' could not be cancelled." });
        });

        app.MapPost("/api/queue/purge", (PurgeQueueRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Printer))
            {
                return Results.BadRequest(new { error = "Printer name must be specified." });
            }

            int purged = _queueService.PurgeSpoolerQueue(req.Printer);
            _eventHub.Publish(new PrintEvent
            {
                Type = "queue_updated",
                Printer = req.Printer,
                Message = $"Purged {purged} job(s) from '{req.Printer}'."
            });

            return Results.Ok(new { success = true, purged });
        });

        // Show start banner in console
        ConsoleUi.ShowBanner();
        if (webUi)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  [WEB SERVER RUNNING]  Port: {port}");
            Console.ResetColor();

            if (requireAuth)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [SECURITY] PIN Protected:  {pin}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  [SECURITY] Open Access (--no-auth enabled)");
                Console.ResetColor();
            }

            Console.WriteLine("\n  Access from this machine or your phone on the same Wi-Fi:");
            Console.ForegroundColor = ConsoleColor.Cyan;
            string pinQuery = requireAuth ? $"?pin={pin}" : "";
            Console.WriteLine($"    Local:    http://localhost:{port}/{pinQuery}");

            foreach (var ip in lanIps)
            {
                Console.WriteLine($"    Network:  http://{ip}:{port}/{pinQuery}");
            }
            Console.ResetColor();
        }

        // Network printer advertising (mDNS / DNS-SD)
        bool advertising = false;
        if (sharing && share.EnableMdns)
        {
            _ippSettings.MdnsHostName = _advertiser.HostName;
            try
            {
                await _advertiser.StartAsync(_dnsSdServices.Create(_sharedPrinters.Printers), ct);
                advertising = true;
                shareWarnings.AddRange(_advertiser.Warnings);
            }
            catch (Exception ex)
            {
                _ippSettings.MdnsHostName = null;
                shareWarnings.Add($"Network discovery (mDNS) could not start: {ex.Message}. Devices can still add the printer by URL.");
            }
        }

        if (sharing)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  [NETWORK PRINTERS]  IPP port: {ippPort}");
            Console.ResetColor();

            foreach (var shared in _sharedPrinters.Printers)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"    {shared.DisplayName}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                if (advertising)
                {
                    Console.WriteLine($"      ipp://{_advertiser.HostName}:{ippPort}/{shared.ResourcePath}");
                }
                foreach (var ip in lanIps.Take(2))
                {
                    Console.WriteLine($"      ipp://{ip}:{ippPort}/{shared.ResourcePath}");
                }
                Console.ResetColor();
            }

            Console.WriteLine(advertising
                ? "  Discoverable from iPhone/iPad (AirPrint), Android, Windows, macOS and Linux print dialogs."
                : "  Discovery is off (--no-mdns): add the printer on each device using one of the URLs above.");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(requireAuth
                ? "  Anyone on this network can print to these printers without the PIN."
                : "  Anyone on this network can print to these printers.");
            Console.ResetColor();

            var firewallHints = _firewall.CheckInboundAccess(ippPort, advertising);
            if (firewallHints.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                foreach (var hint in firewallHints)
                {
                    Console.WriteLine($"  {hint}");
                }
                Console.ResetColor();
            }
        }

        foreach (var warning in shareWarnings)
        {
            ConsoleUi.PrintWarning(warning);
        }

        Console.WriteLine(webUi
            ? "\n  Live SSE status reporting enabled • Drag & drop supported"
            : "\n  Web UI is off. Run 'printman serve' (or add --web) to also print from a browser.");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Enjoying Printman? If this helped you, a coffee is warmly appreciated: ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("https://buymeacoffee.com/eslamx7");
        Console.ResetColor();
        Console.WriteLine(webUi ? "  Press Ctrl+C to stop the server.\n" : "  Press Ctrl+C to stop sharing.\n");

        int cancelPressCount = 0;
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ConsoleCancelEventHandler cancelHandler = (s, e) =>
        {
            cancelPressCount++;
            if (cancelPressCount >= 2)
            {
                Console.ResetColor();
                Console.WriteLine("\nForced termination.");
                Environment.Exit(0);
            }

            e.Cancel = true;
            localCts.Cancel();
            try { _ = app.StopAsync(); } catch { }
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            await app.RunAsync(localCts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(webUi ? "\nWeb server stopped." : "\nPrinter sharing stopped.");
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (advertising)
            {
                // Goodbye packets remove the printers from devices' lists right away
                try { await _advertiser.StopAsync(); } catch { }
            }
        }
    }

    private static bool IsTcpPortAvailable(string bindAddress, int port)
    {
        try
        {
            var probe = new TcpListener(IPAddress.Parse(bindAddress), port);
            probe.Start();
            probe.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static PipelineBatch ToPipelineBatch(WebBatchPrintRequest req) => new()
    {
        Printer = req.Printer,
        Source = "web",
        Items = req.Items.Select(item =>
        {
            PageRange pageRange = PageRange.All;
            if (!string.IsNullOrWhiteSpace(item.Pages))
            {
                try { pageRange = PageRange.Parse(item.Pages); } catch { }
            }

            return new PipelineItem
            {
                FileId = item.FileId,
                PageRange = pageRange,
                Copies = Math.Max(1, item.Copies),
                PaperSizeName = item.PaperSize,
                Orientation = item.Orientation?.ToLowerInvariant() switch
                {
                    "portrait" => PrintOrientation.Portrait,
                    "landscape" => PrintOrientation.Landscape,
                    _ => PrintOrientation.Auto
                },
                Duplex = item.Duplex?.ToLowerInvariant() switch
                {
                    "simplex" => PrintDuplex.Simplex,
                    "vertical" => PrintDuplex.Vertical,
                    "horizontal" => PrintDuplex.Horizontal,
                    _ => PrintDuplex.Default
                },
                ColorMode = item.Color?.ToLowerInvariant() switch
                {
                    "color" => PrintColorMode.Color,
                    "mono" or "monochrome" => PrintColorMode.Monochrome,
                    _ => PrintColorMode.Default
                },
                FitToPage = true
            };
        }).ToList()
    };

    private static List<string> GetLocalLanIpv4Addresses()
    {
        var result = new List<string>();
        int primaryCount = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var props = ni.GetIPProperties();
                // Adapters with a default gateway (Wi-Fi / Ethernet) are listed before virtual and VPN adapters
                bool hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

                foreach (var ip in props.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        if (hasGateway) result.Insert(primaryCount++, ip.Address.ToString());
                        else result.Add(ip.Address.ToString());
                    }
                }
            }
        }
        catch
        {
            // Fallback if network querying fails
        }
        return result;
    }
}
