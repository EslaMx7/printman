using System.Runtime.InteropServices;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services;

public class WindowsPrintQueueService(IPrinterDiscoveryService printerDiscovery) : IPrintQueueService
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly PipelineJobTracker _pipelineJobs = new();

    #region Win32 P/Invoke Declarations

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool EnumJobs(
        IntPtr hPrinter,
        uint FirstJob,
        uint NoJobs,
        uint Level,
        IntPtr pJob,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool SetJob(IntPtr hPrinter, uint JobId, uint Level, IntPtr pJob, uint Command);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool SetPrinter(IntPtr hPrinter, uint Level, IntPtr pPrinter, uint Command);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr hPrinter, uint Level, IntPtr pPrinter, uint cbBuf, out uint pcbNeeded);

    private const uint JOB_CONTROL_PAUSE = 1;
    private const uint JOB_CONTROL_RESUME = 2;
    private const uint JOB_CONTROL_CANCEL = 3;
    private const uint JOB_CONTROL_RESTART = 4;
    private const uint JOB_CONTROL_DELETE = 5;

    private const uint PRINTER_CONTROL_PURGE = 3;

    private const uint JOB_STATUS_PAUSED = 0x00000001;
    private const uint JOB_STATUS_ERROR = 0x00000002;
    private const uint JOB_STATUS_DELETING = 0x00000004;
    private const uint JOB_STATUS_SPOOLING = 0x00000008;
    private const uint JOB_STATUS_PRINTING = 0x00000010;
    private const uint JOB_STATUS_OFFLINE = 0x00000020;
    private const uint JOB_STATUS_PAPEROUT = 0x00000040;
    private const uint JOB_STATUS_PRINTED = 0x00000080;
    private const uint JOB_STATUS_DELETED = 0x00000100;
    private const uint JOB_STATUS_BLOCKED_DEVQ = 0x00000200;
    private const uint JOB_STATUS_USER_INTERVENTION = 0x00000400;
    private const uint JOB_STATUS_RESTART = 0x00000800;
    private const uint JOB_STATUS_COMPLETE = 0x00001000;

    private const uint PRINTER_STATUS_PAUSED = 0x00000001;
    private const uint PRINTER_STATUS_ERROR = 0x00000002;
    private const uint PRINTER_STATUS_PENDING_DELETION = 0x00000004;
    private const uint PRINTER_STATUS_PAPER_JAM = 0x00000008;
    private const uint PRINTER_STATUS_PAPER_OUT = 0x00000010;
    private const uint PRINTER_STATUS_MANUAL_FEED = 0x00000020;
    private const uint PRINTER_STATUS_PAPER_PROBLEM = 0x00000040;
    private const uint PRINTER_STATUS_OFFLINE = 0x00000080;
    private const uint PRINTER_STATUS_IO_ACTIVE = 0x00000100;
    private const uint PRINTER_STATUS_BUSY = 0x00000200;
    private const uint PRINTER_STATUS_PRINTING = 0x00000400;
    private const uint PRINTER_STATUS_OUTPUT_BIN_FULL = 0x00000800;
    private const uint PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;
    private const uint PRINTER_STATUS_WAITING = 0x00002000;
    private const uint PRINTER_STATUS_PROCESSING = 0x00004000;
    private const uint PRINTER_STATUS_INITIALIZING = 0x00008000;
    private const uint PRINTER_STATUS_WARMING_UP = 0x00010000;
    private const uint PRINTER_STATUS_TONER_LOW = 0x00020000;
    private const uint PRINTER_STATUS_NO_TONER = 0x00040000;
    private const uint PRINTER_STATUS_PAGE_PUNT = 0x00080000;
    private const uint PRINTER_STATUS_USER_INTERVENTION = 0x00100000;
    private const uint PRINTER_STATUS_OUT_OF_MEMORY = 0x00200000;
    private const uint PRINTER_STATUS_DOOR_OPEN = 0x00400000;
    private const uint PRINTER_STATUS_SERVER_UNKNOWN = 0x00800000;
    private const uint PRINTER_STATUS_POWER_SAVE = 0x01000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;

        public DateTime ToDateTime()
        {
            try
            {
                if (wYear >= 1601 && wMonth is >= 1 and <= 12 && wDay is >= 1 and <= 31)
                {
                    return new DateTime(wYear, wMonth, wDay, wHour, wMinute, wSecond, DateTimeKind.Local);
                }
            }
            catch { }
            return DateTime.Now;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct JOB_INFO_2
    {
        public uint JobId;
        public IntPtr pPrinterName;
        public IntPtr pMachineName;
        public IntPtr pUserName;
        public IntPtr pDocument;
        public IntPtr pNotifyName;
        public IntPtr pDatatype;
        public IntPtr pPrintProcessor;
        public IntPtr pParameters;
        public IntPtr pDriverName;
        public IntPtr pDevMode;
        public IntPtr pStatus;
        public IntPtr pSecurityDescriptor;
        public uint Status;
        public uint Priority;
        public uint Position;
        public uint StartTime;
        public uint UntilTime;
        public uint TotalPages;
        public uint Size;
        public SYSTEMTIME Submitted;
        public uint Time;
        public uint PagesPrinted;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PRINTER_INFO_2
    {
        public IntPtr pServerName;
        public IntPtr pPrinterName;
        public IntPtr pShareName;
        public IntPtr pPortName;
        public IntPtr pDriverName;
        public IntPtr pComment;
        public IntPtr pLocation;
        public IntPtr pDevMode;
        public IntPtr pSepFile;
        public IntPtr pPrintProcessor;
        public IntPtr pDatatype;
        public IntPtr pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    #endregion

    public IReadOnlyList<PrintJobInfo> GetJobs(string? printerName = null)
    {
        // 1. Include active Printman pipeline jobs (rendering / pre-spooling)
        var result = _pipelineJobs.Snapshot(printerName).ToList();

        // 2. Query native Windows Spooler jobs
        var targetPrinters = new List<string>();
        if (!string.IsNullOrWhiteSpace(printerName))
        {
            var resolved = _printerDiscovery.FindPrinter(printerName);
            if (resolved != null)
            {
                targetPrinters.Add(resolved.Name);
            }
            else
            {
                targetPrinters.Add(printerName);
            }
        }
        else
        {
            // Query all installed printers
            var allPrinters = _printerDiscovery.GetPrinters();
            foreach (var p in allPrinters)
            {
                targetPrinters.Add(p.Name);
            }
        }

        foreach (var pName in targetPrinters)
        {
            EnumerateSpoolerJobs(pName, result);
        }

        return result;
    }

    private void EnumerateSpoolerJobs(string printerName, List<PrintJobInfo> list)
    {
        if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
        {
            return;
        }

        try
        {
            // First call to determine buffer size needed
            EnumJobs(hPrinter, 0, 100, 2, IntPtr.Zero, 0, out uint bytesNeeded, out uint _);
            if (bytesNeeded == 0)
            {
                return;
            }

            var pBuffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                if (EnumJobs(hPrinter, 0, 100, 2, pBuffer, bytesNeeded, out _, out uint count) && count > 0)
                {
                    int structSize = Marshal.SizeOf<JOB_INFO_2>();
                    for (int i = 0; i < count; i++)
                    {
                        var jobInfoPtr = IntPtr.Add(pBuffer, i * structSize);
                        var job = Marshal.PtrToStructure<JOB_INFO_2>(jobInfoPtr);

                        string docName = Marshal.PtrToStringAuto(job.pDocument) ?? "Document";
                        string user = Marshal.PtrToStringAuto(job.pUserName) ?? Environment.UserName;
                        string? customStatus = Marshal.PtrToStringAuto(job.pStatus);

                        var (status, desc) = TranslateJobStatus(job.Status, customStatus);

                        list.Add(new PrintJobInfo
                        {
                            JobId = (int)job.JobId,
                            PipelineJobId = null,
                            PrinterName = printerName,
                            DocumentName = docName,
                            UserName = user,
                            TotalPages = (int)job.TotalPages,
                            PagesPrinted = (int)job.PagesPrinted,
                            SizeBytes = job.Size,
                            SubmittedAt = job.Submitted.ToDateTime(),
                            StatusCode = status,
                            StatusDescription = desc,
                            IsPrintmanPipelineJob = false,
                            CanCancel = true
                        });
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pBuffer);
            }
        }
        catch
        {
            // Ignore spooler interrogation exceptions for non-responsive network printers
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    public PrinterStatusInfo GetPrinterStatus(string printerName)
    {
        var resolved = _printerDiscovery.FindPrinter(printerName);
        string targetName = resolved?.Name ?? printerName;

        var statusInfo = new PrinterStatusInfo
        {
            PrinterName = targetName,
            StatusText = "Ready",
            IsOnline = true,
            HasError = false,
            IsPaperJam = false,
            IsOutOfPaper = false,
            IsDoorOpen = false,
            IsBusy = false,
            IsPaused = false,
            QueuedJobCount = 0
        };

        if (!OpenPrinter(targetName, out var hPrinter, IntPtr.Zero))
        {
            statusInfo.StatusText = "Offline / Unavailable";
            statusInfo.IsOnline = false;
            statusInfo.HasError = true;
            return statusInfo;
        }

        try
        {
            GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out uint bytesNeeded);
            if (bytesNeeded > 0)
            {
                var pBuffer = Marshal.AllocHGlobal((int)bytesNeeded);
                try
                {
                    if (GetPrinter(hPrinter, 2, pBuffer, bytesNeeded, out _))
                    {
                        var info = Marshal.PtrToStructure<PRINTER_INFO_2>(pBuffer);
                        uint status = info.Status;
                        statusInfo.QueuedJobCount = (int)info.cJobs;

                        var reasons = new List<string>();

                        if ((status & PRINTER_STATUS_PAPER_JAM) != 0)
                        {
                            statusInfo.IsPaperJam = true;
                            statusInfo.HasError = true;
                            reasons.Add("Paper Jam");
                        }
                        if ((status & PRINTER_STATUS_PAPER_OUT) != 0)
                        {
                            statusInfo.IsOutOfPaper = true;
                            statusInfo.HasError = true;
                            reasons.Add("Out of Paper");
                        }
                        if ((status & PRINTER_STATUS_DOOR_OPEN) != 0)
                        {
                            statusInfo.IsDoorOpen = true;
                            statusInfo.HasError = true;
                            reasons.Add("Door Open");
                        }
                        if ((status & PRINTER_STATUS_OFFLINE) != 0 || (status & PRINTER_STATUS_NOT_AVAILABLE) != 0)
                        {
                            statusInfo.IsOnline = false;
                            statusInfo.HasError = true;
                            reasons.Add("Offline");
                        }
                        if ((status & PRINTER_STATUS_PAUSED) != 0)
                        {
                            statusInfo.IsPaused = true;
                            reasons.Add("Paused");
                        }
                        if ((status & (PRINTER_STATUS_BUSY | PRINTER_STATUS_PRINTING | PRINTER_STATUS_PROCESSING)) != 0)
                        {
                            statusInfo.IsBusy = true;
                            reasons.Add("Printing / Busy");
                        }
                        if ((status & PRINTER_STATUS_ERROR) != 0 && reasons.Count == 0)
                        {
                            statusInfo.HasError = true;
                            reasons.Add("Hardware Error");
                        }

                        if (reasons.Count > 0)
                        {
                            statusInfo.StatusText = string.Join(", ", reasons);
                        }
                        else
                        {
                            statusInfo.StatusText = statusInfo.QueuedJobCount > 0 ? "Spooling / Active" : "Ready";
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuffer);
                }
            }
        }
        catch
        {
            statusInfo.StatusText = "Unknown Status";
        }
        finally
        {
            ClosePrinter(hPrinter);
        }

        return statusInfo;
    }

    public bool CancelSpoolerJob(string printerName, int jobId)
    {
        var resolved = _printerDiscovery.FindPrinter(printerName);
        string targetName = resolved?.Name ?? printerName;

        if (!OpenPrinter(targetName, out var hPrinter, IntPtr.Zero))
        {
            return false;
        }

        try
        {
            // First attempt cancel (Windows Vista+)
            bool cancelled = SetJob(hPrinter, (uint)jobId, 0, IntPtr.Zero, JOB_CONTROL_CANCEL);
            if (!cancelled)
            {
                // Fallback to delete
                cancelled = SetJob(hPrinter, (uint)jobId, 0, IntPtr.Zero, JOB_CONTROL_DELETE);
            }
            return cancelled;
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    public int PurgeSpoolerQueue(string printerName)
    {
        var resolved = _printerDiscovery.FindPrinter(printerName);
        string targetName = resolved?.Name ?? printerName;

        // Also cancel pipeline jobs for this printer
        _pipelineJobs.CancelAllForPrinter(targetName);

        if (!OpenPrinter(targetName, out var hPrinter, IntPtr.Zero))
        {
            return 0;
        }

        int purgedCount = 0;
        try
        {
            // 1. Purge all jobs via SetPrinter
            bool success = SetPrinter(hPrinter, 0, IntPtr.Zero, PRINTER_CONTROL_PURGE);

            // 2. Also enumerate remaining jobs and call DELETE to ensure stubborn jobs are purged
            EnumJobs(hPrinter, 0, 100, 2, IntPtr.Zero, 0, out uint bytesNeeded, out _);
            if (bytesNeeded > 0)
            {
                var pBuffer = Marshal.AllocHGlobal((int)bytesNeeded);
                try
                {
                    if (EnumJobs(hPrinter, 0, 100, 2, pBuffer, bytesNeeded, out _, out uint count))
                    {
                        purgedCount = (int)count;
                        int structSize = Marshal.SizeOf<JOB_INFO_2>();
                        for (int i = 0; i < count; i++)
                        {
                            var ptr = IntPtr.Add(pBuffer, i * structSize);
                            var job = Marshal.PtrToStructure<JOB_INFO_2>(ptr);
                            SetJob(hPrinter, job.JobId, 0, IntPtr.Zero, JOB_CONTROL_CANCEL);
                            SetJob(hPrinter, job.JobId, 0, IntPtr.Zero, JOB_CONTROL_DELETE);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuffer);
                }
            }

            return Math.Max(purgedCount, success ? 1 : 0);
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
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

        // 2. Try integer spooler Job ID
        if (int.TryParse(jobIdOrPipelineId, out int jobId))
        {
            // If negative, it was a hashed pipeline job ID
            if (jobId < 0)
            {
                return _pipelineJobs.CancelByNumericId(jobId);
            }

            if (!string.IsNullOrWhiteSpace(printerName))
            {
                return CancelSpoolerJob(printerName, jobId);
            }

            // If printerName wasn't provided, search across all printers
            var printers = _printerDiscovery.GetPrinters();
            foreach (var p in printers)
            {
                if (CancelSpoolerJob(p.Name, jobId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (PrintJobStatusCode code, string description) TranslateJobStatus(uint status, string? customStatus)
    {
        if (!string.IsNullOrWhiteSpace(customStatus))
        {
            return (PrintJobStatusCode.Unknown, customStatus);
        }

        if ((status & JOB_STATUS_PAPEROUT) != 0)
            return (PrintJobStatusCode.PaperOut, "Out of Paper");

        if ((status & JOB_STATUS_OFFLINE) != 0)
            return (PrintJobStatusCode.Offline, "Printer Offline");

        if ((status & JOB_STATUS_ERROR) != 0 || (status & JOB_STATUS_BLOCKED_DEVQ) != 0)
            return (PrintJobStatusCode.Error, "Print Error / Blocked");

        if ((status & JOB_STATUS_USER_INTERVENTION) != 0)
            return (PrintJobStatusCode.Error, "User Intervention Required");

        if ((status & JOB_STATUS_PAUSED) != 0)
            return (PrintJobStatusCode.Paused, "Paused");

        if ((status & JOB_STATUS_DELETING) != 0 || (status & JOB_STATUS_DELETED) != 0)
            return (PrintJobStatusCode.Deleting, "Cancelling / Deleting");

        if ((status & JOB_STATUS_PRINTING) != 0)
            return (PrintJobStatusCode.Printing, "Printing");

        if ((status & JOB_STATUS_SPOOLING) != 0)
            return (PrintJobStatusCode.Spooling, "Spooling");

        if ((status & JOB_STATUS_COMPLETE) != 0 || (status & JOB_STATUS_PRINTED) != 0)
            return (PrintJobStatusCode.Completed, "Completed");

        return (PrintJobStatusCode.Queued, "Queued");
    }
}
