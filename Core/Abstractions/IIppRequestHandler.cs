using Printman.Core.Models;

namespace Printman.Core.Abstractions;

/// <summary>
/// Processes one IPP request (RFC 8011) and returns the encoded application/ipp response.
/// </summary>
public interface IIppRequestHandler
{
    Task<byte[]> HandleAsync(IppRequestContext context, CancellationToken ct);
}
