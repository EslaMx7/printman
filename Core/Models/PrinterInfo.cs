namespace OhMyPrinter.Core.Models;

public class PrinterInfo
{
    public required string Name { get; init; }
    public bool IsDefault { get; init; }
    public string Status { get; init; } = "Ready";
    public string? PortName { get; init; }
    public string? DriverName { get; init; }
    public bool SupportsColor { get; init; }
    public bool CanDuplex { get; init; }
    public IReadOnlyList<PaperSizeOption> SupportedPaperSizes { get; init; } = [];
    public IReadOnlyList<string> SupportedResolutions { get; init; } = [];

    public override string ToString() =>
        $"{Name}{(IsDefault ? " [DEFAULT]" : "")} (Port: {PortName ?? "N/A"})";
}
