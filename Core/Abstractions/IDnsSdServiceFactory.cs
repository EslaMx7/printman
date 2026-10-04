using Printman.Core.Models;

namespace Printman.Core.Abstractions;

/// <summary>
/// Describes shared printers as DNS-SD services (service types, subtypes and TXT keys).
/// </summary>
public interface IDnsSdServiceFactory
{
    IReadOnlyList<DnsSdService> Create(IReadOnlyList<SharedPrinter> printers);
}
