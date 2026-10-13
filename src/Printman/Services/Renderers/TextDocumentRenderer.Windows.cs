using System.Drawing;
using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

public partial class TextDocumentRenderer : IGdiDocumentRenderer
{
    private const string FontFamilyName = "Consolas";
    private const float FontSize = 9.5f;

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
}
