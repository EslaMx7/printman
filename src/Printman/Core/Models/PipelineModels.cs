namespace Printman.Core.Models;

public enum PipelineJobState
{
    Pending,
    Processing,
    Completed,
    Failed,
    Canceled
}

/// <summary>
/// A single cached document to print, with fully resolved print options.
/// </summary>
public sealed class PipelineItem
{
    public required string FileId { get; init; }
    public string? DisplayName { get; init; }
    public PageRange PageRange { get; init; } = PageRange.All;
    public int Copies { get; init; } = 1;
    public string? PaperSizeName { get; init; }
    public PrintOrientation Orientation { get; init; } = PrintOrientation.Auto;
    public PrintDuplex Duplex { get; init; } = PrintDuplex.Default;
    public PrintColorMode ColorMode { get; init; } = PrintColorMode.Default;
    public bool FitToPage { get; init; } = true;
    public bool FullPage { get; init; }
    public string? JobTitle { get; init; }
}

/// <summary>
/// A group of documents printed sequentially on one printer.
/// </summary>
public sealed class PipelineBatch
{
    public string? Printer { get; init; }
    public IReadOnlyList<PipelineItem> Items { get; init; } = [];

    /// <summary>Origin of the batch, e.g. "web" or "ipp".</summary>
    public string Source { get; init; } = "web";
}

/// <summary>
/// Handle to an enqueued batch: observable state, cancellation and completion.
/// </summary>
public sealed class PipelineTicket
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<PipelineJobState> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _sync = new();

    public PipelineTicket(PipelineBatch batch)
    {
        Batch = batch;
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public PipelineBatch Batch { get; }
    public PipelineJobState State { get; private set; } = PipelineJobState.Pending;
    public int PagesPrinted { get; private set; }
    public string? Message { get; private set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public CancellationToken CancellationToken => _cts.Token;
    public Task<PipelineJobState> Completion => _completion.Task;
    public bool IsFinished => State is PipelineJobState.Completed or PipelineJobState.Failed or PipelineJobState.Canceled;

    /// <summary>Requests cancellation. Returns false if the ticket already finished.</summary>
    public bool Cancel()
    {
        lock (_sync)
        {
            if (IsFinished) return false;
        }
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    public void MarkProcessing()
    {
        lock (_sync)
        {
            if (IsFinished) return;
            State = PipelineJobState.Processing;
            StartedAt = DateTime.UtcNow;
        }
    }

    public void AddPagesPrinted(int pages)
    {
        lock (_sync) { PagesPrinted += pages; }
    }

    public void Finish(PipelineJobState state, string? message = null)
    {
        lock (_sync)
        {
            if (IsFinished) return;
            State = state;
            Message = message;
            CompletedAt = DateTime.UtcNow;
        }
        _completion.TrySetResult(state);
    }
}
