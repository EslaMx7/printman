using Printman.Core.Abstractions;

namespace Printman.Tests.Fakes;

/// <summary>Resolves renderers from a lookup (case-insensitive extension -> renderer).</summary>
public sealed class FakeDocumentRendererResolver : IDocumentRendererResolver
{
    private readonly Dictionary<string, IDocumentRenderer> _renderers = new(StringComparer.OrdinalIgnoreCase);

    public FakeDocumentRendererResolver(IDictionary<string, IDocumentRenderer>? renderers = null)
    {
        if (renderers is not null)
        {
            foreach (var (extension, renderer) in renderers)
            {
                _renderers[extension] = renderer;
            }
        }
    }

    public List<string> ResolvedPaths { get; } = [];

    public IDocumentRenderer Resolve(string filePath)
    {
        ResolvedPaths.Add(filePath);
        var extension = Path.GetExtension(filePath);
        if (_renderers.TryGetValue(extension, out var renderer))
        {
            return renderer;
        }

        var match = _renderers.FirstOrDefault(kv => Path.GetExtension(filePath).Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
        if (match.Value is not null)
        {
            return match.Value;
        }

        throw new NotSupportedException($"No renderer registered for '{extension}'.");
    }
}
