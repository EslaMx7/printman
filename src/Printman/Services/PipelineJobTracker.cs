using System.Collections.Concurrent;
using Printman.Core.Models;

namespace Printman.Services;

/// <summary>
/// In-flight Printman pipeline jobs (rendering / handing off to the OS print system),
/// shared by every <see cref="Core.Abstractions.IPrintQueueService"/> implementation.
/// </summary>
public sealed class PipelineJobTracker
{
    private readonly ConcurrentDictionary<string, Entry> _jobs = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Entry(
        string PipelineId,
        string PrinterName,
        string DocumentName,
        int TotalPages,
        DateTime SubmittedAt,
        CancellationTokenSource Cts)
    {
        public int PagesPrinted { get; set; }
        public PrintJobStatusCode StatusCode { get; set; } = PrintJobStatusCode.Spooling;
        public string StatusDescription { get; set; } = "Spooling / Rendering";

        // Negative so it never collides with a print system job id
        public int NumericId => -Math.Abs(PipelineId.GetHashCode());
    }

    public void Register(string pipelineJobId, string printerName, string documentName, int totalPages, CancellationTokenSource cts)
    {
        _jobs[pipelineJobId] = new Entry(pipelineJobId, printerName, documentName, totalPages, DateTime.Now, cts);
    }

    public void Update(string pipelineJobId, int pagesPrinted, PrintJobStatusCode status, string description)
    {
        if (_jobs.TryGetValue(pipelineJobId, out var entry))
        {
            entry.PagesPrinted = pagesPrinted;
            entry.StatusCode = status;
            entry.StatusDescription = description;
        }
    }

    public void Unregister(string pipelineJobId) => _jobs.TryRemove(pipelineJobId, out _);

    public bool Cancel(string pipelineJobId)
    {
        if (_jobs.TryGetValue(pipelineJobId, out var entry))
        {
            try
            {
                entry.Cts.Cancel();
                entry.StatusCode = PrintJobStatusCode.Deleting;
                entry.StatusDescription = "Cancellation requested...";
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>Cancels by the negative numeric id shown in queue listings.</summary>
    public bool CancelByNumericId(int jobId)
    {
        var entry = _jobs.Values.FirstOrDefault(e => e.NumericId == jobId);
        return entry != null && Cancel(entry.PipelineId);
    }

    public void CancelAllForPrinter(string printerName)
    {
        foreach (var entry in _jobs.Values)
        {
            if (string.Equals(entry.PrinterName, printerName, StringComparison.OrdinalIgnoreCase))
            {
                Cancel(entry.PipelineId);
            }
        }
    }

    public IEnumerable<PrintJobInfo> Snapshot(string? printerName)
    {
        foreach (var entry in _jobs.Values)
        {
            if (string.IsNullOrWhiteSpace(printerName) ||
                string.Equals(entry.PrinterName, printerName, StringComparison.OrdinalIgnoreCase))
            {
                yield return new PrintJobInfo
                {
                    JobId = entry.NumericId,
                    PipelineJobId = entry.PipelineId,
                    PrinterName = entry.PrinterName,
                    DocumentName = entry.DocumentName,
                    UserName = Environment.UserName,
                    TotalPages = entry.TotalPages,
                    PagesPrinted = entry.PagesPrinted,
                    SizeBytes = 0,
                    SubmittedAt = entry.SubmittedAt,
                    StatusCode = entry.StatusCode,
                    StatusDescription = entry.StatusDescription,
                    IsPrintmanPipelineJob = true,
                    CanCancel = true
                };
            }
        }
    }
}
