using System.Drawing;
using System.Text;
using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

public class TextDocumentRenderer : IDocumentRenderer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".json", ".md", ".xml", ".ini", ".yaml", ".yml", ".sql", ".cmd", ".ps1"
    };

    private const string FontFamilyName = "Consolas";
    private const float FontSize = 9.5f;

    public bool CanHandle(string fileExtension) =>
        SupportedExtensions.Contains(fileExtension);

    public Task<int> GetPageCountAsync(string filePath)
    {
        // Calculate lines based on standard 8.5x11 / A4 printable area (approx 1000 hundredths of an inch height = 10 inches)
        // With margins, approx 9 inches = 900 points. Line height ~ 16 -> ~ 55 lines per page.
        var lines = GetFormattedLines(filePath, 700); // 7 inches width
        int linesPerPage = 55;
        int count = (int)Math.Ceiling((double)lines.Count / linesPerPage);
        return Task.FromResult(Math.Max(1, count));
    }

    public Task RenderPageAsync(
        string filePath,
        int pageNumber,
        Graphics graphics,
        Rectangle printableArea,
        int dpi,
        bool fitToPage)
    {
        using var font = new Font(FontFamilyName, FontSize, FontStyle.Regular, GraphicsUnit.Point);
        using var footerFont = new Font("Segoe UI", 8f, FontStyle.Italic, GraphicsUnit.Point);
        using var brush = new SolidBrush(Color.Black);
        using var footerBrush = new SolidBrush(Color.Gray);

        float lineHeight = font.GetHeight(graphics) + 2f;
        int printableHeight = printableArea.Height - (int)(lineHeight * 2); // reserve for footer
        int linesPerPage = Math.Max(1, (int)(printableHeight / lineHeight));

        var formattedLines = GetFormattedLines(filePath, printableArea.Width);
        int totalPages = Math.Max(1, (int)Math.Ceiling((double)formattedLines.Count / linesPerPage));

        if (pageNumber < 1 || pageNumber > totalPages)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Page {pageNumber} is outside text document range (1 to {totalPages}).");
        }

        int startIndex = (pageNumber - 1) * linesPerPage;
        int count = Math.Min(linesPerPage, formattedLines.Count - startIndex);

        float currentY = printableArea.Y;
        for (int i = 0; i < count; i++)
        {
            graphics.DrawString(formattedLines[startIndex + i], font, brush, printableArea.X, currentY);
            currentY += lineHeight;
        }

        // Draw clean footer
        string footer = $"{Path.GetFileName(filePath)}  •  Page {pageNumber} of {totalPages}";
        float footerY = printableArea.Bottom - lineHeight;
        graphics.DrawString(footer, footerFont, footerBrush, printableArea.X, footerY);

        return Task.CompletedTask;
    }

    private static List<string> GetFormattedLines(string filePath, int availableWidthHundredthsInch)
    {
        var result = new List<string>();
        var rawLines = File.ReadAllLines(filePath, Encoding.UTF8);

        // Approximate characters per line for 9.5pt Consolas (~60 chars for 700 width)
        int maxCharsPerLine = Math.Max(40, availableWidthHundredthsInch / 11);

        foreach (var rawLine in rawLines)
        {
            if (string.IsNullOrEmpty(rawLine))
            {
                result.Add(string.Empty);
                continue;
            }

            var line = rawLine.Replace("\t", "    ");
            while (line.Length > maxCharsPerLine)
            {
                result.Add(line[..maxCharsPerLine]);
                line = line[maxCharsPerLine..];
            }
            result.Add(line);
        }

        return result;
    }
}
