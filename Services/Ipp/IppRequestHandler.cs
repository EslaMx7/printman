using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services.Ipp;

/// <summary>
/// Implements the IPP Everywhere operation set on top of the shared print pipeline.
/// Every response is IPP-encoded (HTTP status is always 200); errors are reported as IPP status codes.
/// </summary>
public class IppRequestHandler(
    ISharedPrinterRegistry registry,
    IIppJobStore jobStore,
    IppPrinterAttributeBuilder attributeBuilder,
    IppDocumentFormats formats,
    IFileCacheService fileCache,
    IPrintJobPipeline pipeline,
    IPrintEventHub eventHub,
    IppServerSettings settings) : IIppRequestHandler
{
    private static readonly string[] DefaultJobListAttributes = ["job-id", "job-uri"];

    private readonly ISharedPrinterRegistry _registry = registry;
    private readonly IIppJobStore _jobStore = jobStore;
    private readonly IppPrinterAttributeBuilder _attributeBuilder = attributeBuilder;
    private readonly IppDocumentFormats _formats = formats;
    private readonly IFileCacheService _fileCache = fileCache;
    private readonly IPrintJobPipeline _pipeline = pipeline;
    private readonly IPrintEventHub _eventHub = eventHub;
    private readonly IppServerSettings _settings = settings;

    public async Task<byte[]> HandleAsync(IppRequestContext context, CancellationToken ct)
    {
        IppMessage request;
        try
        {
            request = await IppMessageReader.ReadAsync(context.Body, IppMessageReader.DefaultMaxAttributeBytes, ct);
        }
        catch (IppParseException ex)
        {
            return Encode(Response(null, IppStatus.BadRequest, ex.Message));
        }

        if (request.VersionMajor is < 1 or > 2)
        {
            return Encode(Response(request, IppStatus.VersionNotSupported, "Only IPP 1.1 and 2.x are supported."));
        }

        var printer = _registry.Find(context.PrinterSlug);
        if (printer == null)
        {
            return Encode(Response(request, IppStatus.NotFound, "Printer not found."));
        }

        var call = new Call(request, printer, context);

        try
        {
            var response = request.Code switch
            {
                IppOperation.GetPrinterAttributes => GetPrinterAttributes(call),
                IppOperation.ValidateJob => ValidateJob(call),
                IppOperation.PrintJob => await PrintJobAsync(call, ct),
                IppOperation.CreateJob => CreateJob(call),
                IppOperation.SendDocument => await SendDocumentAsync(call, ct),
                IppOperation.CloseJob => CloseJob(call),
                IppOperation.GetJobs => GetJobs(call),
                IppOperation.GetJobAttributes => GetJobAttributes(call),
                IppOperation.CancelJob => CancelJob(call),
                IppOperation.CancelMyJobs => CancelMyJobs(call),
                IppOperation.IdentifyPrinter => IdentifyPrinter(call),
                _ => Response(request, IppStatus.OperationNotSupported, "Operation not supported.")
            };
            return Encode(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[IPP] Error handling operation 0x{request.Code:X4}: {ex}");
            Console.ResetColor();
            return Encode(Response(request, IppStatus.InternalError, "Internal printer error."));
        }
    }

    private sealed record Call(IppMessage Request, SharedPrinter Printer, IppRequestContext Context);

    // ---------------------------------------------------------------- Operations

    private IppMessage GetPrinterAttributes(Call call)
    {
        var requested = Keywords(call.Request.FindRequestAttribute("requested-attributes"));
        var response = Response(call.Request, IppStatus.Ok);
        response.Groups.Add(_attributeBuilder.Build(call.Printer, PrinterUri(call), WebUri(call), requested));
        return response;
    }

    private IppMessage ValidateJob(Call call)
    {
        var format = call.Request.FindRequestAttribute("document-format")?.First?.AsString();
        if (format != null && format != IppDocumentFormats.OctetStream && !_formats.Supports(format))
        {
            return Response(call.Request, IppStatus.DocumentFormatNotSupported, $"Document format '{format}' is not supported.");
        }
        return Response(call.Request, IppStatus.Ok);
    }

    private async Task<IppMessage> PrintJobAsync(Call call, CancellationToken ct)
    {
        if (IsBusy(call.Printer))
        {
            return Response(call.Request, IppStatus.Busy, "Printer queue is full, try again later.");
        }

        var job = CreateJobRecord(call);
        var (status, message) = await ReceiveDocumentAsync(call, job, ct);
        if (status != IppStatus.Ok)
        {
            job.Terminate(IppJobState.Aborted, "document-format-error");
            return Response(call.Request, status, message);
        }

        return JobResponse(call, job);
    }

    private IppMessage CreateJob(Call call)
    {
        if (IsBusy(call.Printer))
        {
            return Response(call.Request, IppStatus.Busy, "Printer queue is full, try again later.");
        }

        var job = CreateJobRecord(call);
        return JobResponse(call, job);
    }

    private async Task<IppMessage> SendDocumentAsync(Call call, CancellationToken ct)
    {
        var job = FindJob(call);
        if (job == null)
        {
            return Response(call.Request, IppStatus.NotFound, "Job not found.");
        }

        bool lastDocument = call.Request.FindRequestAttribute("last-document")?.First?.AsBool() ?? true;

        if (!job.AwaitingDocument)
        {
            if (job.Ticket != null && lastDocument && !await HasMoreDataAsync(call.Context.Body, ct))
            {
                return JobResponse(call, job); // Closing an already submitted job
            }
            return Response(call.Request, IppStatus.MultipleDocumentJobsNotSupported, "Only one document per job is supported.");
        }

        var (status, message) = await ReceiveDocumentAsync(call, job, ct);
        if (status == IppStatus.BadRequest && lastDocument)
        {
            // Empty last document: the client closed the job without sending data
            job.Terminate(IppJobState.Aborted, "aborted-by-system");
            return JobResponse(call, job);
        }
        if (status != IppStatus.Ok)
        {
            job.Terminate(IppJobState.Aborted, "document-format-error");
            return Response(call.Request, status, message);
        }

        return JobResponse(call, job);
    }

    private IppMessage CloseJob(Call call)
    {
        var job = FindJob(call);
        if (job == null)
        {
            return Response(call.Request, IppStatus.NotFound, "Job not found.");
        }

        if (job.AwaitingDocument)
        {
            job.Terminate(IppJobState.Aborted, "aborted-by-system");
        }
        return JobResponse(call, job);
    }

    private IppMessage GetJobAttributes(Call call)
    {
        var job = FindJob(call);
        if (job == null)
        {
            return Response(call.Request, IppStatus.NotFound, "Job not found.");
        }

        var requested = Keywords(call.Request.FindRequestAttribute("requested-attributes"));
        var response = Response(call.Request, IppStatus.Ok);
        response.Groups.Add(BuildJobGroup(call, job, requested));
        return response;
    }

    private IppMessage GetJobs(Call call)
    {
        var which = call.Request.FindRequestAttribute("which-jobs")?.First?.AsString() ?? "not-completed";
        bool myJobs = call.Request.FindRequestAttribute("my-jobs")?.First?.AsBool() ?? false;
        var user = call.Request.FindRequestAttribute("requesting-user-name")?.First?.AsString();
        int limit = call.Request.FindRequestAttribute("limit")?.First?.AsInt() ?? int.MaxValue;
        IReadOnlyCollection<string> requested = (IReadOnlyCollection<string>?)Keywords(call.Request.FindRequestAttribute("requested-attributes")) ?? DefaultJobListAttributes;

        if (which is not ("completed" or "not-completed" or "all"))
        {
            return Response(call.Request, IppStatus.AttributesOrValuesNotSupported, $"which-jobs '{which}' is not supported.");
        }

        var jobs = _jobStore.List(call.Printer.Slug)
            .Where(j =>
            {
                bool terminal = IppJobState.IsTerminal(j.GetState().State);
                return which == "all" || (which == "completed" ? terminal : !terminal);
            })
            .Where(j => !myJobs || user == null || string.Equals(j.UserName, user, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(j => j.Id)
            .Take(Math.Max(1, limit));

        var response = Response(call.Request, IppStatus.Ok);
        foreach (var job in jobs)
        {
            response.Groups.Add(BuildJobGroup(call, job, requested));
        }
        return response;
    }

    private IppMessage CancelJob(Call call)
    {
        var job = FindJob(call);
        if (job == null)
        {
            return Response(call.Request, IppStatus.NotFound, "Job not found.");
        }

        if (!TryCancel(job))
        {
            return Response(call.Request, IppStatus.NotPossible, "Job has already finished.");
        }

        _eventHub.Publish(new PrintEvent
        {
            Type = "queue_updated",
            Printer = call.Printer.WindowsName,
            Message = $"Network job #{job.Id} '{job.Name}' cancellation requested."
        });
        return Response(call.Request, IppStatus.Ok);
    }

    private IppMessage CancelMyJobs(Call call)
    {
        var user = call.Request.FindRequestAttribute("requesting-user-name")?.First?.AsString();
        var ids = call.Request.FindRequestAttribute("job-ids")?.Values.Select(v => v.AsInt()).OfType<int>().ToHashSet();

        foreach (var job in _jobStore.List(call.Printer.Slug))
        {
            if (ids != null && !ids.Contains(job.Id)) continue;
            if (user != null && !string.Equals(job.UserName, user, StringComparison.OrdinalIgnoreCase)) continue;
            TryCancel(job);
        }

        return Response(call.Request, IppStatus.Ok);
    }

    private IppMessage IdentifyPrinter(Call call)
    {
        var who = call.Request.FindRequestAttribute("requesting-user-name")?.First?.AsString() ?? call.Context.RemoteAddress?.ToString() ?? "a client";
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  [IPP] Identify request for '{call.Printer.DisplayName}' from {who}");
        Console.ResetColor();
        _eventHub.Publish(new PrintEvent
        {
            Type = "progress",
            Printer = call.Printer.WindowsName,
            Message = $"Identify requested for '{call.Printer.DisplayName}' by {who}."
        });
        return Response(call.Request, IppStatus.Ok);
    }

    // ---------------------------------------------------------------- Job helpers

    private bool IsBusy(SharedPrinter printer) =>
        _jobStore.ActiveCount(printer.Slug) >= _settings.MaxPendingJobs;

    private IppJob CreateJobRecord(Call call)
    {
        var request = call.Request;
        var user = request.FindRequestAttribute("requesting-user-name")?.First?.AsString();
        if (string.IsNullOrWhiteSpace(user))
        {
            user = call.Context.RemoteAddress?.ToString() ?? "anonymous";
        }

        var name = request.FindRequestAttribute("job-name")?.First?.AsString()
                   ?? request.FindRequestAttribute("document-name")?.First?.AsString();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Network print job";
        }

        return _jobStore.Create(call.Printer.Slug, name.Trim(), user.Trim(), ParseJobOptions(call));
    }

    private IppJobOptions ParseJobOptions(Call call)
    {
        var request = call.Request;
        var options = new IppJobOptions();

        if (request.FindJobAttribute("copies")?.First?.AsInt() is int copies)
        {
            options = options with { Copies = Math.Clamp(copies, 1, 99) };
        }

        options = request.FindJobAttribute("sides")?.First?.AsString() switch
        {
            "one-sided" => options with { Duplex = PrintDuplex.Simplex },
            "two-sided-long-edge" => options with { Duplex = PrintDuplex.Vertical },
            "two-sided-short-edge" => options with { Duplex = PrintDuplex.Horizontal },
            _ => options
        };

        options = request.FindJobAttribute("print-color-mode")?.First?.AsString() switch
        {
            "color" => options with { ColorMode = PrintColorMode.Color },
            "monochrome" or "process-monochrome" or "bi-level" or "process-bi-level" or "auto-monochrome"
                => options with { ColorMode = PrintColorMode.Monochrome },
            _ => options
        };

        options = request.FindJobAttribute("orientation-requested")?.First?.AsInt() switch
        {
            3 or 6 => options with { Orientation = PrintOrientation.Portrait },
            4 or 5 => options with { Orientation = PrintOrientation.Landscape },
            _ => options
        };

        if (request.FindJobAttribute("print-scaling")?.First?.AsString() == "none")
        {
            options = options with { FitToPage = false };
        }

        var ranges = request.FindJobAttribute("page-ranges")?.Values
            .Select(v => v.AsRange())
            .OfType<IppRange>()
            .Where(r => r.Lower >= 1 && r.Upper >= r.Lower)
            .Select(r => r.Lower == r.Upper ? $"{r.Lower}" : $"{r.Lower}-{r.Upper}")
            .ToList();
        if (ranges is { Count: > 0 })
        {
            try { options = options with { PageRange = PageRange.Parse(string.Join(',', ranges)) }; } catch (FormatException) { }
        }

        var media = ResolveMedia(request);
        if (media != null)
        {
            var paper = PwgMediaMapper.Map(_registry.GetCapabilities(call.Printer)?.SupportedPaperSizes ?? [])
                .FirstOrDefault(m => m.Media.Name == media.Name).Paper;
            if (paper != null)
            {
                options = options with { PaperSizeName = paper.Name };
            }
        }

        return options;
    }

    private static PwgMedia? ResolveMedia(IppMessage request)
    {
        var mediaCol = request.FindJobAttribute("media-col")?.First?.AsCollection();
        if (mediaCol != null)
        {
            var byName = PwgMediaMapper.FindByName(mediaCol.Get("media-size-name")?.First?.AsString());
            if (byName != null) return byName;

            var size = mediaCol.Get("media-size")?.First?.AsCollection();
            if (size?.Get("x-dimension")?.First?.AsInt() is int x && size.Get("y-dimension")?.First?.AsInt() is int y)
            {
                var bySize = PwgMediaMapper.FindBySize(x, y);
                if (bySize != null) return bySize;
            }
        }

        return PwgMediaMapper.FindByName(request.FindJobAttribute("media")?.First?.AsString());
    }

    private async Task<(short Status, string? Message)> ReceiveDocumentAsync(Call call, IppJob job, CancellationToken ct)
    {
        var declared = call.Request.FindRequestAttribute("document-format")?.First?.AsString();

        var head = new byte[16];
        int headLength = await call.Context.Body.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (headLength == 0)
        {
            return (IppStatus.BadRequest, "No document data was sent.");
        }

        // Content wins over the declared format (clients often send application/octet-stream)
        var format = IppDocumentFormats.Sniff(head.AsSpan(0, headLength));
        if (format == null && declared != null && declared != IppDocumentFormats.OctetStream && _formats.Supports(declared))
        {
            format = declared;
        }
        if (format == null || !_formats.Supports(format))
        {
            return (IppStatus.DocumentFormatNotSupported, $"Document format '{declared ?? "unknown"}' is not supported.");
        }

        var extension = _formats.ExtensionFor(format)!;
        FileCacheResult cached;
        try
        {
            using var document = new PrefixedReadStream(head.AsMemory(0, headLength), call.Context.Body);
            cached = await _fileCache.StoreFileAsync($"{SafeFileName(job.Name)}{extension}", document, _settings.MaxJobBytes, ct);
        }
        catch (InvalidOperationException)
        {
            return (IppStatus.RequestEntityTooLarge, "Document exceeds the maximum job size.");
        }
        catch (ArgumentException)
        {
            return (IppStatus.DocumentFormatNotSupported, "Document format is not supported.");
        }

        bool raster = IppDocumentFormats.IsRaster(format);
        var item = new PipelineItem
        {
            FileId = cached.FileId,
            DisplayName = $"{job.Name} (#{job.Id}, {job.UserName})",
            JobTitle = job.Name,
            Copies = job.Options.Copies,
            Duplex = job.Options.Duplex,
            ColorMode = job.Options.ColorMode,
            PaperSizeName = job.Options.PaperSizeName,
            PageRange = job.Options.PageRange,
            // Raster pages arrive already rotated and sized for the sheet
            Orientation = raster ? PrintOrientation.Portrait : job.Options.Orientation,
            FitToPage = raster || job.Options.FitToPage,
            FullPage = true
        };

        var ticket = _pipeline.Enqueue(new PipelineBatch
        {
            Printer = call.Printer.WindowsName,
            Items = [item],
            Source = "ipp"
        });
        job.Attach(ticket, format);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [IPP] Job #{job.Id} '{job.Name}' from {job.UserName} ({call.Context.RemoteAddress}) -> {call.Printer.WindowsName} [{format}, {cached.FileSizeBytes / 1024} KB]");
        Console.ResetColor();

        _eventHub.Publish(new PrintEvent
        {
            Type = "queued",
            JobId = ticket.Id,
            FileName = job.Name,
            Printer = call.Printer.WindowsName,
            Message = $"Network print job #{job.Id} '{job.Name}' received from {job.UserName}."
        });

        return (IppStatus.Ok, null);
    }

    private static bool TryCancel(IppJob job)
    {
        if (IppJobState.IsTerminal(job.GetState().State))
        {
            return false;
        }

        if (job.Ticket != null)
        {
            return job.Ticket.Cancel();
        }

        job.Terminate(IppJobState.Canceled, "job-canceled-by-user");
        return true;
    }

    private IppJob? FindJob(Call call)
    {
        int? id = call.Request.FindRequestAttribute("job-id")?.First?.AsInt();
        if (id == null)
        {
            var jobUri = call.Request.FindRequestAttribute("job-uri")?.First?.AsString();
            var tail = jobUri?.TrimEnd('/').Split('/').LastOrDefault();
            if (int.TryParse(tail, out int parsed)) id = parsed;
        }

        var job = id is int jobId ? _jobStore.Get(jobId) : null;
        return job != null && job.PrinterSlug == call.Printer.Slug ? job : null;
    }

    private static async Task<bool> HasMoreDataAsync(Stream body, CancellationToken ct)
    {
        var probe = new byte[1];
        return await body.ReadAsync(probe, ct) > 0;
    }

    // ---------------------------------------------------------------- Response helpers

    private IppMessage JobResponse(Call call, IppJob job)
    {
        var response = Response(call.Request, IppStatus.Ok);
        response.Groups.Add(BuildJobGroup(call, job, ["job-id", "job-uri", "job-state", "job-state-reasons", "job-state-message"]));
        return response;
    }

    private IppAttributeGroup BuildJobGroup(Call call, IppJob job, IReadOnlyCollection<string>? requested)
    {
        var (state, reason, message) = job.GetState();
        var printerUri = PrinterUri(call);
        var g = new IppAttributeGroup(IppTag.JobAttributes);

        g.AddInteger("job-id", job.Id);
        g.AddUri("job-uri", $"{printerUri}/{job.Id}");
        g.AddUri("job-printer-uri", printerUri);
        g.AddName("job-name", job.Name);
        g.AddName("job-originating-user-name", job.UserName);
        g.AddEnum("job-state", state);
        g.AddKeyword("job-state-reasons", reason);
        g.AddText("job-state-message", message);
        g.AddInteger("job-printer-up-time", _settings.UpTimeSeconds);
        g.AddInteger("time-at-creation", _settings.ToUpTime(job.CreatedAt));
        if (job.StartedAt is DateTime started) g.AddInteger("time-at-processing", _settings.ToUpTime(started));
        else g.AddNoValue("time-at-processing");
        if (job.CompletedAt is DateTime completed) g.AddInteger("time-at-completed", _settings.ToUpTime(completed));
        else g.AddNoValue("time-at-completed");
        g.AddInteger("job-impressions-completed", job.PagesPrinted);
        g.AddInteger("copies", job.Options.Copies);
        if (job.DocumentFormat != null) g.Add("document-format", IppTag.MimeMediaType, job.DocumentFormat);

        if (requested == null || requested.Count == 0 || requested.Contains("all") || requested.Contains("job-description") || requested.Contains("job-template"))
        {
            return g;
        }

        var filtered = new IppAttributeGroup(IppTag.JobAttributes);
        filtered.Attributes.AddRange(g.Attributes.Where(a => requested.Contains(a.Name)));
        return filtered;
    }

    private static IppMessage Response(IppMessage? request, short status, string? message = null)
    {
        var response = new IppMessage
        {
            VersionMajor = request?.VersionMajor is 1 or 2 ? request.VersionMajor : (byte)2,
            VersionMinor = request?.VersionMajor is 1 or 2 ? request.VersionMinor : (byte)0,
            Code = status,
            RequestId = request?.RequestId ?? 1
        };

        var op = response.AddGroup(IppTag.OperationAttributes);
        op.AddCharset("attributes-charset", "utf-8");
        op.AddLanguage("attributes-natural-language", "en");
        if (!string.IsNullOrEmpty(message))
        {
            op.AddText("status-message", message);
        }
        return response;
    }

    private static byte[] Encode(IppMessage message) => IppMessageWriter.Write(message);

    private static HashSet<string>? Keywords(IppAttribute? attribute) =>
        attribute?.Values.Select(v => v.AsString()).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private static string PrinterUri(Call call) => $"ipp://{call.Context.Host}/{call.Printer.ResourcePath}";

    private string WebUri(Call call)
    {
        var host = call.Context.Host;
        // Strip the port from "host:port" / "[v6]:port"
        int colon = host.LastIndexOf(':');
        if (colon > 0 && host.IndexOf(']') < colon)
        {
            host = host[..colon];
        }
        return $"http://{host}:{_settings.WebPort}/";
    }

    private static string SafeFileName(string name)
    {
        var chars = name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).Trim();
        if (safe.Length > 60) safe = safe[..60];
        return safe.Length == 0 ? "ipp-job" : safe;
    }

    /// <summary>Replays bytes consumed for format sniffing before continuing with the request body.</summary>
    private sealed class PrefixedReadStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
    {
        private ReadOnlyMemory<byte> _prefix = prefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_prefix.IsEmpty)
            {
                int n = Math.Min(buffer.Length, _prefix.Length);
                _prefix[..n].CopyTo(buffer);
                _prefix = _prefix[n..];
                return n;
            }
            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
