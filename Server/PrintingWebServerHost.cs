using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyPrinter.Core.Abstractions;
using OhMyPrinter.Core.Models;
using OhMyPrinter.Interactive;

namespace OhMyPrinter.Server;

public class PrintingWebServerHost(
    IPrinterDiscoveryService printerDiscovery,
    IPrintService printService,
    IDocumentRendererResolver rendererResolver,
    IFileCacheService fileCache,
    IPrintEventHub eventHub)
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IPrintService _printService = printService;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;
    private readonly IFileCacheService _fileCache = fileCache;
    private readonly IPrintEventHub _eventHub = eventHub;

    public async Task<int> RunAsync(int port = 5000, string bindAddress = "0.0.0.0", CancellationToken ct = default)
    {
        var builder = WebApplication.CreateBuilder();

        // Configure quiet logging for a clean console experience
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Parse(bindAddress), port);
        });

        var app = builder.Build();

        // 1. Root SPA
        app.MapGet("/", async ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(WebAssets.IndexHtml);
        });

        // 2. Printers Listing
        app.MapGet("/api/printers", () =>
        {
            var printers = _printerDiscovery.GetPrinters();
            return Results.Ok(printers);
        });

        // 3. Printer Info
        app.MapGet("/api/printers/{name}", (string name) =>
        {
            var printer = _printerDiscovery.FindPrinter(name);
            return printer != null ? Results.Ok(printer) : Results.NotFound(new { error = $"Printer '{name}' not found." });
        });

        // 4. File Upload (Deduplicated with Fast Hashing)
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
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // 5. Batch Print Execution
        app.MapPost("/api/print", (WebBatchPrintRequest req) =>
        {
            if (req.Items == null || req.Items.Count == 0)
            {
                return Results.BadRequest(new { error = "No items specified to print." });
            }

            // Process in background queue and stream progress over SSE
            _ = Task.Run(async () =>
            {
                await ProcessPrintBatchAsync(req, ct);
            }, ct);

            return Results.Accepted(value: new { status = "accepted", count = req.Items.Count });
        });

        // 6. SSE Real-Time Progress Stream
        app.MapGet("/api/events", async (HttpContext ctx, CancellationToken clientCt) =>
        {
            ctx.Response.Headers.Append("Content-Type", "text/event-stream");
            ctx.Response.Headers.Append("Cache-Control", "no-cache");
            ctx.Response.Headers.Append("Connection", "keep-alive");

            var reader = _eventHub.Subscribe();

            try
            {
                // Send recent activity history first
                foreach (var evt in _eventHub.GetRecentEvents(10))
                {
                    var json = JsonSerializer.Serialize(evt);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", clientCt);
                }
                await ctx.Response.Body.FlushAsync(clientCt);

                // Stream live events
                await foreach (var evt in reader.ReadAllAsync(clientCt))
                {
                    var json = JsonSerializer.Serialize(evt);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", clientCt);
                    await ctx.Response.Body.FlushAsync(clientCt);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }
            finally
            {
                _eventHub.Unsubscribe(reader);
            }
        });

        // Show start banner in console
        ConsoleUi.ShowBanner();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n  [WEB SERVER RUNNING]  Port: {port}");
        Console.ResetColor();

        Console.WriteLine("\n  Access from this machine or your phone on the same Wi-Fi:");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"    Local:    http://localhost:{port}");

        var lanIps = GetLocalLanIpv4Addresses();
        foreach (var ip in lanIps)
        {
            Console.WriteLine($"    Network:  http://{ip}:{port}");
        }
        Console.ResetColor();

        Console.WriteLine("\n  Live SSE status reporting enabled • Drag & drop supported");
        Console.WriteLine("  Press Ctrl+C to stop the server.\n");

        try
        {
            await app.RunAsync(ct);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nWeb server stopped.");
            return 0;
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

            var progress = new Progress<string>(msg =>
            {
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

            var result = await _printService.PrintAsync(printRequest, progress, ct);

            if (result.Success)
            {
                _eventHub.Publish(new PrintEvent
                {
                    Type = "completed",
                    FileName = cachedFile.OriginalFileName,
                    Printer = printer.Name,
                    PagesPrinted = result.PagesPrinted,
                    Message = $"Successfully printed '{cachedFile.OriginalFileName}' ({result.PagesPrinted} page(s), {result.CopiesPrinted} copy/copies)."
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
