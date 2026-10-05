using System.Threading.Channels;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services;

public class PrintJobPipeline(
    IPrinterDiscoveryService printerDiscovery,
    IPrintService printService,
    IDocumentRendererResolver rendererResolver,
    IFileCacheService fileCache,
    IPrintEventHub eventHub,
    IPrintQueueService queueService) : IPrintJobPipeline
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IPrintService _printService = printService;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;
    private readonly IFileCacheService _fileCache = fileCache;
    private readonly IPrintEventHub _eventHub = eventHub;
    private readonly IPrintQueueService _queueService = queueService;

    private readonly Channel<PipelineTicket> _queue = Channel.CreateUnbounded<PipelineTicket>();
    private int _pendingCount;

    public int PendingCount => Volatile.Read(ref _pendingCount);
    public string? OutputDirectory { get; set; }

    public PipelineTicket Enqueue(PipelineBatch batch)
    {
        var ticket = new PipelineTicket(batch);
        Interlocked.Increment(ref _pendingCount);
        if (!_queue.Writer.TryWrite(ticket))
        {
            Interlocked.Decrement(ref _pendingCount);
            ticket.Finish(PipelineJobState.Failed, "Print queue is not accepting jobs.");
        }
        return ticket;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct))
            {
                while (_queue.Reader.TryRead(out var ticket))
                {
                    Interlocked.Decrement(ref _pendingCount);
                    try
                    {
                        await ProcessBatchAsync(ticket, ct);
                    }
                    catch (Exception ex)
                    {
                        ticket.Finish(PipelineJobState.Failed, ex.Message);
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[ERROR] Print batch execution error: {ex.Message}");
                        Console.ResetColor();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Release anything still waiting so callers observing tickets do not hang
            while (_queue.Reader.TryRead(out var leftover))
            {
                leftover.Finish(PipelineJobState.Canceled, "Server stopped.");
            }
        }
    }

    private async Task ProcessBatchAsync(PipelineTicket ticket, CancellationToken ct)
    {
        var batch = ticket.Batch;

        if (ticket.CancellationToken.IsCancellationRequested)
        {
            ticket.Finish(PipelineJobState.Canceled, "Canceled before printing started.");
            return;
        }

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
            ticket.Finish(PipelineJobState.Failed, "Target printer could not be resolved.");
            return;
        }

        ticket.MarkProcessing();
        _eventHub.Publish(new PrintEvent
        {
            Type = "queued",
            Printer = printer.Name,
            Message = $"Starting print queue ({batch.Items.Count} document(s)) on '{printer.Name}'..."
        });

        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct, ticket.CancellationToken);
        bool anyFailed = false;
        string? lastError = null;

        int itemIndex = 0;
        foreach (var item in batch.Items)
        {
            if (batchCts.IsCancellationRequested) break;
            itemIndex++;

            var cachedFile = _fileCache.GetFile(item.FileId);
            if (cachedFile == null)
            {
                _eventHub.Publish(new PrintEvent
                {
                    Type = "error",
                    Message = $"File ID '{item.FileId}' not found in cache. Skipping."
                });
                anyFailed = true;
                lastError = "Document not found in cache.";
                continue;
            }

            var displayName = item.DisplayName ?? cachedFile.OriginalFileName;

            var printRequest = new PrintJobRequest
            {
                FilePath = cachedFile.CachedFilePath,
                TargetPrinterName = printer.Name,
                PageRange = item.PageRange,
                PaperSizeName = item.PaperSizeName,
                Copies = Math.Max(1, item.Copies),
                Orientation = item.Orientation,
                Duplex = item.Duplex,
                ColorMode = item.ColorMode,
                FitToPage = item.FitToPage,
                FullPage = item.FullPage,
                JobTitle = item.JobTitle ?? displayName,
                OutputFilePath = BuildOutputFilePath(printer.Name, displayName)
            };

            int totalPages = 1;
            try
            {
                var renderer = _rendererResolver.Resolve(cachedFile.CachedFilePath);
                totalPages = await renderer.GetPageCountAsync(cachedFile.CachedFilePath);
            }
            catch { }

            var pipelineId = Guid.NewGuid().ToString("N")[..8];
            using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(batchCts.Token);
            _queueService.RegisterPipelineJob(pipelineId, printer.Name, displayName, totalPages, itemCts);

            _eventHub.Publish(new PrintEvent
            {
                Type = "queue_updated",
                Printer = printer.Name,
                Message = $"Job '{displayName}' entered queue."
            });

            var progress = new Progress<string>(msg =>
            {
                _queueService.UpdatePipelineJob(pipelineId, itemIndex, PrintJobStatusCode.Spooling, msg);
                _eventHub.Publish(new PrintEvent
                {
                    Type = "progress",
                    FileName = displayName,
                    Printer = printer.Name,
                    Message = $"[{displayName}] {msg}"
                });
            });

            _eventHub.Publish(new PrintEvent
            {
                Type = "progress",
                FileName = displayName,
                Printer = printer.Name,
                Message = $"Processing document {itemIndex}/{batch.Items.Count}: '{displayName}'"
            });

            try
            {
                var result = await _printService.PrintAsync(printRequest, progress, itemCts.Token);

                if (result.Success)
                {
                    ticket.AddPagesPrinted(result.PagesPrinted);
                    _eventHub.Publish(new PrintEvent
                    {
                        Type = "completed",
                        FileName = displayName,
                        Printer = printer.Name,
                        PagesPrinted = result.PagesPrinted,
                        Message = $"Successfully spooled '{displayName}' ({result.PagesPrinted} page(s), {result.CopiesPrinted} copy/copies)."
                    });
                }
                else
                {
                    anyFailed = true;
                    lastError = result.ErrorMessage;
                    _eventHub.Publish(new PrintEvent
                    {
                        Type = "error",
                        FileName = displayName,
                        Printer = printer.Name,
                        Message = $"Printing '{displayName}' failed: {result.ErrorMessage}"
                    });
                }
            }
            catch (OperationCanceledException)
            {
                _eventHub.Publish(new PrintEvent
                {
                    Type = "error",
                    FileName = displayName,
                    Printer = printer.Name,
                    Message = $"Printing of '{displayName}' was cancelled by user."
                });
            }
            catch (Exception ex)
            {
                anyFailed = true;
                lastError = ex.Message;
                _eventHub.Publish(new PrintEvent
                {
                    Type = "error",
                    FileName = displayName,
                    Printer = printer.Name,
                    Message = $"Unexpected printing exception for '{displayName}': {ex.Message}"
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

        if (batchCts.IsCancellationRequested)
        {
            ticket.Finish(PipelineJobState.Canceled, "Canceled.");
        }
        else if (anyFailed)
        {
            ticket.Finish(PipelineJobState.Failed, lastError);
        }
        else
        {
            ticket.Finish(PipelineJobState.Completed);
        }

        _eventHub.Publish(new PrintEvent
        {
            Type = "completed",
            Printer = printer.Name,
            Message = $"All {batch.Items.Count} document(s) in queue processed on '{printer.Name}'."
        });
    }

    private string? BuildOutputFilePath(string printerName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            return null;
        }

        var ext = printerName.Contains("PDF", StringComparison.OrdinalIgnoreCase) ? ".pdf"
            : printerName.Contains("XPS", StringComparison.OrdinalIgnoreCase) ? ".oxps"
            : ".prn";

        var baseName = Path.GetFileNameWithoutExtension(displayName);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(c, '_');
        }
        if (baseName.Length > 60) baseName = baseName[..60];

        return Path.Combine(OutputDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{baseName}{ext}");
    }
}
