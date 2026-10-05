using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Tests.Fakes;

/// <summary>Records submitted jobs and returns a configurable result.</summary>
public sealed class FakePrintService : IPrintService
{
    public List<PrintJobRequest> Requests { get; } = [];
    public List<string> ProgressMessages { get; } = [];

    public PrintJobResult Result { get; set; } = PrintJobResult.Succeeded("Fake Printer", 1, 1);
    public Func<PrintJobRequest, PrintJobResult>? Handler { get; set; }
    public Exception? ThrowOnPrint { get; set; }
    public Action<PrintJobRequest>? OnPrint { get; set; }

    public Task<PrintJobResult> PrintAsync(PrintJobRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        OnPrint?.Invoke(request);
        progress?.Report($"Spooling '{request.FilePath}'");
        ProgressMessages.Add(request.FilePath);

        if (ThrowOnPrint is not null)
        {
            throw ThrowOnPrint;
        }

        return Task.FromResult(Handler?.Invoke(request) ?? Result);
    }
}
