using System.Drawing.Printing;

namespace OhMyPrinter.Core.Models;

public record PaperSizeOption(string Name, int RawKind, int WidthHundredthsInch, int HeightHundredthsInch)
{
    public static PaperSizeOption FromDrawing(PaperSize ps) =>
        new(ps.PaperName, (int)ps.RawKind, ps.Width, ps.Height);

    public double WidthMm => Math.Round(WidthHundredthsInch * 0.254, 1);
    public double HeightMm => Math.Round(HeightHundredthsInch * 0.254, 1);

    public override string ToString() => $"{Name} ({WidthMm} x {HeightMm} mm)";
}
