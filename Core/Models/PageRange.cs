using System.Text.RegularExpressions;

namespace OhMyPrinter.Core.Models;

public class PageRange
{
    private readonly string _rawExpression;
    private readonly List<IPageSelector> _selectors = [];

    public bool IsAllPages => string.IsNullOrWhiteSpace(_rawExpression) ||
                              _rawExpression.Trim().Equals("all", StringComparison.OrdinalIgnoreCase);

    public string RawExpression => _rawExpression;

    private PageRange(string expression, List<IPageSelector> selectors)
    {
        _rawExpression = expression;
        _selectors = selectors;
    }

    public static PageRange All => new("all", []);

    public static PageRange Parse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return All;
        }

        var trimmed = expression.Trim();
        var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selectors = new List<IPageSelector>();

        foreach (var part in parts)
        {
            // Support both 1:3 and 1-3
            var match = Regex.Match(part, @"^(?:(?<start>\d+)\s*[:\-]\s*(?<end>\d+)|(?<single>\d+)|(?<from>\d+)\s*[:\-]|[:\-]\s*(?<to>\d+))$");
            if (!match.Success)
            {
                throw new FormatException($"Invalid page range syntax: '{part}'. Use formats like '1:3', '1-3', '1,3,5', '2-', or '-4'.");
            }

            if (match.Groups["single"].Success)
            {
                var page = int.Parse(match.Groups["single"].Value);
                if (page < 1) throw new FormatException($"Page numbers must be >= 1 (got {page}).");
                selectors.Add(new SinglePageSelector(page));
            }
            else if (match.Groups["start"].Success && match.Groups["end"].Success)
            {
                var start = int.Parse(match.Groups["start"].Value);
                var end = int.Parse(match.Groups["end"].Value);
                if (start < 1 || end < 1) throw new FormatException($"Page numbers must be >= 1 (got {start} to {end}).");
                if (start > end)
                {
                    (start, end) = (end, start); // Automatically normalize order
                }
                selectors.Add(new RangePageSelector(start, end));
            }
            else if (match.Groups["from"].Success)
            {
                var from = int.Parse(match.Groups["from"].Value);
                if (from < 1) throw new FormatException($"Page number must be >= 1 (got {from}).");
                selectors.Add(new FromPageSelector(from));
            }
            else if (match.Groups["to"].Success)
            {
                var to = int.Parse(match.Groups["to"].Value);
                if (to < 1) throw new FormatException($"Page number must be >= 1 (got {to}).");
                selectors.Add(new ToPageSelector(to));
            }
        }

        return new PageRange(trimmed, selectors);
    }

    public List<int> ResolvePages(int totalPages)
    {
        if (totalPages <= 0)
        {
            return [];
        }

        if (IsAllPages)
        {
            return Enumerable.Range(1, totalPages).ToList();
        }

        var result = new HashSet<int>();
        foreach (var selector in _selectors)
        {
            foreach (var page in selector.GetPages(totalPages))
            {
                if (page >= 1 && page <= totalPages)
                {
                    result.Add(page);
                }
            }
        }

        return result.OrderBy(p => p).ToList();
    }

    public override string ToString() => IsAllPages ? "All Pages" : _rawExpression;

    private interface IPageSelector
    {
        IEnumerable<int> GetPages(int totalPages);
    }

    private sealed class SinglePageSelector(int page) : IPageSelector
    {
        public IEnumerable<int> GetPages(int totalPages) => [page];
    }

    private sealed class RangePageSelector(int start, int end) : IPageSelector
    {
        public IEnumerable<int> GetPages(int totalPages) => Enumerable.Range(start, end - start + 1);
    }

    private sealed class FromPageSelector(int from) : IPageSelector
    {
        public IEnumerable<int> GetPages(int totalPages) => from <= totalPages ? Enumerable.Range(from, totalPages - from + 1) : [];
    }

    private sealed class ToPageSelector(int to) : IPageSelector
    {
        public IEnumerable<int> GetPages(int totalPages) => Enumerable.Range(1, Math.Min(to, totalPages));
    }
}
