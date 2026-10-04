using Printman.Core.Models;

namespace Printman.Core.Abstractions;

/// <summary>
/// Serialized print pipeline shared by every server front-end (web UI, IPP).
/// Jobs run one at a time to avoid GDI+ / spooler races (sec-06).
/// </summary>
public interface IPrintJobPipeline
{
    /// <summary>Number of batches waiting to start (excluding the one currently printing).</summary>
    int PendingCount { get; }

    /// <summary>
    /// When set, every job is printed to a file inside this directory instead of paper.
    /// </summary>
    string? OutputDirectory { get; set; }

    /// <summary>Enqueues a batch and returns a ticket to observe or cancel it.</summary>
    PipelineTicket Enqueue(PipelineBatch batch);

    /// <summary>Runs the worker loop until cancelled.</summary>
    Task RunAsync(CancellationToken ct);
}
