using Printman.Core.Models;

namespace Printman.Services;

/// <summary>
/// Fuzzy printer name matching shared by every <see cref="Core.Abstractions.IPrinterDiscoveryService"/>.
/// </summary>
public static class PrinterMatcher
{
    /// <summary>Exact match, then prefix, then substring, then all whitespace-separated tokens.</summary>
    public static PrinterInfo? Find(IReadOnlyList<PrinterInfo> printers, string query)
    {
        var trimmedQuery = query.Trim();

        // 1. Exact match
        var exact = printers.FirstOrDefault(p =>
            string.Equals(p.Name, trimmedQuery, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 2. Starts with
        var starts = printers.FirstOrDefault(p =>
            p.Name.StartsWith(trimmedQuery, StringComparison.OrdinalIgnoreCase));
        if (starts != null) return starts;

        // 3. Substring match
        var contains = printers.FirstOrDefault(p =>
            p.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase));
        if (contains != null) return contains;

        // 4. Token-based match (e.g. "HP Laser" matches "HP LaserJet Professional P1102")
        var tokens = trimmedQuery.Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > 0)
        {
            var tokenMatch = printers.FirstOrDefault(p =>
                tokens.All(t => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase)));
            if (tokenMatch != null) return tokenMatch;
        }

        return null;
    }
}
