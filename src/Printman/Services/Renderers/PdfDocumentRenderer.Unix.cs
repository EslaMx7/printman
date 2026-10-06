using System.Text.RegularExpressions;
using Printman.Services.Cups;

namespace Printman.Services.Renderers;

public partial class PdfDocumentRenderer
{
    // CUPS rasterizes the PDF itself; Printman only needs the page count (for page ranges and progress)
    public async Task<int> GetPageCountAsync(string filePath)
    {
        if (PdfPageCounter.TryCount(filePath) is int count)
        {
            return count;
        }

        // Encrypted or unusual files: ask poppler or qpdf when installed (both ship with most CUPS setups)
        if (ExternalTool.Find("pdfinfo") is string pdfinfo)
        {
            var result = await ExternalTool.RunAsync(pdfinfo, [filePath], timeout: TimeSpan.FromSeconds(30));
            var m = Regex.Match(result.StandardOutput, @"^Pages:\s+(\d+)", RegexOptions.Multiline);
            if (result.ExitCode == 0 && m.Success) return int.Parse(m.Groups[1].Value);
        }

        if (ExternalTool.Find("qpdf") is string qpdf)
        {
            var result = await ExternalTool.RunAsync(qpdf, ["--show-npages", filePath], timeout: TimeSpan.FromSeconds(30));
            if (int.TryParse(result.StandardOutput.Trim(), out int pages) && pages > 0) return pages;
        }

        throw new InvalidDataException("Could not determine the PDF page count (install poppler-utils or qpdf for encrypted PDFs).");
    }
}
