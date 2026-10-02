namespace Printman.Core.Models;

public class PrinterStatusInfo
{
    public required string PrinterName { get; set; }
    public string StatusText { get; set; } = "Ready";
    public bool IsOnline { get; set; } = true;
    public bool HasError { get; set; }
    public bool IsPaperJam { get; set; }
    public bool IsOutOfPaper { get; set; }
    public bool IsDoorOpen { get; set; }
    public bool IsBusy { get; set; }
    public bool IsPaused { get; set; }
    public int QueuedJobCount { get; set; }
}
