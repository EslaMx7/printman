using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Tests.Fakes;

/// <summary>In-memory <see cref="IPrintQueueService"/> that records interactions.</summary>
public sealed class FakePrintQueueService : IPrintQueueService
{
    public List<PrintJobInfo> Jobs { get; } = [];
    public PrinterStatusInfo? Status { get; set; }
    public int PurgeResult { get; set; }
    public bool CancelResult { get; set; } = true;

    public List<string> RegisteredPipelineJobs { get; } = [];
    public List<string> UnregisteredPipelineJobs { get; } = [];
    public List<string> CancelledPipelineJobs { get; } = [];
    public List<(string Printer, int JobId)> CancelledSpoolerJobs { get; } = [];
    public List<string> PurgedPrinters { get; } = [];

    public IReadOnlyList<PrintJobInfo> GetJobs(string? printerName = null) =>
        printerName is null ? Jobs : Jobs.Where(j => j.PrinterName == printerName).ToList();

    public PrinterStatusInfo GetPrinterStatus(string printerName) =>
        Status ?? new PrinterStatusInfo { PrinterName = printerName };

    public bool CancelSpoolerJob(string printerName, int jobId)
    {
        CancelledSpoolerJobs.Add((printerName, jobId));
        return CancelResult;
    }

    public int PurgeSpoolerQueue(string printerName)
    {
        PurgedPrinters.Add(printerName);
        return PurgeResult;
    }

    public void RegisterPipelineJob(string pipelineJobId, string printerName, string documentName, int totalPages, CancellationTokenSource cts) =>
        RegisteredPipelineJobs.Add(pipelineJobId);

    public void UpdatePipelineJob(string pipelineJobId, int pagesPrinted, PrintJobStatusCode status, string description) { }

    public void UnregisterPipelineJob(string pipelineJobId) => UnregisteredPipelineJobs.Add(pipelineJobId);

    public bool CancelPipelineJob(string pipelineJobId)
    {
        CancelledPipelineJobs.Add(pipelineJobId);
        return CancelResult;
    }

    public bool CancelJob(string? printerName, string jobIdOrPipelineId)
    {
        if (int.TryParse(jobIdOrPipelineId, out var id))
        {
            return CancelSpoolerJob(printerName ?? string.Empty, id);
        }

        return CancelPipelineJob(jobIdOrPipelineId);
    }
}
