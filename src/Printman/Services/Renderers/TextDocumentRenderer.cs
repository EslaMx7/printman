using System.Text;
using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

/// <summary>
/// Plain text files. Windows draws monospaced pages with GDI+ (TextDocumentRenderer.Windows.cs);
/// Linux / macOS hand the file to CUPS, which paginates it with its text filter.
/// </summary>
public partial class TextDocumentRenderer : IDocumentRenderer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".json", ".md", ".xml", ".ini", ".yaml", ".yml", ".sql", ".cmd", ".ps1"
    };

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
