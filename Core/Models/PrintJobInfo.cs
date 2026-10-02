namespace Printman.Core.Models;

public enum PrintJobStatusCode
{
    Queued,
    Spooling,
    Printing,
    Paused,
    Error,
    PaperJam,
    PaperOut,
    Offline,
    Deleting,
    Completed,
    Unknown
}

public class PrintJobInfo
{
    public required int JobId { get; init; }
    public string? PipelineJobId { get; init; }
    public required string PrinterName { get; init; }
    public required string DocumentName { get; init; }
    public string? UserName { get; init; }
    public int TotalPages { get; init; }
    public int PagesPrinted { get; init; }
    public long SizeBytes { get; init; }
    public DateTime SubmittedAt { get; init; } = DateTime.UtcNow;
    public PrintJobStatusCode StatusCode { get; init; } = PrintJobStatusCode.Queued;
    public string StatusDescription { get; init; } = "Queued";
    public bool IsPrintmanPipelineJob { get; init; }
    public bool CanCancel { get; init; } = true;
}
