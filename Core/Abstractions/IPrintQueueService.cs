using Printman.Core.Models;

namespace Printman.Core.Abstractions;

public interface IPrintQueueService
{
    /// <summary>
    /// Enumerates all jobs in the native Windows Spooler and active Printman pipeline.
    /// If printerName is specified, limits to that printer.
    /// </summary>
    IReadOnlyList<PrintJobInfo> GetJobs(string? printerName = null);

    /// <summary>
    /// Queries the real-time hardware and spooler status for the given printer.
    /// </summary>
    PrinterStatusInfo GetPrinterStatus(string printerName);

    /// <summary>
    /// Cancels a native Windows Spooler job by Job ID.
    /// </summary>
    bool CancelSpoolerJob(string printerName, int jobId);

    /// <summary>
    /// Purges all jobs in the Windows Spooler queue for the specified printer.
    /// Returns the number of jobs that were purged or attempted.
    /// </summary>
    int PurgeSpoolerQueue(string printerName);

    /// <summary>
    /// Registers an in-flight Printman pipeline job (rendering / sending to spooler).
    /// </summary>
    void RegisterPipelineJob(string pipelineJobId, string printerName, string documentName, int totalPages, CancellationTokenSource cts);

    /// <summary>
    /// Updates status and progress of an active pipeline job.
    /// </summary>
    void UpdatePipelineJob(string pipelineJobId, int pagesPrinted, PrintJobStatusCode status, string description);

    /// <summary>
    /// Unregisters a completed or failed pipeline job.
    /// </summary>
    void UnregisterPipelineJob(string pipelineJobId);

    /// <summary>
    /// Cancels an in-flight Printman pipeline job.
    /// </summary>
    bool CancelPipelineJob(string pipelineJobId);

    /// <summary>
    /// Cancels either a pipeline job or spooler job by ID string or integer.
    /// </summary>
    bool CancelJob(string? printerName, string jobIdOrPipelineId);
}
