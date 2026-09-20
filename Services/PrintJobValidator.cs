using OhMyPrinter.Core.Abstractions;
using OhMyPrinter.Core.Models;

namespace OhMyPrinter.Services;

public class PrintJobValidator(
    IPrinterDiscoveryService printerDiscovery,
    IDocumentRendererResolver rendererResolver) : IPrintJobValidator
{
    private readonly IPrinterDiscoveryService _printerDiscovery = printerDiscovery;
    private readonly IDocumentRendererResolver _rendererResolver = rendererResolver;

    public async Task<ValidationResult> ValidateAsync(PrintJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return ValidationResult.Fail("File path cannot be empty.");
        }

        if (!File.Exists(request.FilePath))
        {
            return ValidationResult.Fail($"File not found: '{request.FilePath}'.");
        }

        IDocumentRenderer renderer;
        try
        {
            renderer = _rendererResolver.Resolve(request.FilePath);
        }
        catch (Exception ex)
        {
            return ValidationResult.Fail(ex.Message);
        }

        if (request.Copies < 1)
        {
            return ValidationResult.Fail($"Number of copies must be at least 1 (got {request.Copies}).");
        }

        var printer = !string.IsNullOrWhiteSpace(request.TargetPrinterName)
            ? _printerDiscovery.FindPrinter(request.TargetPrinterName)
            : _printerDiscovery.GetDefaultPrinter();

        if (printer == null)
        {
            var msg = string.IsNullOrWhiteSpace(request.TargetPrinterName)
                ? "No default printer is configured on this machine."
                : $"Printer matching '{request.TargetPrinterName}' could not be found.";
            return ValidationResult.Fail(msg);
        }

        int totalPages;
        try
        {
            totalPages = await renderer.GetPageCountAsync(request.FilePath);
        }
        catch (Exception ex)
        {
            return ValidationResult.Fail($"Failed to inspect document pages: {ex.Message}");
        }

        if (totalPages <= 0)
        {
            return ValidationResult.Fail("The document does not contain any printable pages.");
        }

        var includedPages = request.PageRange.ResolvePages(totalPages);
        if (includedPages.Count == 0)
        {
            return ValidationResult.Fail(
                $"Page range '{request.PageRange.RawExpression}' did not match any pages in document (total pages: {totalPages}).");
        }

        return ValidationResult.Ok();
    }
}
