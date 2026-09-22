using Printman.Core.Models;

namespace Printman.Core.Abstractions;

public interface IPrintService
{
    /// <summary>
    /// Executes the print job according to the provided request specifications.
    /// </summary>
    Task<PrintJobResult> PrintAsync(PrintJobRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
