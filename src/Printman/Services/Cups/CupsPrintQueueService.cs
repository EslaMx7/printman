using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services.Cups;

/// <summary>
/// CUPS job queue and printer status over IPP (Get-Jobs, Get-Printer-Attributes, Cancel-Job),
/// plus Printman's own in-flight pipeline jobs.
/// </summary>
public sealed class CupsPrintQueueService(IPrinterDiscoveryService printerDiscovery, CupsClient cups) : IPrintQueueService
{
    private static readonly string[] JobAttributes =
    [
        "job-id", "job-name", "job-originating-user-name", "job-printer-uri", "job-state", "job-state-reasons",
        "job-printer-state-message", "job-k-octets", "time-at-creation", "job-impressions", "job-impressions-completed"
    ];

    private static readonly string[] StatusAttributes =
    [
        "printer-state", "printer-state-reasons", "printer-state-message", "printer-is-accepting-jobs", "queued-job-count"
    ];

    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly CupsClient _cups = cups;
    private readonly PipelineJobTracker _pipelineJobs = new();

    public IReadOnlyList<PrintJobInfo> GetJobs(string? printerName = null)
    {
        // 1. Include active Printman pipeline jobs (before CUPS has accepted them)
        var result = _pipelineJobs.Snapshot(printerName).ToList();

        // 2. CUPS jobs, for one printer or all of them
        string? target = null;
        if (!string.IsNullOrWhiteSpace(printerName))
        {
            target = _printerDiscovery.FindPrinter(printerName)?.Name ?? printerName;
        }

        try
        {
            var response = _cups.Send(IppOperation.GetJobs, target != null ? $"/printers/{Uri.EscapeDataString(target)}" : "/", op =>
            {
                op.AddUri("printer-uri", target != null ? CupsClient.PrinterUri(target) : "ipp://localhost/");
                op.AddKeyword("which-jobs", "not-completed");
                op.AddKeywords("requested-attributes", JobAttributes);
            });

            foreach (var job in response.Groups.Where(g => g.Tag == IppTag.JobAttributes))
            {
                if (job.Get("job-id")?.First?.AsInt() is not int id) continue;

                var (code, description) = TranslateJobState(
                    job.Get("job-state")?.First?.AsInt() ?? IppJobState.Pending,
                    Strings(job, "job-state-reasons"),
                    job.Get("job-printer-state-message")?.First?.AsString());

                long? created = job.Get("time-at-creation")?.First?.AsInt();
                result.Add(new PrintJobInfo
                {
                    JobId = id,
                    PrinterName = PrinterNameFromUri(job.Get("job-printer-uri")?.First?.AsString()) ?? target ?? "",
                    DocumentName = job.Get("job-name")?.First?.AsString() ?? "Document",
                    UserName = job.Get("job-originating-user-name")?.First?.AsString(),
                    TotalPages = job.Get("job-impressions")?.First?.AsInt() ?? 0,
                    PagesPrinted = job.Get("job-impressions-completed")?.First?.AsInt() ?? 0,
                    SizeBytes = (job.Get("job-k-octets")?.First?.AsInt() ?? 0) * 1024L,
                    SubmittedAt = created is > 0 ? DateTimeOffset.FromUnixTimeSeconds(created.Value).LocalDateTime : DateTime.Now,
                    StatusCode = code,
                    StatusDescription = description,
                    IsPrintmanPipelineJob = false,
                    CanCancel = true
                });
            }
        }
        catch (CupsException)
        {
            // CUPS unreachable or the printer is gone: show pipeline jobs only
        }

        return result;
    }

    public PrinterStatusInfo GetPrinterStatus(string printerName)
    {
        string targetName = _printerDiscovery.FindPrinter(printerName)?.Name ?? printerName;
        var statusInfo = new PrinterStatusInfo { PrinterName = targetName };

        IppAttributeGroup? attributes;
        try
        {
            var response = _cups.Send(IppOperation.GetPrinterAttributes, $"/printers/{Uri.EscapeDataString(targetName)}", op =>
            {
                op.AddUri("printer-uri", CupsClient.PrinterUri(targetName));
                op.AddKeywords("requested-attributes", StatusAttributes);
            });
            attributes = response.Group(IppTag.PrinterAttributes);
        }
        catch (CupsException)
        {
            attributes = null;
        }

        if (attributes == null)
        {
            statusInfo.StatusText = "Offline / Unavailable";
            statusInfo.IsOnline = false;
            statusInfo.HasError = true;
            return statusInfo;
        }

        int state = attributes.Get("printer-state")?.First?.AsInt() ?? IppPrinterState.Idle;
        statusInfo.QueuedJobCount = attributes.Get("queued-job-count")?.First?.AsInt() ?? 0;

        var reasons = new List<string>();
        foreach (var raw in Strings(attributes, "printer-state-reasons"))
        {
            // "media-empty-error" -> "media-empty"; "-report" reasons are informational
            if (raw.EndsWith("-report", StringComparison.Ordinal)) continue;
            var reason = raw.Replace("-error", "").Replace("-warning", "");

            switch (reason)
            {
                case "media-jam":
                    statusInfo.IsPaperJam = true;
                    statusInfo.HasError = true;
                    reasons.Add("Paper Jam");
                    break;
                case "media-empty" or "media-needed":
                    statusInfo.IsOutOfPaper = true;
                    statusInfo.HasError = true;
                    reasons.Add("Out of Paper");
                    break;
                case "door-open" or "cover-open" or "interlock-open":
                    statusInfo.IsDoorOpen = true;
                    statusInfo.HasError = true;
                    reasons.Add("Door Open");
                    break;
                case "offline" or "shutdown" or "connecting-to-device":
                    statusInfo.IsOnline = false;
                    statusInfo.HasError = true;
                    reasons.Add("Offline");
                    break;
                case "toner-empty" or "marker-supply-empty":
                    statusInfo.HasError = true;
                    reasons.Add("Out of Toner");
                    break;
                case "paused":
                    statusInfo.IsPaused = true;
                    break;
                default:
                    if (raw.EndsWith("-error", StringComparison.Ordinal))
                    {
                        statusInfo.HasError = true;
                        reasons.Add(reason);
                    }
                    break;
            }
        }

        if (state == IppPrinterState.Stopped)
        {
            statusInfo.IsPaused = true;
        }
        if (statusInfo.IsPaused)
        {
            reasons.Add("Paused");
        }
        if (state == IppPrinterState.Processing)
        {
            statusInfo.IsBusy = true;
            reasons.Add("Printing / Busy");
        }
        if (!(attributes.Get("printer-is-accepting-jobs")?.First?.AsBool() ?? true))
        {
            reasons.Add("Not accepting jobs");
        }

        statusInfo.StatusText = reasons.Count > 0
            ? string.Join(", ", reasons.Distinct())
            : statusInfo.QueuedJobCount > 0 ? "Spooling / Active" : "Ready";

        return statusInfo;
    }

    public bool CancelSpoolerJob(string printerName, int jobId)
    {
        try
        {
            _cups.Send(IppOperation.CancelJob, "/jobs/", op => op.AddUri("job-uri", CupsClient.JobUri(jobId)));
            return true;
        }
        catch (CupsException)
        {
            return false;
        }
    }

    public int PurgeSpoolerQueue(string printerName)
    {
        string targetName = _printerDiscovery.FindPrinter(printerName)?.Name ?? printerName;

        // Also cancel pipeline jobs for this printer
        _pipelineJobs.CancelAllForPrinter(targetName);

        // Cancel job by job (as the job owner) instead of Purge-Jobs, which needs CUPS admin rights
        int purged = 0;
        foreach (var job in GetJobs(targetName).Where(j => !j.IsPrintmanPipelineJob))
        {
            if (CancelSpoolerJob(targetName, job.JobId))
            {
                purged++;
            }
        }
        return purged;
    }

    public void RegisterPipelineJob(string pipelineJobId, string printerName, string documentName, int totalPages, CancellationTokenSource cts) =>
        _pipelineJobs.Register(pipelineJobId, printerName, documentName, totalPages, cts);

    public void UpdatePipelineJob(string pipelineJobId, int pagesPrinted, PrintJobStatusCode status, string description) =>
        _pipelineJobs.Update(pipelineJobId, pagesPrinted, status, description);

    public void UnregisterPipelineJob(string pipelineJobId) => _pipelineJobs.Unregister(pipelineJobId);

    public bool CancelPipelineJob(string pipelineJobId) => _pipelineJobs.Cancel(pipelineJobId);

    public bool CancelJob(string? printerName, string jobIdOrPipelineId)
    {
        // 1. Try pipeline job
        if (CancelPipelineJob(jobIdOrPipelineId))
        {
            return true;
        }

        // 2. Integer id: negative ids are hashed pipeline jobs, positive ids are CUPS jobs (unique across printers)
        if (int.TryParse(jobIdOrPipelineId, out int jobId))
        {
            return jobId < 0
                ? _pipelineJobs.CancelByNumericId(jobId)
                : CancelSpoolerJob(printerName ?? "", jobId);
        }

        return false;
    }

    private static (PrintJobStatusCode Code, string Description) TranslateJobState(int state, IReadOnlyList<string> reasons, string? printerMessage)
    {
        if (reasons.Any(r => r.StartsWith("media-empty", StringComparison.Ordinal) || r.StartsWith("media-needed", StringComparison.Ordinal)))
            return (PrintJobStatusCode.PaperOut, "Out of Paper");

        if (reasons.Any(r => r.StartsWith("media-jam", StringComparison.Ordinal)))
            return (PrintJobStatusCode.PaperJam, "Paper Jam");

        return state switch
        {
            IppJobState.Pending => (PrintJobStatusCode.Queued, "Queued"),
            IppJobState.PendingHeld => (PrintJobStatusCode.Paused, "Held"),
            IppJobState.Processing => (PrintJobStatusCode.Printing,
                string.IsNullOrWhiteSpace(printerMessage) ? "Printing" : $"Printing: {printerMessage}"),
            IppJobState.ProcessingStopped => (PrintJobStatusCode.Paused, "Stopped (printer paused)"),
            IppJobState.Canceled or IppJobState.Aborted => (PrintJobStatusCode.Deleting, "Cancelled"),
            IppJobState.Completed => (PrintJobStatusCode.Completed, "Completed"),
            _ => (PrintJobStatusCode.Unknown, "Unknown")
        };
    }

    private static string? PrinterNameFromUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        int slash = uri.LastIndexOf('/');
        return slash >= 0 && slash < uri.Length - 1 ? Uri.UnescapeDataString(uri[(slash + 1)..]) : null;
    }

    private static List<string> Strings(IppAttributeList group, string name) =>
        (group.Get(name)?.Values ?? []).Select(v => v.AsString()).OfType<string>().ToList();
}
