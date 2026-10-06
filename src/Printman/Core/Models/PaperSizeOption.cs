namespace Printman.Core.Models;

public record PaperSizeOption(string Name, int RawKind, int WidthHundredthsInch, int HeightHundredthsInch)
{
    /// <summary>Print system keyword for this size (PWG media name on CUPS); null on Windows.</summary>
    public string? Keyword { get; init; }

    public double WidthMm => Math.Round(WidthHundredthsInch * 0.254, 1);
    public double HeightMm => Math.Round(HeightHundredthsInch * 0.254, 1);

    public override string ToString() => $"{Name} ({WidthMm} x {HeightMm} mm)";
}
