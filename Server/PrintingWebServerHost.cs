using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
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
    IPrintService printService,
    IDocumentRendererResolver rendererResolver,
    IFileCacheService fileCache,
    IPrintEventHub eventHub,
    IPrintQueueService queueService)
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IPrintService _printService = printService;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;
    private readonly IFileCacheService _fileCache = fileCache;
    private readonly IPrintEventHub _eventHub = eventHub;
    private readonly IPrintQueueService _queueService = queueService;

    public Task<int> RunAsync(int port, string bindAddress, CancellationToken ct) =>
        RunAsync(port, bindAddress, pin: null, requireAuth: true, maxUploadMb: 50, cacheLimitMb: 500, ct);

    public async Task<int> RunAsync(
        int port = 5000,
        string bindAddress = "0.0.0.0",
        string? pin = null,
        bool requireAuth = true,
        int maxUploadMb = 50,
        int cacheLimitMb = 500,
        CancellationToken ct = default)
    {
        // 1. Configure storage bounds and quotas (sec-02)
        _fileCache.MaxFileSizeBytes = maxUploadMb * 1024L * 1024L;
        _fileCache.MaxCacheSizeBytes = cacheLimitMb * 1024L * 1024L;

        // 2. Resolve pairing PIN (sec-01)
        if (requireAuth && string.IsNullOrWhiteSpace(pin))
        {
            pin = RandomNumberGenerator.GetString("0123456789", 6);
        }

        var activeSessions = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        var lanIps = GetLocalLanIpv4Addresses();

        var builder = WebApplication.CreateBuilder();

        // Quiet logging
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);

        // Fast shutdown timeout (prevents hanging on open connections)
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(1);
        });

        // Kestrel request limits (sec-02)
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Parse(bindAddress), port);
            options.Limits.MaxRequestBodySize = maxUploadMb * 1024L * 1024L;
        });

        var app = builder.Build();

        // 3. Serialized Print Queue (sec-03)
        var printQueue = Channel.CreateUnbounded<WebBatchPrintRequest>();
        int queueLength = 0;
        using var queueCts = CancellationTokenSource.CreateLinkedTokenSource(ct, app.Lifetime.ApplicationStopping);

        _ = Task.Run(async () =>
        {
            try
            {
                while (await printQueue.Reader.WaitToReadAsync(queueCts.Token))
                {
                    while (printQueue.Reader.TryRead(out var batch))
                    {
                        try
                        {
                            Interlocked.Decrement(ref queueLength);
                            await ProcessPrintBatchAsync(batch, queueCts.Token);
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[ERROR] Print batch execution error: {ex.Message}");
                            Console.ResetColor();
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, ct);

        // 4. Background Spooler Queue Observer
        _ = Task.Run(async () =>
        {
            int lastJobCount = -1;
            string lastStatusSummary = "";
            while (!ct.IsCancellationRequested)
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

        // Security Middleware (sec-01, sec-04)
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

        // 1. Root SPA
        app.MapGet("/", async ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(WebAssets.IndexHtml);
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

            int currentPos = Interlocked.Increment(ref queueLength);
            printQueue.Writer.TryWrite(req);

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

        Console.WriteLine("\n  Live SSE status reporting enabled • Drag & drop supported");
        Console.WriteLine("  Press Ctrl+C to stop the server.\n");

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
            Console.WriteLine("\nWeb server stopped.");
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private async Task ProcessPrintBatchAsync(WebBatchPrintRequest batch, CancellationToken ct)
    {
        var targetPrinterName = batch.Printer;
        var printer = !string.IsNullOrWhiteSpace(targetPrinterName)
            ? _printerDiscovery.FindPrinter(targetPrinterName)
            : _printerDiscovery.GetDefaultPrinter();

        if (printer == null)
        {
            _eventHub.Publish(new PrintEvent
            {
                Type = "error",
                Message = $"Target printer '{targetPrinterName}' could not be resolved."
            });
            return;
        }

        _eventHub.Publish(new PrintEvent
        {
            Type = "queued",
            Printer = printer.Name,
            Message = $"Starting print queue ({batch.Items.Count} document(s)) on '{printer.Name}'..."
        });

        int itemIndex = 0;
        foreach (var item in batch.Items)
        {
            if (ct.IsCancellationRequested) break;
            itemIndex++;

            var cachedFile = _fileCache.GetFile(item.FileId);
            if (cachedFile == null)
            {
                _eventHub.Publish(new PrintEvent
                {
                    Type = "error",
                    Message = $"File ID '{item.FileId}' not found in cache. Skipping."
                });
                continue;
            }

            var orientation = item.Orientation?.ToLowerInvariant() switch
            {
                "portrait" => PrintOrientation.Portrait,
                "landscape" => PrintOrientation.Landscape,
                _ => PrintOrientation.Auto
            };

            var duplex = item.Duplex?.ToLowerInvariant() switch
            {
                "simplex" => PrintDuplex.Simplex,
                "vertical" => PrintDuplex.Vertical,
                "horizontal" => PrintDuplex.Horizontal,
                _ => PrintDuplex.Default
            };

            var color = item.Color?.ToLowerInvariant() switch
            {
                "color" => PrintColorMode.Color,
                "mono" or "monochrome" => PrintColorMode.Monochrome,
                _ => PrintColorMode.Default
            };

            PageRange pageRange = PageRange.All;
            if (!string.IsNullOrWhiteSpace(item.Pages))
            {
                try { pageRange = PageRange.Parse(item.Pages); } catch { }
            }

            var printRequest = new PrintJobRequest
            {
                FilePath = cachedFile.CachedFilePath,
                TargetPrinterName = printer.Name,
                PageRange = pageRange,
                PaperSizeName = item.PaperSize,
                Copies = Math.Max(1, item.Copies),
                Orientation = orientation,
                Duplex = duplex,
                ColorMode = color,
                FitToPage = true
            };

            int totalPages = 1;
            try
            {
                var renderer = _rendererResolver.Resolve(cachedFile.CachedFilePath);
                totalPages = await renderer.GetPageCountAsync(cachedFile.CachedFilePath);
            }
            catch { }

            var pipelineId = Guid.NewGuid().ToString("N")[..8];
            using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _queueService.RegisterPipelineJob(pipelineId, printer.Name, cachedFile.OriginalFileName, totalPages, itemCts);

            _eventHub.Publish(new PrintEvent
            {
                Type = "queue_updated",
                Printer = printer.Name,
                Message = $"Job '{cachedFile.OriginalFileName}' entered queue."
            });

            var progress = new Progress<string>(msg =>
            {
                _queueService.UpdatePipelineJob(pipelineId, itemIndex, PrintJobStatusCode.Spooling, msg);
                _eventHub.Publish(new PrintEvent
                {
                    Type = "progress",
                    FileName = cachedFile.OriginalFileName,
                    Printer = printer.Name,
                    Message = $"[{cachedFile.OriginalFileName}] {msg}"
                });
            });

            _eventHub.Publish(new PrintEvent
            {
                Type = "progress",
                FileName = cachedFile.OriginalFileName,
                Printer = printer.Name,
                Message = $"Processing document {itemIndex}/{batch.Items.Count}: '{cachedFile.OriginalFileName}'"
            });

            try
            {
                var result = await _printService.PrintAsync(printRequest, progress, itemCts.Token);

                if (result.Success)
                {
                    _eventHub.Publish(new PrintEvent
                    {
                        Type = "completed",
                        FileName = cachedFile.OriginalFileName,
                        Printer = printer.Name,
                        PagesPrinted = result.PagesPrinted,
                        Message = $"Successfully spooled '{cachedFile.OriginalFileName}' ({result.PagesPrinted} page(s), {result.CopiesPrinted} copy/copies)."
                    });
                }
                else
                {
                    _eventHub.Publish(new PrintEvent
                    {
                        Type = "error",
                        FileName = cachedFile.OriginalFileName,
                        Printer = printer.Name,
                        Message = $"Printing '{cachedFile.OriginalFileName}' failed: {result.ErrorMessage}"
                    });
                }
            }
            catch (OperationCanceledException)
            {
                _eventHub.Publish(new PrintEvent
                {
                    Type = "error",
                    FileName = cachedFile.OriginalFileName,
                    Printer = printer.Name,
                    Message = $"Printing of '{cachedFile.OriginalFileName}' was cancelled by user."
                });
            }
            catch (Exception ex)
            {
                _eventHub.Publish(new PrintEvent
                {
                    Type = "error",
                    FileName = cachedFile.OriginalFileName,
                    Printer = printer.Name,
                    Message = $"Unexpected printing exception for '{cachedFile.OriginalFileName}': {ex.Message}"
                });
            }
            finally
            {
                _queueService.UnregisterPipelineJob(pipelineId);
                _eventHub.Publish(new PrintEvent
                {
                    Type = "queue_updated",
                    Printer = printer.Name,
                    Message = $"Queue updated."
                });
            }
        }

        _eventHub.Publish(new PrintEvent
        {
            Type = "completed",
            Printer = printer.Name,
            Message = $"All {batch.Items.Count} document(s) in queue processed on '{printer.Name}'."
        });
    }

    private static List<string> GetLocalLanIpv4Addresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        result.Add(ip.Address.ToString());
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
