using System.Globalization;
using Printman.Core.Models;

namespace Printman.Services.Ipp;

/// <summary>
/// PWG 5101.1 self-describing media size name, dimensions in hundredths of millimetres.
/// </summary>
public sealed record PwgMedia(string Name, int WidthHmm, int HeightHmm);

/// <summary>
/// Maps Windows paper sizes to standard PWG media names advertised over IPP.
/// </summary>
public static class PwgMediaMapper
{
    private const int ToleranceHmm = 150; // 1.5 mm

    public static readonly IReadOnlyList<PwgMedia> Standard =
    [
        new("iso_a4_210x297mm", 21000, 29700),
        new("na_letter_8.5x11in", 21590, 27940),
        new("na_legal_8.5x14in", 21590, 35560),
        new("iso_a3_297x420mm", 29700, 42000),
        new("iso_a5_148x210mm", 14800, 21000),
        new("iso_a6_105x148mm", 10500, 14800),
        new("iso_b5_176x250mm", 17600, 25000),
        new("jis_b5_182x257mm", 18200, 25700),
        new("jis_b4_257x364mm", 25700, 36400),
        new("na_executive_7.25x10.5in", 18415, 26670),
        new("na_ledger_11x17in", 27940, 43180),
        new("na_invoice_5.5x8.5in", 13970, 21590),
        new("na_foolscap_8.5x13in", 21590, 33020),
        new("na_govt-letter_8x10in", 20320, 25400),
        new("na_index-4x6_4x6in", 10160, 15240),
        new("om_small-photo_100x150mm", 10000, 15000),
        new("na_5x7_5x7in", 12700, 17780),
        new("na_index-3x5_3x5in", 7620, 12700),
        new("oe_photo-l_3.5x5in", 8890, 12700),
        new("iso_c5_162x229mm", 16200, 22900),
        new("iso_dl_110x220mm", 11000, 22000),
        new("na_number-10_4.125x9.5in", 10478, 24130),
        new("na_monarch_3.875x7.5in", 9843, 19050)
    ];

    /// <summary>
    /// Matches printer paper sizes to standard PWG media. Returns pairs in the printer's order,
    /// one entry per PWG name. Paper is null for the A4/Letter fallback when nothing matches.
    /// </summary>
    public static IReadOnlyList<(PwgMedia Media, PaperSizeOption? Paper)> Map(IEnumerable<PaperSizeOption> paperSizes)
    {
        var result = new List<(PwgMedia, PaperSizeOption?)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var paper in paperSizes)
        {
            var media = FindBySize(
                (int)Math.Round(paper.WidthHundredthsInch * 25.4),
                (int)Math.Round(paper.HeightHundredthsInch * 25.4));

            if (media != null && seen.Add(media.Name))
            {
                result.Add((media, paper));
            }
        }

        if (result.Count == 0)
        {
            result.Add((Standard[0], null));
            result.Add((Standard[1], null));
        }

        return result;
    }

    public static PwgMedia? FindByName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Standard.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Closest standard size within tolerance, in either orientation.</summary>
    public static PwgMedia? FindBySize(int widthHmm, int heightHmm)
    {
        PwgMedia? best = null;
        int bestDelta = int.MaxValue;

        foreach (var m in Standard)
        {
            int portrait = Math.Abs(m.WidthHmm - widthHmm) + Math.Abs(m.HeightHmm - heightHmm);
            int rotated = Math.Abs(m.WidthHmm - heightHmm) + Math.Abs(m.HeightHmm - widthHmm);
            int delta = Math.Min(portrait, rotated);
            bool within = (Math.Abs(m.WidthHmm - widthHmm) <= ToleranceHmm && Math.Abs(m.HeightHmm - heightHmm) <= ToleranceHmm) ||
                          (Math.Abs(m.WidthHmm - heightHmm) <= ToleranceHmm && Math.Abs(m.HeightHmm - widthHmm) <= ToleranceHmm);

            if (within && delta < bestDelta)
            {
                best = m;
                bestDelta = delta;
            }
        }

        return best;
    }

    /// <summary>Region default: A4 for metric locales, Letter otherwise (when available).</summary>
    public static PwgMedia PickDefault(IReadOnlyList<(PwgMedia Media, PaperSizeOption? Paper)> mapped)
    {
        bool metric = true;
        try { metric = RegionInfo.CurrentRegion.IsMetric; } catch { }

        var preferred = metric ? "iso_a4_210x297mm" : "na_letter_8.5x11in";
        return mapped.FirstOrDefault(m => m.Media.Name == preferred).Media ?? mapped[0].Media;
    }
}
