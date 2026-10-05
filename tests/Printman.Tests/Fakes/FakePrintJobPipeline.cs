using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Tests.Fakes;

/// <summary>Records enqueued batches without spawning a worker.</summary>
public sealed class FakePrintJobPipeline : IPrintJobPipeline
{
    public List<PipelineBatch> Enqueued { get; } = [];
    public string? OutputDirectory { get; set; }

    /// <summary>Overrides the ticket returned from <see cref="Enqueue"/> (e.g. to pre-cancel one).</summary>
    public Func<PipelineBatch, PipelineTicket>? TicketFactory { get; set; }

    public int PendingCount => Enqueued.Count;

    public PipelineTicket Enqueue(PipelineBatch batch)
    {
        Enqueued.Add(batch);
        return TicketFactory?.Invoke(batch) ?? new PipelineTicket(batch);
    }

    public Task RunAsync(CancellationToken ct) => Task.CompletedTask;
}
